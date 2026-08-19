// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Net.Sockets;
using Josupeit.Mail.SpamAssassin.Client;

namespace Spamc;

// The transport the library deliberately does not ship, and the whole of what a consumer has to write to reach a
// daemon: two streams - here the same one twice, as they usually are - and a way to be let go of.
//
// This one is over a Socket rather than a TcpClient, which is the only reason it differs from the copy in the
// package readme: a Unix domain socket is a Socket and is not a TcpClient, and -U has to work.
internal sealed class SpamdConnection(Socket socket) : ISpamAssassinConnection
{
    private readonly NetworkStream _stream = new(socket, ownsSocket: true);

    public Stream RequestStream => _stream;

    public Stream ResponseStream => _stream;

    // The default only flushes. On a socket there is more to say: shutting down the sending half is how the
    // protocol expects a client to announce that its request is finished, and a daemon reading a request without a
    // length header depends on hearing it.
    public void CompleteRequest() => socket.Shutdown(SocketShutdown.Send);

    public Task CompleteRequestAsync(CancellationToken cancellationToken = default)
    {
        CompleteRequest();

        return Task.CompletedTask;
    }

    public void Dispose() => _stream.Dispose();

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
