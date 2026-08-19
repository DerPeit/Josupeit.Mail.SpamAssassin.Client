// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// The code the daemon puts on the first line of its response.
/// </summary>
/// <remarks>
/// These are the values of <c>sysexits.h</c>, which the daemon reuses rather than defining a numbering of its
/// own. A value that is not declared here is possible and is not a malformed response: the set may grow, so a
/// caller switching on it needs a default arm.
/// </remarks>
public enum SpamAssassinResponseCode
{
    /// <summary>
    /// The request succeeded. The only code that is followed by a body.
    /// </summary>
    Ok = 0,

    /// <summary>
    /// The request was not one the daemon understands - a command it does not know, or a header combination it
    /// rejects.
    /// </summary>
    Usage = 64,

    /// <summary>
    /// The message could not be read as mail.
    /// </summary>
    DataError = 65,

    /// <summary>
    /// The daemon could not read something it needed.
    /// </summary>
    NoInput = 66,

    /// <summary>
    /// The user named in the request does not exist on the daemon's host.
    /// </summary>
    NoUser = 67,

    /// <summary>
    /// A host the daemon needed could not be resolved.
    /// </summary>
    NoHost = 68,

    /// <summary>
    /// The service is unavailable - typically the daemon refusing work it is not configured to do, such as a
    /// tell request without <c>--allow-tell</c>.
    /// </summary>
    Unavailable = 69,

    /// <summary>
    /// An internal error in SpamAssassin itself.
    /// </summary>
    Software = 70,

    /// <summary>
    /// An operating-system error on the daemon's host.
    /// </summary>
    OsError = 71,

    /// <summary>
    /// A file the daemon needed is missing on its host.
    /// </summary>
    OsFile = 72,

    /// <summary>
    /// The daemon could not create a file it needed.
    /// </summary>
    CantCreate = 73,

    /// <summary>
    /// An I/O error on the daemon's side.
    /// </summary>
    IoError = 74,

    /// <summary>
    /// A temporary failure: the same request may well succeed later. Worth a retry where nothing else is.
    /// </summary>
    TempFail = 75,

    /// <summary>
    /// The daemon found the request itself malformed - a bad length, a header it could not parse, or a body
    /// that did not match the length announced for it.
    /// </summary>
    Protocol = 76,

    /// <summary>
    /// The client is not permitted to do what it asked.
    /// </summary>
    NoPermission = 77,

    /// <summary>
    /// The daemon's own configuration is wrong.
    /// </summary>
    Config = 78,

    /// <summary>
    /// The daemon gave up waiting for the request. Its own addition to the list - <c>sysexits.h</c> stops at 78 -
    /// and, like <see cref="TempFail"/>, a code the same request may survive on a second attempt.
    /// </summary>
    Timeout = 79,
}
