// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// The base of everything this library throws about the daemon's side of the conversation.
/// </summary>
/// <remarks>
/// Catch this to mean "the scan did not happen, and not because of my arguments", and catch one of the types derived
/// from it when the difference matters - it usually does, because only some of them are worth retrying:
/// <list type="bullet">
/// <item><description><see cref="ErrorResponseSpamAssassinException"/> - the daemon answered, and refused.</description></item>
/// <item><description><see cref="MalformedResponseSpamAssassinException"/> - the daemon answered something that is not a response.</description></item>
/// <item><description><see cref="TruncatedResponseSpamAssassinException"/> - the daemon stopped talking mid-answer.</description></item>
/// <item><description><see cref="ResponseTooLargeSpamAssassinException"/> - the answer passed a limit the client was configured with.</description></item>
/// </list>
/// <para>
/// It is abstract on purpose: every failure this library knows about has a name, and a bare "something went wrong"
/// would tell a caller nothing it could act on. Failures of the caller's own streams are wrapped in none of these - an
/// <see cref="System.IO.IOException"/> stays one, so a connection problem still looks like a connection problem.
/// </para>
/// </remarks>
public abstract class SpamAssassinException : Exception
{
    /// <summary>
    /// Creates the exception with a message describing what went wrong.
    /// </summary>
    /// <param name="message">The description, which is what a log will show.</param>
    protected SpamAssassinException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message and the failure underneath it.
    /// </summary>
    /// <remarks>
    /// Nothing in this library wraps another exception today - a stream's failure is deliberately left as it is - so this
    /// exists for a derived type that has a cause worth carrying, and shows up as uncovered in a coverage report for
    /// exactly that reason.
    /// </remarks>
    /// <param name="message">The description, which is what a log will show.</param>
    /// <param name="innerException">The failure this one was raised for.</param>
    protected SpamAssassinException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
