// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

[TestFixture]
public sealed class ArgumentTests
{
    private const string OK = "SPAMD/1.1 0 EX_OK\r\nContent-length: 0\r\nSpam: False ; 0.1 / 5.0\r\n\r\n";

    private static readonly byte[] _message = Encoding.ASCII.GetBytes(
        "From: Nully McNullface <nully.mcnullface@example.com>\r\nSubject: nothing here\r\n\r\n");

    [Test]
    public void A_missing_message_is_refused_by_the_name_the_caller_used()
    {
        var client = Client();

        var missing = Assert.Throws<ArgumentNullException>(() => client.Scan((Stream)null!))!;

        Assert.That(missing.ParamName, Is.EqualTo("message"));
    }

    [Test]
    public void A_stream_that_cannot_seek_is_refused_with_an_explanation()
    {
        var client = Client();

        var refused = Assert.Throws<ArgumentException>(() => client.Scan(new NonSeekableStream(_message)))!;

        Assert.That(refused.ParamName, Is.EqualTo("message"));
        Assert.That(refused.Message, Does.Contain("length"));
    }

    [Test]
    public void A_stream_that_cannot_be_read_is_refused()
    {
        var client = Client();

        using var unreadable = new MemoryStream(new byte[8], writable: true);

        unreadable.Close();

        Assert.Throws<ArgumentException>(() => client.Scan(unreadable));
    }

    [Test]
    public void A_negative_length_is_refused()
    {
        var client = Client();

        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => client.Scan(new MemoryStream(_message), -1))!;

        Assert.That(refused.ParamName, Is.EqualTo("length"));
    }

    [Test]
    public void A_missing_destination_is_refused_by_the_name_the_caller_used()
    {
        var client = Client();

        var missing = Assert.Throws<ArgumentNullException>(() => client.Process(_message, null!))!;

        Assert.That(missing.ParamName, Is.EqualTo("destination"));
    }

    [Test]
    public void A_destination_that_cannot_be_written_to_is_refused()
    {
        var client = Client();

        using var readOnly = new MemoryStream(new byte[8], writable: false);

        var refused = Assert.Throws<ArgumentException>(() => client.Process(_message, readOnly))!;

        Assert.That(refused.ParamName, Is.EqualTo("destination"));
    }

    [Test]
    public void A_missing_instruction_is_refused()
    {
        var client = Client();

        var missing = Assert.Throws<ArgumentNullException>(() => client.Tell(null!, _message))!;

        Assert.That(missing.ParamName, Is.EqualTo("instruction"));
    }

    [Test]
    public void The_message_is_read_from_where_the_caller_left_the_stream()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd);

        var stream = new MemoryStream(_message) { Position = 6 };

        client.Scan(stream);

        Assert.That(spamd.LastRequestText, Does.Contain($"Content-length: {_message.Length - 6}\r\n"));
        Assert.That(spamd.LastRequestText, Does.EndWith(Encoding.ASCII.GetString(_message, 6, _message.Length - 6)));
    }

    [Test]
    public void An_empty_message_is_sent_as_one_rather_than_as_no_message_at_all()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd);

        client.Scan(ReadOnlyMemory<byte>.Empty);

        Assert.That(spamd.LastRequestText, Is.EqualTo("CHECK SPAMC/1.5\r\nContent-length: 0\r\n\r\n"));
    }

    [Test]
    public void A_client_without_a_connection_source_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new SpamAssassinClient((ISpamAssassinConnectionSource)null!));
    }

    [Test]
    public void A_client_needs_both_connection_delegates()
    {
        var spamd = new FakeSpamd(OK);

        Assert.Throws<ArgumentNullException>(() => new SpamAssassinClient(null!, spamd.ConnectAsync));
        Assert.Throws<ArgumentNullException>(() => new SpamAssassinClient(spamd.Connect, null!));
    }

    // The blocking half takes no token: one cannot interrupt a read already in flight.
    [Test]
    public void A_cancelled_token_stops_the_command_before_a_connection_is_taken()
    {
        var spamd = new FakeSpamd(OK);
        var client = new SpamAssassinClient(spamd);

        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await client.ScanAsync(_message, cancellation.Token));
        Assert.That(spamd.Connections, Is.Zero);
    }

    private static SpamAssassinClient Client() => new(new FakeSpamd(OK));
}
