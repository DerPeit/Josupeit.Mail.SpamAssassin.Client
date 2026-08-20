// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

// Sixty hand-written delegations to fourteen cores invite a copy-paste mistake: an overload that sends the wrong
// command, or hands its message to the wrong core. Each test calls one command through all four shapes in both colours
// and asserts the eight requests are byte-identical.
[TestFixture]
public sealed class OverloadTests
{
    private const string VERDICT = "Spam: True ; 6.6 / 5.0";

    private static readonly byte[] _message = Encoding.ASCII.GetBytes(
        "From: Ophelia Overload <ophelia.overload@example.com>\r\nSubject: eight ways to ask\r\n\r\nSame question, every time.\r\n");

    [Test]
    public async Task Every_scan_overload_sends_the_same_check_request()
    {
        var spamd = Spamd($"{VERDICT}\r\n\r\n");
        var client = new SpamAssassinClient(spamd);

        client.Scan(Seekable());
        client.Scan(Unseekable(), _message.Length);
        client.Scan(new ReadOnlySequence<byte>(_message));
        client.Scan(_message.AsMemory());
        await client.ScanAsync(Seekable());
        await client.ScanAsync(Unseekable(), _message.Length);
        await client.ScanAsync(new ReadOnlySequence<byte>(_message));
        await client.ScanAsync(_message.AsMemory());

        AssertAllIdentical(spamd, "CHECK");
    }

    [Test]
    public async Task Every_symbols_overload_sends_the_same_symbols_request()
    {
        var spamd = Spamd($"Content-length: 5\r\n{VERDICT}\r\n\r\nGTUBE");
        var client = new SpamAssassinClient(spamd);

        client.Symbols(Seekable());
        client.Symbols(Unseekable(), _message.Length);
        client.Symbols(new ReadOnlySequence<byte>(_message));
        client.Symbols(_message.AsMemory());
        await client.SymbolsAsync(Seekable());
        await client.SymbolsAsync(Unseekable(), _message.Length);
        await client.SymbolsAsync(new ReadOnlySequence<byte>(_message));
        await client.SymbolsAsync(_message.AsMemory());

        AssertAllIdentical(spamd, "SYMBOLS");
    }

    [Test]
    public async Task Every_report_overload_sends_the_same_report_request()
    {
        var spamd = Spamd($"Content-length: 6\r\n{VERDICT}\r\n\r\nreport");
        var client = new SpamAssassinClient(spamd);

        client.Report(Seekable());
        client.Report(Unseekable(), _message.Length);
        client.Report(new ReadOnlySequence<byte>(_message));
        client.Report(_message.AsMemory());
        await client.ReportAsync(Seekable());
        await client.ReportAsync(Unseekable(), _message.Length);
        await client.ReportAsync(new ReadOnlySequence<byte>(_message));
        await client.ReportAsync(_message.AsMemory());

        AssertAllIdentical(spamd, "REPORT");
    }

    [Test]
    public async Task Every_report_if_spam_overload_sends_the_same_request()
    {
        var spamd = Spamd($"Content-length: 6\r\nSpam: Yes ; 6.6 / 5.0\r\n\r\nreport");
        var client = new SpamAssassinClient(spamd);

        client.ReportIfSpam(Seekable());
        client.ReportIfSpam(Unseekable(), _message.Length);
        client.ReportIfSpam(new ReadOnlySequence<byte>(_message));
        client.ReportIfSpam(_message.AsMemory());
        await client.ReportIfSpamAsync(Seekable());
        await client.ReportIfSpamAsync(Unseekable(), _message.Length);
        await client.ReportIfSpamAsync(new ReadOnlySequence<byte>(_message));
        await client.ReportIfSpamAsync(_message.AsMemory());

        AssertAllIdentical(spamd, "REPORT_IFSPAM");
    }

    [Test]
    public async Task Every_process_overload_sends_the_same_process_request()
    {
        var spamd = Spamd($"Content-length: 8\r\n{VERDICT}\r\n\r\nrewrite!");
        var client = new SpamAssassinClient(spamd);

        client.Process(Seekable(), new MemoryStream());
        client.Process(Unseekable(), _message.Length, new MemoryStream());
        client.Process(new ReadOnlySequence<byte>(_message), new MemoryStream());
        client.Process(_message.AsMemory(), new MemoryStream());
        await client.ProcessAsync(Seekable(), new MemoryStream());
        await client.ProcessAsync(Unseekable(), _message.Length, new MemoryStream());
        await client.ProcessAsync(new ReadOnlySequence<byte>(_message), new MemoryStream());
        await client.ProcessAsync(_message.AsMemory(), new MemoryStream());

        AssertAllIdentical(spamd, "PROCESS");
    }

    [Test]
    public async Task Every_headers_overload_sends_the_same_headers_request()
    {
        var spamd = Spamd($"Content-length: 8\r\n{VERDICT}\r\n\r\nrewrite!");
        var client = new SpamAssassinClient(spamd);

        client.Headers(Seekable(), new MemoryStream());
        client.Headers(Unseekable(), _message.Length, new MemoryStream());
        client.Headers(new ReadOnlySequence<byte>(_message), new MemoryStream());
        client.Headers(_message.AsMemory(), new MemoryStream());
        await client.HeadersAsync(Seekable(), new MemoryStream());
        await client.HeadersAsync(Unseekable(), _message.Length, new MemoryStream());
        await client.HeadersAsync(new ReadOnlySequence<byte>(_message), new MemoryStream());
        await client.HeadersAsync(_message.AsMemory(), new MemoryStream());

        AssertAllIdentical(spamd, "HEADERS");
    }

    [Test]
    public async Task Every_tell_overload_sends_the_same_tell_request()
    {
        var spamd = Spamd("DidSet: local\r\n\r\n\r\n");
        var client = new SpamAssassinClient(spamd);

        client.Tell(TellInstruction.LearnAsSpam, Seekable());
        client.Tell(TellInstruction.LearnAsSpam, Unseekable(), _message.Length);
        client.Tell(TellInstruction.LearnAsSpam, new ReadOnlySequence<byte>(_message));
        client.Tell(TellInstruction.LearnAsSpam, _message.AsMemory());
        await client.TellAsync(TellInstruction.LearnAsSpam, Seekable());
        await client.TellAsync(TellInstruction.LearnAsSpam, Unseekable(), _message.Length);
        await client.TellAsync(TellInstruction.LearnAsSpam, new ReadOnlySequence<byte>(_message));
        await client.TellAsync(TellInstruction.LearnAsSpam, _message.AsMemory());

        AssertAllIdentical(spamd, "TELL");
    }

    private static FakeSpamd Spamd(string tail) => new($"SPAMD/1.1 0 EX_OK\r\n{tail}");

    private static MemoryStream Seekable() => new(_message);

    private static NonSeekableStream Unseekable() => new(_message);

    private static void AssertAllIdentical(FakeSpamd spamd, string command)
    {
        Assert.That(spamd.Requests, Has.Count.EqualTo(8));
        Assert.That(spamd.Requests, Has.All.EqualTo(spamd.Requests[0]));
        Assert.That(spamd.LastRequestText, Does.StartWith($"{command} SPAMC/1.5\r\nContent-length: {_message.Length}\r\n"));
        Assert.That(spamd.LastRequestText, Does.EndWith(Encoding.ASCII.GetString(_message)));
    }
}
