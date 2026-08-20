// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// Thrown when a response passed one of the limits in <see cref="SpamAssassinClientOptions"/>.
/// </summary>
/// <remarks>
/// The limits exist because a response is input like any other: the size of the header block and the size of a text body
/// are both things the daemon decides and the client has to hold. Hitting one is either a daemon behaving strangely or a
/// limit set below what this daemon legitimately produces - so the limit that was passed is on the exception, to make
/// telling those two apart possible without going back to the configuration.
/// </remarks>
public sealed class ResponseTooLargeSpamAssassinException : SpamAssassinException
{
    /// <summary>
    /// Creates the exception for a response that grew past what was allowed.
    /// </summary>
    /// <param name="message">The description, which is what a log will show.</param>
    /// <param name="limit">The limit that was passed, in bytes.</param>
    public ResponseTooLargeSpamAssassinException(string message, long limit)
        : base(message)
    {
        Limit = limit;
    }

    /// <summary>
    /// The limit that was passed, in bytes.
    /// </summary>
    public long Limit { get; }
}
