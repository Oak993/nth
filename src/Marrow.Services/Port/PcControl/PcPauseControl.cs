// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12, ED-NEW-pcctl-14; DESIGN.md 10.1). Pause, the user's hold on an
// app's stream. A paused app is neither acted in nor looked at: its calls hold at the next boundary (before a step, between
// batch steps, between 64-character text chunks), in order, with nothing dropped and no timeout, and a call waiting for it
// answers waiting_for_user "paused" at once. Pause is per app, for every chat that uses it; a chat's Pause covers the apps
// that chat used; Pause all also latches, so an app that starts work while it is on starts paused. Only the user pauses
// and resumes (the pill, its rows, the chat strip, the pause chord, the tray); no tool can. A stop clears the pause state
// of what it covers (its held steps are cancelled by the stop itself); sleep and lock leave it as it is. Shared by every
// chat, like the lanes and the run control.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>Where a Pause or Resume came from (the pill's chip and the chat's "Resumed from ..." line).</summary>
public enum PcPauseSource
{
    Pill,
    Row,
    Chat,
    Chord,
    Tray,
}

/// <summary>A paused app as the pill shows it: "Pausing" while a step it started still runs, "Paused" when none does.</summary>
public enum PcPausePhase
{
    Pausing,
    Paused,
}

/// <summary>
/// One paused app for the pill: its phase, when it paused (engine clock) and how long ago. A step still running 2 s after
/// the Pause shows "Pausing after this step"; at 5 s the pill offers "Stop this app".
/// </summary>
public sealed record PcPauseStatus(string App, PcPausePhase Phase, PcPauseSource Source, long PausedAtMs, long ElapsedMs)
{
    public bool LongUnit => Phase == PcPausePhase.Pausing && ElapsedMs >= PcPauseControl.LongUnitMs;
    public bool OffersStop => Phase == PcPausePhase.Pausing && ElapsedMs >= PcPauseControl.OfferStopMs;
}

/// <summary>A pause of one app that has ended: when it began and ended (engine clock).</summary>
public readonly record struct PcPauseSpan(long StartMs, long EndMs)
{
    public long LengthMs => EndMs - StartMs;
}

public sealed class PcPauseControl : IDisposable
{
    /// <summary>A step still running this long after the Pause shows "Pausing after this step".</summary>
    public const int LongUnitMs = 2_000;

    /// <summary>A step still running this long after the Pause makes the pill offer "Stop this app".</summary>
    public const int OfferStopMs = 5_000;

    private readonly Lock gate = new();
    private readonly IPcClock clock;
    private readonly PcRunControl control;
    private readonly Dictionary<string, Hold> holds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PcPauseSpan> ended = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> epochs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> acting = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> pausing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> used = new(StringComparer.Ordinal);
    private readonly List<Joined> joined = [];
    private PcPauseSource latchSource;
    private bool latched;

    public PcPauseControl(IPcClock clock, PcRunControl control)
    {
        this.clock = clock;
        this.control = control;
        control.Stopped += OnStopped;
    }

    /// <summary>Raised (outside the lock) after any change of the pause state, the latch or a paused app's phase.</summary>
    public event Action? Changed;

    /// <summary>Raised after an app paused: the real-input borrow queue (unit P3) withdraws its requests for it.</summary>
    public event Action<string, PcPauseSource>? AppPaused;

    /// <summary>Raised after the user resumed an app (not after a stop cleared it): the borrow queue re-queues its steps at the back, and the chat may continue (unit P28).</summary>
    public event Action<string, PcPauseSource>? AppResumed;

    /// <summary>Pause all is on: an app, window or Hydra head that starts work starts paused.</summary>
    public bool Latched
    {
        get
        {
            lock (gate) return latched;
        }
    }

    /// <summary>The paused apps, in the order they paused.</summary>
    public IReadOnlyList<string> PausedApps
    {
        get
        {
            lock (gate) return holds.OrderBy(h => h.Value.Order).Select(h => h.Key).ToList();
        }
    }

    /// <summary>The live apps: those with a Tactus call in flight, and the paused ones.</summary>
    public IReadOnlyList<string> Live
    {
        get
        {
            lock (gate) return LiveApps().ToList();
        }
    }

    public bool IsPaused(string app)
    {
        lock (gate) return holds.ContainsKey(app);
    }

    /// <summary>Counts the app's pauses: a step that saw another number at its start has been paused since.</summary>
    public long Epoch(string app)
    {
        lock (gate) return epochs.GetValueOrDefault(app);
    }

    /// <summary>Whether the app was paused after <paramref name="epoch"/> was read (and whether it still is or not).</summary>
    public bool PausedSince(string app, long epoch) => Epoch(app) != epoch;

    /// <summary>The app's latest pause that has ended; null when it never paused (or is paused now for the first time).</summary>
    public PcPauseSpan? LastPause(string app)
    {
        lock (gate) return ended.TryGetValue(app, out var span) ? span : null;
    }

    /// <summary>The pill's view of a paused app; null when it is not paused.</summary>
    public PcPauseStatus? Status(string app)
    {
        lock (gate)
        {
            if (!holds.TryGetValue(app, out var hold)) return null;
            var phase = acting.GetValueOrDefault(app) > 0 ? PcPausePhase.Pausing : PcPausePhase.Paused;
            return new PcPauseStatus(app, phase, hold.Source, hold.Since, clock.NowMs - hold.Since);
        }
    }

    /// <summary>A token cancelled the moment the app pauses (already cancelled while it is paused): it cuts a settle wait short.</summary>
    public CancellationToken PausedToken(string app)
    {
        lock (gate)
        {
            if (!pausing.TryGetValue(app, out var source)) pausing[app] = source = new CancellationTokenSource();
            return source.Token;
        }
    }

    /// <summary>Pauses what the scope covers. Pause all also turns the latch on.</summary>
    public void Pause(PcScope scope, PcPauseSource source)
    {
        var raised = new List<string>();
        lock (gate)
        {
            IEnumerable<string> apps;
            switch (scope.Kind)
            {
                case PcScopeKind.All:
                    latched = true;
                    latchSource = source;
                    apps = LiveApps().Concat(used.Values.SelectMany(a => a));
                    break;
                case PcScopeKind.App:
                    apps = [scope.Key!];
                    break;
                default:
                    apps = used.GetValueOrDefault(scope.Key!) ?? [];
                    break;
            }
            foreach (var app in apps.Distinct(StringComparer.Ordinal).ToList())
            {
                if (PauseApp(app, source)) raised.Add(app);
            }
        }
        Raise(raised, source, paused: true);
    }

    /// <summary>Resumes what the scope covers. Resume all also ends the latch and resumes apps paused one by one.</summary>
    public void Resume(PcScope scope, PcPauseSource source)
    {
        var raised = new List<string>();
        lock (gate)
        {
            IEnumerable<string> apps;
            switch (scope.Kind)
            {
                case PcScopeKind.All:
                    latched = false;
                    apps = holds.Keys;
                    break;
                case PcScopeKind.App:
                    apps = [scope.Key!];
                    break;
                default:
                    apps = used.GetValueOrDefault(scope.Key!) ?? [];
                    break;
            }
            foreach (var app in apps.ToList())
            {
                if (ResumeApp(app)) raised.Add(app);
            }
        }
        Raise(raised, source, paused: false);
    }

    /// <summary>
    /// The pause chord (DESIGN.md 10.1): it resumes only when every live app is paused (or, with nothing live, when the
    /// latch is on); otherwise it pauses all, so nothing paused by hand resumes unseen. Returns true when it resumed.
    /// </summary>
    public bool Chord()
    {
        bool resumes;
        lock (gate)
        {
            var live = LiveApps().ToList();
            resumes = ChordResumes(live.Count, live.Count(holds.ContainsKey), latched);
        }
        if (resumes) Resume(PcScope.All, PcPauseSource.Chord);
        else Pause(PcScope.All, PcPauseSource.Chord);
        return resumes;
    }

    /// <summary>The chord's rule: resume when every live app is paused, or when nothing is live and the latch is on.</summary>
    public static bool ChordResumes(int live, int paused, bool latched) => live > 0 ? paused == live : latched;

    /// <summary>
    /// A call of <paramref name="thread"/> entered <paramref name="app"/>: the app counts as the chat's (its Pause covers
    /// it) and as live, and while the app is paused the call's wait reads "paused". With the latch on, the app starts paused.
    /// Disposing ends the call's part.
    /// </summary>
    public IDisposable Join(string thread, string app, PcWaitState wait)
    {
        var entry = new Joined(this, app, wait);
        var started = false;
        lock (gate)
        {
            if (!used.TryGetValue(thread, out var apps)) used[thread] = apps = new HashSet<string>(StringComparer.Ordinal);
            apps.Add(app);
            joined.Add(entry);
            if (latched && !holds.ContainsKey(app)) started = PauseApp(app, latchSource);
            else if (holds.ContainsKey(app)) entry.Scope = wait.Waiting(PcWaitReason.Paused);
        }
        if (started) Raise([app], latchSource, paused: true);
        else Changed?.Invoke();
        return entry;
    }

    /// <summary>Completes when the app is not paused (at once when it is not); a stop cancels it through the token.</summary>
    public async Task Held(string app, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task released;
            lock (gate)
            {
                if (!holds.TryGetValue(app, out var hold)) return;
                released = hold.Released.Task;
            }
            await released.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A step of the app is running (an atomic unit in flight): the pill reads "Pausing" until every one ends.</summary>
    public IDisposable Acting(string app)
    {
        lock (gate) acting[app] = acting.GetValueOrDefault(app) + 1;
        return new Unit(this, app);
    }

    /// <summary>The chat's turn ended: it forgets the apps it used that are not paused (a paused one stays the chat's until resumed).</summary>
    public void EndTurn(string thread)
    {
        lock (gate)
        {
            if (!used.TryGetValue(thread, out var apps)) return;
            apps.RemoveWhere(a => !holds.ContainsKey(a));
            if (apps.Count == 0) used.Remove(thread);
        }
    }

    public void Dispose()
    {
        control.Stopped -= OnStopped;
        lock (gate)
        {
            foreach (var source in pausing.Values) source.Dispose();
            pausing.Clear();
        }
    }

    // A stop clears the pause state of what it covers; its held steps are cancelled by the stop itself, before this runs.
    // A chat's Stop clears only apps no other chat uses, so another chat's held steps never resume unseen. Sleep keeps it.
    private void OnStopped(PcScope scope, PcStopSource source)
    {
        if (source == PcStopSource.Sleep) return;
        var cleared = false;
        lock (gate)
        {
            IEnumerable<string> apps;
            switch (scope.Kind)
            {
                case PcScopeKind.All:
                    cleared = latched;
                    latched = false;
                    apps = holds.Keys;
                    break;
                case PcScopeKind.App:
                    apps = [scope.Key!];
                    break;
                default:
                    var own = used.GetValueOrDefault(scope.Key!) ?? [];
                    apps = own.Where(a => !used.Any(u => u.Key != scope.Key && u.Value.Contains(a)));
                    break;
            }
            foreach (var app in apps.ToList()) cleared |= ResumeApp(app);
        }
        if (cleared) Changed?.Invoke();
    }

    // Under the lock.
    private IEnumerable<string> LiveApps() => joined.Select(j => j.App).Concat(holds.Keys).Distinct(StringComparer.Ordinal);

    // Under the lock. True when the app was not paused before.
    private bool PauseApp(string app, PcPauseSource source)
    {
        if (holds.ContainsKey(app)) return false;
        holds[app] = new Hold(clock.NowMs, source, holds.Count == 0 ? 0 : holds.Values.Max(h => h.Order) + 1);
        epochs[app] = epochs.GetValueOrDefault(app) + 1;
        if (pausing.TryGetValue(app, out var cut))
        {
            try { _ = cut.CancelAsync(); }
            catch (ObjectDisposedException) { }
        }
        else
        {
            var source2 = new CancellationTokenSource();
            source2.Cancel();
            pausing[app] = source2;
        }
        foreach (var entry in joined.Where(j => j.App == app && j.Scope is null)) entry.Scope = entry.Wait.Waiting(PcWaitReason.Paused);
        return true;
    }

    // Under the lock. True when the app was paused.
    private bool ResumeApp(string app)
    {
        if (!holds.Remove(app, out var hold)) return false;
        ended[app] = new PcPauseSpan(hold.Since, clock.NowMs);
        if (pausing.Remove(app, out var old)) old.Dispose();
        foreach (var entry in joined.Where(j => j.App == app))
        {
            entry.Scope?.Dispose();
            entry.Scope = null;
        }
        hold.Released.TrySetResult();
        return true;
    }

    private void Raise(List<string> apps, PcPauseSource source, bool paused)
    {
        if (apps.Count == 0) return;
        foreach (var app in apps)
        {
            if (paused) AppPaused?.Invoke(app, source);
            else AppResumed?.Invoke(app, source);
        }
        Changed?.Invoke();
    }

    private void Leave(Joined entry)
    {
        lock (gate)
        {
            joined.Remove(entry);
            entry.Scope?.Dispose();
            entry.Scope = null;
        }
        Changed?.Invoke();
    }

    private void Finish(string app)
    {
        var phase = false;
        lock (gate)
        {
            var left = acting.GetValueOrDefault(app) - 1;
            if (left <= 0) acting.Remove(app);
            else acting[app] = left;
            phase = left <= 0 && holds.ContainsKey(app);
        }
        // "Pausing" became "Paused".
        if (phase) Changed?.Invoke();
    }

    private sealed class Hold(long since, PcPauseSource source, long order)
    {
        public long Since { get; } = since;
        public PcPauseSource Source { get; } = source;
        public long Order { get; } = order;
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Joined(PcPauseControl owner, string app, PcWaitState wait) : IDisposable
    {
        private int done;

        public string App { get; } = app;
        public PcWaitState Wait { get; } = wait;

        /// <summary>The call's "paused" wait while the app is paused (under the owner's lock).</summary>
        public IDisposable? Scope { get; set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0) owner.Leave(this);
        }
    }

    private sealed class Unit(PcPauseControl owner, string app) : IDisposable
    {
        private int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0) owner.Finish(app);
        }
    }
}
