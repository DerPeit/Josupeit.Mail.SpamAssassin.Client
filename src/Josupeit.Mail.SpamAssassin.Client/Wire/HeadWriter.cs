// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// Builds the head of a request into a pooled buffer. A class rather than a struct: a mutable struct holding a
// rented array is one accidental copy away from returning the same array twice.
internal sealed class HeadWriter : IDisposable
{
    private const int INITIAL_CAPACITY = 256;

    private byte[] _buffer;
    private int _length;

    public HeadWriter()
    {
        _buffer = ArrayPool<byte>.Shared.Rent(INITIAL_CAPACITY);
    }

    public ReadOnlyMemory<byte> Written => _buffer.AsMemory(0, _length);

    public void Append(ReadOnlySpan<byte> value)
    {
        EnsureRoom(value.Length);

        value.CopyTo(_buffer.AsSpan(_length));

        _length += value.Length;
    }

    public void AppendAscii(string value)
    {
        EnsureRoom(value.Length);

        _length += Encoding.ASCII.GetBytes(value, _buffer.AsSpan(_length));
    }

    public void Append(long value)
    {
        const int WIDEST = 20;

        // 20 covers long.MinValue including its sign, so the format cannot fail for want of room - but a silent
        // failure here writes "Content-length: " with nothing after it, and spamd answers EX_PROTOCOL without
        // saying why.
        EnsureRoom(WIDEST);

        if (!Utf8Formatter.TryFormat(value, _buffer.AsSpan(_length), out var written))
            throw new InvalidOperationException($"Unable to write the request head. {value} did not fit the {WIDEST} bytes reserved for it.");

        _length += written;
    }

    public void AppendCrLf() => Append("\r\n"u8);

    public void Dispose()
    {
        var buffer = _buffer;

        _buffer = [];
        _length = 0;

        if (buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    private void EnsureRoom(int count)
    {
        if (_length + count <= _buffer.Length)
            return;

        var grown = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _length + count));

        _buffer.AsSpan(0, _length).CopyTo(grown);
        ArrayPool<byte>.Shared.Return(_buffer);

        _buffer = grown;
    }
}
