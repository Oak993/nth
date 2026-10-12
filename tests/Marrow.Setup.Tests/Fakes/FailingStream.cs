namespace Marrow.Setup.Tests.Fakes;

/// <summary>A readable stream of zeros that throws an IOException after <paramref name="failAfter"/> bytes (a disk or a scanner giving up).</summary>
public sealed class FailingStream(long failAfter) : Stream
{
    private long position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => failAfter * 2;

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (position >= failAfter) throw new IOException("The device is not ready.");
        var read = (int)Math.Min(count, failAfter - position);
        Array.Clear(buffer, offset, read);
        position += read;
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
