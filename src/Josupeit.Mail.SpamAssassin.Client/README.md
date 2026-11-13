# Josupeit.Mail.SpamAssassin.Client

A client for the **spamc/spamd protocol** - the HTTP-like request/response protocol that SpamAssassin's scanning daemon speaks - for .NET.

```
dotnet add package Josupeit.Mail.SpamAssassin.Client
```

Targets `net8.0`, announces protocol version **1.5**, and covers the full command set: `CHECK`, `SYMBOLS`, `REPORT`, `REPORT_IFSPAM`, `PROCESS`, `HEADERS`, `TELL`, `PING` and `SKIP`. Every operation comes in two colours - an `async` one and a genuinely blocking one - and the package depends on nothing outside the framework but `Microsoft.IO.RecyclableMemoryStream`, which pools the buffers it cannot avoid.

## The transport is yours

The library never opens a socket, resolves a host, or knows whether `spamd` runs beside it or across a data centre. It is handed a pair of streams - one it writes the request to, one it reads the response from - and speaks the protocol over them. TCP, a Unix domain socket, TLS through stunnel, or a pair of `MemoryStream`s in a test are all the same to it.

`spamd` serves exactly one command per connection and then closes it, so the client is constructed with a factory that produces a fresh connection per call rather than with a connection of its own. One client instance therefore serves as many scans as you like, without pretending the daemon supports something it does not.

## Using it

Teach the library how to reach the daemon by implementing one small interface - two streams and a way to be disposed:

```csharp
internal sealed class TcpSpamdConnection(TcpClient socket) : ISpamAssassinConnection
{
    private readonly NetworkStream _stream = socket.GetStream();

    public Stream RequestStream => _stream;

    public Stream ResponseStream => _stream;

    // The default only flushes. On a socket there is more to say: shutting down the sending half is how the
    // protocol expects a client to announce that its request is finished.
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
```

Then ask it things. One client instance serves any number of scans; it takes a fresh connection for each, because that is all the daemon allows:

```csharp
var client = new SpamAssassinClient(
    connect: () => new TcpSpamdConnection(new TcpClient("spamd.example.com", 783)),
    connectAsync: async cancellationToken =>
    {
        var socket = new TcpClient();

        try
        {
            await socket.ConnectAsync("spamd.example.com", 783, cancellationToken);

            return new TcpSpamdConnection(socket);
        }
        catch
        {
            // A socket that failed to connect is still a socket, and nothing else is going to close it: the client
            // only ever disposes a connection it was handed.
            socket.Dispose();

            throw;
        }
    });

var verdict = await client.ScanAsync(message);

if (verdict.IsSpam)
    logger.LogInformation("Scored {Score} against a threshold of {Threshold}.", verdict.Score, verdict.Threshold);

// The rewritten message goes straight into a stream you supply, so a large one never sits in memory.
await using var marked = File.Create("marked.eml");

await client.ProcessAsync(message, marked);

// And the write side of the protocol, for a message a user moved into their spam folder.
await client.TellAsync(TellInstruction.LearnAsSpam, message);
```

A daemon that refuses a request throws `ErrorResponseSpamAssassinException` with the code it sent; a daemon that answers something unreadable, stops mid-answer, or exceeds the limits you configured throws one of the other three `SpamAssassinException` types. Failures of your own streams are not wrapped.

## Licence

[EUPL-1.2](https://codeberg.org/DerPeit/Josupeit.Mail.SpamAssassin.Client/src/branch/main/LICENSE.md).
