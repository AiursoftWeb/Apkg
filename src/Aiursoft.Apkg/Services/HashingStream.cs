using System.Security.Cryptography;

namespace Aiursoft.Apkg.Services;

/// <summary>
/// A write-only <see cref="Stream"/> wrapper that feeds data into an
/// <see cref="IncrementalHash"/> while forwarding writes to the underlying stream.
/// </summary>
internal class HashingStream(Stream baseStream, IncrementalHash hasher) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => baseStream.Length;
    public override long Position
    {
        get => baseStream.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => baseStream.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => baseStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        hasher.AppendData(buffer, offset, count);
        baseStream.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        hasher.AppendData(buffer, offset, count);
        await baseStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        hasher.AppendData(buffer.Span);
        await baseStream.WriteAsync(buffer, cancellationToken);
    }
}
