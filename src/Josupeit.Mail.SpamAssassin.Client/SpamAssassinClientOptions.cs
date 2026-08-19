// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Buffers;
using System.Text;

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// What a <see cref="SpamAssassinClient"/> announces to the daemon, and the limits it holds the daemon to.
/// </summary>
/// <remarks>
/// Every setting here is either something the request says about itself or a bound on what a response may make
/// the client allocate. The defaults suit a daemon on the same host or the same network as the caller; tighten
/// the limits when it is somewhere less trusted, because a response is as much attacker-controlled input as
/// anything else that arrives over a socket.
/// </remarks>
public sealed record SpamAssassinClientOptions
{
    /// <summary>
    /// The default for <see cref="ProtocolVersion"/>: 1.5, the newest the daemon defines and the one every
    /// command in this library is available under.
    /// </summary>
    public static readonly Version DefaultProtocolVersion = new(1, 5);

    /// <summary>
    /// The default for <see cref="MaxResponseHeadLength"/>: 8 kibibytes, which is far more than the handful of
    /// short headers a daemon actually sends.
    /// </summary>
    public static readonly int DefaultMaxResponseHeadLength = 8 * 1024;

    /// <summary>
    /// The default for <see cref="MaxTextResponseLength"/>: one mebibyte, comfortably above any report or list
    /// of rule names SpamAssassin produces.
    /// </summary>
    public static readonly int DefaultMaxTextResponseLength = 1024 * 1024;

    /// <summary>
    /// The default for <see cref="TransferBufferSize"/>: 16 kibibytes.
    /// </summary>
    public static readonly int DefaultTransferBufferSize = 16 * 1024;

    // The daemon rejects anything but [-A-Za-z0-9_]+ outright, so the check belongs here, where the caller can still
    // see why.
    private static readonly SearchValues<char> _allowedUserCharacters =
        SearchValues.Create("-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz");

    private readonly string? _user;
    private readonly Version _protocolVersion = DefaultProtocolVersion;
    private readonly Encoding _textEncoding = Encoding.UTF8;
    private readonly int _maxResponseHeadLength = DefaultMaxResponseHeadLength;
    private readonly int _maxTextResponseLength = DefaultMaxTextResponseLength;
    private readonly int _transferBufferSize = DefaultTransferBufferSize;

    /// <summary>
    /// The user whose configuration the daemon should scan with, or <see langword="null"/> to let it use its
    /// own defaults.
    /// </summary>
    /// <remarks>
    /// What this buys depends entirely on the daemon: with per-user configuration enabled it selects a rule set,
    /// a threshold and a Bayes database; without it, the name is little more than something for the log. The
    /// daemon insists on a traditional UNIX name, so anything outside <c>[-A-Za-z0-9_]</c> is rejected here
    /// rather than by a response code later.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is empty, or holds a character the daemon does not allow.</exception>
    public string? User
    {
        get => _user;

        init
        {
            if (value is not null)
            {
                if (value.Length == 0)
                    throw new ArgumentException("Unable to set the user. An empty name is not a name; use null to send no user at all.", nameof(User));

                if (value.AsSpan().IndexOfAnyExcept(_allowedUserCharacters) >= 0)
                    throw new ArgumentException("Unable to set the user. The daemon allows only the characters [-A-Za-z0-9_] in a user name.", nameof(User));
            }

            _user = value;
        }
    }

    /// <summary>
    /// Whether to compress the request body and tell the daemon so.
    /// </summary>
    /// <remarks>
    /// Worth having over a slow link and pointless over a local socket. Two things to know before switching it
    /// on: the daemon has to have been started with compression allowed, or it refuses the request; and the
    /// length it is told has to be the length of the <em>compressed</em> body, which is not known until the body
    /// has been compressed - so a compressed request is buffered whole, whichever overload handed the message
    /// over.
    /// </remarks>
    public bool CompressRequests { get; init; }

    /// <summary>
    /// The protocol version the request announces. Defaults to <see cref="DefaultProtocolVersion"/>.
    /// </summary>
    /// <remarks>
    /// Lower it only to humour an old daemon, and know what each step costs: below 1.5 a ping is not defined,
    /// below 1.4 there is no headers command, and below 1.3 the daemon sends no verdict alongside a rewritten
    /// message and only one header per response. Below 1.2 there is no length header at all, which this library
    /// depends on, so 1.2 is the floor.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is not one of 1.2 through 1.5.</exception>
    public Version ProtocolVersion
    {
        get => _protocolVersion;

        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(ProtocolVersion));

            if (value.Major != 1 || value.Minor is < 2 or > 5)
                throw new ArgumentOutOfRangeException(nameof(ProtocolVersion), value, "Unable to set the protocol version. Only 1.2 through 1.5 are defined, and only those are understood here.");

            _protocolVersion = value;
        }
    }

    /// <summary>
    /// How to read the text a daemon sends back - a report, or a list of rule names. Defaults to UTF-8.
    /// </summary>
    /// <remarks>
    /// The protocol says nothing about this, and the daemon quotes parts of the message it scanned, so the bytes
    /// can be anything the message was. UTF-8 with its replacement character is the least surprising reading;
    /// <see cref="Encoding.Latin1"/> is the one that never loses a byte, at the cost of mangling anything that
    /// really was UTF-8. Response <em>headers</em> are not affected - those are read as Latin-1 always, because
    /// they are single-byte by definition.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public Encoding TextEncoding
    {
        get => _textEncoding;

        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(TextEncoding));

            _textEncoding = value;
        }
    }

    /// <summary>
    /// The most bytes a response's status line and headers may take up together, in bytes.
    /// </summary>
    /// <remarks>
    /// The header block has to be held whole to be parsed, and its end is a marker the daemon is trusted to
    /// send, so without a bound a daemon that never sends one makes the client grow a buffer until the host
    /// gives up. Passing it is a protocol error, not a truncation.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 64.</exception>
    public int MaxResponseHeadLength
    {
        get => _maxResponseHeadLength;

        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 64, nameof(MaxResponseHeadLength));

            _maxResponseHeadLength = value;
        }
    }

    /// <summary>
    /// The most bytes of text a response may carry, in bytes - a report, or a list of rule names.
    /// </summary>
    /// <remarks>
    /// This bounds the one case where the client holds a response body in memory. It does not apply to a
    /// rewritten message: that goes straight into the stream the caller supplied, and how large a thing the
    /// caller is willing to write there is not for this library to decide.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public int MaxTextResponseLength
    {
        get => _maxTextResponseLength;

        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(MaxTextResponseLength));

            _maxTextResponseLength = value;
        }
    }

    /// <summary>
    /// The size of the pooled buffers used to move a message to the daemon and a rewritten one back, in bytes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 512.</exception>
    public int TransferBufferSize
    {
        get => _transferBufferSize;

        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 512, nameof(TransferBufferSize));

            _transferBufferSize = value;
        }
    }
}
