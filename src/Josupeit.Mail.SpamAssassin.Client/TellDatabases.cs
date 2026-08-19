// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// Which of the daemon's two databases a tell request addresses.
/// </summary>
/// <remarks>
/// The two are not variations of one thing. The local database is this installation's Bayes store, and
/// writing to it teaches this daemon alone. The remote one is a collaborative network - Razor, Pyzor, DCC -
/// where writing to it publishes a judgement about the message to everybody else using that network. A tell
/// request that names both does both.
/// </remarks>
[Flags]
public enum TellDatabases
{
    /// <summary>
    /// Neither database.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Bayes database of the daemon that is being told, which affects only its own future scoring.
    /// Requires that Bayes is enabled there at all; a daemon with it switched off reports having done
    /// nothing.
    /// </summary>
    Local = 1,

    /// <summary>
    /// The collaborative networks the daemon is configured for. Writing here leaves the machine and cannot
    /// be taken back except by the matching revoke.
    /// </summary>
    Remote = 2,
}
