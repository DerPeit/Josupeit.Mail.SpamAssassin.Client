// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Text;

namespace Josupeit.Mail.SpamAssassin.Client.IntegrationTests;

// Asserts the one thing the unit tests cannot: that a real spamd agrees. Where a test asserts on a number, the number
// comes from a rule that ships with SpamAssassin - see TestMessages.
[TestFixture]
public sealed class SpamdTests : SpamdFixture
{
    [Test]
    public void The_daemon_answers_a_ping()
    {
        var client = CreateClient();

        var version = client.Ping();

        Assert.That(version.Major, Is.EqualTo(1));
        Assert.That(version.Minor, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void A_spam_message_comes_back_as_spam()
    {
        var client = CreateClient();

        var verdict = client.Scan(TestMessages.Spam());

        Assert.That(verdict.IsSpam, Is.True);
        Assert.That(verdict.Score, Is.GreaterThan(verdict.Threshold));
        Assert.That(verdict.Threshold, Is.EqualTo(5.0));
    }

    [Test]
    public void A_clean_message_comes_back_clean()
    {
        var client = CreateClient();

        var verdict = client.Scan(TestMessages.Ham());

        Assert.That(verdict.IsSpam, Is.False);
        Assert.That(verdict.Score, Is.LessThan(verdict.Threshold));
    }

    [Test]
    public async Task The_awaited_path_reaches_the_same_daemon()
    {
        var client = CreateClient();

        var verdict = await client.ScanAsync(TestMessages.Spam());

        Assert.That(verdict.IsSpam, Is.True);
    }

    [Test]
    public void The_rule_that_fired_is_named()
    {
        var client = CreateClient();

        var result = client.Symbols(TestMessages.Spam());

        Assert.That(result.Symbols, Contains.Item("GTUBE"));
        Assert.That(result.Verdict.IsSpam, Is.True);
    }

    [Test]
    public void A_report_explains_the_score()
    {
        var client = CreateClient();

        var result = client.Report(TestMessages.Spam());

        Assert.That(result.Report, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Report, Does.Contain("GTUBE"));
    }

    [Test]
    public void A_clean_message_earns_no_report_from_report_if_spam()
    {
        var client = CreateClient();

        var result = client.ReportIfSpam(TestMessages.Ham());

        Assert.That(result.Verdict.IsSpam, Is.False);
        Assert.That(result.Report, Is.Null);
    }

    [Test]
    public void A_spam_message_earns_one()
    {
        var client = CreateClient();

        var result = client.ReportIfSpam(TestMessages.Spam());

        Assert.That(result.Verdict.IsSpam, Is.True);
        Assert.That(result.Report, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void A_processed_message_comes_back_marked_up_and_intact()
    {
        var client = CreateClient();

        using var destination = new MemoryStream();

        var result = client.Process(TestMessages.Spam(), destination);

        var rewritten = Encoding.ASCII.GetString(destination.ToArray());

        Assert.That(rewritten, Does.Contain("X-Spam-"));
        Assert.That(rewritten, Does.Contain(TestMessages.BodyMarker));
        Assert.That(result.Length, Is.EqualTo(destination.Length));
        Assert.That(result.Verdict.IsSpam, Is.True);
    }

    [Test]
    public void The_headers_command_stops_at_the_body()
    {
        var client = CreateClient();

        using var destination = new MemoryStream();

        var result = client.Headers(TestMessages.Spam(), destination);

        var rewritten = Encoding.ASCII.GetString(destination.ToArray());

        Assert.That(rewritten, Does.Contain("X-Spam-"));
        Assert.That(rewritten, Does.Not.Contain(TestMessages.BodyMarker));
        Assert.That(result.Length, Is.EqualTo(destination.Length));
    }

    [Test]
    public void A_message_larger_than_one_buffer_survives_the_round_trip()
    {
        var client = CreateClient();

        var padding = string.Join("\r\n", Enumerable.Repeat("Filler that exists only to make this message long.", 4000));
        var message = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(TestMessages.Ham()) + padding + "\r\n");

        using var destination = new MemoryStream();

        var result = client.Process(message, destination);

        Assert.That(result.Length, Is.GreaterThan(message.Length));
        Assert.That(Encoding.ASCII.GetString(destination.ToArray()), Does.Contain(TestMessages.BodyMarker));
    }

    [Test]
    public void A_stream_that_cannot_seek_is_sent_with_the_length_the_caller_gives()
    {
        var client = CreateClient();

        var message = TestMessages.Spam();

        using var unseekable = new NonSeekableStream(message);

        var verdict = client.Scan(unseekable, message.Length);

        Assert.That(verdict.IsSpam, Is.True);
    }

    [Test]
    public void A_compressed_request_is_understood()
    {
        var client = CreateClient(new() { CompressRequests = true });

        var verdict = client.Scan(TestMessages.Spam());

        Assert.That(verdict.IsSpam, Is.True);
    }

    [Test]
    public void A_skipped_connection_costs_the_daemon_nothing()
    {
        var client = CreateClient();

        Assert.That(() => client.Skip(), Throws.Nothing);
        Assert.That(client.Scan(TestMessages.Ham()).IsSpam, Is.False);
    }

    // The reason the container runs with --allow-tell. Forgetting again puts the daemon back where it started, which is
    // what keeps this test from leaving anything behind for the others.
    [Test]
    public async Task A_message_can_be_learned_and_forgotten_again()
    {
        var client = CreateClient();
        var message = TestMessages.Ham();

        var learned = client.Tell(TellInstruction.LearnAsSpam, message);
        var forgotten = client.Tell(TellInstruction.Forget, message);

        // A daemon that cannot write its Bayes database answers successfully and changes nothing, so the log is the only
        // place the reason appears.
        Assert.That(learned.Set, Is.EqualTo(TellDatabases.Local), $"spamd learned nothing. Its log says:{Environment.NewLine}{await ReadSpamdLog()}");
        Assert.That(forgotten.Removed, Is.EqualTo(TellDatabases.Local));
    }

    [Test]
    public void Learning_the_same_message_twice_changes_nothing_the_second_time()
    {
        var client = CreateClient();
        var message = TestMessages.Ham();

        try
        {
            client.Tell(TellInstruction.LearnAsSpam, message);

            var again = client.Tell(TellInstruction.LearnAsSpam, message);

            Assert.That(again.Set, Is.EqualTo(TellDatabases.None));
        }
        finally
        {
            // Cleanup, not part of the act: in a finally so that a throw above still puts the daemon back.
            client.Tell(TellInstruction.Forget, message);
        }
    }

    [Test]
    public async Task The_awaited_path_learns_too()
    {
        var client = CreateClient();
        var message = TestMessages.Ham();

        try
        {
            var learned = await client.TellAsync(TellInstruction.LearnAsHam, message);

            Assert.That(learned.Set, Is.EqualTo(TellDatabases.Local));
        }
        finally
        {
            // Cleanup, not part of the act: in a finally so that a throw above still puts the daemon back.
            await client.TellAsync(TellInstruction.Forget, message);
        }
    }
}
