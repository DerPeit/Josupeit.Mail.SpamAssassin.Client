// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client.IntegrationTests;

// A readable stream that refuses to seek and refuses to say how long it is: the case the explicit-length overloads
// exist for. The unit tests have their own copy - a test double is not worth a shared project, and a BufferedStream
// over a MemoryStream is not a substitute, because it seeks whenever the stream underneath it does.
internal sealed class NonSeekableStream(byte[] content) : Stream
{
    private readonly MemoryStream _inner = new(content, writable: false);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
