// PORT-ONLY: Tactus, PC control (OD-0140, OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.11 items 3 and 4,
// critique C21, C22). Event-driven waiting. The app's own change events (out-of-context WinEvents, filtered by process;
// UIA events only where an app raises no WinEvents) bump a per-window version, and Tactus waits on that version with
// tight timeouts instead of sleeping. Structure changes and windows that open are counted on their own, so the batch
// settle rule can tell a pending structure change from a value change. A window another window of the same process opens
// (a dialog) bumps every watched window of that process. The same version tells the warm tree cache whether a window's
// tree is still current. Subscriptions exist only while a window is in use and are dropped 10 s after its last step;
// handlers only record and return.
using System.Collections.Concurrent;

namespace Marrow.Services.Port.PcControl;

public enum PcChangeKind
{
    /// <summary>A name, value, state, selection or location changed.</summary>
    Property,

    /// <summary>Elements were added, removed, shown, hidden or reordered.</summary>
    Structure,

    /// <summary>A top-level window of the process opened or was shown (a dialog, a popup).</summary>
    Window,
}

/// <summary>A source of change events (WinEvents, UIA events). It calls back with the root window, its process and the kind.</summary>
public interface IPcChangeSource
{
    void Watch(nint window, int processId, Action<nint, int, PcChangeKind> changed);
    void Unwatch(nint window);
}

public interface IPcChangeFeed
{
    /// <summary>Starts watching a window (idempotent) and marks it in use now.</summary>
    void Watch(nint window, int processId);

    /// <summary>The window's change count; it only grows.</summary>
    long Version(nint window);

    /// <summary>The window's count of structure changes and windows opened; it only grows.</summary>
    long StructureVersion(nint window);

    /// <summary>True as soon as the version passes <paramref name="seen"/>; false after <paramref name="timeoutMs"/> without a change.</summary>
    Task<bool> Changed(nint window, long seen, int timeoutMs, CancellationToken cancellationToken);
}

public sealed class PcChangeFeed : IPcChangeFeed, IDisposable
{
    /// <summary>How long a window stays subscribed after its last step (critique C22).</summary>
    public const int IdleUnwatchMs = 10_000;

    /// <summary>
    /// The pseudo-window for "a top-level window was shown anywhere", watched only while pc_launch_app waits for a new
    /// app's first window; sources watch it with process id 0.
    /// </summary>
    public const nint Desktop = 0;

    private readonly ConcurrentDictionary<nint, Entry> windows = new();
    private readonly IReadOnlyList<IPcChangeSource> sources;
    private readonly IPcClock clock;

    public PcChangeFeed(IReadOnlyList<IPcChangeSource> sources, IPcClock clock)
    {
        this.sources = sources;
        this.clock = clock;
    }

    public void Watch(nint window, int processId)
    {
        var added = false;
        var entry = windows.GetOrAdd(window, _ =>
        {
            added = true;
            return new Entry(processId);
        });
        var use = entry.Touch(clock.NowMs);
        if (added)
        {
            foreach (var source in sources) source.Watch(window, processId, Signal);
        }
        _ = DropWhenIdle(window, entry, use);
    }

    // The window's subscriptions end 10 s after its last use, unless it was used again meanwhile.
    private async Task DropWhenIdle(nint window, Entry entry, long use)
    {
        try { await clock.Delay(IdleUnwatchMs, CancellationToken.None).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        if (entry.LastUse == use && windows.TryGetValue(window, out var current) && ReferenceEquals(current, entry)) Unwatch(window);
    }

    public void Unwatch(nint window)
    {
        if (!windows.TryRemove(window, out var entry)) return;
        foreach (var source in sources) source.Unwatch(window);
        entry.Wake();
    }

    /// <summary>
    /// Counts one change and wakes its waiters. A change in a window that is not watched but belongs to a watched process
    /// (a dialog it opened) counts for every watched window of that process. Thread-safe; only records and returns.
    /// </summary>
    public void Signal(nint window, int processId, PcChangeKind kind)
    {
        if (window != Desktop && kind == PcChangeKind.Window && windows.TryGetValue(Desktop, out var desktop)) desktop.Bump(kind);
        if (windows.TryGetValue(window, out var entry))
        {
            entry.Bump(kind);
            return;
        }
        foreach (var (key, other) in windows)
        {
            if (key != Desktop && other.ProcessId == processId) other.Bump(kind == PcChangeKind.Property ? PcChangeKind.Property : PcChangeKind.Window);
        }
    }

    public long Version(nint window) => windows.TryGetValue(window, out var entry) ? entry.Version : 0;

    public long StructureVersion(nint window) => windows.TryGetValue(window, out var entry) ? entry.Structure : 0;

    public async Task<bool> Changed(nint window, long seen, int timeoutMs, CancellationToken cancellationToken)
    {
        if (!windows.TryGetValue(window, out var entry)) return false;
        var next = entry.Next;
        if (entry.Version > seen) return true;
        if (timeoutMs <= 0) return false;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = clock.Delay(timeoutMs, stop.Token);
        await Task.WhenAny(next, timeout).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return entry.Version > seen;
    }

    public void Dispose()
    {
        foreach (var window in windows.Keys.ToList()) Unwatch(window);
    }

    private sealed class Entry(int processId)
    {
        private long version;
        private long structure;
        private long lastUse;
        private TaskCompletionSource next = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ProcessId { get; } = processId;
        public long Version => Interlocked.Read(ref version);
        public long Structure => Interlocked.Read(ref structure);
        public long LastUse => Interlocked.Read(ref lastUse);

        /// <summary>Completes at the next change.</summary>
        public Task Next => Volatile.Read(ref next).Task;

        public long Touch(long now)
        {
            Interlocked.Exchange(ref lastUse, now);
            return now;
        }

        public void Bump(PcChangeKind kind)
        {
            if (kind != PcChangeKind.Property) Interlocked.Increment(ref structure);
            Interlocked.Increment(ref version);
            Wake();
        }

        public void Wake() => Interlocked.Exchange(ref next, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }
}

/// <summary>How a settle wait ended: whether the check held, whether the cap was reached, how long it took.</summary>
public sealed record PcSettleResult(bool Held, bool TimedOut, long ElapsedMs);

/// <summary>
/// Waits after an action until its check holds and the window has been quiet for 100 ms (no change event), at most 3 s, or
/// 8 s while a busy indicator shows (DESIGN.md 2.11 item 4). Never a fixed sleep: every wait ends on a change event or a
/// timeout. With <c>skipQuiet</c> (the batch settle rule, critique C21) a check that already holds returns at once when no
/// structure change or new window arrived since the action; a pending change always forces the wait.
/// </summary>
public sealed class PcSettle(IPcChangeFeed feed, IPcClock clock)
{
    public const int QuietMs = 100;
    public const int CapMs = 3000;
    public const int BusyCapMs = 8000;

    public async Task<PcSettleResult> Until(nint window, long structureSeen, bool busy, bool skipQuiet, Func<Task<bool>> check, CancellationToken cancellationToken)
    {
        var start = clock.NowMs;
        var deadline = start + (busy ? BusyCapMs : CapMs);
        if (skipQuiet && feed.StructureVersion(window) == structureSeen && await check().ConfigureAwait(false))
            return new PcSettleResult(true, false, clock.NowMs - start);
        while (true)
        {
            var left = (int)Math.Max(0, deadline - clock.NowMs);
            var version = feed.Version(window);
            if (left > 0 && await feed.Changed(window, version, Math.Min(QuietMs, left), cancellationToken).ConfigureAwait(false)) continue;
            // Quiet for 100 ms (or out of time): look.
            if (await check().ConfigureAwait(false)) return new PcSettleResult(true, false, clock.NowMs - start);
            left = (int)Math.Max(0, deadline - clock.NowMs);
            if (left <= 0) return new PcSettleResult(false, true, clock.NowMs - start);
            // Not yet: wait for the app's next change, until the cap.
            version = feed.Version(window);
            if (!await feed.Changed(window, version, left, cancellationToken).ConfigureAwait(false))
                return new PcSettleResult(await check().ConfigureAwait(false), true, clock.NowMs - start);
        }
    }

    /// <summary>Waits until the window has been quiet for 100 ms, at most the cap (the end of a batch, pc_wait idle).</summary>
    public async Task<PcSettleResult> Quiet(nint window, int capMs, CancellationToken cancellationToken)
    {
        var start = clock.NowMs;
        while (true)
        {
            var left = (int)(start + capMs - clock.NowMs);
            if (left <= 0) return new PcSettleResult(false, true, clock.NowMs - start);
            // A last slice shorter than 100 ms that stays quiet is the cap, not a quiet window.
            var slice = Math.Min(QuietMs, left);
            if (!await feed.Changed(window, feed.Version(window), slice, cancellationToken).ConfigureAwait(false))
                return slice == QuietMs ? new PcSettleResult(true, false, clock.NowMs - start) : new PcSettleResult(false, true, clock.NowMs - start);
        }
    }
}
