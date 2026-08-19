// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Text;

namespace Josupeit.Mail.SpamAssassin.Client.IntegrationTests;

// The two messages every test here needs, and a marker to find them by afterwards.
//
// Both are built fresh on every call, and the Guid in the Message-ID is load-bearing rather than decoration: it is
// what keeps the Bayes tests from seeing each other's work. Two of them learn a message and forget it again, and a
// third asserts that learning something new reports a change - which it would not, if "something new" were the same
// message the earlier tests had already fed the daemon. Take the Guid out and those failures are hard to explain.
internal static class TestMessages
{
    public const string BodyMarker = "The body a rewritten message still has to contain.";

    // Scores 1000 by rule, so a spam verdict is certain without inventing something that merely looks like spam.
    // Documented at https://spamassassin.apache.org/gtube/.
    private const string GTUBE = "XJS*C4JDBQADN1.NSBN3*2IDNEN*GTUBE-STANDARD-ANTI-UBE-TEST-EMAIL*C.34X";

    public static byte[] Spam() => Encoding.ASCII.GetBytes(
        $"""
        From: Ivor Inbox <ivor.inbox@example.com>
        To: Wendy Whitelist <wendy.whitelist@example.com>
        Subject: an offer no rule set can refuse
        Message-ID: <{Guid.NewGuid():N}@example.com>
        Date: Wed, 19 Aug 2026 09:12:00 +0200

        {GTUBE}

        {BodyMarker}

        """.ReplaceLineEndings("\r\n"));

    public static byte[] Ham() => Encoding.ASCII.GetBytes(
        $"""
        From: Prudence Plaintext <prudence.plaintext@example.com>
        To: Wendy Whitelist <wendy.whitelist@example.com>
        Subject: minutes of the tuesday meeting
        Message-ID: <{Guid.NewGuid():N}@example.com>
        Date: Wed, 19 Aug 2026 09:14:00 +0200

        Attached below, as agreed. Nothing was decided, as usual.

        {BodyMarker}

        """.ReplaceLineEndings("\r\n"));
}
