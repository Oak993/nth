namespace Marrow.Services.Tests.Fixtures;

/// <summary>A read-only stream that cannot seek or say how long it is, so an answer built on it has no content length.</summary>
internal sealed class UnsizedStream(byte[] bytes) : Stream
{
    private int position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var take = Math.Min(count, bytes.Length - position);
        Array.Copy(bytes, position, buffer, offset, take);
        position += take;
        return take;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
