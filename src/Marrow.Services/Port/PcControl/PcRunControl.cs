// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 4.4, critique
// C11). Stop, the user's control over a run now that their own mouse and keyboard never interrupt Tactus. A stop covers
// everything, one app's stream, or one chat's calls: it cancels the running call at once and every queued or batched call
// it covers, and the engine refuses further PC input for the rest of that turn (for that app, for a per-app stop). A stop
// never waits on a worker: it cancels the calls' tokens and returns; a worker stuck in a hung app is left behind. The
// paths are the configured stop chord (Ctrl+Alt+Esc by default), the pill's Stop (all) and its per-app stop, the chat's
// Stop, and Tactus switched off in Settings. Esc stops only during a borrow of the real pointer or focus (unit P3/P5), never
// because a controlled window is in front (DESIGN.md 4.4 rejects that); three Escs within 1 s, the first in a window Tactus
// is using, stop all (pill v2, DESIGN.md 10.2 path 5). The paths are named in PcStopControl; Pause and Resume are
// PcPauseControl's. It also carries the notice of a window a Tactus step opened that took the user's foreground
// (DESIGN.md 2.9), for the pill.
namespace Marrow.Services.Port.PcControl;

/// <summary>A window a Tactus step opened took the foreground from the user: the pill says so for 2 s.</summary>
public sealed record PcOpenedNotice(string App, string AppName, string Title)
{
    public const int ShowMs = 2000;

    /// <summary>"Excel opened Save As".</summary>
    public string Text => AppName + " opened " + Title;
}

public enum PcScopeKind
{
    All,
    App,
    Thread,
}

/// <summary>What a stop covers: everything, one app (its approval key) or one chat thread.</summary>
public readonly record struct PcScope(PcScopeKind Kind, string? Key)
{
    public static PcScope All { get; } = new(PcScopeKind.All, null);
    public static PcScope App(string key) => new(PcScopeKind.App, key);
    public static PcScope Thread(string id) => new(PcScopeKind.Thread, id);

    public bool Covers(string thread, string app) => Kind switch
    {
        PcScopeKind.All => true,
        PcScopeKind.App => Key == app,
        _ => Key == thread,
    };
}

/// <summary>Where a stop came from (for the audit and the pill).</summary>
public enum PcStopSource
{
    Pill,
    StopKey,
    Escape,
    Chat,
    Settings,

    /// <summary>The PC is going to sleep (WM_POWERBROADCAST): every stream stops and its call answers locked.</summary>
    Sleep,

    /// <summary>Esc three times within 1 s in a window Tactus is using (DESIGN.md 10.2 path 5).</summary>
    EscapeThreeTimes,

    /// <summary>The tray menu's "Stop Tactus" (DESIGN.md 10.2 path 8).</summary>
    Tray,
}

/// <summary>A watch over one call: its token is cancelled by a stop that covers it.</summary>
public interface IPcWatch : IDisposable
{
    CancellationToken Token { get; }
    bool Stopped { get; }

    /// <summary>Where the stop that ended this call came from; null while it was not stopped.</summary>
    PcStopSource? StoppedBy { get; }
}

public sealed class PcRunControl
{
    private readonly Lock gate = new();
    private readonly List<Watching> running = [];

    /// <summary>Raised after a stop, with its scope and source (engines record the turn-wide refusal; the pill redraws).</summary>
    public event Action<PcScope, PcStopSource>? Stopped;

    /// <summary>A window a Tactus step opened took the user's foreground (DESIGN.md 2.9): the pill shows the notice for 2 s.</summary>
    public event Action<PcOpenedNotice>? WindowOpened;

    public void Opened(PcOpenedNotice notice) => WindowOpened?.Invoke(notice);

    /// <summary>A watch over one call by <paramref name="thread"/> in <paramref name="app"/>, on <paramref name="window"/>.</summary>
    public IPcWatch Watch(CancellationToken outer, string thread, string app, nint window)
    {
        var watch = new Watching(this, outer, thread, app, window);
        lock (gate) running.Add(watch);
        return watch;
    }

    /// <summary>The windows of the calls running or waiting now.</summary>
    public IReadOnlyCollection<nint> ActiveWindows
    {
        get
        {
            lock (gate) return running.Select(w => w.Window).ToHashSet();
        }
    }

    /// <summary>Cancels every call the scope covers and returns at once; nothing here waits for a call to end.</summary>
    public void Stop(PcScope scope, PcStopSource source)
    {
        Watching[] stopping;
        lock (gate) stopping = running.Where(w => scope.Covers(w.Thread, w.App)).ToArray();
        foreach (var watch in stopping) watch.Stop(source);
        Stopped?.Invoke(scope, source);
    }

    /// <summary>The stop chord (Ctrl+Alt+Esc by default): stops everything, from anywhere.</summary>
    public void StopKey() => Stop(PcScope.All, PcStopSource.StopKey);

    /// <summary>Tactus switched off in Settings: everything stops at once.</summary>
    public void SwitchedOff() => Stop(PcScope.All, PcStopSource.Settings);

    /// <summary>The PC is suspending: every stream stops (DESIGN.md 2.10); it is not a stop for the rest of the turn.</summary>
    public void Sleeping() => Stop(PcScope.All, PcStopSource.Sleep);

    private void Remove(Watching watch)
    {
        lock (gate) running.Remove(watch);
    }

    private sealed class Watching(PcRunControl owner, CancellationToken outer, string thread, string app, nint window) : IPcWatch
    {
        private readonly CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(outer);
        private int stopped;
        private int by;

        public string Thread { get; } = thread;
        public string App { get; } = app;
        public nint Window { get; } = window;
        public CancellationToken Token => source.Token;
        public bool Stopped => Volatile.Read(ref stopped) == 1;
        public PcStopSource? StoppedBy => Stopped ? (PcStopSource)Volatile.Read(ref by) : null;

        public void Stop(PcStopSource from)
        {
            Volatile.Write(ref by, (int)from);
            Interlocked.Exchange(ref stopped, 1);
            // The token is marked cancelled before this returns; its callbacks run on the thread pool, so the stop never
            // runs (or waits on) a call's own code.
            try { _ = source.CancelAsync(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            owner.Remove(this);
            source.Dispose();
        }
    }
}
