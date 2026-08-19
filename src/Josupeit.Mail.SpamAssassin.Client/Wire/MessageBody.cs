// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Buffers;
using System.IO;

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// The four shapes a caller can hand a message over in, reduced to a length and somewhere to read from.
//
// The factories carry the argument validation for the whole public surface, hence the "message" parameter name:
// every overload on ISpamAssassinClient calls its parameter that, so the exception points at the caller's own
// identifier rather than at something internal.
internal readonly struct MessageBody
{
    private const string PARAMETER_NAME = "message";

    // No body at all - a ping or a skip - which sends no length header, where an empty message sends
    // "Content-length: 0".
    public static readonly MessageBody None = default;

    private MessageBody(Stream? stream, long length, ReadOnlySequence<byte> buffer, bool hasContent)
    {
        Stream = stream;
        Length = length;
        Buffer = buffer;
        HasContent = hasContent;
    }

    public Stream? Stream { get; }

    public long Length { get; }

    public ReadOnlySequence<byte> Buffer { get; }

    public bool HasContent { get; }

    public static MessageBody FromSeekableStream(Stream? message)
    {
        ArgumentNullException.ThrowIfNull(message, PARAMETER_NAME);

        if (!message.CanRead)
            throw new ArgumentException("Unable to send the message. The stream cannot be read from.", PARAMETER_NAME);

        if (!message.CanSeek)
            throw new ArgumentException("Unable to send the message. The stream cannot seek, so its length cannot be established - and the daemon has to be told the length before the body. Pass the length explicitly, or buffer the message yourself.", PARAMETER_NAME);

        // From the current position to the end, not the whole stream.
        var remaining = Math.Max(0, message.Length - message.Position);

        return new(message, remaining, default, hasContent: true);
    }

    public static MessageBody FromStream(Stream? message, long length)
    {
        ArgumentNullException.ThrowIfNull(message, PARAMETER_NAME);

        if (!message.CanRead)
            throw new ArgumentException("Unable to send the message. The stream cannot be read from.", PARAMETER_NAME);

        ArgumentOutOfRangeException.ThrowIfNegative(length, nameof(length));

        return new(message, length, default, hasContent: true);
    }

    public static MessageBody FromSequence(ReadOnlySequence<byte> message) =>
        new(stream: null, message.Length, message, hasContent: true);

    public static MessageBody FromMemory(ReadOnlyMemory<byte> message) =>
        new(stream: null, message.Length, new(message), hasContent: true);
}
