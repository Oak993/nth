using System.Diagnostics;

namespace Marrow.UI.Port;

public readonly struct AuroraBootWorkScope : IDisposable
{
    private readonly AuroraBootTrace? trace;
    private readonly string? name;
    private readonly long started;
    private readonly int thread;
    internal AuroraBootWorkScope(AuroraBootTrace trace, string name)
    {
        this.trace = trace; this.name = name;
        started = Stopwatch.GetTimestamp(); thread = Environment.CurrentManagedThreadId;
    }
    public void Dispose()
    {
        if (trace is not null) trace.Record(new(name!, started, Stopwatch.GetTimestamp(), thread, Environment.CurrentManagedThreadId));
    }
}
