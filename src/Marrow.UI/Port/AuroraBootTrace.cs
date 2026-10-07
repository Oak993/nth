using System.Diagnostics;

namespace Marrow.UI.Port;

// PORT-ONLY: authenticated native diagnostic opt-in. Fixed storage; no logging/I/O on either frame thread.
public sealed class AuroraBootTrace
{
    private readonly AuroraBootWorkEvent[] events = new AuroraBootWorkEvent[16384];
    private int count;
    public static AuroraBootTrace? Current;
    public static long Frequency => Stopwatch.Frequency;
    public static AuroraBootWorkScope Begin(string name) => Volatile.Read(ref Current) is { } trace ? new(trace, name) : default;
    internal void Record(AuroraBootWorkEvent item)
    {
        var index = Interlocked.Increment(ref count) - 1;
        if (index < events.Length) events[index] = item;
    }
    public AuroraBootWorkEvent[] Snapshot() => events.Take(Math.Min(Volatile.Read(ref count), events.Length)).Where(x => x.Finished != 0).OrderBy(x => x.Started).ToArray();
}
