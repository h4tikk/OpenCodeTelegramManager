namespace OpenCodeManager.Application;
public sealed class LimitedWriteStream(Stream inner, long limit) : Stream
{
    private long written;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => written;
    public override long Position { get => written; set => throw new NotSupportedException(); }

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
    {
        Check(count);
        inner.Write(buffer, offset, count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        Check(buffer.Length);
        await inner.WriteAsync(buffer, ct);
    }

    private void Check(int count)
    {
        written += count;
        if (written > limit)
            throw new InvalidDataException("Download exceeds configured limit");
    }
}
