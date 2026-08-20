// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

[TestFixture]
public sealed class RequestWireTests
{
    private const string OK = "SPAMD/1.1 0 EX_OK\r\nContent-length: 0\r\nSpam: False ; 0.4 / 5.0\r\n\r\n";

    private static readonly byte[] _message = Encoding.ASCII.GetBytes(
        "From: Rita Regression <rita.regression@example.com>\r\nSubject: still green\r\n\r\nAll tests pass. Suspicious.\r\n");

    [Test]
    public void Scan_sends_a_check_request_carrying_the_message_and_its_length()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd);

        client.Scan(new MemoryStream(_message));

        Assert.That(spamd.LastRequestText, Is.EqualTo($"CHECK SPAMC/1.5\r\nContent-length: {_message.Length}\r\n\r\n{Encoding.ASCII.GetString(_message)}"));
    }

    [Test]
    public void The_user_is_announced_when_the_options_name_one()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd, new() { User = "rita_regression" });

        client.Scan(_message);

        Assert.That(Head(spamd.LastRequest), Is.EqualTo($"CHECK SPAMC/1.5\r\nContent-length: {_message.Length}\r\nUser: rita_regression"));
    }

    [Test]
    public void The_announced_protocol_version_is_the_one_the_options_ask_for()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd, new() { ProtocolVersion = new(1, 3) });

        client.Scan(_message);

        Assert.That(spamd.LastRequestText, Does.StartWith("CHECK SPAMC/1.3\r\n"));
    }

    [Test]
    public void Ping_sends_the_command_and_the_blank_line_and_no_length()
    {
        var spamd = new FakeSpamd("SPAMD/1.5 0 PONG\r\n");
        var client = new SpamAssassinClient(spamd);

        var version = client.Ping();

        Assert.That(spamd.LastRequestText, Is.EqualTo("PING SPAMC/1.5\r\n\r\n"));
        Assert.That(version, Is.EqualTo(new Version(1, 5)));
    }

    [Test]
    public void Skip_sends_the_command_and_expects_nothing_back()
    {
        var spamd = new FakeSpamd([]);
        var client = new SpamAssassinClient(spamd);

        client.Skip();

        Assert.That(spamd.LastRequestText, Is.EqualTo("SKIP SPAMC/1.5\r\n\r\n"));
        Assert.That(spamd.Connections, Is.EqualTo(1));
    }

    [Test]
    public async Task Both_colours_write_the_same_bytes()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd);

        client.Scan(new MemoryStream(_message));
        await client.ScanAsync(new MemoryStream(_message));

        Assert.That(spamd.Requests, Has.Count.EqualTo(2));
        Assert.That(spamd.Requests[1], Is.EqualTo(spamd.Requests[0]));
    }

    [Test]
    public void All_four_ways_of_handing_over_a_message_write_the_same_bytes()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd);

        client.Scan(new MemoryStream(_message));
        client.Scan(new NonSeekableStream(_message), _message.Length);
        client.Scan(new ReadOnlySequence<byte>(_message));
        client.Scan(_message.AsMemory());

        Assert.That(spamd.Requests, Has.Count.EqualTo(4));
        Assert.That(spamd.Requests, Has.All.EqualTo(spamd.Requests[0]));
    }

    [Test]
    public void A_message_in_several_segments_arrives_as_one()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd);

        client.Scan(SegmentedSequence(_message, 7));

        Assert.That(Body(spamd.LastRequest), Is.EqualTo(_message));
    }

    [Test]
    public void Compression_announces_zlib_and_the_length_of_the_compressed_body()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd, new() { CompressRequests = true });

        client.Scan(_message);

        var body = Body(spamd.LastRequest);

        Assert.That(Head(spamd.LastRequest), Is.EqualTo($"CHECK SPAMC/1.5\r\nContent-length: {body.Length}\r\nCompress: zlib"));
        Assert.That(Inflate(body), Is.EqualTo(_message));
    }

    // The head starts in a 256-byte buffer, and a long user name is the shortest way past it.
    [Test]
    public void A_head_larger_than_its_first_buffer_is_written_whole()
    {
        var user = new string('u', 400);

        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd, new() { User = user });

        client.Scan(_message);

        Assert.That(Head(spamd.LastRequest), Is.EqualTo($"CHECK SPAMC/1.5\r\nContent-length: {_message.Length}\r\nUser: {user}"));
        Assert.That(Body(spamd.LastRequest), Is.EqualTo(_message));
    }

    [Test]
    public void A_connection_is_taken_and_disposed_for_every_command()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd);

        client.Scan(_message);
        client.Scan(_message);

        Assert.That(spamd.Connections, Is.EqualTo(2));
        Assert.That(spamd.Disposals, Is.EqualTo(2));
        Assert.That(spamd.Completions, Is.EqualTo(2));
    }

    [Test]
    public void The_delegate_constructor_reaches_the_synchronous_delegate_for_a_blocking_call()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd.Connect, spamd.ConnectAsync);

        client.Scan(_message);

        Assert.That(spamd.LastConnectionWasAsynchronous, Is.False);
    }

    [Test]
    public async Task The_delegate_constructor_reaches_the_asynchronous_delegate_for_an_awaited_call()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd.Connect, spamd.ConnectAsync);

        await client.ScanAsync(_message);

        Assert.That(spamd.LastConnectionWasAsynchronous, Is.True);
    }

    private static string Head(byte[] request) => Encoding.ASCII.GetString(request, 0, Separator(request));

    private static byte[] Body(byte[] request) => request[(Separator(request) + 4)..];

    private static int Separator(byte[] request) => request.AsSpan().IndexOf("\r\n\r\n"u8);

    private static ReadOnlySequence<byte> SegmentedSequence(byte[] content, int segmentLength)
    {
        var first = new Segment(content.AsMemory(0, segmentLength));
        var last = first;

        for (var offset = segmentLength; offset < content.Length; offset += segmentLength)
            last = last.Append(content.AsMemory(offset, Math.Min(segmentLength, content.Length - offset)));

        return new(first, 0, last, last.Memory.Length);
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var source = new MemoryStream(compressed);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        zlib.CopyTo(inflated);

        return inflated.ToArray();
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };

            Next = next;

            return next;
        }
    }
}
