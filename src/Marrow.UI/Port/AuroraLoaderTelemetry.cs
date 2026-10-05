namespace Marrow.UI.Port;

// PORT-ONLY: optional fixed-capacity native capture; recording never allocates on the render thread.
public sealed class AuroraLoaderTelemetry
{
    private readonly double[] intervals = new double[1024];
    private readonly double[] drawing = new double[1024];
    private readonly long[] allocations = new long[1024];
    private readonly long[] handlerAllocations = new long[1024];
    private int count;
    private long previous;
    public int Count => Volatile.Read(ref count);
    public double IntervalMilliseconds(int index) => intervals[index];
    public double DrawingMilliseconds(int index) => drawing[index];
    public long AllocatedBytes(int index) => allocations[index];
    public long HandlerAllocatedBytes(int index) => handlerAllocations[index];
    internal void Record(long begin, long end, long bytes, long handlerBytes)
    {
        var at = count;
        if (at >= intervals.Length) return;
        intervals[at] = previous == 0 ? 0 : (begin - previous) * 1000.0 / TimeProvider.System.TimestampFrequency;
        drawing[at] = (end - begin) * 1000.0 / TimeProvider.System.TimestampFrequency;
        allocations[at] = bytes;
        handlerAllocations[at] = handlerBytes;
        previous = begin;
        Volatile.Write(ref count, at + 1);
    }
}
