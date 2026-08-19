// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Josupeit.Mail.SpamAssassin.Client;

namespace Spamc;

// One run of the sample. The exit code is the answer: 0 and 1 are ham and spam, anything else a sysexits code
// saying why there is no verdict.
internal sealed class SpamcRunner(SpamcOptions options, ISpamAssassinClient client, Stream input, Stream output)
{
    // libspamc's own numbers, outside sysexits.
    private const int EX_ISSPAM = 1;

    private const int EX_NOTSPAM = 0;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // Held whole because the daemon needs the length before the body, and because passing the message through
        // after a failure means still having it.
        await using var message = new MemoryStream();

        // spamc reads stdin whatever the mode, which is why `spamc -K` at a terminal looks hung.
        if (options.Mode != SpamcMode.Ping)
        {
            var oversized = await BufferAsync(message, cancellationToken);

            // Too large to scan is not a failure: the message goes on its way and the run succeeds.
            if (oversized)
                return await PassThroughAsync(message, cancellationToken);
        }

        try
        {
            return await ExecuteAsync(message, cancellationToken);
        }
        catch (Exception failure) when (failure is SpamAssassinException or IOException or SocketException or OperationCanceledException)
        {
            return await FailAsync(message, failure);
        }
    }

    private static int ExitCodeFor(Exception failure) => failure switch
    {
        ErrorResponseSpamAssassinException refusal => (int)refusal.Code,
        SpamAssassinException => (int)SpamAssassinResponseCode.Protocol,

        // A socket failure arrives as itself while connecting and wrapped once the stream is in use.
        SocketException => (int)SpamAssassinResponseCode.Unavailable,
        IOException { InnerException: SocketException } => (int)SpamAssassinResponseCode.Unavailable,

        OperationCanceledException => (int)SpamAssassinResponseCode.TempFail,
        _ => (int)SpamAssassinResponseCode.IoError,
    };

    // spamd reports what it did, not whether it did what was asked, so the two have to be compared.
    private static bool DidWhatWasAsked(TellInstruction instruction, TellResult result, TellDatabases database) =>
        instruction.Set.HasFlag(database)
            ? result.Set.HasFlag(database)
            : result.Removed.HasFlag(database);

    // CRLF first and then a bare LF, because a message that reached a filter has been through anything.
    private static int BodyOffset(ReadOnlySpan<byte> message)
    {
        var separator = message.IndexOf("\r\n\r\n"u8);

        if (separator >= 0)
            return separator + 4;

        separator = message.IndexOf("\n\n"u8);

        return separator >= 0 ? separator + 2 : -1;
    }

    // spamc formats this with %.1f in the C locale.
    private static string Summary(SpamVerdict verdict) =>
        string.Create(CultureInfo.InvariantCulture, $"{verdict.Score:F1}/{verdict.Threshold:F1}\n");

    // One byte past the limit is all it takes to answer "too large". The rest stays in stdin for PassThroughAsync.
    private async Task<bool> BufferAsync(MemoryStream message, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var limit = options.MaxMessageLength + 1L;

        while (message.Length < limit)
        {
            var wanted = (int)Math.Min(buffer.Length, limit - message.Length);
            var read = await input.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);

            if (read == 0)
                break;

            await message.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        message.Position = 0;

        return message.Length > options.MaxMessageLength;
    }

    private async Task<int> ExecuteAsync(MemoryStream message, CancellationToken cancellationToken)
    {
        switch (options.Mode)
        {
            case SpamcMode.Ping:
            {
                var version = await client.PingAsync(cancellationToken);

                await WriteAsync($"SPAMD/{version.Major}.{version.Minor} 0\n", cancellationToken);

                return EX_NOTSPAM;
            }

            case SpamcMode.Check:
            {
                var verdict = await client.ScanAsync(message, cancellationToken);

                await WriteAsync(Summary(verdict), cancellationToken);

                // -c reports the verdict in the exit code whether -E was given or not.
                return SpamExitCode(verdict, always: true);
            }

            case SpamcMode.Symbols:
            {
                var result = await client.SymbolsAsync(message, cancellationToken);

                await WriteAsync($"{string.Join(',', result.Symbols)}\n", cancellationToken);

                return SpamExitCode(result.Verdict, always: false);
            }

            case SpamcMode.Report:
            case SpamcMode.ReportIfSpam:
            {
                var result = options.Mode == SpamcMode.Report
                    ? await client.ReportAsync(message, cancellationToken)
                    : await client.ReportIfSpamAsync(message, cancellationToken);

                // -r on a clean message gets no report at all, which is not the same as an empty one.
                await WriteAsync(Summary(result.Verdict) + result.Report, cancellationToken);

                return SpamExitCode(result.Verdict, always: false);
            }

            case SpamcMode.Headers:
            {
                var result = await client.HeadersAsync(message, output, cancellationToken);

                // HEADERS stops at the blank line, so the original body has to go back on behind it.
                await CopyBodyAsync(message, cancellationToken);

                return SpamExitCode(result.Verdict, always: false);
            }

            case SpamcMode.Learn:
            case SpamcMode.ReportMessage:
            {
                var instruction = options.Instruction ?? throw new InvalidOperationException("Unable to tell the daemon anything: the mode was reached without an instruction.");

                var result = await client.TellAsync(instruction, message, cancellationToken);

                // Learning is local and reporting is remote, and each mode cares only about its own half.
                var asked = options.Mode == SpamcMode.Learn
                    ? DidWhatWasAsked(instruction, result, TellDatabases.Local)
                    : DidWhatWasAsked(instruction, result, TellDatabases.Remote);

                var told = (options.Mode, asked) switch
                {
                    (SpamcMode.Learn, true) => "Message successfully un/learned\n",
                    (SpamcMode.Learn, false) => "Message was already un/learned\n",
                    (_, true) => "Message successfully reported/revoked\n",
                    (_, false) => "Unable to report/revoke message\n",
                };

                await WriteAsync(told, cancellationToken);

                return EX_NOTSPAM;
            }

            case SpamcMode.Process:
            {
                // Socket to stdout: nothing the size of a message is held anywhere.
                var result = await client.ProcessAsync(message, output, cancellationToken);

                return SpamExitCode(result.Verdict, always: false);
            }

            // Named rather than folded into a default, so that a mode added to SpamcMode and forgotten here says so
            // instead of quietly behaving as PROCESS.
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Unable to run. The mode is not one this sample knows.");
        }
    }

    // What spamc does when the exchange did not happen: every mode still writes the shape a script expects.
    private async Task<int> FailAsync(MemoryStream message, Exception failure)
    {
        await Console.Error.WriteLineAsync($"spamc: {failure.Message}");

        // CancellationToken.None throughout: the deadline that fired is why this is running at all.
        switch (options.Mode)
        {
            case SpamcMode.Check:
            case SpamcMode.Report:
            case SpamcMode.ReportIfSpam:
            {
                await WriteAsync("0/0\n", CancellationToken.None);

                break;
            }

            case SpamcMode.Symbols:
            {
                await WriteAsync("\n", CancellationToken.None);

                break;
            }

            case SpamcMode.Ping:
            case SpamcMode.Learn:
            case SpamcMode.ReportMessage:
            {
                break;
            }

            case SpamcMode.Process:
            case SpamcMode.Headers:
            {
                if (options.SafeFallback)
                    await PassThroughAsync(message, CancellationToken.None);

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Unable to report the failure. The mode is not one this sample knows.");
        }

        // -K and -L report the failure whatever the fallback says; -C does not, which is upstream's oddity and
        // copied rather than corrected.
        return options.Mode is SpamcMode.Ping or SpamcMode.Learn || !options.SafeFallback
            ? ExitCodeFor(failure)
            : (int)SpamAssassinResponseCode.Ok;
    }

    private async Task<int> PassThroughAsync(MemoryStream message, CancellationToken cancellationToken)
    {
        message.Position = 0;

        await message.CopyToAsync(output, cancellationToken);

        // Whatever did not fit in the buffer is still in stdin, and copies nothing when the message was read whole.
        await input.CopyToAsync(output, cancellationToken);

        return (int)SpamAssassinResponseCode.Ok;
    }

    private async Task CopyBodyAsync(MemoryStream message, CancellationToken cancellationToken)
    {
        // Always succeeds: the stream is this class's own.
        message.TryGetBuffer(out var buffer);

        var body = BodyOffset(buffer.AsSpan());

        if (body < 0)
            return;

        message.Position = body;

        await message.CopyToAsync(output, cancellationToken);
    }

    private int SpamExitCode(SpamVerdict verdict, bool always) =>
        verdict.IsSpam && (always || options.UseExitCode) ? EX_ISSPAM : EX_NOTSPAM;

    // \n rather than the platform's line ending: what leaves here is a message or a line another program parses.
    private async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        await output.WriteAsync(bytes, cancellationToken);
    }
}
