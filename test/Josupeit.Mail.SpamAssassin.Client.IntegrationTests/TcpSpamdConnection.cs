// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Net.Sockets;

namespace Josupeit.Mail.SpamAssassin.Client.IntegrationTests;

// The transport the library deliberately does not provide, written the way a consumer would write it. It is the same
// implementation the package readme shows; if one changes, so does the other.
internal sealed class TcpSpamdConnection(TcpClient socket) : ISpamAssassinConnection
{
    private readonly NetworkStream _stream = socket.GetStream();

    public Stream RequestStream => _stream;

    public Stream ResponseStream => _stream;

    // The default only flushes. Shutting down the sending half is how the protocol expects a client to announce that
    // its request is finished.
    public void CompleteRequest() => socket.Client.Shutdown(SocketShutdown.Send);

    public Task CompleteRequestAsync(CancellationToken cancellationToken = default)
    {
        CompleteRequest();

        return Task.CompletedTask;
    }

    public void Dispose() => socket.Dispose();

    public ValueTask DisposeAsync()
    {
        socket.Dispose();

        return default;
    }
}
