// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// Talks to SpamAssassin's scanning daemon: hands it a message and returns what it said about it.
/// </summary>
/// <remarks>
/// <para>
/// One method per thing the protocol can do, and each of them is one complete exchange with the daemon - a
/// request written, a response read, the connection finished with. The daemon serves exactly one command per
/// connection and then closes it, so the client is given a way to obtain a connection rather than a
/// connection, and every call gets a fresh one. Nothing here holds a socket open between calls, and nothing
/// here opens one either: the transport is the caller's, and this interface only speaks over it.
/// </para>
/// <para>
/// <strong>The four ways to hand over a message.</strong> The daemon has to be told the length before the
/// body, so a message whose length cannot be established has to be buffered whole - which is a decision made
/// here rather than hidden:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="Stream"/> alone - read from its current position to the end, and it must be able to seek, so
/// that the length is read rather than guessed. Nothing is buffered.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Stream"/> with an explicit length - for a stream that cannot seek but whose length the caller
/// knows anyway. Nothing is buffered, and the caller carries the promise: a length that turns out to be wrong
/// makes the daemon answer <see cref="SpamAssassinResponseCode.Protocol"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="ReadOnlySequence{T}"/> or <see cref="ReadOnlyMemory{T}"/> - a message that is already in
/// memory, in one piece or several. The length is free and no copy is made.
/// </description>
/// </item>
/// </list>
/// <para>
/// The one exception to "nothing is buffered" is compression: when the client is configured to offer
/// <c>Compress: zlib</c>, the length the daemon needs is the length of the <em>compressed</em> body, which is
/// not known until the body has been compressed. Compressing a request therefore buffers it, whichever
/// overload was used.
/// </para>
/// <para>
/// <strong>Two colours of every operation.</strong> The <c>Async</c> methods await the streams and take a
/// <see cref="CancellationToken"/>, which is honoured between reads and writes. The ones without the suffix
/// block on the streams, genuinely, rather than waiting on a task - and they take no token, because there is
/// nothing honest for one to do there: a blocking read already in progress cannot be interrupted, so a token
/// would only be checked between operations and would promise more than it delivers. A synchronous caller who
/// needs a bound puts a timeout on the streams it supplies, which is the only thing that actually cuts a read
/// short.
/// </para>
/// <para>
/// <strong>What goes wrong, and how.</strong> A response code other than
/// <see cref="SpamAssassinResponseCode.Ok"/> is a refusal with no body behind it, and surfaces as
/// <see cref="ErrorResponseSpamAssassinException"/>. A response that cannot be read as one at all surfaces as
/// <see cref="SpamAssassinException"/>. Failures of the caller's streams are not wrapped: an
/// <see cref="IOException"/> stays one, so a connection problem still looks like a connection problem.
/// </para>
/// <para>
/// <strong>Concurrency.</strong> An implementation holds no per-call state, so calls may overlap as long as
/// the connections they are given are independent. Whether they are is a property of the connection source
/// the client was built with, not of the client.
/// </para>
/// </remarks>
public interface ISpamAssassinClient
{
    /// <summary>
    /// Asks the daemon to confirm that it is alive and answering.
    /// </summary>
    /// <remarks>
    /// No message is sent and nothing is scanned, which makes this the cheapest thing to put in a health
    /// check. It still costs a connection.
    /// </remarks>
    /// <returns>The protocol version the daemon announced in its answer.</returns>
    Version Ping();

    /// <inheritdoc cref="Ping"/>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the protocol version the daemon announced in its answer.</returns>
    Task<Version> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the daemon that this connection has nothing for it after all.
    /// </summary>
    /// <remarks>
    /// The polite end to a connection that was opened before the decision to scan was made. The daemon
    /// answers nothing and closes; there is no way to learn anything from it, and not calling this is not an
    /// error either - it only saves the daemon from logging a client that vanished.
    /// </remarks>
    void Skip();

    /// <inheritdoc cref="Skip"/>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes once the daemon has been told.</returns>
    Task SkipAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Scan(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from, or cannot seek, so its length cannot be established.</exception>
    SpamVerdict Scan(Stream message);

    /// <inheritdoc cref="Scan(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    SpamVerdict Scan(Stream message, long length);

    /// <summary>
    /// Asks whether the message is spam, and for nothing else.
    /// </summary>
    /// <remarks>
    /// The cheapest of the scanning commands: no report, no rule names, no rewritten message - the daemon
    /// sends the verdict and closes. It is what a filter that only wants to decide accept-or-reject should
    /// ask for.
    /// </remarks>
    /// <param name="message">The message, already in memory.</param>
    /// <returns>The daemon's verdict.</returns>
    /// <exception cref="ErrorResponseSpamAssassinException">The daemon refused the request.</exception>
    /// <exception cref="SpamAssassinException">The daemon answered something that could not be read as a response, stopped mid-answer, or sent more than the configured limits allow.</exception>
    SpamVerdict Scan(ReadOnlySequence<byte> message);

    /// <inheritdoc cref="Scan(ReadOnlySequence{byte})"/>
    SpamVerdict Scan(ReadOnlyMemory<byte> message);

    /// <inheritdoc cref="Scan(Stream)"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the daemon's verdict.</returns>
    Task<SpamVerdict> ScanAsync(Stream message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Scan(Stream, long)"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the daemon's verdict.</returns>
    Task<SpamVerdict> ScanAsync(Stream message, long length, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Scan(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the daemon's verdict.</returns>
    Task<SpamVerdict> ScanAsync(ReadOnlySequence<byte> message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Scan(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the daemon's verdict.</returns>
    Task<SpamVerdict> ScanAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Symbols(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from, or cannot seek, so its length cannot be established.</exception>
    SymbolsResult Symbols(Stream message);

    /// <inheritdoc cref="Symbols(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    SymbolsResult Symbols(Stream message, long length);

    /// <summary>
    /// Asks whether the message is spam, and which rules said so.
    /// </summary>
    /// <remarks>
    /// The verdict plus the names of the rules that fired - enough to explain a score without asking for the
    /// prose report. The names come from the daemon's rule set, so they are stable only as long as it is.
    /// </remarks>
    /// <param name="message">The message, already in memory.</param>
    /// <returns>The verdict and the rules that produced it.</returns>
    /// <exception cref="ErrorResponseSpamAssassinException">The daemon refused the request.</exception>
    /// <exception cref="SpamAssassinException">The daemon answered something that could not be read as a response, stopped mid-answer, or sent more than the configured limits allow.</exception>
    SymbolsResult Symbols(ReadOnlySequence<byte> message);

    /// <inheritdoc cref="Symbols(ReadOnlySequence{byte})"/>
    SymbolsResult Symbols(ReadOnlyMemory<byte> message);

    /// <inheritdoc cref="Symbols(Stream)"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and the rules that produced it.</returns>
    Task<SymbolsResult> SymbolsAsync(Stream message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Symbols(Stream, long)"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and the rules that produced it.</returns>
    Task<SymbolsResult> SymbolsAsync(Stream message, long length, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Symbols(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and the rules that produced it.</returns>
    Task<SymbolsResult> SymbolsAsync(ReadOnlySequence<byte> message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Symbols(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and the rules that produced it.</returns>
    Task<SymbolsResult> SymbolsAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Report(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from, or cannot seek, so its length cannot be established.</exception>
    ReportResult Report(Stream message);

    /// <inheritdoc cref="Report(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    ReportResult Report(Stream message, long length);

    /// <summary>
    /// Asks whether the message is spam, and for the report explaining the answer.
    /// </summary>
    /// <remarks>
    /// The report is prose meant for a person: which rules hit, and what each of them cost. Use
    /// <see cref="ReportIfSpam(Stream)"/> instead when a report about a clean message is of
    /// no interest, and the daemon can be spared writing it.
    /// </remarks>
    /// <param name="message">The message, already in memory.</param>
    /// <returns>The verdict and the report, which is never <see langword="null"/> here.</returns>
    /// <exception cref="ErrorResponseSpamAssassinException">The daemon refused the request.</exception>
    /// <exception cref="SpamAssassinException">The daemon answered something that could not be read as a response, stopped mid-answer, or sent more than the configured limits allow.</exception>
    ReportResult Report(ReadOnlySequence<byte> message);

    /// <inheritdoc cref="Report(ReadOnlySequence{byte})"/>
    ReportResult Report(ReadOnlyMemory<byte> message);

    /// <inheritdoc cref="Report(Stream)"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and the report.</returns>
    Task<ReportResult> ReportAsync(Stream message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Report(Stream, long)"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and the report.</returns>
    Task<ReportResult> ReportAsync(Stream message, long length, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Report(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and the report.</returns>
    Task<ReportResult> ReportAsync(ReadOnlySequence<byte> message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Report(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and the report.</returns>
    Task<ReportResult> ReportAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ReportIfSpam(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from, or cannot seek, so its length cannot be established.</exception>
    ReportResult ReportIfSpam(Stream message);

    /// <inheritdoc cref="ReportIfSpam(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    ReportResult ReportIfSpam(Stream message, long length);

    /// <summary>
    /// Asks whether the message is spam, and for the report only if it is.
    /// </summary>
    /// <remarks>
    /// The same as <see cref="Report(Stream)"/> for a message the daemon calls spam. For one
    /// it calls clean, it sends no report at all - not an empty one - and the result carries
    /// <see langword="null"/> to say so.
    /// </remarks>
    /// <param name="message">The message, already in memory.</param>
    /// <returns>The verdict, and the report when the message is spam.</returns>
    /// <exception cref="ErrorResponseSpamAssassinException">The daemon refused the request.</exception>
    /// <exception cref="SpamAssassinException">The daemon answered something that could not be read as a response, stopped mid-answer, or sent more than the configured limits allow.</exception>
    ReportResult ReportIfSpam(ReadOnlySequence<byte> message);

    /// <inheritdoc cref="ReportIfSpam(ReadOnlySequence{byte})"/>
    ReportResult ReportIfSpam(ReadOnlyMemory<byte> message);

    /// <inheritdoc cref="ReportIfSpam(Stream)"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict, and the report when the message is spam.</returns>
    Task<ReportResult> ReportIfSpamAsync(Stream message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ReportIfSpam(Stream, long)"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict, and the report when the message is spam.</returns>
    Task<ReportResult> ReportIfSpamAsync(Stream message, long length, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ReportIfSpam(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict, and the report when the message is spam.</returns>
    Task<ReportResult> ReportIfSpamAsync(ReadOnlySequence<byte> message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ReportIfSpam(ReadOnlySequence{byte})"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict, and the report when the message is spam.</returns>
    Task<ReportResult> ReportIfSpamAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Process(ReadOnlySequence{byte}, Stream)"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <param name="destination">Where the rewritten message is written. Not closed, and not rewound.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from, or cannot seek, so its length cannot be established; or <paramref name="destination"/> cannot be written to.</exception>
    RewriteResult Process(Stream message, Stream destination);

    /// <inheritdoc cref="Process(ReadOnlySequence{byte}, Stream)"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <param name="destination">Where the rewritten message is written. Not closed, and not rewound.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from, or <paramref name="destination"/> cannot be written to.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    RewriteResult Process(Stream message, long length, Stream destination);

    /// <summary>
    /// Has the daemon rewrite the message with its findings in it, and writes the result to
    /// <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// What comes back is a complete message: the original with SpamAssassin's headers added, and - for a
    /// message it calls spam, depending on how it is configured - the body rewritten into a report with the
    /// original attached. It is copied straight into <paramref name="destination"/> as it arrives, so a
    /// message far larger than memory is no harder than a small one.
    /// </remarks>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="destination">Where the rewritten message is written. Not closed, and not rewound.</param>
    /// <returns>The verdict, and how much was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> cannot be written to.</exception>
    /// <exception cref="ErrorResponseSpamAssassinException">The daemon refused the request.</exception>
    /// <exception cref="SpamAssassinException">The daemon answered something that could not be read as a response, stopped mid-answer, or sent more than the configured limits allow.</exception>
    RewriteResult Process(ReadOnlySequence<byte> message, Stream destination);

    /// <inheritdoc cref="Process(ReadOnlySequence{byte}, Stream)"/>
    RewriteResult Process(ReadOnlyMemory<byte> message, Stream destination);

    /// <inheritdoc cref="Process(Stream, Stream)"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <param name="destination">Where the rewritten message is written. Not closed, and not rewound.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and how much was written.</returns>
    Task<RewriteResult> ProcessAsync(Stream message, Stream destination, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Process(Stream, long, Stream)"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <param name="destination">Where the rewritten message is written. Not closed, and not rewound.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and how much was written.</returns>
    Task<RewriteResult> ProcessAsync(Stream message, long length, Stream destination, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Process(ReadOnlySequence{byte}, Stream)"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="destination">Where the rewritten message is written. Not closed, and not rewound.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and how much was written.</returns>
    Task<RewriteResult> ProcessAsync(ReadOnlySequence<byte> message, Stream destination, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Process(ReadOnlySequence{byte}, Stream)"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="destination">Where the rewritten message is written. Not closed, and not rewound.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and how much was written.</returns>
    Task<RewriteResult> ProcessAsync(ReadOnlyMemory<byte> message, Stream destination, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Headers(ReadOnlySequence{byte}, Stream)"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <param name="destination">Where the rewritten headers are written. Not closed, and not rewound.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from, or cannot seek, so its length cannot be established; or <paramref name="destination"/> cannot be written to.</exception>
    RewriteResult Headers(Stream message, Stream destination);

    /// <inheritdoc cref="Headers(ReadOnlySequence{byte}, Stream)"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <param name="destination">Where the rewritten headers are written. Not closed, and not rewound.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from, or <paramref name="destination"/> cannot be written to.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    RewriteResult Headers(Stream message, long length, Stream destination);

    /// <summary>
    /// Has the daemon rewrite the message, and writes only its headers to <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// The same work as <see cref="Process(Stream, Stream)"/>, with the body left off the
    /// answer: what arrives ends after the blank line that separates headers from body. The saving is on the
    /// wire, not in the daemon - it rewrote the whole message either way - and it is the right command for a
    /// caller who already has the body and only wants the markup to add to it.
    /// </remarks>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="destination">Where the rewritten headers are written. Not closed, and not rewound.</param>
    /// <returns>The verdict, and how much was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> cannot be written to.</exception>
    /// <exception cref="ErrorResponseSpamAssassinException">The daemon refused the request.</exception>
    /// <exception cref="SpamAssassinException">The daemon answered something that could not be read as a response, stopped mid-answer, or sent more than the configured limits allow.</exception>
    RewriteResult Headers(ReadOnlySequence<byte> message, Stream destination);

    /// <inheritdoc cref="Headers(ReadOnlySequence{byte}, Stream)"/>
    RewriteResult Headers(ReadOnlyMemory<byte> message, Stream destination);

    /// <inheritdoc cref="Headers(Stream, Stream)"/>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <param name="destination">Where the rewritten headers are written. Not closed, and not rewound.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and how much was written.</returns>
    Task<RewriteResult> HeadersAsync(Stream message, Stream destination, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Headers(Stream, long, Stream)"/>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <param name="destination">Where the rewritten headers are written. Not closed, and not rewound.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and how much was written.</returns>
    Task<RewriteResult> HeadersAsync(Stream message, long length, Stream destination, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Headers(ReadOnlySequence{byte}, Stream)"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="destination">Where the rewritten headers are written. Not closed, and not rewound.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and how much was written.</returns>
    Task<RewriteResult> HeadersAsync(ReadOnlySequence<byte> message, Stream destination, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Headers(ReadOnlySequence{byte}, Stream)"/>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="destination">Where the rewritten headers are written. Not closed, and not rewound.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with the verdict and how much was written.</returns>
    Task<RewriteResult> HeadersAsync(ReadOnlyMemory<byte> message, Stream destination, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Tell(TellInstruction, ReadOnlySequence{byte})"/>
    /// <param name="instruction">What to do to which database, from the factories on <see cref="TellInstruction"/>.</param>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="instruction"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from, or cannot seek, so its length cannot be established.</exception>
    TellResult Tell(TellInstruction instruction, Stream message);

    /// <inheritdoc cref="Tell(TellInstruction, ReadOnlySequence{byte})"/>
    /// <param name="instruction">What to do to which database, from the factories on <see cref="TellInstruction"/>.</param>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="instruction"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> cannot be read from.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    TellResult Tell(TellInstruction instruction, Stream message, long length);

    /// <summary>
    /// Teaches the daemon something about the message: learns it, forgets it, reports it or revokes it,
    /// according to <paramref name="instruction"/>.
    /// </summary>
    /// <remarks>
    /// Nothing is scanned and no verdict comes back - this is the write side of the protocol. The daemon has to
    /// have been started with <c>--allow-tell</c> or it refuses outright, and what it reports doing may be less
    /// than was asked of it without that being an error; see <see cref="TellResult"/>.
    /// </remarks>
    /// <param name="instruction">What to do to which database, from the factories on <see cref="TellInstruction"/>.</param>
    /// <param name="message">The message, already in memory.</param>
    /// <returns>What the daemon actually changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instruction"/> is <see langword="null"/>.</exception>
    /// <exception cref="ErrorResponseSpamAssassinException">The daemon refused the request - <see cref="SpamAssassinResponseCode.Unavailable"/> when it does not allow tell requests at all.</exception>
    /// <exception cref="SpamAssassinException">The daemon answered something that could not be read as a response, stopped mid-answer, or sent more than the configured limits allow.</exception>
    TellResult Tell(TellInstruction instruction, ReadOnlySequence<byte> message);

    /// <inheritdoc cref="Tell(TellInstruction, ReadOnlySequence{byte})"/>
    TellResult Tell(TellInstruction instruction, ReadOnlyMemory<byte> message);

    /// <inheritdoc cref="Tell(TellInstruction, Stream)"/>
    /// <param name="instruction">What to do to which database, from the factories on <see cref="TellInstruction"/>.</param>
    /// <param name="message">The message, read from its current position to the end. Must be seekable.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with what the daemon actually changed.</returns>
    Task<TellResult> TellAsync(TellInstruction instruction, Stream message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Tell(TellInstruction, Stream, long)"/>
    /// <param name="instruction">What to do to which database, from the factories on <see cref="TellInstruction"/>.</param>
    /// <param name="message">The message, read from its current position.</param>
    /// <param name="length">How many bytes of <paramref name="message"/> to send. The caller vouches for it.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with what the daemon actually changed.</returns>
    Task<TellResult> TellAsync(TellInstruction instruction, Stream message, long length, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Tell(TellInstruction, ReadOnlySequence{byte})"/>
    /// <param name="instruction">What to do to which database, from the factories on <see cref="TellInstruction"/>.</param>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with what the daemon actually changed.</returns>
    Task<TellResult> TellAsync(TellInstruction instruction, ReadOnlySequence<byte> message, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Tell(TellInstruction, ReadOnlySequence{byte})"/>
    /// <param name="instruction">What to do to which database, from the factories on <see cref="TellInstruction"/>.</param>
    /// <param name="message">The message, already in memory.</param>
    /// <param name="cancellationToken">Cancels the exchange. Honoured between reads and writes, not during one.</param>
    /// <returns>A task that completes with what the daemon actually changed.</returns>
    Task<TellResult> TellAsync(TellInstruction instruction, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);
}
