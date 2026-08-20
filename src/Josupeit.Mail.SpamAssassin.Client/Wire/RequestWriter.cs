// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IO;

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// Writes one request: the head, then the message. BuildHead touches no stream, which is what lets the two colours
// produce byte-identical requests.
internal sealed class RequestWriter(SpamAssassinClientOptions options, HeadWriter head, Memory<byte> transferBuffer, RecyclableMemoryStreamManager streams)
{
    public void BuildHead(SpamcCommand command, TellInstruction? instruction, long? contentLength, bool compressed)
    {
        head.Append(CommandToken(command));
        head.Append(" SPAMC/"u8);
        head.Append(options.ProtocolVersion.Major);
        head.Append("."u8);
        head.Append(options.ProtocolVersion.Minor);
        head.AppendCrLf();

        if (contentLength.HasValue)
        {
            head.Append("Content-length: "u8);
            head.Append(contentLength.Value);
            head.AppendCrLf();
        }

        if (options.User is { } user)
        {
            head.Append("User: "u8);
            head.AppendAscii(user);
            head.AppendCrLf();
        }

        if (compressed)
        {
            head.Append("Compress: zlib"u8);
            head.AppendCrLf();
        }

        if (instruction is not null)
            AppendTellHeaders(instruction);

        // The blank line that ends the head. Since protocol 1.5 a ping needs it too, or the daemon keeps waiting.
        head.AppendCrLf();
    }

    public void WriteTo(Stream output, in MessageBody body)
    {
        output.Write(head.Written.Span);

        WriteBody(output, body);
    }

    public async Task WriteToAsync(Stream output, MessageBody body, CancellationToken cancellationToken)
    {
        await output.WriteAsync(head.Written, cancellationToken).ConfigureAwait(false);

        await WriteBodyAsync(output, body, cancellationToken).ConfigureAwait(false);
    }

    // The one place the library buffers a message the caller took care to stream: the daemon insists on the length,
    // and the compressed length is not knowable any other way. The caller owns what comes back.
    public RecyclableMemoryStream Compress(in MessageBody body)
    {
        var compressed = streams.GetStream("spamc-request");

        try
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                WriteBody(zlib, body);

            compressed.Position = 0;

            return compressed;
        }
        catch
        {
            compressed.Dispose();

            throw;
        }
    }

    public async ValueTask<RecyclableMemoryStream> CompressAsync(MessageBody body, CancellationToken cancellationToken)
    {
        var compressed = streams.GetStream("spamc-request");

        try
        {
            var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true);

            await using (zlib.ConfigureAwait(false))
                await WriteBodyAsync(zlib, body, cancellationToken).ConfigureAwait(false);

            compressed.Position = 0;

            return compressed;
        }
        catch
        {
            await compressed.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private static ReadOnlySpan<byte> CommandToken(SpamcCommand command) => command switch
    {
        SpamcCommand.Check => "CHECK"u8,
        SpamcCommand.Symbols => "SYMBOLS"u8,
        SpamcCommand.Report => "REPORT"u8,
        SpamcCommand.ReportIfSpam => "REPORT_IFSPAM"u8,
        SpamcCommand.Process => "PROCESS"u8,
        SpamcCommand.Headers => "HEADERS"u8,
        SpamcCommand.Tell => "TELL"u8,
        SpamcCommand.Ping => "PING"u8,
        SpamcCommand.Skip => "SKIP"u8,
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unable to write the request. The command is not one this library knows."),
    };

    private static ReadOnlySpan<byte> DatabasesToken(TellDatabases databases) => databases switch
    {
        TellDatabases.Local => "local"u8,
        TellDatabases.Remote => "remote"u8,
        _ => "local,remote"u8,
    };

    private static EndOfStreamException EndedEarly(long announced, long remaining) =>
        new($"Unable to send the message. It ended {remaining} bytes before the {announced} that were announced to the daemon, which is now waiting for bytes that do not exist.");

    private void WriteBody(Stream output, in MessageBody body)
    {
        if (!body.HasContent)
            return;

        if (body.Stream is not { } stream)
        {
            foreach (var segment in body.Buffer)
                output.Write(segment.Span);

            return;
        }

        var remaining = body.Length;

        while (remaining > 0)
        {
            var wanted = (int)Math.Min(remaining, transferBuffer.Length);
            var read = stream.Read(transferBuffer.Span[..wanted]);

            if (read <= 0)
                throw EndedEarly(body.Length, remaining);

            output.Write(transferBuffer.Span[..read]);

            remaining -= read;
        }
    }

    private async Task WriteBodyAsync(Stream output, MessageBody body, CancellationToken cancellationToken)
    {
        if (!body.HasContent)
            return;

        if (body.Stream is not { } stream)
        {
            foreach (var segment in body.Buffer)
                await output.WriteAsync(segment, cancellationToken).ConfigureAwait(false);

            return;
        }

        var remaining = body.Length;

        while (remaining > 0)
        {
            var wanted = (int)Math.Min(remaining, transferBuffer.Length);
            var read = await stream.ReadAsync(transferBuffer[..wanted], cancellationToken).ConfigureAwait(false);

            if (read <= 0)
                throw EndedEarly(body.Length, remaining);

            await output.WriteAsync(transferBuffer[..read], cancellationToken).ConfigureAwait(false);

            remaining -= read;
        }
    }

    private void AppendTellHeaders(TellInstruction instruction)
    {
        if (instruction.MessageClass is { } messageClass)
        {
            head.Append("Message-class: "u8);
            head.Append(messageClass == Client.MessageClass.Spam ? "spam"u8 : "ham"u8);
            head.AppendCrLf();
        }

        if (instruction.Set != TellDatabases.None)
        {
            head.Append("Set: "u8);
            head.Append(DatabasesToken(instruction.Set));
            head.AppendCrLf();
        }

        if (instruction.Remove != TellDatabases.None)
        {
            head.Append("Remove: "u8);
            head.Append(DatabasesToken(instruction.Remove));
            head.AppendCrLf();
        }
    }
}
