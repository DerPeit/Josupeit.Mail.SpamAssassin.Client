// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

[TestFixture]
public sealed class ScanResponseTests
{
    private static readonly byte[] _message = Encoding.ASCII.GetBytes(
        "From: Barry Bayes <barry.bayes@example.com>\r\nSubject: probabilities\r\n\r\nNinety-nine percent certain.\r\n");

    [Test]
    public void A_spam_verdict_carries_the_score_and_the_threshold()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nSpam: True ; 15.3 / 5.0\r\n\r\n");

        var verdict = client.Scan(_message);

        Assert.That(verdict.IsSpam, Is.True);
        Assert.That(verdict.Score, Is.EqualTo(15.3));
        Assert.That(verdict.Threshold, Is.EqualTo(5.0));
    }

    [Test]
    public void A_clean_verdict_is_read_as_one()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nSpam: False ; -1.2 / 5.0\r\n\r\n");

        var verdict = client.Scan(_message);

        Assert.That(verdict.IsSpam, Is.False);
        Assert.That(verdict.Score, Is.EqualTo(-1.2));
    }

    // The one command that spells the flag differently: spamd answers a report-if-spam request with Yes or No.
    [TestCase("Yes", true)]
    [TestCase("No", false)]
    [TestCase("true", true)]
    [TestCase("FALSE", false)]
    public void The_verdict_flag_is_read_in_every_spelling_the_daemon_uses(string flag, bool expected)
    {
        var client = Client($"SPAMD/1.1 0 EX_OK\r\nContent-length: 0\r\nSpam: {flag} ; 8.0 / 5.0\r\n\r\n");

        var result = client.ReportIfSpam(_message);

        Assert.That(result.Verdict.IsSpam, Is.EqualTo(expected));
    }

    [Test]
    [SetCulture("de-DE")]
    public void The_score_is_read_with_the_invariant_culture()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nSpam: False ; 2.6 / 5.0\r\n\r\n");

        var verdict = client.Scan(_message);

        Assert.That(verdict.Score, Is.EqualTo(2.6));
    }

    [Test]
    public void The_rules_that_hit_come_back_as_symbols()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nContent-length: 24\r\nSpam: True ; 1002.5 / 5.0\r\n\r\nGTUBE,NO_RELAYS,BAYES_99");

        var result = client.Symbols(_message);

        Assert.That(result.Symbols, Is.EqualTo(new[] { "GTUBE", "NO_RELAYS", "BAYES_99" }));
        Assert.That(result.Verdict.IsSpam, Is.True);
    }

    // Older daemons terminate the line, newer ones do not, and how many line breaks follow is not defined either.
    [TestCase("GTUBE,BAYES_99")]
    [TestCase("GTUBE,BAYES_99\r\n")]
    [TestCase("GTUBE, BAYES_99\r\n\r\n")]
    public void A_symbols_line_is_read_whether_or_not_it_is_terminated(string body)
    {
        var client = Client($"SPAMD/1.1 0 EX_OK\r\nSpam: True ; 1002.5 / 5.0\r\n\r\n{body}");

        var result = client.Symbols(_message);

        Assert.That(result.Symbols, Is.EqualTo(new[] { "GTUBE", "BAYES_99" }));
    }

    [Test]
    public void No_rules_hitting_is_an_empty_list_rather_than_nothing()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nContent-length: 0\r\nSpam: False ; 0.0 / 5.0\r\n\r\n");

        var result = client.Symbols(_message);

        Assert.That(result.Symbols, Is.Empty);
    }

    [Test]
    public void A_report_comes_back_as_the_daemon_wrote_it()
    {
        const string REPORT = "Spam detection software has identified this message as spam.\r\n\r\n 1.2 MISSING_DATE Missing Date: header\r\n";

        var client = Client($"SPAMD/1.1 0 EX_OK\r\nContent-length: {REPORT.Length}\r\nSpam: True ; 6.1 / 5.0\r\n\r\n{REPORT}");

        var result = client.Report(_message);

        Assert.That(result.Report, Is.EqualTo(REPORT));
    }

    [Test]
    public void A_clean_message_earns_no_report_from_report_if_spam()
    {
        var client = Client("SPAMD/1.1 0 EX_OK\r\nContent-length: 0\r\nSpam: No ; 0.9 / 5.0\r\n\r\n");

        var result = client.ReportIfSpam(_message);

        Assert.That(result.Report, Is.Null);
        Assert.That(result.Verdict.IsSpam, Is.False);
    }

    [Test]
    public void A_rewritten_message_is_copied_into_the_destination()
    {
        const string REWRITTEN = "X-Spam-Flag: YES\r\nSubject: [SPAM] probabilities\r\n\r\nNinety-nine percent certain.\r\n";

        var client = Client($"SPAMD/1.1 0 EX_OK\r\nContent-length: {REWRITTEN.Length}\r\nSpam: True ; 9.9 / 5.0\r\n\r\n{REWRITTEN}");

        using var destination = new MemoryStream();

        var result = client.Process(_message, destination);

        Assert.That(Encoding.ASCII.GetString(destination.ToArray()), Is.EqualTo(REWRITTEN));
        Assert.That(result.Length, Is.EqualTo(REWRITTEN.Length));
        Assert.That(result.Verdict.IsSpam, Is.True);
    }

    [Test]
    public void Rewritten_headers_are_copied_into_the_destination()
    {
        const string REWRITTEN = "X-Spam-Status: No, score=0.9\r\n\r\n";

        var spamd = new FakeSpamd($"SPAMD/1.1 0 EX_OK\r\nContent-length: {REWRITTEN.Length}\r\nSpam: False ; 0.9 / 5.0\r\n\r\n{REWRITTEN}");
        var client = new SpamAssassinClient(spamd);

        using var destination = new MemoryStream();

        var result = client.Headers(_message, destination);

        Assert.That(spamd.LastRequestText, Does.StartWith("HEADERS SPAMC/1.5\r\n"));
        Assert.That(Encoding.ASCII.GetString(destination.ToArray()), Is.EqualTo(REWRITTEN));
        Assert.That(result.Length, Is.EqualTo(REWRITTEN.Length));
    }

    [Test]
    public void A_body_longer_than_one_read_is_copied_whole()
    {
        var rewritten = new string('.', 200_000);

        var client = Client($"SPAMD/1.1 0 EX_OK\r\nContent-length: {rewritten.Length}\r\nSpam: False ; 0.0 / 5.0\r\n\r\n{rewritten}");

        using var destination = new MemoryStream();

        var result = client.Process(_message, destination);

        Assert.That(result.Length, Is.EqualTo(rewritten.Length));
        Assert.That(destination.Length, Is.EqualTo(rewritten.Length));
    }

    [Test]
    public async Task The_awaited_path_reads_the_same_answer_as_the_blocking_one()
    {
        const string RESPONSE = "SPAMD/1.1 0 EX_OK\r\nContent-length: 14\r\nSpam: True ; 7.5 / 5.0\r\n\r\nGTUBE,BAYES_99";

        var blocking = Client(RESPONSE).Symbols(_message);
        var awaited = await Client(RESPONSE).SymbolsAsync(_message);

        Assert.That(awaited, Is.EqualTo(blocking));
    }

    private static SpamAssassinClient Client(string response) => new(new FakeSpamd(response));
}
