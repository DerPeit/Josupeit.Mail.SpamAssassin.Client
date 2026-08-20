// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IO;

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// Reads one response off a stream: the head first, then whatever the command expects behind it.
//
// One pooled buffer serves both halves, which is why this is a class with state: the read that finds the end of the
// header block almost always drags the first bytes of the body with it, and those have to survive until the body is
// asked for.
internal sealed class ResponseReader(Stream stream, SpamAssassinClientOptions options, ResponseParser parser, RecyclableMemoryStreamManager streams) : IDisposable
{
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(options.TransferBufferSize);
    private int _start;
    private int _end;

    private static ReadOnlySpan<byte> HeadTerminator => "\r\n\r\n"u8;

    public ResponseHead ReadHead()
    {
        while (true)
        {
            if (TryTakeHead(out var head))
                return head;

            var room = MakeRoom();
            var read = stream.Read(_buffer.AsSpan(_end, room));

            if (read <= 0)
                return TakeHeadAtEndOfStream();

            _end += read;
        }
    }

    public async ValueTask<ResponseHead> ReadHeadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryTakeHead(out var head))
                return head;

            var room = MakeRoom();
            var read = await stream.ReadAsync(_buffer.AsMemory(_end, room), cancellationToken).ConfigureAwait(false);

            if (read <= 0)
                return TakeHeadAtEndOfStream();

            _end += read;
        }
    }

    public long CopyBody(Stream destination, long? contentLength, long? maxLength = null)
    {
        var remaining = contentLength;
        var copied = 0L;

        while (remaining is null or > 0)
        {
            if (_start == _end && !Fill())
                break;

            var take = Take(remaining);

            EnsureWithinLimit(copied + take, maxLength);

            destination.Write(_buffer.AsSpan(_start, take));

            _start += take;
            copied += take;
            remaining -= take;
        }

        return Complete(copied, remaining, contentLength);
    }

    public async ValueTask<long> CopyBodyAsync(Stream destination, long? contentLength, long? maxLength, CancellationToken cancellationToken)
    {
        var remaining = contentLength;
        var copied = 0L;

        while (remaining is null or > 0)
        {
            if (_start == _end && !await FillAsync(cancellationToken).ConfigureAwait(false))
                break;

            var take = Take(remaining);

            EnsureWithinLimit(copied + take, maxLength);

            await destination.WriteAsync(_buffer.AsMemory(_start, take), cancellationToken).ConfigureAwait(false);

            _start += take;
            copied += take;
            remaining -= take;
        }

        return Complete(copied, remaining, contentLength);
    }

    public string ReadText(long? contentLength)
    {
        using var text = NewTextBuffer(contentLength);

        CopyBody(text, contentLength, options.MaxTextResponseLength);

        return Decode(text);
    }

    public async ValueTask<string> ReadTextAsync(long? contentLength, CancellationToken cancellationToken)
    {
        using var text = NewTextBuffer(contentLength);

        await CopyBodyAsync(text, contentLength, options.MaxTextResponseLength, cancellationToken).ConfigureAwait(false);

        return Decode(text);
    }

    public void Dispose()
    {
        var buffer = _buffer;

        _buffer = [];
        _start = 0;
        _end = 0;

        // Cleared on the way back: this buffer held a response body, and for PROCESS that is somebody's whole mail.
        if (buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
    }

    private static long Complete(long copied, long? remaining, long? contentLength)
    {
        if (remaining is > 0)
            throw new TruncatedResponseSpamAssassinException($"Unable to read the daemon's response. Its body ended after {copied} of the {contentLength} bytes it announced.", contentLength, copied);

        return copied;
    }

    // Checked before the write rather than after it, so the limit is a limit rather than a limit plus whatever the
    // last read happened to bring.
    private static void EnsureWithinLimit(long copied, long? maxLength)
    {
        if (maxLength is { } max && copied > max)
            throw new ResponseTooLargeSpamAssassinException($"Unable to read the daemon's response. Its body passed the {max} bytes allowed for a text response.", max);
    }

    private bool Fill() => Filled(stream.Read(_buffer, 0, _buffer.Length));

    private async ValueTask<bool> FillAsync(CancellationToken cancellationToken)
    {
        var read = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);

        return Filled(read);
    }

    private bool Filled(int read)
    {
        if (read <= 0)
            return false;

        _start = 0;
        _end = read;

        return true;
    }

    private bool TryTakeHead(out ResponseHead head)
    {
        var unread = _buffer.AsSpan(_start, _end - _start);
        var terminator = unread.IndexOf(HeadTerminator);

        if (terminator < 0)
        {
            head = default;

            return false;
        }

        head = parser.ParseHead(unread[..terminator]);

        _start += terminator + HeadTerminator.Length;

        return true;
    }

    // A refusal is a status line and a closed connection - no header block, so no blank line to look for. Reading
    // what arrived is what turns it into the response code the caller sees rather than a truncation complaint.
    private ResponseHead TakeHeadAtEndOfStream()
    {
        var unread = _buffer.AsSpan(_start, _end - _start);

        if (unread.IsEmpty)
            throw new TruncatedResponseSpamAssassinException("Unable to read the daemon's response. It closed the connection without answering at all.", announcedLength: null, receivedLength: 0);

        var head = parser.ParseHead(unread);

        _start = _end;

        return head;
    }

    private int MakeRoom()
    {
        var unread = _end - _start;

        if (unread > options.MaxResponseHeadLength)
            throw new ResponseTooLargeSpamAssassinException($"Unable to read the daemon's response. Its status line and headers passed the {options.MaxResponseHeadLength} bytes allowed for them without ending.", options.MaxResponseHeadLength);

        if (_end < _buffer.Length)
            return _buffer.Length - _end;

        // Nothing needs compacting: _start stays at zero while the head is being read, so a full buffer here means
        // the head itself has outgrown it.
        var grown = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);

        _buffer.AsSpan(0, _end).CopyTo(grown);
        ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);

        _buffer = grown;

        return _buffer.Length - _end;
    }

    private int Take(long? remaining)
    {
        var available = _end - _start;

        return remaining is { } left && left < available ? (int)left : available;
    }

    private RecyclableMemoryStream NewTextBuffer(long? contentLength)
    {
        if (contentLength is { } announced && announced > options.MaxTextResponseLength)
            throw new ResponseTooLargeSpamAssassinException($"Unable to read the daemon's response. It announced {announced} bytes of text, past the {options.MaxTextResponseLength} allowed for one.", options.MaxTextResponseLength);

        return contentLength is { } capacity
            ? streams.GetStream("spamd-response", capacity)
            : streams.GetStream("spamd-response");
    }

    // GetBuffer would consolidate the pooled blocks into one large array and defeat the point, so the text is decoded
    // off the sequence instead.
    private string Decode(RecyclableMemoryStream text)
    {
        var sequence = text.GetReadOnlySequence();

        if (sequence.IsSingleSegment)
            return options.TextEncoding.GetString(sequence.FirstSpan);

        var length = (int)sequence.Length;
        var contiguous = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            sequence.CopyTo(contiguous);

            return options.TextEncoding.GetString(contiguous.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(contiguous, clearArray: true);
        }
    }
}
