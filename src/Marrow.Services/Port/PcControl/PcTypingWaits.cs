// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-37; DESIGN.md 2.9, 4.5 "Waiting for you to type"). The writes that
// wait now because the user types in their target control (the yield rule), for the pill's "Waiting while you type" with
// the field and the app ("Subject (Outlook)"). The newest wait is the one the pill names. Thread-safe; Changed is raised on
// the thread that began or ended a wait.
namespace Marrow.Services.Port.PcControl;

/// <summary>A write waiting while the user types: the app's key and the field's name.</summary>
public sealed record PcTypingWait(string App, string Field);

public sealed class PcTypingWaits
{
    private readonly Lock gate = new();
    private readonly List<Entry> open = [];

    /// <summary>A wait began or ended.</summary>
    public event Action? Changed;

    /// <summary>The newest wait still open; null when no write waits for the user's typing.</summary>
    public PcTypingWait? Current
    {
        get
        {
            lock (gate) return open.Count == 0 ? null : open[^1].Wait;
        }
    }

    /// <summary>A write waits while the user types in <paramref name="wait"/>'s field, until the scope is disposed.</summary>
    public IDisposable Begin(PcTypingWait wait)
    {
        ArgumentNullException.ThrowIfNull(wait);
        var entry = new Entry(this, wait);
        lock (gate) open.Add(entry);
        Changed?.Invoke();
        return entry;
    }

    private void End(Entry entry)
    {
        bool removed;
        lock (gate) removed = open.Remove(entry);
        if (removed) Changed?.Invoke();
    }

    private sealed class Entry(PcTypingWaits owner, PcTypingWait wait) : IDisposable
    {
        private int done;

        public PcTypingWait Wait { get; } = wait;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0) owner.End(this);
        }
    }
}
