// PORT-ONLY: Tactus, what the pill says, from the engine (OD-0145, ED-NEW-pcctl-32; DESIGN.md 4.5 "States", 10.1, 10.2,
// 10.3, 10.4, 10.5). No Swift twin. Turns the shared parts into the pill's facts: the apps the chats used this turn and the
// paused ones (first seen first), each with its pause phase and its latest step in the timeline's words ("Clicked Save"),
// the app of the latest step, the broker's asks (PillAskBridge), Stopped for 2 s after a stop of everything, each stopped
// app "Stopped by you" on its row until a new step runs there or the turn ends, "<app> opened <title>" for 2 s, "Pause key
// is taken" once (4 s), the chords as Settings has them (the pause chord left out while it does not work), a write that
// waits while the user types ("Waiting while you type", the field and the app) and, while an app pauses, the step it waits
// for ("dragging in Paint", ED-NEW-pcctl-37). The real mouse and keyboard (ED-NEW-pcctl-38; DESIGN.md 2.7, 4.5): one
// request for every shown wait, the first app's ("Hands off for a moment", "Click Save in Paint +1 more", the sentence in
// the hint row, "Esc stops this step" while it runs, "Release the mouse first" when Do it now meets a held key or button),
// in the gate's words while the foreground is protected (with Not now) or the input check restarts; "Done, all yours" for
// 1.2 s after a waited borrow, the 1 s chip after one that went at once, the kept keys with "Put them back", the held
// keys, and "Stop key is taken" from a failed arm until the stop key works or Settings opens. Changed is raised on any
// thread; the screen posts the facts.
// + web surfaces W11 (ED-NEW-web-w11-1; DESIGN 5.10 "The pill: 'Using Visus' with the step line"): the app "visus" joins
// through the pause control like any app; its line is the web tools' latest step, "Check the card" while Visus's own card
// waits, "You took over Visus" while the user works in a driven tab, and "The page stays as it is" while it alone is paused.
using Marrow.App.Views.Browser;
using Marrow.Core.Localisation;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Web;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Port.PcControl;

public sealed class TactusPillFeed : IDisposable
{
    /// <summary>How long "Pause key is taken by another app" shows (once per failed registration; ED-NEW-pcctl-32 item 2).</summary>
    public const double PauseKeyTakenSeconds = 4;

    /// <summary>An audit result of a call the user's stop ended.</summary>
    private const string StoppedResult = "stopped_by_user";

    private readonly PcAssembly assembly;
    private readonly PillAskBridge cards;
    private readonly Func<double> now;
    private readonly Func<bool> pauseKeyBroken;
    private readonly WebAgentHub web;
    private readonly VisusAgentAsks webAsks;
    private readonly Lock gate = new();
    private readonly List<string> order = [];
    private readonly Dictionary<string, string> names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> steps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> keyOfExe = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> stoppedApps = new(StringComparer.Ordinal);
    private readonly List<PillNotice> notices = [];
    // the lease's waits the pill shows, and when it first showed each
    private readonly Dictionary<PcBorrowWait, double> shownWaits = [];
    private int kept;
    private double keptSince;
    private double? heldSince;
    private string? latest;
    private PillStop? stopped;
    private bool disposed;

    /// <param name="now">The pill's clock (seconds).</param>
    /// <param name="pauseKeyBroken">Whether the pause chord does not work now (TactusHotkeys.PauseProblem).</param>
    /// <param name="web">The web tools' hub (the process's by default).</param>
    /// <param name="webAsks">The web tools' cards (the process's by default).</param>
    public TactusPillFeed(PcAssembly assembly, PillAskBridge cards, Func<double> now, Func<bool>? pauseKeyBroken = null, WebAgentHub? web = null, VisusAgentAsks? webAsks = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(now);
        this.assembly = assembly;
        this.cards = cards;
        this.now = now;
        this.pauseKeyBroken = pauseKeyBroken ?? (() => false);
        this.web = web ?? WebAgentHub.Shared.Current;
        this.webAsks = webAsks ?? VisusAgentAsks.Shared;
        this.web.Activity += OnWeb;
        this.web.Changed += Raise;
        this.webAsks.Changed += Raise;
        assembly.Pause.Changed += OnPause;
        assembly.Lanes.ActiveChanged += OnLanes;
        assembly.Stop.StoppedIn += OnStopped;
        assembly.Control.WindowOpened += OnOpened;
        assembly.Pointer.Aimed += OnAimed;
        cards.Changed += Raise;
        assembly.Typing.Changed += Raise;
        assembly.Lease.Waiting += OnWaiting;
        assembly.Lease.WaitEnded += OnWaitEnded;
        assembly.Lease.RunningChanged += OnRunning;
        assembly.Lease.Used += OnUsed;
        assembly.Lease.KeptChanged += OnKept;
        assembly.Lease.HoldingKeysChanged += OnHolding;
    }

    /// <summary>The facts changed (any thread).</summary>
    public event Action? Changed;

    /// <summary>A request for the real mouse or keyboard appeared where none showed (the optional sound; any thread).</summary>
    public event Action? Asked;

    /// <summary>The wait the pill's hands-off request shows now (its ring fills); null when none, or when it waits on a protected window or the input check.</summary>
    public PcBorrowWait? HandsOff
    {
        get
        {
            var waits = assembly.Lease.Waits;
            lock (gate) return Head(waits) is { } head && head.Check.Block is not (PcGateBlock.Protected or PcGateBlock.Unhealthy) ? head : null;
        }
    }

    /// <summary>
    /// A step was written to a chat's audit log: it becomes its app's line, and its app the latest. A step the user's stop
    /// ended changes nothing (the app reads "Stopped by you").
    /// </summary>
    public void Step(PcAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.App is not { Length: > 0 } exe || entry.Result == StoppedResult) return;
        var live = assembly.Pause.Chats(PcScope.All).Values.SelectMany(a => a).ToList();
        lock (gate)
        {
            foreach (var app in live) Map(app);
            if (!keyOfExe.TryGetValue(Path.GetFileName(exe), out var key)) return;
            steps[key] = TactusToolWords.Step(entry);
            stoppedApps.Remove(key);
            latest = key;
        }
        Raise();
    }

    /// <summary>The pause chord failed its checks (TactusHotkeys.PauseKeyProblem): the pill says so once.</summary>
    public void PauseKeyTaken()
    {
        lock (gate) notices.Add(PillNotice.PauseKeyTaken(now()) with { Lasts = PauseKeyTakenSeconds });
        Raise();
    }

    /// <summary>The stop chord could not be registered (TactusHotkeys.StopKeyChecked): the pill says so until it works.</summary>
    public void StopKeyTaken()
    {
        lock (gate)
        {
            if (notices.Any(n => n.Kind == PillNoticeKind.StopKeyTaken)) return;
            notices.Add(PillNotice.StopKeyTaken(now()));
        }
        Raise();
    }

    /// <summary>The stop chord registered: "Stop key is taken" is over.</summary>
    public void StopKeyWorks()
    {
        lock (gate)
            if (notices.RemoveAll(n => n.Kind == PillNoticeKind.StopKeyTaken) == 0) return;
        Raise();
    }

    /// <summary>"Do it now": the request the pill shows passes the gate's stillness at once (never a held key or button).</summary>
    public void DoItNow()
    {
        var waits = assembly.Lease.Waits;
        lock (gate) Head(waits)?.DoItNow();
        Raise();
    }

    /// <summary>"Not now": the request the pill shows gives way (deferred_by_user); the next app's request, if any, takes the pill.</summary>
    public void NotNow()
    {
        var waits = assembly.Lease.Waits;
        lock (gate) Head(waits)?.NotNow();
        Raise();
    }

    /// <summary>"Put them back" sent the kept keys: "Put back 6 keys" for 1.2 s.</summary>
    public void PutBack(int keys)
    {
        if (keys <= 0) return;
        lock (gate) notices.Add(PillNotice.PutBack(keys, now()));
        Raise();
    }

    /// <summary>The user pressed a notice's Settings: the key notices are done.</summary>
    public void SettingsOpened()
    {
        lock (gate) notices.RemoveAll(n => n.Kind is PillNoticeKind.PauseKeyTaken or PillNoticeKind.StopKeyTaken);
        Raise();
    }

    /// <summary>The pill's facts now.</summary>
    public PillFacts Facts()
    {
        var at = now();
        var chats = assembly.Pause.Chats(PcScope.All);
        var paused = assembly.Pause.PausedApps;
        var settings = assembly.Settings;
        var asks = cards.Asks;
        var broken = pauseKeyBroken();
        var typing = assembly.Typing.Current;
        var waits = assembly.Lease.Waits;
        var webCard = webAsks.Pending.Count > 0;
        var webTookOver = web.AnyTookOver;
        lock (gate)
        {
            // in the engine's order (chats by key, each chat's apps by key, then the paused ones in the order they paused), so
            // apps first seen together keep one order; those seen earlier (a join, a lane, an aim) keep theirs
            var listed = chats.Values.SelectMany(apps => apps).Concat(paused).ToList();
            var current = new HashSet<string>(listed, StringComparer.Ordinal);
            foreach (var key in stoppedApps.Keys.Where(key => !current.Contains(key)).ToList()) stoppedApps.Remove(key);
            foreach (var key in listed) Seen(key);
            order.RemoveAll(key => !current.Contains(key));
            notices.RemoveAll(n => !n.Live(at) && at >= n.Since);
            var list = new List<PillApp>(order.Count);
            foreach (var key in order)
            {
                var name = NameOf(key);
                var status = assembly.Pause.Status(key);
                var phase = stoppedApps.ContainsKey(key) ? PillAppPhase.Stopped
                    : status?.Phase == PcPausePhase.Pausing ? PillAppPhase.Pausing
                    : status is not null ? PillAppPhase.Paused
                    : PillAppPhase.Working;
                var since = phase == PillAppPhase.Stopped ? stoppedApps[key] : status is { } s ? at - ((s.ElapsedMs) / 1000.0) : 0;
                var visus = key == WebAgentHub.App;
                list.Add(new PillApp(key, name, steps.GetValueOrDefault(key, ""), phase, since,
                    Busy: phase == PillAppPhase.Pausing && status?.Busy is { Length: > 0 } busy ? $"{busy} in {name}" : null,
                    WaitsOnAsk: (visus && webCard) || asks.Any(a => !a.Withdrawn && string.Equals(a.AppName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    TookOver = visus && webTookOver,
                    OwnCard = visus && webCard,
                    PausedLine = visus ? UiText.Get("agent.step.page_stays") : null,
                });
            }
            return new PillFacts
            {
                Apps = list,
                LatestApp = latest is { } l && current.Contains(l) ? l : order.LastOrDefault(),
                Asks = asks,
                Stopped = stopped is { } stop && at < stop.Since + PillStop.Seconds ? stop : null,
                Notices = [.. notices, .. InputNotices(waits)],
                PauseChord = broken || notices.Any(n => n.Kind == PillNoticeKind.PauseKeyTaken) ? "" : settings.PauseChord,
                StopChord = settings.StopChord,
                Typing = typing is { } wait ? new PillTyping(wait.Field, NameOf(wait.App)) : null,
            };
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        assembly.Pause.Changed -= OnPause;
        assembly.Lanes.ActiveChanged -= OnLanes;
        assembly.Stop.StoppedIn -= OnStopped;
        assembly.Control.WindowOpened -= OnOpened;
        assembly.Pointer.Aimed -= OnAimed;
        cards.Changed -= Raise;
        assembly.Typing.Changed -= Raise;
        assembly.Lease.Waiting -= OnWaiting;
        assembly.Lease.WaitEnded -= OnWaitEnded;
        assembly.Lease.RunningChanged -= OnRunning;
        assembly.Lease.Used -= OnUsed;
        assembly.Lease.KeptChanged -= OnKept;
        assembly.Lease.HoldingKeysChanged -= OnHolding;
        web.Activity -= OnWeb;
        web.Changed -= Raise;
        webAsks.Changed -= Raise;
    }

    // web W11: a web step's line becomes Visus's, and Visus the latest app; a stop or a turn's end changes nothing here (the
    // pause control's rows and the stop path say so).
    private void OnWeb(WebAgentActivity activity)
    {
        if (activity.Phase != WebAgentPhase.Working || activity.Step.Length == 0) return;
        lock (gate)
        {
            steps[WebAgentHub.App] = activity.Step;
            stoppedApps.Remove(WebAgentHub.App);
            latest = WebAgentHub.App;
        }
        Raise();
    }

    // Under gate: a key joins the order and its exe name maps to it.
    private void Seen(string key)
    {
        if (!order.Contains(key)) order.Add(key);
        Map(key);
    }

    // Under gate: an exe key's file name finds it (the audit log names the exe only).
    private void Map(string key)
    {
        if (key.StartsWith("exe:", StringComparison.Ordinal)) keyOfExe.TryAdd(Path.GetFileName(key[4..]), key);
    }

    private string NameOf(string key) => names.TryGetValue(key, out var name) ? name : PcChatMirror.NameOf(key);

    // A call joined an app, or a pause moved: an app the chats use joins the order when it is first seen.
    private void OnPause()
    {
        var listed = assembly.Pause.Chats(PcScope.All).Values.SelectMany(apps => apps).Concat(assembly.Pause.PausedApps).ToList();
        lock (gate)
            foreach (var key in listed) Seen(key);
        Raise();
    }

    private void OnLanes(IReadOnlyList<string> active)
    {
        lock (gate)
            foreach (var key in active) Seen(key);
        Raise();
    }

    private void OnAimed(PcPointerAim aim)
    {
        lock (gate)
        {
            stoppedApps.Remove(aim.App);
            if (aim.AppName is { Length: > 0 } name) names[aim.App] = name;
            if (aim.ExePath is { Length: > 0 } exe) keyOfExe[Path.GetFileName(exe)] = aim.App;
            latest = aim.App;
        }
        Raise();
    }

    private void OnStopped(PcStopPath path, PcScope scope)
    {
        var at = now();
        lock (gate)
        {
            switch (scope.Kind)
            {
                case PcScopeKind.All:
                    stopped = new PillStop(at, path == PcStopPath.EscapeThreeTimes);
                    foreach (var app in order.Concat(assembly.Pause.Chats(PcScope.All).Values.SelectMany(a => a)).Concat(assembly.Pause.PausedApps)) stoppedApps[app] = at;
                    break;
                case PcScopeKind.App when scope.Key is { } app:
                    stoppedApps[app] = at;
                    break;
                default:
                    // a chat's Stop: its apps read Stopped on their rows
                    foreach (var app in assembly.Pause.Chats(scope).Values.SelectMany(a => a)) stoppedApps[app] = at;
                    break;
            }
        }
        Raise();
    }

    private void OnOpened(PcOpenedNotice notice)
    {
        lock (gate) notices.Add(PillNotice.OpenedWindow(notice.AppName, notice.Title, now()));
        Raise();
    }

    // ---- the real mouse and keyboard (ED-NEW-pcctl-38) ----

    // Under gate: the wait the request shows, of the waits the pill shows in the lease's order: the one that runs, else the
    // queue's head, else the first.
    private PcBorrowWait? Head(IReadOnlyList<PcBorrowWait> waits)
    {
        var open = waits.Where(shownWaits.ContainsKey).ToList();
        return open.FirstOrDefault(w => w.Running) ?? open.FirstOrDefault(w => !w.Queued) ?? open.FirstOrDefault();
    }

    // A key is held and no button: "Release the keys first" rather than the mouse.
    private bool KeysOnly()
    {
        var state = assembly.Input.Snapshot();
        return state.HeldKeys > 0 && state.HeldButtons == 0;
    }

    // Under gate: the notices of the real mouse and keyboard now.
    private IEnumerable<PillNotice> InputNotices(IReadOnlyList<PcBorrowWait> waits)
    {
        if (Head(waits) is { } head)
        {
            var since = shownWaits[head];
            var check = head.Check;
            var name = NameOf(head.Stream);
            var more = waits.Where(shownWaits.ContainsKey).Select(w => w.Stream).Distinct(StringComparer.Ordinal).Count(s => s != head.Stream);
            yield return check.Block switch
            {
                PcGateBlock.Unhealthy => PillNotice.InputRestarting(since),
                PcGateBlock.Protected => PillNotice.ProtectedForeground(check.Waiting, since),
                _ => PillNotice.HandsOff(Upper(Do(head)) + " in " + name, $"Tactus needs {Needs(head.Kind)} for one step: {Do(head)} in {name}.", more, head.Running,
                    head.DoItNowChosen && check.Block == PcGateBlock.Held, since, KeysOnly()),
            };
        }
        // while the keys are held the user's click is what helps; Put them back works once it came
        if (heldSince is { } held) yield return PillNotice.KeysHeld(held);
        else if (kept > 0) yield return PillNotice.KeptKeys(kept, keptSince);
    }

    private static string Do(PcBorrowWait wait) => wait.Words?.Do ?? "do one step";

    private static string Needs(PcBorrowKind? kind) => kind switch
    {
        PcBorrowKind.Pointer => "the mouse",
        PcBorrowKind.Focus => "the keyboard",
        _ => "a still moment",
    };

    private static string Upper(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];

    private void OnWaiting(PcBorrowWait wait)
    {
        bool asked;
        lock (gate)
        {
            asked = shownWaits.Count == 0 && !shownWaits.ContainsKey(wait);
            shownWaits.TryAdd(wait, now());
        }
        Raise();
        if (asked) Asked?.Invoke();
    }

    private void OnWaitEnded(PcBorrowWait wait)
    {
        lock (gate)
        {
            shownWaits.Remove(wait);
            if (wait.Ran) notices.Add(PillNotice.Done(Upper(wait.Words?.Did ?? "did one step") + " in " + NameOf(wait.Stream), now()));
        }
        Raise();
    }

    private void OnRunning(PcBorrowWait wait) => Raise();

    private void OnUsed(PcBorrowWait wait)
    {
        lock (gate) notices.Add(PillNotice.Borrowed($"Used {Needs(wait.Kind)} for {wait.Words?.Chip ?? "one step"} in {NameOf(wait.Stream)}", now()));
        Raise();
    }

    private void OnKept(int count)
    {
        lock (gate)
        {
            if (count > 0 && kept == 0) keptSince = now();
            kept = Math.Max(0, count);
        }
        Raise();
    }

    private void OnHolding(bool holding)
    {
        lock (gate) heldSince = holding ? heldSince ?? now() : null;
        Raise();
    }

    private void Raise() => Changed?.Invoke();
}
