using System.Text;
using System.Threading.Channels;

namespace Marrow.Services.Tests.Fixtures;

// An HTTP body fake with explicit chunks. The test decides when a delta, done or EOF arrives; cancellation aborts an awaiting read.
internal sealed class CloudReplyStream : Stream
{
    private readonly Channel<byte[]> chunks = Channel.CreateUnbounded<byte[]>();
    private byte[] held = [];
    private int offset;
    public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Send(string text) => chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
    public void Complete() => chunks.Writer.TryComplete();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            if (offset == held.Length)
            {
                Waiting.TrySetResult();
                if (!await chunks.Reader.WaitToReadAsync(cancellationToken)) return 0;
                held = await chunks.Reader.ReadAsync(cancellationToken);
                offset = 0;
            }
            var count = Math.Min(buffer.Length, held.Length - offset);
            held.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            return count;
        }
        catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
