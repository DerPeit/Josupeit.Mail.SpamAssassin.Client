// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// What a message is being declared to be, when a tell request writes something to a database.
/// </summary>
/// <remarks>
/// This says what the message <em>is</em>, not what should happen to it. Removing a message from a database
/// needs no class at all: the daemon forgets what it previously learned without being told which way it once
/// leaned.
/// </remarks>
public enum MessageClass
{
    /// <summary>
    /// Unsolicited mail. Learning a message as this makes the daemon score similar mail higher.
    /// </summary>
    Spam,

    /// <summary>
    /// Legitimate mail. Learning a message as this makes the daemon score similar mail lower.
    /// </summary>
    Ham,
}
