// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

[TestFixture]
public sealed class TellTests
{
    // The daemon terminates a tell response's header block twice over, and a reader that insists on exactly one blank
    // line breaks against a real spamd.
    private const string DID_NOTHING = "SPAMD/1.1 0 EX_OK\r\n\r\n\r\n";

    private static readonly byte[] _message = Encoding.ASCII.GetBytes(
        "From: Hammy Hamilton <hammy.hamilton@example.com>\r\nSubject: definitely not spam\r\n\r\nBuy nothing.\r\n");

    [Test]
    public void Learning_as_spam_declares_the_class_and_writes_locally()
    {
        var spamd = new FakeSpamd(DID_NOTHING);
        var client = new SpamAssassinClient(spamd);

        client.Tell(TellInstruction.LearnAsSpam, _message);

        Assert.That(spamd.LastRequestText, Does.StartWith($"TELL SPAMC/1.5\r\nContent-length: {_message.Length}\r\nMessage-class: spam\r\nSet: local\r\n\r\n"));
    }

    [Test]
    public void Learning_as_ham_declares_the_other_class()
    {
        var spamd = new FakeSpamd(DID_NOTHING);
        var client = new SpamAssassinClient(spamd);

        client.Tell(TellInstruction.LearnAsHam, _message);

        Assert.That(spamd.LastRequestText, Does.Contain("Message-class: ham\r\nSet: local\r\n"));
    }

    [Test]
    public void Forgetting_names_no_class_and_removes_locally()
    {
        var spamd = new FakeSpamd(DID_NOTHING);
        var client = new SpamAssassinClient(spamd);

        client.Tell(TellInstruction.Forget, _message);

        Assert.That(spamd.LastRequestText, Does.Contain("Remove: local\r\n").And.Not.Contains("Message-class"));
    }

    [Test]
    public void Reporting_as_spam_writes_to_both_databases()
    {
        var spamd = new FakeSpamd(DID_NOTHING);
        var client = new SpamAssassinClient(spamd);

        client.Tell(TellInstruction.ReportAsSpam, _message);

        Assert.That(spamd.LastRequestText, Does.Contain("Message-class: spam\r\nSet: local,remote\r\n"));
    }

    [Test]
    public void Revoking_as_ham_writes_locally_and_withdraws_remotely()
    {
        var spamd = new FakeSpamd(DID_NOTHING);
        var client = new SpamAssassinClient(spamd);

        client.Tell(TellInstruction.RevokeAsHam, _message);

        Assert.That(spamd.LastRequestText, Does.Contain("Message-class: ham\r\nSet: local\r\nRemove: remote\r\n"));
    }

    [Test]
    public void What_the_daemon_changed_is_reported_back()
    {
        var client = new SpamAssassinClient(new FakeSpamd("SPAMD/1.1 0 EX_OK\r\nDidSet: local,remote\r\nDidRemove: local\r\n\r\n\r\n"));

        var result = client.Tell(TellInstruction.ReportAsSpam, _message);

        Assert.That(result.Set, Is.EqualTo(TellDatabases.Local | TellDatabases.Remote));
        Assert.That(result.Removed, Is.EqualTo(TellDatabases.Local));
    }

    // A daemon with Bayes switched off answers successfully and changes nothing, and that is not an error.
    [Test]
    public void A_daemon_that_did_nothing_says_so_without_failing()
    {
        var client = new SpamAssassinClient(new FakeSpamd(DID_NOTHING));

        var result = client.Tell(TellInstruction.LearnAsSpam, _message);

        Assert.That(result.Set, Is.EqualTo(TellDatabases.None));
        Assert.That(result.Removed, Is.EqualTo(TellDatabases.None));
    }

    [Test]
    public async Task The_awaited_path_tells_the_daemon_the_same_thing()
    {
        var spamd = new FakeSpamd(DID_NOTHING);
        var client = new SpamAssassinClient(spamd);

        client.Tell(TellInstruction.LearnAsSpam, _message);
        await client.TellAsync(TellInstruction.LearnAsSpam, _message);

        Assert.That(spamd.Requests[1], Is.EqualTo(spamd.Requests[0]));
    }

    [Test]
    public void A_tell_the_daemon_does_not_allow_surfaces_as_the_refusal_it_is()
    {
        var client = new SpamAssassinClient(new FakeSpamd("SPAMD/1.0 69 Service Unavailable: TELL commands are not enabled, set the --allow-tell switch.\r\n"));

        var refusal = Assert.Throws<ErrorResponseSpamAssassinException>(() => client.Tell(TellInstruction.LearnAsSpam, _message))!;

        Assert.That(refusal.Code, Is.EqualTo(SpamAssassinResponseCode.Unavailable));
        Assert.That(refusal.ResponseText, Does.Contain("--allow-tell"));
    }
}
