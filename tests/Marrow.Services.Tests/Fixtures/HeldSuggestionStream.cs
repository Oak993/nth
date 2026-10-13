namespace Marrow.Services.Tests.Fixtures;

/// <summary>A body which supplies no bytes until explicitly released, and honours a reader's cancellation.</summary>
internal sealed class HeldSuggestionStream : Stream
{
    private readonly TaskCompletionSource<int> held = new();
    public bool ReadStarted { get; private set; }
    public CancellationToken ReadToken { get; private set; }
    public void Release() => held.TrySetResult(0);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ReadStarted = true;
        ReadToken = cancellationToken;
        return new ValueTask<int>(held.Task.WaitAsync(cancellationToken));
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
