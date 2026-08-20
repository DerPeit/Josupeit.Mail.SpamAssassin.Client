// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

// The awaited half of each command, which the rest of the suite only spot-checks: it has its own pumping loops, its own
// compression call and its own body copy, none of which the blocking path exercises.
[TestFixture]
public sealed class AsyncPathTests
{
    private static readonly byte[] _message = Encoding.ASCII.GetBytes(
        "From: Asa Await <asa.await@example.com>\r\nSubject: eventually\r\n\r\nNot yet, but soon.\r\n");

    [Test]
    public async Task A_ping_comes_back_with_the_version_the_daemon_announced()
    {
        var client = Client("SPAMD/1.5 0 PONG\r\n");

        var version = await client.PingAsync();

        Assert.That(version, Is.EqualTo(new Version(1, 5)));
    }

    [Test]
    public async Task A_skip_expects_no_answer()
    {
        var spamd = new FakeSpamd([]);
        var client = new SpamAssassinClient(spamd);

        await client.SkipAsync();

        Assert.That(spamd.LastRequestText, Is.EqualTo("SKIP SPAMC/1.5\r\n\r\n"));
    }

    [Test]
    public async Task A_report_comes_back_whole()
    {
        const string REPORT = "This message is probably spam. 6.6 points, threshold 5.0.\r\n";

        var client = Client($"SPAMD/1.1 0 EX_OK\r\nContent-length: {REPORT.Length}\r\nSpam: True ; 6.6 / 5.0\r\n\r\n{REPORT}");

        var result = await client.ReportAsync(_message);

        Assert.That(result.Report, Is.EqualTo(REPORT));
        Assert.That(result.Verdict.IsSpam, Is.True);
    }

    [Test]
    public async Task A_clean_message_earns_no_report_from_report_if_spam()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nContent-length: 0\r\nSpam: No ; 0.2 / 5.0\r\n\r\n");

        var result = await client.ReportIfSpamAsync(_message);

        Assert.That(result.Report, Is.Null);
        Assert.That(result.Verdict.IsSpam, Is.False);
    }

    [Test]
    public async Task A_rewritten_message_is_copied_into_the_destination()
    {
        const string REWRITTEN = "X-Spam-Flag: YES\r\n\r\nNot yet, but soon.\r\n";

        var client = Client($"SPAMD/1.1 0 EX_OK\r\nContent-length: {REWRITTEN.Length}\r\nSpam: True ; 9.1 / 5.0\r\n\r\n{REWRITTEN}");

        using var destination = new MemoryStream();

        var result = await client.ProcessAsync(_message, destination);

        Assert.That(Encoding.ASCII.GetString(destination.ToArray()), Is.EqualTo(REWRITTEN));
        Assert.That(result.Length, Is.EqualTo(REWRITTEN.Length));
    }

    [Test]
    public async Task Rewritten_headers_are_copied_into_the_destination()
    {
        const string REWRITTEN = "X-Spam-Status: No, score=0.2\r\n\r\n";

        var spamd = new FakeSpamd($"SPAMD/1.1 0 EX_OK\r\nContent-length: {REWRITTEN.Length}\r\nSpam: False ; 0.2 / 5.0\r\n\r\n{REWRITTEN}");
        var client = new SpamAssassinClient(spamd);

        using var destination = new MemoryStream();

        await client.HeadersAsync(_message, destination);

        Assert.That(spamd.LastRequestText, Does.StartWith("HEADERS SPAMC/1.5\r\n"));
        Assert.That(Encoding.ASCII.GetString(destination.ToArray()), Is.EqualTo(REWRITTEN));
    }

    // The body arrives in more pieces than one read returns, which is the only way to exercise the asynchronous refill.
    [Test]
    public async Task A_body_longer_than_the_transfer_buffer_is_copied_whole()
    {
        var rewritten = new string('.', 40_000);

        var client = new SpamAssassinClient(
            new FakeSpamd($"SPAMD/1.1 0 EX_OK\r\nContent-length: {rewritten.Length}\r\nSpam: False ; 0.0 / 5.0\r\n\r\n{rewritten}"),
            new() { TransferBufferSize = 1024 });

        using var destination = new MemoryStream();

        var result = await client.ProcessAsync(_message, destination);

        Assert.That(result.Length, Is.EqualTo(rewritten.Length));
        Assert.That(destination.Length, Is.EqualTo(rewritten.Length));
    }

    [Test]
    public async Task A_body_without_an_announced_length_is_copied_until_the_daemon_stops()
    {
        const string REWRITTEN = "X-Spam-Flag: YES\r\n\r\nAll of it, with no length to go by.\r\n";

        var client = Client($"SPAMD/1.1 0 EX_OK\r\nSpam: True ; 7.0 / 5.0\r\n\r\n{REWRITTEN}");

        using var destination = new MemoryStream();

        var result = await client.ProcessAsync(_message, destination);

        Assert.That(Encoding.ASCII.GetString(destination.ToArray()), Is.EqualTo(REWRITTEN));
        Assert.That(result.Length, Is.EqualTo(REWRITTEN.Length));
    }

    [Test]
    public async Task A_tell_reports_what_the_daemon_changed()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nDidSet: local\r\nDidRemove: remote\r\n\r\n\r\n");

        var result = await client.TellAsync(TellInstruction.RevokeAsHam, _message);

        Assert.That(result.Set, Is.EqualTo(TellDatabases.Local));
        Assert.That(result.Removed, Is.EqualTo(TellDatabases.Remote));
    }

    [Test]
    public async Task A_compressed_request_announces_the_compressed_length()
    {
        var spamd = new FakeSpamd("SPAMD/1.1 0 EX_OK\r\nSpam: False ; 0.2 / 5.0\r\n\r\n");
        var client = new SpamAssassinClient(spamd, new() { CompressRequests = true });

        await client.ScanAsync(_message);

        var request = spamd.LastRequest;
        var separator = request.AsSpan().IndexOf("\r\n\r\n"u8);
        var body = request[(separator + 4)..];

        Assert.That(Encoding.ASCII.GetString(request, 0, separator), Does.Contain($"Content-length: {body.Length}\r\n").And.Contain("Compress: zlib"));
        Assert.That(Inflate(body), Is.EqualTo(_message));
    }

    [Test]
    public void A_message_shorter_than_the_length_it_was_given_fails_before_the_daemon_can_object()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nSpam: False ; 0.0 / 5.0\r\n\r\n");

        Assert.ThrowsAsync<EndOfStreamException>(async () => await client.ScanAsync(new MemoryStream(_message), _message.Length + 100));
    }

    [Test]
    public void A_refusal_surfaces_as_the_exception_named_for_it()
    {
        var client = Client("SPAMD/1.0 74 IO error: the daemon could not read something\r\n");

        var refusal = Assert.ThrowsAsync<ErrorResponseSpamAssassinException>(async () => await client.ReportAsync(_message))!;

        Assert.That(refusal.Code, Is.EqualTo(SpamAssassinResponseCode.IoError));
    }

    private static SpamAssassinClient Client(string response) => new(new FakeSpamd(response));

    private static byte[] Inflate(byte[] compressed)
    {
        using var source = new MemoryStream(compressed);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        zlib.CopyTo(inflated);

        return inflated.ToArray();
    }
}
