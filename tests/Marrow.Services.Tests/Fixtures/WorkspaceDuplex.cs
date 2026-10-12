using System.Threading.Channels;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: an in-memory pipe for the workspace's link tests (unit P18, ED-NEW-pcws-5): two connected ends, each reading
// what the other writes. Disposing or breaking one end ends the other's reads (end of stream) and fails its writes, as a
// broken named pipe does. No OS pipe, no other process.
public sealed class WorkspaceDuplex : Stream
{
    private readonly Channel<byte[]> incoming;
    private readonly Channel<byte[]> outgoing;
    private byte[]? current;
    private int offset;

    private WorkspaceDuplex(Channel<byte[]> incoming, Channel<byte[]> outgoing)
    {
        this.incoming = incoming;
        this.outgoing = outgoing;
    }

    public static (WorkspaceDuplex Nth, WorkspaceDuplex Agent) Pair()
    {
        var toAgent = Channel.CreateUnbounded<byte[]>();
        var toNth = Channel.CreateUnbounded<byte[]>();
        return (new WorkspaceDuplex(toNth, toAgent), new WorkspaceDuplex(toAgent, toNth));
    }

    /// <summary>Everything this end wrote, as text (what went over the pipe).</summary>
    public List<string> Written { get; } = [];

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>The pipe breaks: both ends' reads end and their writes fail.</summary>
    public void Break()
    {
        incoming.Writer.TryComplete();
        outgoing.Writer.TryComplete();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (current is null || offset >= current.Length)
        {
            if (!await incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) return 0;
            if (incoming.Reader.TryRead(out var next))
            {
                current = next;
                offset = 0;
            }
        }
        var count = Math.Min(buffer.Length, current.Length - offset);
        current.AsMemory(offset, count).CopyTo(buffer);
        offset += count;
        return count;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = buffer.ToArray();
        lock (Written) Written.Add(System.Text.Encoding.UTF8.GetString(bytes));
        return outgoing.Writer.TryWrite(bytes) ? ValueTask.CompletedTask : throw new IOException("the pipe is broken");
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("async only");

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("async only");

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) Break();
        base.Dispose(disposing);
    }
}
