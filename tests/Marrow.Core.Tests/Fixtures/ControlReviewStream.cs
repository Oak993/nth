namespace Marrow.Core.Tests.Fixtures;

// Separate read/write buffers, with observable disposal, for the production connection loop's fake-only tests.
internal sealed class ControlReviewStream(byte[] input) : Stream
{
    private readonly MemoryStream read = new(input);
    private readonly MemoryStream written = new();
    internal bool Disposed { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => read.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => read.ReadAsync(buffer, cancellationToken);
    public override void Write(byte[] buffer, int offset, int count) => written.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => written.WriteAsync(buffer, cancellationToken);
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Disposed = true; read.Dispose(); written.Dispose(); }
        base.Dispose(disposing);
    }
}
