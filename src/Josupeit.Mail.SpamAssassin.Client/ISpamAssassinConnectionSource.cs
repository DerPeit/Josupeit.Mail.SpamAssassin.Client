// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Threading;
using System.Threading.Tasks;

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// Where a <see cref="SpamAssassinClient"/> gets its connections from.
/// </summary>
/// <remarks>
/// Implement this - or hand the equivalent pair of delegates to the client's constructor - to decide how the
/// daemon is reached. It is called once per command, so an implementation is asked for a connection often and
/// should be cheap; it is also called from wherever the caller called the client, so it has to tolerate being
/// used concurrently if the client is.
/// </remarks>
public interface ISpamAssassinConnectionSource
{
    /// <summary>
    /// Opens a connection to the daemon, blocking until it is there.
    /// </summary>
    /// <remarks>
    /// No cancellation token, deliberately: the blocking half of this library takes none anywhere, because a token
    /// cannot interrupt a blocking call once it is in flight. Bound this the way the call itself can be bound - with a
    /// connect timeout on whatever is underneath.
    /// </remarks>
    /// <returns>The connection, which the client disposes when the command is over.</returns>
    ISpamAssassinConnection Connect();

    /// <inheritdoc cref="Connect"/>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>A task that completes with the connection, which the client disposes when the command is over.</returns>
    Task<ISpamAssassinConnection> ConnectAsync(CancellationToken cancellationToken = default);
}
