using System.Threading.Channels;

namespace Marrow.Setup.Tests.Fakes;

internal sealed class DuplexMemoryStream(ChannelReader<byte> input, ChannelWriter<byte> output) : Stream
{
    private int disposed;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        if (!await input.WaitToReadAsync(cancellationToken)) return 0;
        var count = 0;
        while (count < buffer.Length && input.TryRead(out var value)) buffer.Span[count++] = value;
        return count;
    }
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(DuplexMemoryStream));
        foreach (var value in buffer.Span)
            if (!output.TryWrite(value)) throw new IOException("Fake peer closed.");
        return ValueTask.CompletedTask;
    }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0) output.TryComplete();
        base.Dispose(disposing);
    }
}
