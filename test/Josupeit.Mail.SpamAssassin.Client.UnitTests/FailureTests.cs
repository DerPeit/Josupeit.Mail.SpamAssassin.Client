// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

[TestFixture]
public sealed class FailureTests
{
    // Written out as a literal it would be invisible in this file and would drive whatever terminal printed it.
    private const char ESCAPE = (char)0x1b;

    private static readonly byte[] _message = Encoding.ASCII.GetBytes(
        "From: Trudy Truncated <trudy.truncated@example.com>\r\nSubject: cut off mid-\r\n\r\n");

    // An error response is one line and then a closed connection, so reading it at all is what turns the refusal into a
    // response code rather than a truncation complaint.
    [Test]
    public void A_refusal_arrives_as_its_code_and_its_text()
    {
        var client = Client("SPAMD/1.0 76 Bad header line: (Content-Length mismatch)\r\n");

        var refusal = Assert.Throws<ErrorResponseSpamAssassinException>(() => client.Scan(_message))!;

        Assert.That(refusal.Code, Is.EqualTo(SpamAssassinResponseCode.Protocol));
        Assert.That(refusal.ResponseText, Is.EqualTo("Bad header line: (Content-Length mismatch)"));
    }

    // Headers are decoded as Latin-1, which cannot fail, so every control byte the daemon sent survives into a string
    // that ends up in a log or on a terminal. A bare CR there forges a log line and an escape sequence drives the
    // terminal, so what comes out is escaped rather than passed on.
    [Test]
    public void Control_characters_in_a_refusal_are_escaped_rather_than_passed_on()
    {
        var client = Client($"SPAMD/1.0 76 Refused{ESCAPE}[2J\rforged: ok\r\n");

        var refusal = Assert.Throws<ErrorResponseSpamAssassinException>(() => client.Scan(_message))!;

        Assert.That(refusal.ResponseText, Is.EqualTo("Refused\\x1b[2J\\x0dforged: ok"));
        Assert.That(refusal.Message.Contains(ESCAPE), Is.False);
    }

    [Test]
    public void Control_characters_in_a_malformed_header_are_escaped_rather_than_passed_on()
    {
        var client = Client($"SPAMD/1.1 0 EX_OK\r\nno colon here{ESCAPE}[2J\r\n\r\n");

        var malformed = Assert.Throws<MalformedResponseSpamAssassinException>(() => client.Scan(_message))!;

        Assert.That(malformed.Message, Does.Contain("no colon here\\x1b[2J"));
        Assert.That(malformed.Message.Contains(ESCAPE), Is.False);
    }

    // NumberStyles.Float does not stop TryParse accepting these, and a score no caller can compare is worse than a
    // refusal to read the response at all.
    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("-Infinity")]
    public void A_score_that_is_not_a_finite_number_is_reported_as_malformed(string score)
    {
        var client = Client($"SPAMD/1.1 0 EX_OK\r\nSpam: True ; {score} / 5.0\r\n\r\n");

        Assert.Throws<MalformedResponseSpamAssassinException>(() => client.Scan(_message));
    }

    [Test]
    public void A_code_the_enumeration_does_not_declare_is_still_reported()
    {
        var client = Client("SPAMD/1.0 123 Something New\r\n");

        var refusal = Assert.Throws<ErrorResponseSpamAssassinException>(() => client.Scan(_message))!;

        Assert.That((int)refusal.Code, Is.EqualTo(123));
    }

    [Test]
    public void A_daemon_that_says_nothing_at_all_is_a_truncated_response()
    {
        var client = Client(string.Empty);

        var truncated = Assert.Throws<TruncatedResponseSpamAssassinException>(() => client.Scan(_message))!;

        Assert.That(truncated.ReceivedLength, Is.Zero);
        Assert.That(truncated.AnnouncedLength, Is.Null);
    }

    [Test]
    public void A_body_shorter_than_announced_is_a_truncated_response()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nContent-length: 400\r\nSpam: False ; 0.0 / 5.0\r\n\r\nonly this much");

        using var destination = new MemoryStream();

        var truncated = Assert.Throws<TruncatedResponseSpamAssassinException>(() => client.Process(_message, destination))!;

        Assert.That(truncated.AnnouncedLength, Is.EqualTo(400));
        Assert.That(truncated.ReceivedLength, Is.EqualTo(14));
    }

    [TestCase("HTTP/1.1 200 OK\r\n\r\n")]
    [TestCase("SPAMD/1.1\r\n\r\n")]
    [TestCase("SPAMD/one 0 EX_OK\r\n\r\n")]
    [TestCase("SPAMD/1.1 not-a-code EX_OK\r\n\r\n")]
    public void Something_that_is_not_a_response_is_reported_as_malformed(string response)
    {
        var client = Client(response);

        Assert.Throws<MalformedResponseSpamAssassinException>(() => client.Scan(_message));
    }

    [TestCase("SPAMD/1.1 0 EX_OK\r\nSpam: True 15.3 / 5.0\r\n\r\n")]
    [TestCase("SPAMD/1.1 0 EX_OK\r\nSpam: True ; 15.3\r\n\r\n")]
    [TestCase("SPAMD/1.1 0 EX_OK\r\nSpam: Perhaps ; 15.3 / 5.0\r\n\r\n")]
    [TestCase("SPAMD/1.1 0 EX_OK\r\nSpam: True ; high / 5.0\r\n\r\n")]
    public void A_spam_header_that_does_not_parse_is_reported_as_malformed(string response)
    {
        var client = Client(response);

        Assert.Throws<MalformedResponseSpamAssassinException>(() => client.Scan(_message));
    }

    [Test]
    public void A_scan_answered_without_a_verdict_is_reported_as_malformed()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nContent-length: 0\r\n\r\n");

        Assert.Throws<MalformedResponseSpamAssassinException>(() => client.Scan(_message));
    }

    [Test]
    public void A_header_block_that_never_ends_is_stopped_at_the_configured_limit()
    {
        var padding = Enumerable.Range(0, 200).Select(index => $"X-Padding-{index}: something a daemon would never send\r\n");
        var endless = "SPAMD/1.1 0 EX_OK\r\n" + string.Concat(padding) + "Spam: False ; 0.0 / 5.0\r\n";
        var client = new SpamAssassinClient(new FakeSpamd(endless), new() { MaxResponseHeadLength = 512 });

        var tooLarge = Assert.Throws<ResponseTooLargeSpamAssassinException>(() => client.Scan(_message))!;

        Assert.That(tooLarge.Limit, Is.EqualTo(512));
    }

    [Test]
    public void A_text_body_larger_than_allowed_is_refused_before_it_is_read()
    {
        var client = new SpamAssassinClient(
            new FakeSpamd("SPAMD/1.1 0 EX_OK\r\nContent-length: 5000\r\nSpam: True ; 9.0 / 5.0\r\n\r\n" + new string('r', 5000)),
            new() { MaxTextResponseLength = 1024 });

        var tooLarge = Assert.Throws<ResponseTooLargeSpamAssassinException>(() => client.Report(_message))!;

        Assert.That(tooLarge.Limit, Is.EqualTo(1024));
    }

    [Test]
    public void A_text_body_larger_than_allowed_is_stopped_even_when_no_length_was_announced()
    {
        var client = new SpamAssassinClient(
            new FakeSpamd("SPAMD/1.1 0 EX_OK\r\nSpam: True ; 9.0 / 5.0\r\n\r\n" + new string('r', 5000)),
            new() { MaxTextResponseLength = 1024 });

        Assert.Throws<ResponseTooLargeSpamAssassinException>(() => client.Report(_message));
    }

    [Test]
    public void A_message_shorter_than_the_length_it_was_given_fails_before_the_daemon_can_object()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nSpam: False ; 0.0 / 5.0\r\n\r\n");

        Assert.Throws<EndOfStreamException>(() => client.Scan(new MemoryStream(_message), _message.Length + 100));
    }

    [Test]
    public void Every_failure_is_a_SpamAssassinException()
    {
        var client = Client("SPAMD/1.0 75 Timeout: something took too long\r\n");

        Assert.Throws<ErrorResponseSpamAssassinException>(() => client.Scan(_message));
        Assert.That(() => client.Scan(_message), Throws.InstanceOf<SpamAssassinException>());
    }

    [Test]
    public void The_awaited_path_fails_the_same_way()
    {
        var client = Client("SPAMD/1.0 69 Service Unavailable: nope\r\n");

        Assert.ThrowsAsync<ErrorResponseSpamAssassinException>(async () => await client.ScanAsync(_message));
    }

    private static SpamAssassinClient Client(string response) => new(new FakeSpamd(response));
}
