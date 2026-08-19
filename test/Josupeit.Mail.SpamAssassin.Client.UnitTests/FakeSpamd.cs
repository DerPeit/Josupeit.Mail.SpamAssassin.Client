// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

// A daemon that answers with whatever it was told to answer with, and keeps the requests it was sent. Every connection
// it hands out is a fresh pair of memory streams, which is the whole of what the library asks of a transport.
internal sealed class FakeSpamd : ISpamAssassinConnectionSource
{
    private readonly byte[] _response;

    public FakeSpamd(string response)
        : this(Encoding.ASCII.GetBytes(response))
    {
    }

    public FakeSpamd(byte[] response)
    {
        _response = response;
    }

    public List<byte[]> Requests { get; } = [];

    public int Connections { get; private set; }

    public int Disposals { get; private set; }

    public int Completions { get; private set; }

    public bool LastConnectionWasAsynchronous { get; private set; }

    public byte[] LastRequest => Requests[^1];

    public string LastRequestText => Encoding.ASCII.GetString(LastRequest);

    public ISpamAssassinConnection Connect()
    {
        Connections++;
        LastConnectionWasAsynchronous = false;

        return new FakeConnection(this, _response);
    }

    public Task<ISpamAssassinConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Connections++;
        LastConnectionWasAsynchronous = true;

        return Task.FromResult<ISpamAssassinConnection>(new FakeConnection(this, _response));
    }

    private sealed class FakeConnection : ISpamAssassinConnection
    {
        private readonly FakeSpamd _owner;
        private readonly MemoryStream _request = new();
        private readonly MemoryStream _response;

        private bool _recorded;

        public FakeConnection(FakeSpamd owner, byte[] response)
        {
            _owner = owner;
            _response = new(response, writable: false);
        }

        public Stream RequestStream => _request;

        public Stream ResponseStream => _response;

        public void CompleteRequest()
        {
            _owner.Completions++;

            Record();
        }

        public Task CompleteRequestAsync(CancellationToken cancellationToken = default)
        {
            CompleteRequest();

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _owner.Disposals++;

            Record();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return default;
        }

        // Recorded on whichever of the two happens first, so a request that failed on the way out is still inspectable.
        private void Record()
        {
            if (_recorded)
                return;

            _recorded = true;

            _owner.Requests.Add(_request.ToArray());
        }
    }
}
