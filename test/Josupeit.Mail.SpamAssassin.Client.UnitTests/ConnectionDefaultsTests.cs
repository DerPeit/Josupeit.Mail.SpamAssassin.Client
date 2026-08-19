// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

// ISpamAssassinConnection's default CompleteRequest and CompleteRequestAsync. Every other double here overrides them,
// so without this fixture the documented default would never run at all.
[TestFixture]
public sealed class ConnectionDefaultsTests
{
    private const string OK = "SPAMD/1.1 0 EX_OK\r\nSpam: False ; 0.3 / 5.0\r\n\r\n";

    private static readonly byte[] _message = Encoding.ASCII.GetBytes(
        "From: Fiona Flush <fiona.flush@example.com>\r\nSubject: buffered\r\n\r\nStill in the buffer.\r\n");

    [Test]
    public void A_connection_that_adds_nothing_gets_its_request_flushed()
    {
        var connection = new PlainConnection();
        var client = new SpamAssassinClient(new PlainSource(connection));

        client.Scan(_message);

        Assert.That(connection.Flushes, Is.GreaterThanOrEqualTo(1));
        Assert.That(connection.Sent, Does.StartWith("CHECK SPAMC/1.5\r\n"));
    }

    [Test]
    public async Task The_awaited_default_flushes_too()
    {
        var connection = new PlainConnection();
        var client = new SpamAssassinClient(new PlainSource(connection));

        await client.ScanAsync(_message);

        Assert.That(connection.AsynchronousFlushes, Is.GreaterThanOrEqualTo(1));
        Assert.That(connection.Sent, Does.StartWith("CHECK SPAMC/1.5\r\n"));
    }

    private sealed class PlainSource(PlainConnection connection) : ISpamAssassinConnectionSource
    {
        public ISpamAssassinConnection Connect() => connection;

        public Task<ISpamAssassinConnection> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ISpamAssassinConnection>(connection);
    }

    // Overrides neither completion method, so both come from the interface.
    private sealed class PlainConnection : ISpamAssassinConnection
    {
        private readonly CountingStream _request = new();

        public Stream RequestStream => _request;

        public Stream ResponseStream { get; } = new MemoryStream(Encoding.ASCII.GetBytes(OK), writable: false);

        public int Flushes => _request.Flushes;

        public int AsynchronousFlushes => _request.AsynchronousFlushes;

        public string Sent => Encoding.ASCII.GetString(_request.ToArray());

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => default;
    }

    private sealed class CountingStream : MemoryStream
    {
        public int Flushes { get; private set; }

        public int AsynchronousFlushes { get; private set; }

        public override void Flush()
        {
            Flushes++;

            base.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            AsynchronousFlushes++;

            return base.FlushAsync(cancellationToken);
        }
    }
}
