// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Net;
using System.Net.Sockets;
using Josupeit.Mail.SpamAssassin.Client;

namespace Spamc;

// Where the client gets a connection from, which it asks for once per command: the daemon serves exactly one
// request per connection and then closes, so there is nothing here to pool or to keep alive.
internal sealed class SpamdConnectionSource(SpamcOptions options) : ISpamAssassinConnectionSource
{
    public ISpamAssassinConnection Connect()
    {
        var socket = NewSocket();

        try
        {
            socket.Connect(EndPoint());

            return new SpamdConnection(socket);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }

    public async Task<ISpamAssassinConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var socket = NewSocket();

        try
        {
            await socket.ConnectAsync(EndPoint(), cancellationToken);

            return new SpamdConnection(socket);
        }
        catch
        {
            // A socket that failed to connect is still a socket, and nothing else is going to close it: the
            // client only ever disposes a connection it was handed.
            socket.Dispose();

            throw;
        }
    }

    private Socket NewSocket()
    {
        var socket = options.SocketPath is null
            ? new Socket(SocketType.Stream, ProtocolType.Tcp)
            : new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        // These bound the blocking colour and nothing else - they are what Socket applies to a blocking read or
        // write. The awaited colour is bounded by its cancellation token instead, which is the honest way round:
        // a blocking read already in flight cannot be interrupted by a token, and a timeout on the stream is the
        // only thing that actually cuts it short. This sample awaits, so they are here for the reader.
        socket.SendTimeout = (int)options.Timeout.TotalMilliseconds;
        socket.ReceiveTimeout = (int)options.Timeout.TotalMilliseconds;

        return socket;
    }

    // A DnsEndPoint rather than a resolved address, so the socket does the lookup and a name with several
    // addresses is tried in turn. spamc resolves the name itself in order to offer -H; this does not.
    private EndPoint EndPoint() => options.SocketPath is { } path
        ? new UnixDomainSocketEndPoint(path)
        : new DnsEndPoint(options.Host, options.Port);
}
