// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Text;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

[TestFixture]
public sealed class ClientOptionsTests
{
    [Test]
    public void The_defaults_announce_the_newest_protocol_and_no_user()
    {
        var options = new SpamAssassinClientOptions();

        Assert.That(options.ProtocolVersion, Is.EqualTo(new Version(1, 5)));
        Assert.That(options.User, Is.Null);
        Assert.That(options.CompressRequests, Is.False);
        Assert.That(options.TextEncoding, Is.EqualTo(Encoding.UTF8));
    }

    [Test]
    public void A_user_the_daemon_would_accept_is_kept()
    {
        var options = new SpamAssassinClientOptions { User = "quentin_quota-7" };

        Assert.That(options.User, Is.EqualTo("quentin_quota-7"));
    }

    [Test]
    public void An_empty_user_is_refused_because_it_is_not_a_name()
    {
        var refused = Assert.Throws<ArgumentException>(() => _ = new SpamAssassinClientOptions { User = string.Empty })!;

        Assert.That(refused.ParamName, Is.EqualTo("User"));
    }

    // The daemon refuses anything else outright, so the check belongs where the caller can still see why.
    [TestCase("quentin quota")]
    [TestCase("quentin@example.com")]
    [TestCase("quentin.quota")]
    [TestCase("quentin\r\nX-Injected: header")]
    public void A_user_the_daemon_would_refuse_is_refused_here_instead(string user)
    {
        var refused = Assert.Throws<ArgumentException>(() => _ = new SpamAssassinClientOptions { User = user })!;

        Assert.That(refused.ParamName, Is.EqualTo("User"));
    }

    [TestCase(1, 1)]
    [TestCase(1, 6)]
    [TestCase(2, 0)]
    [TestCase(0, 9)]
    public void A_protocol_version_that_is_not_defined_is_refused(int major, int minor)
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpamAssassinClientOptions { ProtocolVersion = new(major, minor) })!;

        Assert.That(refused.ParamName, Is.EqualTo("ProtocolVersion"));
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void Every_defined_protocol_version_is_accepted(int minor)
    {
        var options = new SpamAssassinClientOptions { ProtocolVersion = new(1, minor) };

        Assert.That(options.ProtocolVersion.Minor, Is.EqualTo(minor));
    }

    [Test]
    public void A_missing_protocol_version_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new SpamAssassinClientOptions { ProtocolVersion = null! });
    }

    [Test]
    public void A_missing_text_encoding_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new SpamAssassinClientOptions { TextEncoding = null! });
    }

    [Test]
    public void An_encoding_that_never_loses_a_byte_can_be_chosen()
    {
        var options = new SpamAssassinClientOptions { TextEncoding = Encoding.Latin1 };

        Assert.That(options.TextEncoding, Is.EqualTo(Encoding.Latin1));
    }

    // A limit that bounds nothing is refused rather than silently treated as "no limit".
    [Test]
    public void A_head_limit_below_the_shortest_possible_response_is_refused()
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpamAssassinClientOptions { MaxResponseHeadLength = 63 })!;

        Assert.That(refused.ParamName, Is.EqualTo("MaxResponseHeadLength"));
    }

    [Test]
    public void A_text_limit_of_nothing_is_refused()
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpamAssassinClientOptions { MaxTextResponseLength = 0 })!;

        Assert.That(refused.ParamName, Is.EqualTo("MaxTextResponseLength"));
    }

    [Test]
    public void A_transfer_buffer_too_small_to_be_worth_pumping_through_is_refused()
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpamAssassinClientOptions { TransferBufferSize = 511 })!;

        Assert.That(refused.ParamName, Is.EqualTo("TransferBufferSize"));
    }

    [Test]
    public void The_limits_accept_the_values_at_their_edge()
    {
        var options = new SpamAssassinClientOptions
        {
            MaxResponseHeadLength = 64,
            MaxTextResponseLength = 1,
            TransferBufferSize = 512,
        };

        Assert.That(options.MaxResponseHeadLength, Is.EqualTo(64));
        Assert.That(options.MaxTextResponseLength, Is.EqualTo(1));
        Assert.That(options.TransferBufferSize, Is.EqualTo(512));
    }
}
