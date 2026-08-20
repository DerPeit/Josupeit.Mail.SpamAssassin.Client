// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// Both delegates are required: a synchronous half waiting on the asynchronous one would be the sync-over-async
// this library refuses to ship.
internal sealed class DelegateConnectionSource(
    Func<ISpamAssassinConnection> connect,
    Func<CancellationToken, Task<ISpamAssassinConnection>> connectAsync) : ISpamAssassinConnectionSource
{
    public ISpamAssassinConnection Connect() => connect();

    public Task<ISpamAssassinConnection> ConnectAsync(CancellationToken cancellationToken = default) => connectAsync(cancellationToken);
}
