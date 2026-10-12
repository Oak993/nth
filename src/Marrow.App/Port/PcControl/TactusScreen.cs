// PORT-ONLY: Tactus, the pill on screen for one set of shared parts (OD-0145, ED-NEW-pcctl-32; DESIGN.md 4.5, 10.1 to 10.7).
// No Swift twin. On the UI thread: the pill host shows the feed's facts; its card shows the broker's asks (PillAskBridge),
// and an answer there resolves the same entry the chat mirror shows; the pill's buttons and rows run the pause and stop
// paths; "Answer Tactus" opens the card and puts the keyboard in it; while the card holds the keyboard (and for 300 ms after
// it handed it back) a borrow waits (PcSurface.OwnCardUp); every action's aim draws its pointer and names the monitor of
// the latest step; the pause chord's problem shows once; the pill's place and monitor follow Settings, and a drag saves
// them. With "Answer approvals and questions in the pill" off the card is not offered: a click opens the ask's chat. The
// user's pointer reaches the host (where it is, for the card's shift; a held button, for the auto-expand) and the bands'
// hit test (a press that began in another app holds the region; each record for the style-flip test) (ED-NEW-pcctl-34).
// An app whose pointer is parked (its target cannot be seen) reads "working behind" on its row (ED-NEW-pcctl-37).
// The real mouse and keyboard (ED-NEW-pcctl-38): "Do it now" and "Not now" answer the request the pill shows, "Put them
// back" replays the kept keys and says so, the mark's ring fills each frame from the gate's stillness (HandsRing) while
// the hands-off request shows, a new request plays the soft sound when Settings has it on, and the stop chord's checks
// show and clear "Stop key is taken".
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Port.PcControl;

/// <summary>What the screen puts together; the desktop binds the real windows and calls, tests bind fakes.</summary>
public sealed class TactusScreenParts
{
    public required PcAssembly Assembly { get; init; }
    public required TactusPillHost Host { get; init; }
    public required PillScreens Screens { get; init; }
    public required MonitorMap Map { get; init; }
    public required PillAskBridge Cards { get; init; }
    public required TactusPillFeed Feed { get; init; }

    /// <summary>The Tactus chords (the pause chord's problem); none in tests that do not need them.</summary>
    public TactusHotkeys? Hotkeys { get; init; }

    /// <summary>The card's keyboard (P27); none: the card never holds it.</summary>
    public CardFocus? Focus { get; init; }

    /// <summary>The Tactus pointers (P14); none: no pointer is drawn.</summary>
    public PointerOverlays? Pointers { get; init; }

    /// <summary>The user's pointer and buttons (ED-NEW-pcctl-34); none: the host knows neither.</summary>
    public TactusPointerState? Pointer { get; init; }

    /// <summary>The bands (their hit tests and windows), for the user's pointer; none in tests that do not need them.</summary>
    public PillOverlays? Overlays { get; init; }

    /// <summary>Writes the whole settings (PcSettingsStore.Write in the app).</summary>
    public Action<PcSettings> Save { get; init; } = _ => { };

    /// <summary>Runs an action on the UI thread (posted; inline in tests).</summary>
    public Action<Action> Post { get; init; } = action => action();

    /// <summary>Runs an action on the UI thread after some seconds.</summary>
    public Action<double, Action> After { get; init; } = (_, _) => { };

    /// <summary>The pill's clock (seconds), the host's.</summary>
    public required Func<double> Now { get; init; }

    /// <summary>A notice's Settings: opens Settings at Tactus.</summary>
    public Action? OpenSettings { get; init; }

    /// <summary>Opens a chat at its asks (thread id, the card's draft when there is one).</summary>
    public Action<string, string?>? OpenChat { get; init; }

    /// <summary>The soft sound of a new hands-off request ("Sound when Tactus needs a moment"); none: silent.</summary>
    public Action? Chime { get; init; }

    /// <summary>Puts the keyboard in the open card (activates the input band and focuses the reply field; native).</summary>
    public Action? KeyboardToCard { get; init; }

    /// <summary>Disposed with the screen (the chat mirror, the band windows).</summary>
    public IReadOnlyList<IDisposable> Owned { get; init; } = [];
}

public sealed class TactusScreen : IDisposable
{
    /// <summary>A pull a little after a timed fact ends (the Stopped state, a notice), so the pill drops it.</summary>
    public const double ExpiryMargin = 0.05;

    private readonly TactusScreenParts parts;
    private readonly PcAssembly assembly;
    private readonly TactusPillHost host;
    private readonly Action<TimeSpan> frame;
    private readonly Lock gate = new();
    private readonly HandsRing ring = new();
    private double? ringAt;
    private bool pullPosted;
    private bool framing;
    private double? expiry;
    private bool disposed;

    public TactusScreen(TactusScreenParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        this.parts = parts;
        assembly = parts.Assembly;
        host = parts.Host;
        frame = OnFrame;
        host.CardAnswered += OnAnswered;
        host.DraftChanged += OnDraft;
        host.CardChanged += OnCardChanged;
        host.Command += OnCommand;
        host.AnchorChosen += OnAnchorChosen;
        host.MonitorDropped += OnMonitorDropped;
        parts.Cards.OpenChat += OnOpenChat;
        parts.Cards.SendToChat += OnSendToChat;
        parts.Feed.Changed += PullLater;
        parts.Feed.Asked += OnAsked;
        assembly.Pointer.Aimed += OnAimed;
        assembly.Pause.AppPaused += OnAppPaused;
        assembly.Pause.AppResumed += OnAppResumed;
        if (parts.Pointers is { } pointers) pointers.FramesWanted += OnFramesWanted;
        if (parts.Focus is { } focus) focus.ActivationFailed += host.KeyboardRefused;
        if (parts.Pointer is { } pointer)
        {
            host.PointerAt = pointer.PointerAt;
            host.ButtonHeld = pointer.ButtonHeld;
            pointer.HeldElsewhereChanged += OnHeldElsewhere;
            pointer.Moved += OnPointerMoved;
        }
        if (parts.Overlays is { } overlays)
        {
            overlays.HandlesChanged += OnHandles;
            OnHandles();
        }
        if (parts.Hotkeys is { } keys)
        {
            keys.PauseKeyProblem += OnPauseKeyProblem;
            keys.StopKeyChecked += OnStopKeyChecked;
            if (keys.PauseProblem is not null) parts.Feed.PauseKeyTaken();
        }
        Follow(assembly.Settings);
        Pull();
    }

    /// <summary>The card holds the keyboard, or handed it back less than 300 ms ago: a borrow waits (PcSurface.OwnCardUp).</summary>
    public bool CardHasKeyboard => parts.Focus is { } focus && (focus.BorrowWait() is not { } wait || wait > TimeSpan.Zero);

    /// <summary>"Answer Tactus" (the chord and the tray item): the card opens on the waiting ask and takes the keyboard.</summary>
    public void Answer() => parts.Post(() =>
    {
        if (disposed || parts.Cards.Asks.Count == 0) return;
        if (!host.CardOpen) host.Pressed(new PillCommand(PillCommandKind.Click));
        if (host.CardOpen) parts.KeyboardToCard?.Invoke();
    });

    /// <summary>A step a chat's audit log wrote (any thread): its app's line on the pill.</summary>
    public void Step(PcAuditEntry entry) => parts.Feed.Step(entry);

    /// <summary>Settings changed: the pill's place and monitor follow.</summary>
    public void SettingsChanged() => parts.Post(() =>
    {
        if (disposed) return;
        Follow(assembly.Settings);
        Pull();
    });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        host.CardAnswered -= OnAnswered;
        host.DraftChanged -= OnDraft;
        host.CardChanged -= OnCardChanged;
        host.Command -= OnCommand;
        host.AnchorChosen -= OnAnchorChosen;
        host.MonitorDropped -= OnMonitorDropped;
        parts.Cards.OpenChat -= OnOpenChat;
        parts.Cards.SendToChat -= OnSendToChat;
        parts.Feed.Changed -= PullLater;
        parts.Feed.Asked -= OnAsked;
        assembly.Pointer.Aimed -= OnAimed;
        assembly.Pause.AppPaused -= OnAppPaused;
        assembly.Pause.AppResumed -= OnAppResumed;
        if (parts.Pointers is { } pointers) pointers.FramesWanted -= OnFramesWanted;
        if (parts.Focus is { } focus) focus.ActivationFailed -= host.KeyboardRefused;
        if (parts.Pointer is { } pointer)
        {
            pointer.HeldElsewhereChanged -= OnHeldElsewhere;
            pointer.Moved -= OnPointerMoved;
        }
        if (parts.Overlays is { } overlays) overlays.HandlesChanged -= OnHandles;
        if (parts.Hotkeys is { } keys)
        {
            keys.PauseKeyProblem -= OnPauseKeyProblem;
            keys.StopKeyChecked -= OnStopKeyChecked;
        }
        if (framing) FrameClock.Source.Current.CancelFrame(frame);
        parts.Feed.Dispose();
        host.Dispose();
        parts.Cards.Dispose();
        foreach (var owned in parts.Owned.Reverse()) owned.Dispose();
    }

    // ---- the facts ----

    private void PullLater()
    {
        lock (gate)
        {
            if (pullPosted) return;
            pullPosted = true;
        }
        parts.Post(Pull);
    }

    private void Pull()
    {
        lock (gate) pullPosted = false;
        if (disposed) return;
        var facts = parts.Feed.Facts();
        // an app whose pointer works where the user cannot see it reads "working behind" on its row (ED-NEW-pcctl-21 item 5,
        // ED-NEW-pcctl-37); the pointers live on this thread
        if (parts.Pointers is { } parked && facts.Apps.Any(a => parked.Parked(a.Key)))
            facts = facts with { Apps = [.. facts.Apps.Select(a => parked.Parked(a.Key) ? a with { Behind = true } : a)] };
        host.Bodies = assembly.Settings.AnswerInPill ? parts.Cards.Body : null;
        host.Update(facts);
        if (parts.Pointers is { } pointers)
        {
            pointers.Active([.. facts.Apps.Where(a => a.Phase != PillAppPhase.Stopped).Select(a => a.Key)]);
        }
        // the pointers' frames, and the ring's while a hands-off request shows
        WantFrames();
        Expire(facts);
    }

    // One pull just after the soonest timed fact ends.
    private void Expire(PillFacts facts)
    {
        var now = parts.Now();
        var ends = new List<double>();
        if (facts.Stopped is { } stop) ends.Add(stop.Since + PillStop.Seconds);
        ends.AddRange(facts.Notices.Where(n => n.Lasts > 0).Select(n => n.Since + n.Lasts));
        var next = ends.Where(end => end > now).DefaultIfEmpty(double.NaN).Min();
        if (double.IsNaN(next) || (expiry is { } planned && planned <= next && planned > now)) return;
        expiry = next;
        parts.After(next - now + ExpiryMargin, () =>
        {
            if (expiry == next) expiry = null;
            Pull();
        });
    }

    private void Follow(PcSettings settings)
    {
        host.Anchor = settings.PillAnchor switch
        {
            "left" => PillAnchor.Left,
            "right" => PillAnchor.Right,
            _ => PillAnchor.Centre,
        };
        switch (settings.PillMonitor)
        {
            case "main":
                parts.Screens.ShowOn = PillShowOn.Main;
                break;
            case "latest" or "":
                parts.Screens.ShowOn = PillShowOn.LatestStep;
                break;
            default:
                parts.Screens.ShowOn = PillShowOn.Chosen;
                parts.Screens.ChosenId = settings.PillMonitor;
                break;
        }
    }

    // ---- the pill's buttons, rows and card ----

    private void OnCommand(PillCommand command)
    {
        switch (command.Kind)
        {
            case PillCommandKind.PauseAll:
                assembly.Pause.Pause(PcScope.All, PcPauseSource.Pill);
                break;
            case PillCommandKind.ResumeAll:
                assembly.Pause.Resume(PcScope.All, PcPauseSource.Pill);
                break;
            case PillCommandKind.Stop:
                assembly.Stop.PillStop();
                break;
            case PillCommandKind.PauseApp when command.App is { } app:
                assembly.Pause.Pause(PcScope.App(app), PcPauseSource.Row);
                break;
            case PillCommandKind.ResumeApp when command.App is { } app:
                assembly.Pause.Resume(PcScope.App(app), PcPauseSource.Row);
                break;
            case PillCommandKind.StopApp or PillCommandKind.StopThisApp when command.App is { } app:
                assembly.Stop.PillRow(app);
                break;
            case PillCommandKind.PutBack:
                PutBack();
                break;
            case PillCommandKind.DoItNow:
                parts.Feed.DoItNow();
                break;
            case PillCommandKind.NotNow:
                parts.Feed.NotNow();
                break;
            case PillCommandKind.Settings:
                parts.Feed.SettingsOpened();
                parts.OpenSettings?.Invoke();
                break;
            case PillCommandKind.Click when host.Bodies is null && parts.Cards.Asks.Count > 0:
                // asks stay in the chat (Answer in the pill is off): a click opens the chat of the first ask
                if (assembly.Asks.Find(parts.Cards.Asks[0].Id) is { } pending) parts.OpenChat?.Invoke(pending.Thread, null);
                break;
        }
    }

    // "Put them back": the kept keys go back to the window they were typed in, and the pill says so.
    private void PutBack()
    {
        var keys = assembly.Lease.KeptKeys;
        if (assembly.Lease.PutBack()) parts.Feed.PutBack(keys);
    }

    private void OnAnswered(PillCardAnswer answer)
    {
        parts.Cards.Answer(answer);
        parts.Focus?.HandBack();
    }

    private void OnDraft(string id, string draft)
    {
        parts.Cards.Draft(id, draft);
        parts.Focus?.Typed();
    }

    private void OnCardChanged(bool open)
    {
        if (!open) parts.Focus?.HandBack();
    }

    private void OnAnchorChosen(PillAnchor anchor)
    {
        var name = anchor switch
        {
            PillAnchor.Left => "left",
            PillAnchor.Right => "right",
            _ => "centre",
        };
        parts.Save(assembly.Settings with { PillAnchor = name });
    }

    private void OnMonitorDropped(string monitorId)
    {
        parts.Screens.ChosenId = monitorId;
        parts.Screens.ShowOn = PillShowOn.Chosen;
        parts.Save(assembly.Settings with { PillMonitor = monitorId, PillMonitorName = parts.Map.ById(monitorId)?.Name });
    }

    private void OnOpenChat(string thread, string id, string? draft) => parts.OpenChat?.Invoke(thread, draft);

    private void OnSendToChat(string thread, string text) => parts.OpenChat?.Invoke(thread, text);

    private void OnPauseKeyProblem(PcChordProblem problem) => parts.Feed.PauseKeyTaken();

    // The stop chord was tried (the engine's Arm, any thread): taken shows "Stop key is taken", working clears it.
    private void OnStopKeyChecked(bool works)
    {
        if (works) parts.Feed.StopKeyWorks();
        else parts.Feed.StopKeyTaken();
    }

    // A hands-off request appeared where none showed (any thread): the soft sound, when Settings has it on.
    private void OnAsked() => parts.Post(() =>
    {
        if (!disposed && assembly.Settings.Sound) parts.Chime?.Invoke();
    });

    // ---- the pointers ----

    private void OnAimed(PcPointerAim aim) => parts.Post(() =>
    {
        if (disposed) return;
        if (parts.Map.At(aim.Point) is { } monitor) parts.Screens.Step(monitor.Id, parts.Now());
        if (parts.Pointers is not { } pointers) return;
        var parked = pointers.Parked(aim.App);
        pointers.Aimed(aim);
        WantFrames();
        // parked or back in view: the row's "working behind" follows
        if (pointers.Parked(aim.App) != parked) PullLater();
    });

    private void OnAppPaused(string app, PcPauseSource source) => parts.Post(() =>
    {
        if (!disposed) parts.Pointers?.Paused(app, true);
    });

    private void OnAppResumed(string app, PcPauseSource source) => parts.Post(() =>
    {
        if (!disposed) parts.Pointers?.Paused(app, false);
    });

    private void OnFramesWanted() => parts.Post(WantFrames);

    // ---- the user's pointer ----

    // A press that began in another app: the bands' hit regions wait until every button is up (the hook reader's thread).
    private void OnHeldElsewhere(bool held) => parts.Post(() =>
    {
        if (!disposed) parts.Overlays?.ButtonHeldElsewhere(held);
    });

    // Each pointer record for the style-flip test, on the hook reader's thread (PillOverlays.Pointer is made for it).
    private void OnPointerMoved(int x, int y, double seconds)
    {
        if (!disposed) parts.Overlays?.Pointer(x, y, seconds);
    }

    // The bands' windows changed: a press on one of them is the pill's own.
    private void OnHandles()
    {
        if (parts.Overlays is { } overlays) parts.Pointer?.Own(overlays.Handles);
    }

    private void WantFrames()
    {
        if (disposed || framing) return;
        if (parts.Pointers is null && ringAt is null && parts.Feed.HandsOff is null) return;
        framing = true;
        FrameClock.Source.Current.RequestFrame(frame);
    }

    private void OnFrame(TimeSpan _)
    {
        framing = false;
        if (disposed) return;
        var pointers = parts.Pointers?.Frame() == true;
        if (Ring() | pointers) WantFrames();
    }

    // ---- the hands-off ring ----

    // Each frame while the hands-off request shows, the ring's fill follows the gate's stillness (HandsRing: up at once,
    // down smoothly, full in 90 ms once the borrow runs). Once no request shows the ring springs away with its fill as it
    // was, and the next request starts from the stillness then.
    private bool Ring()
    {
        if (parts.Feed.HandsOff is not { } wait)
        {
            ringAt = null;
            ring.Reset();
            return false;
        }
        var now = parts.Now();
        var step = ringAt is { } last ? now - last : 0;
        ringAt = now;
        host.RingFill(ring.Next(PcInputGate.Stillness(assembly.Input.Snapshot(), assembly.Lease.Clock.NowMs), step, wait.Running));
        return true;
    }
}
