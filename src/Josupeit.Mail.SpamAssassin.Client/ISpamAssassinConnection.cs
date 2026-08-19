// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// One conversation with the daemon: a stream to write the request to, and a stream to read the response from.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of the transport as far as this library is concerned. Where the two streams come from - a
/// TCP socket, a Unix domain socket, a TLS tunnel, a pair of buffers in a test - is the implementer's business,
/// and so is whether they are in fact the same stream, which they usually are.
/// </para>
/// <para>
/// A connection is used for exactly one command and then disposed, because that is all the daemon allows: it
/// serves one request per connection and closes. An implementation therefore does not need to be reusable, and
/// one that pools something underneath should do that behind <see cref="ISpamAssassinConnectionSource"/> rather
/// than by surviving its own disposal.
/// </para>
/// </remarks>
public interface ISpamAssassinConnection : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// The stream the request is written to.
    /// </summary>
    Stream RequestStream { get; }

    /// <summary>
    /// The stream the response is read from.
    /// </summary>
    Stream ResponseStream { get; }

    /// <summary>
    /// Called once the whole request has been written, before anything is read back.
    /// </summary>
    /// <remarks>
    /// The default flushes <see cref="RequestStream"/>, which is what a buffered stream needs and what an
    /// unbuffered one shrugs at. Override it where the transport can say more than that: on a socket, this is
    /// where the sending half is shut down, which is how the protocol expects a client to signal that it is
    /// done writing. Every request this library sends announces its length, so a daemon does not depend on that
    /// signal - but a daemon reading a request without one does, and shutting down costs nothing here.
    /// </remarks>
    void CompleteRequest() => RequestStream.Flush();

    /// <inheritdoc cref="CompleteRequest"/>
    /// <param name="cancellationToken">Cancels the wait. Shutting down a socket's sending half can block on a full
    /// send buffer, which is the case this is here for.</param>
    /// <returns>A task that completes once the daemon has been told that the request is finished.</returns>
    Task CompleteRequestAsync(CancellationToken cancellationToken = default) => RequestStream.FlushAsync(cancellationToken);
}
