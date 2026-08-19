// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// Thrown when what came back cannot be read as a response: a status line in an unexpected shape, a header without a
/// colon, a score that is not a number, or a scanning command answered without the verdict it is defined to carry.
/// </summary>
/// <remarks>
/// Not something a healthy daemon does. In practice it means the streams are not talking to a spamd at all - a wrong
/// port, a proxy answering in HTTP, a TLS handshake being read as text - so it is worth logging the message and looking
/// at the endpoint rather than retrying.
/// </remarks>
public sealed class MalformedResponseSpamAssassinException : SpamAssassinException
{
    /// <summary>
    /// Creates the exception, describing what about the response could not be made sense of.
    /// </summary>
    /// <param name="message">The description, which is what a log will show.</param>
    public MalformedResponseSpamAssassinException(string message)
        : base(message)
    {
    }
}
