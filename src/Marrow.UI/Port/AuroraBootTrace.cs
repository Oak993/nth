using System.Diagnostics;

namespace Marrow.UI.Port;

// PORT-ONLY: authenticated native diagnostic opt-in. Fixed storage; no logging/I/O on either frame thread.
public sealed class AuroraBootTrace
{
    private readonly AuroraBootWorkEvent[] events = new AuroraBootWorkEvent[131072];
    private int count;
    private long queueAt;
    private int queueThread;
    public void QueuePosted() { queueThread = Environment.CurrentManagedThreadId; queueAt = Stopwatch.GetTimestamp(); }
    public void RecordQueueDelay() => Record(new("Diagnostic: UI queue delay", queueAt, Stopwatch.GetTimestamp(), queueThread, Environment.CurrentManagedThreadId));
    public static AuroraBootTrace? Current;
    public static long Frequency => Stopwatch.Frequency;
    public static AuroraBootWorkScope Begin(string name) => Volatile.Read(ref Current) is { } trace ? new(trace, name) : default;
    internal void Record(AuroraBootWorkEvent item)
    {
        var index = Interlocked.Increment(ref count) - 1;
        if (index < events.Length) events[index] = item;
    }
    public void RecordExternalWork(string name, long started, long finished, int thread, int endThread) =>
        Record(new(name, started, finished, thread, endThread));
    public AuroraBootWorkEvent[] Snapshot() => events.Take(Math.Min(Volatile.Read(ref count), events.Length)).Where(x => x.Finished != 0).OrderBy(x => x.Started).ToArray();
}
