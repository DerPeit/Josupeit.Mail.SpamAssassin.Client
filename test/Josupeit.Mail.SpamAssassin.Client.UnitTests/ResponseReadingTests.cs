// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

// The reader's buffer arithmetic is the most error-prone code in the library and the least visible: get it wrong and a
// response is silently truncated or a header is read twice. These tests force each path with a buffer small enough to
// make it happen.
[TestFixture]
public sealed class ResponseReadingTests
{
    private static readonly byte[] _message = Encoding.ASCII.GetBytes(
        "From: Bertha Buffer <bertha.buffer@example.com>\r\nSubject: mind the boundary\r\n\r\nExactly one byte too many.\r\n");

    [Test]
    public void A_header_block_larger_than_the_buffer_makes_the_buffer_grow()
    {
        var padding = string.Concat(Enumerable.Range(0, 40).Select(index => $"X-Padding-{index:D2}: {new string('p', 100)}\r\n"));

        var client = new SpamAssassinClient(
            new FakeSpamd($"SPAMD/1.1 0 EX_OK\r\n{padding}Spam: True ; 8.0 / 5.0\r\n\r\n"),
            new() { TransferBufferSize = 512, MaxResponseHeadLength = 64 * 1024 });

        var verdict = client.Scan(_message);

        Assert.That(verdict.IsSpam, Is.True);
        Assert.That(verdict.Score, Is.EqualTo(8.0));
    }

    // The last piece brings the terminator plus the first bytes of the body, so the reader has to keep what it has not
    // consumed.
    [Test]
    public void A_head_that_arrives_in_pieces_is_read_and_the_body_behind_it_survives()
    {
        const string REPORT = "The report that follows the header block in the very same read.";

        var padding = string.Concat(Enumerable.Range(0, 6).Select(index => $"X-Padding-{index}: {new string('p', 60)}\r\n"));

        var client = new SpamAssassinClient(
            new FakeSpamd($"SPAMD/1.1 0 EX_OK\r\n{padding}Content-length: {REPORT.Length}\r\nSpam: True ; 8.0 / 5.0\r\n\r\n{REPORT}"),
            new() { TransferBufferSize = 512 });

        var result = client.Report(_message);

        Assert.That(result.Report, Is.EqualTo(REPORT));
    }

    [Test]
    public void A_text_response_spanning_several_pooled_blocks_is_decoded_whole()
    {
        var report = string.Concat(Enumerable.Range(0, 4000).Select(index => $"line {index:D4} of a report that will not fit in one block\r\n"));

        var client = new SpamAssassinClient(
            new FakeSpamd($"SPAMD/1.1 0 EX_OK\r\nContent-length: {report.Length}\r\nSpam: True ; 12.0 / 5.0\r\n\r\n{report}"),
            new() { TransferBufferSize = 4096, MaxTextResponseLength = 4 * 1024 * 1024 });

        var result = client.Report(_message);

        Assert.That(result.Report, Has.Length.EqualTo(report.Length));
        Assert.That(result.Report, Is.EqualTo(report));
    }

    [Test]
    public void A_report_is_decoded_with_the_encoding_the_options_name()
    {
        var body = new byte[] { 0xE4, 0x2D, 0xE9 };
        var response = Encoding.ASCII.GetBytes($"SPAMD/1.1 0 EX_OK\r\nContent-length: {body.Length}\r\nSpam: False ; 1.0 / 5.0\r\n\r\n").Concat(body).ToArray();

        var client = new SpamAssassinClient(new FakeSpamd(response), new() { TextEncoding = Encoding.Latin1 });

        var result = client.Report(_message);

        Assert.That(result.Report, Is.EqualTo("ä-é"));
    }

    [Test]
    public void A_blank_line_among_the_headers_is_stepped_over()
    {
        var client = new SpamAssassinClient(new FakeSpamd("SPAMD/1.1 0 EX_OK\r\n \r\nSpam: True ; 6.0 / 5.0\r\n\r\n"));

        var verdict = client.Scan(_message);

        Assert.That(verdict.IsSpam, Is.True);
    }

    [Test]
    public void A_header_without_a_colon_is_reported_as_malformed()
    {
        var client = new SpamAssassinClient(new FakeSpamd("SPAMD/1.1 0 EX_OK\r\nthis is not a header\r\nSpam: True ; 6.0 / 5.0\r\n\r\n"));

        Assert.Throws<MalformedResponseSpamAssassinException>(() => client.Scan(_message));
    }

    [TestCase("Content-length: quite long")]
    [TestCase("Content-length: -12")]
    public void A_length_that_is_not_a_length_is_reported_as_malformed(string header)
    {
        var client = new SpamAssassinClient(new FakeSpamd($"SPAMD/1.1 0 EX_OK\r\n{header}\r\nSpam: True ; 6.0 / 5.0\r\n\r\n"));

        Assert.Throws<MalformedResponseSpamAssassinException>(() => client.Scan(_message));
    }

    // A status line may stop after the code: the text that usually follows it is a courtesy, not a field.
    [Test]
    public void A_status_line_with_nothing_after_the_code_is_read()
    {
        var client = new SpamAssassinClient(new FakeSpamd("SPAMD/1.1 0\r\nSpam: True ; 6.0 / 5.0\r\n\r\n"));

        var verdict = client.Scan(_message);

        Assert.That(verdict.IsSpam, Is.True);
    }

    [Test]
    public void A_refusal_with_nothing_after_the_code_still_carries_the_code()
    {
        var client = new SpamAssassinClient(new FakeSpamd("SPAMD/1.0 76\r\n"));

        var refusal = Assert.Throws<ErrorResponseSpamAssassinException>(() => client.Scan(_message))!;

        Assert.That(refusal.Code, Is.EqualTo(SpamAssassinResponseCode.Protocol));
        Assert.That(refusal.ResponseText, Is.Empty);
    }

    [Test]
    public void A_header_the_client_does_not_know_is_ignored_rather_than_refused()
    {
        var client = new SpamAssassinClient(new FakeSpamd("SPAMD/1.1 0 EX_OK\r\nX-Something-New: from a later daemon\r\nSpam: True ; 6.0 / 5.0\r\n\r\n"));

        var verdict = client.Scan(_message);

        Assert.That(verdict.IsSpam, Is.True);
    }

    [Test]
    public void A_stream_that_cannot_be_read_is_refused_even_with_a_length()
    {
        var client = new SpamAssassinClient(new FakeSpamd("SPAMD/1.1 0 EX_OK\r\nSpam: False ; 0.0 / 5.0\r\n\r\n"));

        using var unreadable = new MemoryStream(new byte[8]);

        unreadable.Close();

        var refused = Assert.Throws<ArgumentException>(() => client.Scan(unreadable, 8))!;

        Assert.That(refused.ParamName, Is.EqualTo("message"));
    }

    // Compression reads the message before anything is sent, so a failure there happens with a pooled stream already
    // in hand. What these two assert is only that the failure reaches the caller as itself. That the pooled stream is
    // also let go of is the catch in RequestWriter.Compress, and nothing here can watch it happen: the
    // RecyclableMemoryStreamManager whose events would say so is private to the client.
    [Test]
    public void A_message_that_fails_while_being_compressed_reports_the_failure()
    {
        var client = new SpamAssassinClient(
            new FakeSpamd("SPAMD/1.1 0 EX_OK\r\nSpam: False ; 0.0 / 5.0\r\n\r\n"),
            new() { CompressRequests = true });

        Assert.Throws<IOException>(() => client.Scan(new FailingStream(_message.Length), _message.Length));
    }

    [Test]
    public void A_message_that_fails_while_being_compressed_reports_the_failure_when_awaited()
    {
        var client = new SpamAssassinClient(
            new FakeSpamd("SPAMD/1.1 0 EX_OK\r\nSpam: False ; 0.0 / 5.0\r\n\r\n"),
            new() { CompressRequests = true });

        Assert.ThrowsAsync<IOException>(async () => await client.ScanAsync(new FailingStream(_message.Length), _message.Length));
    }

    // A stream that claims a length and then refuses to hand over the bytes.
    private sealed class FailingStream(long length) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("The message went away.");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
