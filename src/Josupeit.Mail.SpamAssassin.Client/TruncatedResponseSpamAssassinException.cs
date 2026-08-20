// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// Thrown when the daemon stopped talking in the middle of its answer, or before starting one.
/// </summary>
/// <remarks>
/// Two shapes: a connection that closed without a single byte of response, and a body that ended before the length the
/// daemon announced for it. Both mean the daemon or the network gave up rather than that either side misunderstood the
/// protocol, which makes this the failure here that a retry can plausibly fix.
/// </remarks>
public sealed class TruncatedResponseSpamAssassinException : SpamAssassinException
{
    /// <summary>
    /// Creates the exception for a response that ended early.
    /// </summary>
    /// <param name="message">The description, which is what a log will show.</param>
    /// <param name="announcedLength">
    /// How many bytes of body the daemon said it would send, or <see langword="null"/> when it never said.
    /// </param>
    /// <param name="receivedLength">How many bytes arrived before the connection ended.</param>
    public TruncatedResponseSpamAssassinException(string message, long? announcedLength, long receivedLength)
        : base(message)
    {
        AnnouncedLength = announcedLength;
        ReceivedLength = receivedLength;
    }

    /// <summary>
    /// The body length the daemon announced, or <see langword="null"/> when it never got as far as announcing one.
    /// </summary>
    public long? AnnouncedLength { get; }

    /// <summary>
    /// How many bytes arrived before the connection ended.
    /// </summary>
    public long ReceivedLength { get; }
}
