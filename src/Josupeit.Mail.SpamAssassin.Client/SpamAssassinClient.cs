// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Josupeit.Mail.SpamAssassin.Client.Wire;
using Microsoft.IO;

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// The client: turns each method call into one exchange with the daemon over a connection it asks for and disposes.
/// </summary>
/// <remarks>
/// <para>
/// Safe to share. It keeps nothing per call and no connection between calls, so one instance can serve a whole
/// application - as long as the connection source it was built with tolerates being asked for connections
/// concurrently, which is the part this class cannot decide for it.
/// </para>
/// <para>
/// See <see cref="ISpamAssassinClient"/> for what the methods mean; the interface carries the documentation, and this
/// class adds nothing to it but the two ways of being constructed.
/// </para>
/// </remarks>
public sealed class SpamAssassinClient : ISpamAssassinClient
{
    private readonly ISpamAssassinConnectionSource _connections;
    private readonly SpamAssassinClientOptions _options;
    private readonly ResponseParser _parser = new();

    // One manager per client, shared by every exchange it runs.
    private readonly RecyclableMemoryStreamManager _streams = new();

    /// <summary>
    /// Creates a client that gets its connections from a source.
    /// </summary>
    /// <param name="connections">Where to get a connection to the daemon. Asked once per command.</param>
    /// <param name="options">
    /// What to announce and which limits to hold the daemon to, or <see langword="null"/> for the defaults.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="connections"/> is <see langword="null"/>.</exception>
    public SpamAssassinClient(ISpamAssassinConnectionSource connections, SpamAssassinClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connections);

        _connections = connections;
        _options = options ?? new();
    }

    /// <summary>
    /// Creates a client that gets its connections from a pair of delegates.
    /// </summary>
    /// <remarks>
    /// Both are required, and that is deliberate: a client whose blocking methods had to wait on the asynchronous
    /// delegate would be the sync-over-async this library refuses to ship. Supply only the colour you use and pass a
    /// throwing delegate for the other if that is honest about your transport.
    /// </remarks>
    /// <param name="connect">Opens a connection, blocking. Takes no token: see the remarks on ISpamAssassinConnectionSource.Connect.</param>
    /// <param name="connectAsync">Opens a connection, asynchronously.</param>
    /// <param name="options">
    /// What to announce and which limits to hold the daemon to, or <see langword="null"/> for the defaults.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="connect"/> or <paramref name="connectAsync"/> is <see langword="null"/>.
    /// </exception>
    public SpamAssassinClient(
        Func<ISpamAssassinConnection> connect,
        Func<CancellationToken, Task<ISpamAssassinConnection>> connectAsync,
        SpamAssassinClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(connectAsync);

        _connections = new DelegateConnectionSource(connect, connectAsync);
        _options = options ?? new();
    }

    /// <inheritdoc />
    public Version Ping() =>
        Exchange(SpamcCommand.Ping, instruction: null, MessageBody.None, ResponseHandling.Head, destination: null).Head.ProtocolVersion;

    /// <inheritdoc />
    public async Task<Version> PingAsync(CancellationToken cancellationToken = default)
    {
        var exchange = await ExchangeAsync(SpamcCommand.Ping, instruction: null, MessageBody.None, ResponseHandling.Head, destination: null, cancellationToken).ConfigureAwait(false);

        return exchange.Head.ProtocolVersion;
    }

    /// <inheritdoc />
    public void Skip() =>
        Exchange(SpamcCommand.Skip, instruction: null, MessageBody.None, ResponseHandling.None, destination: null);

    /// <inheritdoc />
    public async Task SkipAsync(CancellationToken cancellationToken = default) =>
        await ExchangeAsync(SpamcCommand.Skip, instruction: null, MessageBody.None, ResponseHandling.None, destination: null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public SpamVerdict Scan(Stream message) =>
        ScanCore(MessageBody.FromSeekableStream(message));

    /// <inheritdoc />
    public SpamVerdict Scan(Stream message, long length) =>
        ScanCore(MessageBody.FromStream(message, length));

    /// <inheritdoc />
    public SpamVerdict Scan(ReadOnlySequence<byte> message) =>
        ScanCore(MessageBody.FromSequence(message));

    /// <inheritdoc />
    public SpamVerdict Scan(ReadOnlyMemory<byte> message) =>
        ScanCore(MessageBody.FromMemory(message));

    /// <inheritdoc />
    public Task<SpamVerdict> ScanAsync(Stream message, CancellationToken cancellationToken = default) =>
        ScanCoreAsync(MessageBody.FromSeekableStream(message), cancellationToken);

    /// <inheritdoc />
    public Task<SpamVerdict> ScanAsync(Stream message, long length, CancellationToken cancellationToken = default) =>
        ScanCoreAsync(MessageBody.FromStream(message, length), cancellationToken);

    /// <inheritdoc />
    public Task<SpamVerdict> ScanAsync(ReadOnlySequence<byte> message, CancellationToken cancellationToken = default) =>
        ScanCoreAsync(MessageBody.FromSequence(message), cancellationToken);

    /// <inheritdoc />
    public Task<SpamVerdict> ScanAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
        ScanCoreAsync(MessageBody.FromMemory(message), cancellationToken);

    /// <inheritdoc />
    public SymbolsResult Symbols(Stream message) =>
        SymbolsCore(MessageBody.FromSeekableStream(message));

    /// <inheritdoc />
    public SymbolsResult Symbols(Stream message, long length) =>
        SymbolsCore(MessageBody.FromStream(message, length));

    /// <inheritdoc />
    public SymbolsResult Symbols(ReadOnlySequence<byte> message) =>
        SymbolsCore(MessageBody.FromSequence(message));

    /// <inheritdoc />
    public SymbolsResult Symbols(ReadOnlyMemory<byte> message) =>
        SymbolsCore(MessageBody.FromMemory(message));

    /// <inheritdoc />
    public Task<SymbolsResult> SymbolsAsync(Stream message, CancellationToken cancellationToken = default) =>
        SymbolsCoreAsync(MessageBody.FromSeekableStream(message), cancellationToken);

    /// <inheritdoc />
    public Task<SymbolsResult> SymbolsAsync(Stream message, long length, CancellationToken cancellationToken = default) =>
        SymbolsCoreAsync(MessageBody.FromStream(message, length), cancellationToken);

    /// <inheritdoc />
    public Task<SymbolsResult> SymbolsAsync(ReadOnlySequence<byte> message, CancellationToken cancellationToken = default) =>
        SymbolsCoreAsync(MessageBody.FromSequence(message), cancellationToken);

    /// <inheritdoc />
    public Task<SymbolsResult> SymbolsAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
        SymbolsCoreAsync(MessageBody.FromMemory(message), cancellationToken);

    /// <inheritdoc />
    public ReportResult Report(Stream message) =>
        ReportCore(MessageBody.FromSeekableStream(message));

    /// <inheritdoc />
    public ReportResult Report(Stream message, long length) =>
        ReportCore(MessageBody.FromStream(message, length));

    /// <inheritdoc />
    public ReportResult Report(ReadOnlySequence<byte> message) =>
        ReportCore(MessageBody.FromSequence(message));

    /// <inheritdoc />
    public ReportResult Report(ReadOnlyMemory<byte> message) =>
        ReportCore(MessageBody.FromMemory(message));

    /// <inheritdoc />
    public Task<ReportResult> ReportAsync(Stream message, CancellationToken cancellationToken = default) =>
        ReportCoreAsync(MessageBody.FromSeekableStream(message), cancellationToken);

    /// <inheritdoc />
    public Task<ReportResult> ReportAsync(Stream message, long length, CancellationToken cancellationToken = default) =>
        ReportCoreAsync(MessageBody.FromStream(message, length), cancellationToken);

    /// <inheritdoc />
    public Task<ReportResult> ReportAsync(ReadOnlySequence<byte> message, CancellationToken cancellationToken = default) =>
        ReportCoreAsync(MessageBody.FromSequence(message), cancellationToken);

    /// <inheritdoc />
    public Task<ReportResult> ReportAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
        ReportCoreAsync(MessageBody.FromMemory(message), cancellationToken);

    /// <inheritdoc />
    public ReportResult ReportIfSpam(Stream message) =>
        ReportIfSpamCore(MessageBody.FromSeekableStream(message));

    /// <inheritdoc />
    public ReportResult ReportIfSpam(Stream message, long length) =>
        ReportIfSpamCore(MessageBody.FromStream(message, length));

    /// <inheritdoc />
    public ReportResult ReportIfSpam(ReadOnlySequence<byte> message) =>
        ReportIfSpamCore(MessageBody.FromSequence(message));

    /// <inheritdoc />
    public ReportResult ReportIfSpam(ReadOnlyMemory<byte> message) =>
        ReportIfSpamCore(MessageBody.FromMemory(message));

    /// <inheritdoc />
    public Task<ReportResult> ReportIfSpamAsync(Stream message, CancellationToken cancellationToken = default) =>
        ReportIfSpamCoreAsync(MessageBody.FromSeekableStream(message), cancellationToken);

    /// <inheritdoc />
    public Task<ReportResult> ReportIfSpamAsync(Stream message, long length, CancellationToken cancellationToken = default) =>
        ReportIfSpamCoreAsync(MessageBody.FromStream(message, length), cancellationToken);

    /// <inheritdoc />
    public Task<ReportResult> ReportIfSpamAsync(ReadOnlySequence<byte> message, CancellationToken cancellationToken = default) =>
        ReportIfSpamCoreAsync(MessageBody.FromSequence(message), cancellationToken);

    /// <inheritdoc />
    public Task<ReportResult> ReportIfSpamAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
        ReportIfSpamCoreAsync(MessageBody.FromMemory(message), cancellationToken);

    /// <inheritdoc />
    public RewriteResult Process(Stream message, Stream destination) =>
        RewriteCore(SpamcCommand.Process, MessageBody.FromSeekableStream(message), RequireDestination(destination));

    /// <inheritdoc />
    public RewriteResult Process(Stream message, long length, Stream destination) =>
        RewriteCore(SpamcCommand.Process, MessageBody.FromStream(message, length), RequireDestination(destination));

    /// <inheritdoc />
    public RewriteResult Process(ReadOnlySequence<byte> message, Stream destination) =>
        RewriteCore(SpamcCommand.Process, MessageBody.FromSequence(message), RequireDestination(destination));

    /// <inheritdoc />
    public RewriteResult Process(ReadOnlyMemory<byte> message, Stream destination) =>
        RewriteCore(SpamcCommand.Process, MessageBody.FromMemory(message), RequireDestination(destination));

    /// <inheritdoc />
    public Task<RewriteResult> ProcessAsync(Stream message, Stream destination, CancellationToken cancellationToken = default) =>
        RewriteCoreAsync(SpamcCommand.Process, MessageBody.FromSeekableStream(message), RequireDestination(destination), cancellationToken);

    /// <inheritdoc />
    public Task<RewriteResult> ProcessAsync(Stream message, long length, Stream destination, CancellationToken cancellationToken = default) =>
        RewriteCoreAsync(SpamcCommand.Process, MessageBody.FromStream(message, length), RequireDestination(destination), cancellationToken);

    /// <inheritdoc />
    public Task<RewriteResult> ProcessAsync(ReadOnlySequence<byte> message, Stream destination, CancellationToken cancellationToken = default) =>
        RewriteCoreAsync(SpamcCommand.Process, MessageBody.FromSequence(message), RequireDestination(destination), cancellationToken);

    /// <inheritdoc />
    public Task<RewriteResult> ProcessAsync(ReadOnlyMemory<byte> message, Stream destination, CancellationToken cancellationToken = default) =>
        RewriteCoreAsync(SpamcCommand.Process, MessageBody.FromMemory(message), RequireDestination(destination), cancellationToken);

    /// <inheritdoc />
    public RewriteResult Headers(Stream message, Stream destination) =>
        RewriteCore(SpamcCommand.Headers, MessageBody.FromSeekableStream(message), RequireDestination(destination));

    /// <inheritdoc />
    public RewriteResult Headers(Stream message, long length, Stream destination) =>
        RewriteCore(SpamcCommand.Headers, MessageBody.FromStream(message, length), RequireDestination(destination));

    /// <inheritdoc />
    public RewriteResult Headers(ReadOnlySequence<byte> message, Stream destination) =>
        RewriteCore(SpamcCommand.Headers, MessageBody.FromSequence(message), RequireDestination(destination));

    /// <inheritdoc />
    public RewriteResult Headers(ReadOnlyMemory<byte> message, Stream destination) =>
        RewriteCore(SpamcCommand.Headers, MessageBody.FromMemory(message), RequireDestination(destination));

    /// <inheritdoc />
    public Task<RewriteResult> HeadersAsync(Stream message, Stream destination, CancellationToken cancellationToken = default) =>
        RewriteCoreAsync(SpamcCommand.Headers, MessageBody.FromSeekableStream(message), RequireDestination(destination), cancellationToken);

    /// <inheritdoc />
    public Task<RewriteResult> HeadersAsync(Stream message, long length, Stream destination, CancellationToken cancellationToken = default) =>
        RewriteCoreAsync(SpamcCommand.Headers, MessageBody.FromStream(message, length), RequireDestination(destination), cancellationToken);

    /// <inheritdoc />
    public Task<RewriteResult> HeadersAsync(ReadOnlySequence<byte> message, Stream destination, CancellationToken cancellationToken = default) =>
        RewriteCoreAsync(SpamcCommand.Headers, MessageBody.FromSequence(message), RequireDestination(destination), cancellationToken);

    /// <inheritdoc />
    public Task<RewriteResult> HeadersAsync(ReadOnlyMemory<byte> message, Stream destination, CancellationToken cancellationToken = default) =>
        RewriteCoreAsync(SpamcCommand.Headers, MessageBody.FromMemory(message), RequireDestination(destination), cancellationToken);

    /// <inheritdoc />
    public TellResult Tell(TellInstruction instruction, Stream message) =>
        TellCore(RequireInstruction(instruction), MessageBody.FromSeekableStream(message));

    /// <inheritdoc />
    public TellResult Tell(TellInstruction instruction, Stream message, long length) =>
        TellCore(RequireInstruction(instruction), MessageBody.FromStream(message, length));

    /// <inheritdoc />
    public TellResult Tell(TellInstruction instruction, ReadOnlySequence<byte> message) =>
        TellCore(RequireInstruction(instruction), MessageBody.FromSequence(message));

    /// <inheritdoc />
    public TellResult Tell(TellInstruction instruction, ReadOnlyMemory<byte> message) =>
        TellCore(RequireInstruction(instruction), MessageBody.FromMemory(message));

    /// <inheritdoc />
    public Task<TellResult> TellAsync(TellInstruction instruction, Stream message, CancellationToken cancellationToken = default) =>
        TellCoreAsync(RequireInstruction(instruction), MessageBody.FromSeekableStream(message), cancellationToken);

    /// <inheritdoc />
    public Task<TellResult> TellAsync(TellInstruction instruction, Stream message, long length, CancellationToken cancellationToken = default) =>
        TellCoreAsync(RequireInstruction(instruction), MessageBody.FromStream(message, length), cancellationToken);

    /// <inheritdoc />
    public Task<TellResult> TellAsync(TellInstruction instruction, ReadOnlySequence<byte> message, CancellationToken cancellationToken = default) =>
        TellCoreAsync(RequireInstruction(instruction), MessageBody.FromSequence(message), cancellationToken);

    /// <inheritdoc />
    public Task<TellResult> TellAsync(TellInstruction instruction, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
        TellCoreAsync(RequireInstruction(instruction), MessageBody.FromMemory(message), cancellationToken);

    private static SpamVerdict RequireVerdict(in ExchangeResult exchange) =>
        exchange.Head.Verdict ?? throw new MalformedResponseSpamAssassinException("Unable to read the daemon's response. It answered a scanning command without the spam header that carries the verdict, which is what a daemon does when the request announced a protocol below 1.3.");

    private static void EnsureSuccess(in ResponseHead head)
    {
        if (head.Code != SpamAssassinResponseCode.Ok)
            throw new ErrorResponseSpamAssassinException(head.Code, head.ResponseText);
    }

    private static Stream RequireDestination(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
            throw new ArgumentException("Unable to write the rewritten message. The destination stream cannot be written to.", nameof(destination));

        return destination;
    }

    private static TellInstruction RequireInstruction(TellInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        return instruction;
    }

    // A clean message earns no report at all, and an empty body is how that arrives, so it becomes null rather than
    // an empty string.
    private static string? ReportOrNothing(in ExchangeResult exchange) =>
        exchange.Text is { Length: > 0 } report ? report : null;

    private SpamVerdict ScanCore(MessageBody message) =>
        RequireVerdict(Exchange(SpamcCommand.Check, instruction: null, message, ResponseHandling.Head, destination: null));

    private async Task<SpamVerdict> ScanCoreAsync(MessageBody message, CancellationToken cancellationToken)
    {
        var exchange = await ExchangeAsync(SpamcCommand.Check, instruction: null, message, ResponseHandling.Head, destination: null, cancellationToken).ConfigureAwait(false);

        return RequireVerdict(exchange);
    }

    private SymbolsResult SymbolsCore(MessageBody message)
    {
        var exchange = Exchange(SpamcCommand.Symbols, instruction: null, message, ResponseHandling.Text, destination: null);

        return new(RequireVerdict(exchange), _parser.ParseSymbols(exchange.Text));
    }

    private async Task<SymbolsResult> SymbolsCoreAsync(MessageBody message, CancellationToken cancellationToken)
    {
        var exchange = await ExchangeAsync(SpamcCommand.Symbols, instruction: null, message, ResponseHandling.Text, destination: null, cancellationToken).ConfigureAwait(false);

        return new(RequireVerdict(exchange), _parser.ParseSymbols(exchange.Text));
    }

    private ReportResult ReportCore(MessageBody message)
    {
        var exchange = Exchange(SpamcCommand.Report, instruction: null, message, ResponseHandling.Text, destination: null);

        return new(RequireVerdict(exchange), exchange.Text ?? string.Empty);
    }

    private async Task<ReportResult> ReportCoreAsync(MessageBody message, CancellationToken cancellationToken)
    {
        var exchange = await ExchangeAsync(SpamcCommand.Report, instruction: null, message, ResponseHandling.Text, destination: null, cancellationToken).ConfigureAwait(false);

        return new(RequireVerdict(exchange), exchange.Text ?? string.Empty);
    }

    private ReportResult ReportIfSpamCore(MessageBody message)
    {
        var exchange = Exchange(SpamcCommand.ReportIfSpam, instruction: null, message, ResponseHandling.Text, destination: null);

        return new(RequireVerdict(exchange), ReportOrNothing(exchange));
    }

    private async Task<ReportResult> ReportIfSpamCoreAsync(MessageBody message, CancellationToken cancellationToken)
    {
        var exchange = await ExchangeAsync(SpamcCommand.ReportIfSpam, instruction: null, message, ResponseHandling.Text, destination: null, cancellationToken).ConfigureAwait(false);

        return new(RequireVerdict(exchange), ReportOrNothing(exchange));
    }

    private RewriteResult RewriteCore(SpamcCommand command, MessageBody message, Stream destination)
    {
        var exchange = Exchange(command, instruction: null, message, ResponseHandling.Body, destination);

        return new(RequireVerdict(exchange), exchange.BodyLength);
    }

    private async Task<RewriteResult> RewriteCoreAsync(SpamcCommand command, MessageBody message, Stream destination, CancellationToken cancellationToken)
    {
        var exchange = await ExchangeAsync(command, instruction: null, message, ResponseHandling.Body, destination, cancellationToken).ConfigureAwait(false);

        return new(RequireVerdict(exchange), exchange.BodyLength);
    }

    private TellResult TellCore(TellInstruction instruction, MessageBody message)
    {
        var exchange = Exchange(SpamcCommand.Tell, instruction, message, ResponseHandling.Head, destination: null);

        return new(exchange.Head.DidSet, exchange.Head.DidRemove);
    }

    private async Task<TellResult> TellCoreAsync(TellInstruction instruction, MessageBody message, CancellationToken cancellationToken)
    {
        var exchange = await ExchangeAsync(SpamcCommand.Tell, instruction, message, ResponseHandling.Head, destination: null, cancellationToken).ConfigureAwait(false);

        return new(exchange.Head.DidSet, exchange.Head.DidRemove);
    }

    private ExchangeResult Exchange(
        SpamcCommand command,
        TellInstruction? instruction,
        MessageBody message,
        ResponseHandling handling,
        Stream? destination)
    {
        var transferBuffer = ArrayPool<byte>.Shared.Rent(_options.TransferBufferSize);

        try
        {
            using var head = new HeadWriter();

            var writer = new RequestWriter(_options, head, transferBuffer, _streams);

            using var compressed = _options.CompressRequests && message.HasContent ? writer.Compress(message) : null;
            var body = compressed is null ? message : MessageBody.FromStream(compressed, compressed.Length);

            writer.BuildHead(command, instruction, body.HasContent ? body.Length : null, compressed is not null);

            using var connection = _connections.Connect();

            writer.WriteTo(connection.RequestStream, body);
            connection.CompleteRequest();

            if (handling == ResponseHandling.None)
                return new(ResponseHead.None, null, 0);

            using var reader = new ResponseReader(connection.ResponseStream, _options, _parser, _streams);

            var responseHead = reader.ReadHead();

            EnsureSuccess(responseHead);

            return handling switch
            {
                ResponseHandling.Text => new(responseHead, reader.ReadText(responseHead.ContentLength), 0),
                ResponseHandling.Body => new(responseHead, null, reader.CopyBody(destination!, responseHead.ContentLength)),
                _ => new(responseHead, null, 0),
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(transferBuffer, clearArray: true);
        }
    }

    private async Task<ExchangeResult> ExchangeAsync(
        SpamcCommand command,
        TellInstruction? instruction,
        MessageBody message,
        ResponseHandling handling,
        Stream? destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var transferBuffer = ArrayPool<byte>.Shared.Rent(_options.TransferBufferSize);

        // Disposed in the finally rather than by an await using: the declaration form generates a DisposeAsync the
        // ConfigureAwait rule cannot reach (CAC001), and the block form cannot take a null.
        RecyclableMemoryStream? compressed = null;

        try
        {
            using var head = new HeadWriter();

            var writer = new RequestWriter(_options, head, transferBuffer, _streams);

            if (_options.CompressRequests && message.HasContent)
                compressed = await writer.CompressAsync(message, cancellationToken).ConfigureAwait(false);

            var body = compressed is null ? message : MessageBody.FromStream(compressed, compressed.Length);

            writer.BuildHead(command, instruction, body.HasContent ? body.Length : null, compressed is not null);

            var connection = await _connections.ConnectAsync(cancellationToken).ConfigureAwait(false);

            await using (connection.ConfigureAwait(false))
            {
                await writer.WriteToAsync(connection.RequestStream, body, cancellationToken).ConfigureAwait(false);
                await connection.CompleteRequestAsync(cancellationToken).ConfigureAwait(false);

                if (handling == ResponseHandling.None)
                    return new(ResponseHead.None, null, 0);

                using var reader = new ResponseReader(connection.ResponseStream, _options, _parser, _streams);

                var responseHead = await reader.ReadHeadAsync(cancellationToken).ConfigureAwait(false);

                EnsureSuccess(responseHead);

                // Deliberately the same shape as the switch in Exchange: the two are only safe because they can be
                // read side by side, and a difference in form hides a difference in meaning.
                return handling switch
                {
                    ResponseHandling.Text => new(responseHead, await reader.ReadTextAsync(responseHead.ContentLength, cancellationToken).ConfigureAwait(false), 0),
                    ResponseHandling.Body => new(responseHead, null, await reader.CopyBodyAsync(destination!, responseHead.ContentLength, maxLength: null, cancellationToken).ConfigureAwait(false)),
                    _ => new(responseHead, null, 0),
                };
            }
        }
        finally
        {
            if (compressed is not null)
                await compressed.DisposeAsync().ConfigureAwait(false);

            ArrayPool<byte>.Shared.Return(transferBuffer, clearArray: true);
        }
    }
}
