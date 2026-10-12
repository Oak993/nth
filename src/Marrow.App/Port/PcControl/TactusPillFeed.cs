// PORT-ONLY: Tactus, what the pill says, from the engine (OD-0145, ED-NEW-pcctl-32; DESIGN.md 4.5 "States", 10.1, 10.2,
// 10.3, 10.4, 10.5). No Swift twin. Turns the shared parts into the pill's facts: the apps the chats used this turn and the
// paused ones (first seen first), each with its pause phase and its latest step in the timeline's words ("Clicked Save"),
// the app of the latest step, the broker's asks (PillAskBridge), Stopped for 2 s after a stop of everything, each stopped
// app "Stopped by you" on its row until a new step runs there or the turn ends, "<app> opened <title>" for 2 s, "Pause key
// is taken" once (4 s), and the chords as Settings has them (the pause chord left out while it does not work). Changed is raised on any thread; the screen posts the facts.
// + web surfaces W11 (ED-NEW-web-w11-1; DESIGN 5.10 "The pill: 'Using Visus' with the step line"): the app "visus" joins
// through the pause control like any app; its line is the web tools' latest step, "Check the card" while Visus's own card
// waits, "You took over Visus" while the user works in a driven tab, and "The page stays as it is" while it alone is paused.
using Marrow.App.Views.Browser;
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
    }

    /// <summary>The facts changed (any thread).</summary>
    public event Action? Changed;

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
                    WaitsOnAsk: (visus && webCard) || asks.Any(a => !a.Withdrawn && string.Equals(a.AppName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    TookOver = visus && webTookOver,
                    OwnCard = visus && webCard,
                    PausedLine = visus ? "The page stays as it is" : null,
                });
            }
            return new PillFacts
            {
                Apps = list,
                LatestApp = latest is { } l && current.Contains(l) ? l : order.LastOrDefault(),
                Asks = asks,
                Stopped = stopped is { } stop && at < stop.Since + PillStop.Seconds ? stop : null,
                Notices = [.. notices],
                PauseChord = broken || notices.Any(n => n.Kind == PillNoticeKind.PauseKeyTaken) ? "" : settings.PauseChord,
                StopChord = settings.StopChord,
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

    private void Raise() => Changed?.Invoke();
}
