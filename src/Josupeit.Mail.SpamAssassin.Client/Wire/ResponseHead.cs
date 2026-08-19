// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// Everything a response says before its body. A header this does not name was ignored on purpose: the protocol
// reserves the right to add them.
internal readonly record struct ResponseHead(
    Version ProtocolVersion,
    SpamAssassinResponseCode Code,
    string ResponseText,
    long? ContentLength,
    SpamVerdict? Verdict,
    TellDatabases DidSet,
    TellDatabases DidRemove)
{
    // No response at all - a skip - where a default ResponseHead would put nulls in ProtocolVersion and ResponseText,
    // which this type says cannot happen.
    public static readonly ResponseHead None = new(
        new Version(0, 0),
        SpamAssassinResponseCode.Ok,
        string.Empty,
        ContentLength: null,
        Verdict: null,
        TellDatabases.None,
        TellDatabases.None);
}
