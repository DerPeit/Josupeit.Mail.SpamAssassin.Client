// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// Thrown when the daemon answered with a code other than <see cref="SpamAssassinResponseCode.Ok"/>.
/// </summary>
/// <remarks>
/// A non-zero code is a refusal rather than a result: the daemon sends the status line, no body, and closes the
/// connection, so there is nothing for a method to return. That is why this is an exception and not a field on every
/// result type - a caller who did not check would otherwise read a verdict that was never computed.
/// <para>
/// <see cref="SpamAssassinResponseCode.TempFail"/> and <see cref="SpamAssassinResponseCode.Timeout"/> are the two codes
/// that say the same request may succeed later; the rest will not without something changing.
/// </para>
/// </remarks>
public sealed class ErrorResponseSpamAssassinException : SpamAssassinException
{
    /// <summary>
    /// Creates the exception from the status line the daemon sent.
    /// </summary>
    /// <param name="code">The response code from the status line.</param>
    /// <param name="responseText">The text that followed the code on the status line.</param>
    public ErrorResponseSpamAssassinException(SpamAssassinResponseCode code, string responseText)
        : base($"The SpamAssassin daemon refused the request with code {(int)code} ({responseText}).")
    {
        Code = code;
        ResponseText = responseText;
    }

    /// <summary>
    /// The code from the status line. May hold a value <see cref="SpamAssassinResponseCode"/> does not declare, because
    /// the daemon is free to use any of <c>sysexits.h</c>.
    /// </summary>
    public SpamAssassinResponseCode Code { get; }

    /// <summary>
    /// The text the daemon put after the code - conventionally the <c>sysexits.h</c> name, sometimes with an explanation
    /// of what it objected to.
    /// </summary>
    public string ResponseText { get; }
}
