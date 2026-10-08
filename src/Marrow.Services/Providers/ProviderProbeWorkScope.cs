using System.Diagnostics;

namespace Marrow.Services.Providers;

public readonly struct ProviderProbeWorkScope : IDisposable
{
    private readonly Action<string, long, long, int, int>? record;
    private readonly string? name;
    private readonly long started;
    private readonly int thread;
    internal ProviderProbeWorkScope(Action<string, long, long, int, int> record, string name, long started, int thread)
    {
        this.record = record; this.name = name; this.started = started; this.thread = thread;
    }
    public void Dispose() => record?.Invoke(name!, started, Stopwatch.GetTimestamp(), thread, Environment.CurrentManagedThreadId);
}
