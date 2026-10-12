using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-8). A change source for PcChangeFeed tests: it records each subscription and
// lets a test raise the events WinEvents would raise for a watched window.
public sealed class PcFakeChangeSource : IPcChangeSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<nint, Action<nint, int, PcChangeKind>> callbacks = [];

    public List<(nint Window, int ProcessId)> Watches { get; } = [];
    public List<nint> Unwatches { get; } = [];

    public void Watch(nint window, int processId, Action<nint, int, PcChangeKind> changed)
    {
        lock (gate)
        {
            Watches.Add((window, processId));
            callbacks[window] = changed;
        }
    }

    public void Unwatch(nint window)
    {
        lock (gate)
        {
            Unwatches.Add(window);
            callbacks.Remove(window);
        }
    }

    public bool Watching(nint window)
    {
        lock (gate) return callbacks.ContainsKey(window);
    }

    /// <summary>An event in <paramref name="window"/> (or a window of the same process) reported to the watch on <paramref name="through"/>.</summary>
    public void Raise(nint through, nint window, int processId, PcChangeKind kind)
    {
        Action<nint, int, PcChangeKind>? changed;
        lock (gate) changed = callbacks.GetValueOrDefault(through);
        changed?.Invoke(window, processId, kind);
    }
}
