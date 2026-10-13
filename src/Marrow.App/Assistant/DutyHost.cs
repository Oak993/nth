// PORT-ONLY: Dextra (DESIGN.md rev 5, 7, 12.6, 15.1; unit AS-10; ED-NEW-dextra-as10-11, -12, -14, -15, -16). No Swift twin.
// Everything the duties need from nth's process, put together once: the scheduler over the assistant store, the launcher that
// starts a duty's mission, the feed in and out, the hold of Stop all, the tray's items, the two background switches and the first
// duty's card, the Task Scheduler entries that wake the PC, and the desktop gate for a locked PC. It holds no window: it runs
// the same with nth's window closed, which is what the hidden window needs. Dispose ends it.
using Marrow.App.Tray;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Core.Models;

namespace Marrow.App.Assistant;

/// <summary>What a test replaces in a duty host: the clock, the web, the Run key, Task Scheduler, powercfg and the desktop. All optional.</summary>
public sealed class DutySeams
{
    public WebWatcher? Web { get; init; }

    public IRunKey? RunKey { get; init; }

    public ITaskSchedulerHost? TaskHost { get; init; }

    public IProcessRunner? Processes { get; init; }

    public IDesktopWatch? Desktop { get; init; }

    public TimeProvider? Time { get; init; }
}

public sealed class DutyHostOptions
{
    public required AssistantStore Store { get; init; }

    public required AssistantController Controller { get; init; }

    public required AssistantEventBus Events { get; init; }

    /// <summary>The ids of the assistants whose duties are loaded.</summary>
    public required Func<IReadOnlyList<string>> AssistantIds { get; init; }

    public required Action<Action> OnMain { get; init; }

    public required Func<IReadOnlyList<MissionProject>> Projects { get; init; }

    public required Func<string, bool> FolderGranted { get; init; }

    public required Func<string> ExePath { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    public StartupMode Mode { get; init; } = StartupMode.Normal;

    public AppTray? Tray { get; init; }

    public IRunKey RunKey { get; init; } = new RegistryRunKey();

    public ITaskSchedulerHost TaskHost { get; init; } = new SchtasksHost();

    public IProcessRunner Processes { get; init; } = SystemProcessRunner.Instance;

    public IDesktopWatch Desktop { get; init; } = new SystemDesktopWatch();

    public WebWatcher? Web { get; init; }

    /// <summary>Ends nth when a start for one duty is over and nothing else is live (7.4).</summary>
    public Action? Quit { get; init; }

    public ProviderKind Provider { get; init; } = ProviderKind.Claude;

    public string? Model { get; init; }

    public Func<IReadOnlyList<string>> AccountNames { get; init; } = () => [];

    public Func<Guid, Guid?> ChatProject { get; init; } = _ => null;

    public Func<string, string> AssistantName { get; init; } = id => id;

    public Action<string>? Faulted { get; init; }

    /// <summary>The system folder for schtasks and powercfg; null is Windows'.</summary>
    public string? SystemFolder { get; init; }

    /// <summary>How long a change to the duties waits before the wake entries are brought in line.</summary>
    public TimeSpan WakeSyncDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The morning digest (AS-11, 6): when set, the host makes the Morning digest duty once (every day at 08:00 by default), composes the digest where a
    /// mission would be started for that duty, and keeps the digest and its suggestions in the store. Null: no digest (a host made for a test of something else).
    /// </summary>
    public DigestSetup? Digest { get; init; }
}

public sealed class DutyHost : IDisposable
{
    private readonly DutyHostOptions o;
    private readonly AssistantController controller;
    private readonly IDisposable feedSubscription;
    private readonly WakeSync wake;
    private readonly DigestBook? digestBook;
    private readonly HashSet<Guid> ended = [];
    private readonly object gate = new();
    private readonly CancellationTokenSource stopping = new();
    private ITimer? wakeTimer;
    private DutyTray? tray;
    private WakeTimerState timers = WakeTimerState.Unknown;
    private bool quitWhenDone;
    private bool wakeAsked;
    private bool disposed;

    public DutyHost(DutyHostOptions options)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
        controller = o.Controller;
        Web = o.Web;
        Launcher = new DutyLauncher(controller, new DutyLauncherOptions
        {
            Projects = o.Projects,
            OnMain = o.OnMain,
            Provider = o.Provider,
            Model = o.Model,
            AccountNames = o.AccountNames,
        });
        // The digest duty is nth's own: its launcher composes the digest where a mission would be started, and every other duty goes to the mission launcher.
        var digestLauncher = o.Digest is null ? null : new DigestLauncher(Launcher);
        Switches = new BackgroundSwitches(o.Store.Background, o.Store.SaveBackground, new StartWithWindows(o.RunKey), o.ExePath);
        Scheduler = new AssistantScheduler(new AssistantSchedulerOptions
        {
            Store = new AssistantDutyStore(o.Store),
            Launcher = (IDutyLauncher?)digestLauncher ?? Launcher,
            AssistantIds = o.AssistantIds,
            Time = o.Time,
            MayFire = () => controller.Hold.DutiesMayFire,
            FolderGranted = path => o.FolderGranted(path) || Switches.IsFolderApproved(path),
            Web = Web,
            DesktopLocked = () => o.Desktop.IsLocked,
            // The failure breaker (13.2, AS-15): a duty it paused does not fire; the owner's Run now still does.
            BreakerPaused = id => controller.Duties?.IsPaused(id) == true,
            Feed = Publish,
            Faulted = o.Faulted,
        });
        FirstCard = new FirstDutyCard(Switches);
        Desktop = new DesktopGate(o.Desktop, o.Time, AskResume, NoteWaiting);
        Tools = new DutyTools(Scheduler, new DutyToolsOptions
        {
            ChatProject = o.ChatProject,
            AssistantName = o.AssistantName,
            ApproveFolder = Switches.ApproveFolder,
            Web = Web,
            Time = o.Time,
        });
        wake = new WakeSync(o.TaskHost, o.ExePath(), o.Time);
        if (o.Digest is { } digestSetup && digestLauncher is not null)
        {
            digestBook = new DigestBook();
            digestBook.Attach(o.Store);
            Digests = new DigestService(new DigestServiceOptions
            {
                Controller = controller,
                Scheduler = Scheduler,
                Book = digestBook,
                OnMain = o.OnMain,
                Projects = o.Projects,
                AssistantIds = o.AssistantIds,
                Setup = digestSetup,
                Time = o.Time,
                Faulted = o.Faulted,
            });
            digestLauncher.Bind(Digests);
        }
        feedSubscription = o.Events.Subscribe(OnFeed);
        controller.Runner.Changed += OnMission;
        controller.Hold.Changed += OnHold;
        Scheduler.DutyAdded += OnDutyAdded;
        Scheduler.Changed += OnSchedulerChanged;
        quitWhenDone = o.Mode.ForOneDuty;
        if (o.Tray is { } icon) tray = DutyTray.Bind(icon, Scheduler, Switches);
    }

    public AssistantScheduler Scheduler { get; }

    public BackgroundSwitches Switches { get; }

    public FirstDutyCard FirstCard { get; }

    /// <summary>What Tactus (or any action on the desktop) asks before it acts for a mission (7.4).</summary>
    public DesktopGate Desktop { get; }

    public IDutyTools Tools { get; }

    public IDutyLauncher Launcher { get; }

    public WebWatcher? Web { get; }

    /// <summary>The morning digest and the Suggestions lane (AS-11); null when the host was made without a digest.</summary>
    public DigestService? Digests { get; }

    /// <summary>What the Task Scheduler last refused for each duty, for its card.</summary>
    public IReadOnlyDictionary<string, string> WakeProblems => wake.Problems;

    /// <summary>The power plan's answer on wake timers, read at start and whenever a duty asks to wake the PC.</summary>
    public WakeTimerState WakeTimers => timers;

    /// <summary>
    /// What the duty's card says about waking the PC: whether it can be offered, and why not (it needs the desktop; it is not a time duty),
    /// plus the power plan's words when wake timers are off (7.4).
    /// </summary>
    public WakeOffer WakeAdvice(Duty duty)
    {
        var offer = duty.WakeOffer();
        if (!offer.Offered) return offer;
        return timers.Allowed == false ? new WakeOffer(true, timers.Words) : offer;
    }

    /// <summary>The owner's own Save in the duty editor (7.1: his Save confirms it, so no card): makes or changes the duty. Saving a duty that watches a folder is his grant of it.</summary>
    public (Duty? Duty, string? Problem) Save(Duty duty)
    {
        ArgumentNullException.ThrowIfNull(duty);
        if (duty.Trigger is FolderChangeTrigger folder && Switches.ApproveFolder(folder.Path) is { } refused) return (null, refused);
        var before = Scheduler.Find(duty.Id);
        var saved = before is null ? Scheduler.Add(duty) : Scheduler.Update(duty);
        // Turning a duty back on is the owner letting it go on after the breaker paused it (the count starts again).
        if (saved.Duty is { Enabled: true } && before is { Enabled: false }) ResumeAfterBreaker(duty.Id, duty.AssistantId);
        return saved;
    }

    /// <summary>The owner lets a duty the failure breaker paused go on ("Try it again" from the duty's own page): the count starts again and its card is withdrawn.</summary>
    public void ResumeAfterBreaker(string dutyId, string assistantId = "")
    {
        controller.Duties?.Resume(dutyId, assistantId);
        Scheduler.Reevaluate();
    }

    /// <summary>Starts the scheduler: loads the duties, catches up or skips what came due while nth was closed, and, for a start made for one duty, runs it.</summary>
    public IReadOnlyList<string> Start()
    {
        var notes = Scheduler.Start();
        // The Morning digest duty is made once, after the duties are loaded (a save before that would write only it, and leave the others out).
        if (Digests?.EnsureDuty() is { } why) o.Faulted?.Invoke(why);
        ReraisePaused();
        SyncWake();
        _ = ReadWakeTimers();
        if (o.Mode.DutyId is { } id)
        {
            var refused = Scheduler.WakeRun(id);
            wakeAsked = true;
            if (refused is not null) QuitIfDone();
        }
        return notes;
    }

    private async Task ReadWakeTimers()
    {
        try { timers = await WakeTimerPolicy.Read(o.Processes, o.SystemFolder, stopping.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    // ---- the feed ----

    private void Publish(DutyFeedLine line)
    {
        var (kind, severity) = line.Kind switch
        {
            DutyFeedKind.Fired => (FeedEventKind.DutyFired, FeedSeverity.Info),
            DutyFeedKind.Quiet => (FeedEventKind.DutyFired, FeedSeverity.Quiet),
            DutyFeedKind.Skipped => (FeedEventKind.DutySkipped, FeedSeverity.Info),
            DutyFeedKind.WatcherChanged => (FeedEventKind.WatcherChanged, FeedSeverity.Info),
            _ => (FeedEventKind.WatcherChanged, FeedSeverity.Error),
        };
        o.Events.Publish(FeedDraft.About(kind, severity, o.Time.GetUtcNow(), line.Text, line.Mission, Guid.TryParse(line.AssistantId, out var assistant) ? assistant : null));
    }

    private void OnFeed(IReadOnlyList<FeedEvent> batch)
    {
        foreach (var e in batch)
        {
            if (DutyEvents.FromFeed(e) is { } duty) Scheduler.OnEvent(duty);
        }
    }

    // ---- missions ----

    private void OnMission(Marrow.Services.Assistant.MissionRecord record)
    {
        var state = record.Mission.State;
        // A mission that is Blocked (its lead chat ended with an error) will not go on by itself: for the duty that run has failed, and the
        // mission stays on the board for the owner.
        if (state is not (MissionState.Done or MissionState.Failed or MissionState.Stopped or MissionState.Blocked)) return;
        lock (gate)
        {
            if (!ended.Add(record.Id)) return;
        }
        if (record.Mission.Origin == MissionOrigin.Duty)
        {
            var outcome = state switch { MissionState.Done => DutyRunOutcome.Done, MissionState.Failed or MissionState.Blocked => DutyRunOutcome.Failed, _ => DutyRunOutcome.Stopped };
            Scheduler.MissionEnded(record.Id, outcome, record.Report);
            ReportToBreaker(record, outcome);
        }
        if (state == MissionState.Done) Scheduler.OnEvent(DutyEvents.MissionFinished(record));
        QuitIfDone();
    }

    // The breaker counts how runs end (13.2): a run that finished (even one that found nothing changed) is a success, a failed one a failure. A run the
    // owner stopped, and a dry run, are neither. The reason is data (a page or file can put words in it): the watch cuts it and never logs it.
    private void ReportToBreaker(Marrow.Services.Assistant.MissionRecord record, DutyRunOutcome outcome)
    {
        if (controller.Duties is not { } watch || record.DryRun || record.DutyId is not { } dutyId || outcome == DutyRunOutcome.Stopped) return;
        var duty = Scheduler.Find(dutyId);
        var name = duty?.Name ?? record.Mission.Title;
        var assistant = duty?.AssistantId ?? record.Mission.AssistantId;
        try { watch.RunEnded(new Marrow.App.Assistant.DutyRun(dutyId, name, assistant, outcome == DutyRunOutcome.Done, outcome == DutyRunOutcome.Done ? null : record.Reason ?? record.Report)); }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException) { o.Faulted?.Invoke("A run could not be counted for the failure breaker: " + error.Message); }
    }

    // nth started and the breaker still holds some duties: their cards did not survive the restart, so they are raised again.
    private void ReraisePaused()
    {
        if (controller.Duties is not { } watch) return;
        foreach (var health in watch.PausedDuties)
        {
            if (Scheduler.Find(health.DutyId) is { } duty) watch.Reraise(duty.Id, duty.Name, duty.AssistantId);
        }
    }

    private void OnHold() => Scheduler.Reevaluate();

    // A start made for one duty ends with it, unless the owner kept nth working or something else is live (7.4).
    private void QuitIfDone()
    {
        if (!quitWhenDone || !wakeAsked || Switches.KeepWorkingWhenClosed || o.Quit is not { } quit) return;
        if (Scheduler.Duties.Any(v => v.Running || v.Held)) return;
        if (controller.Runner.Missions.Any(r => r.Mission.State is not (MissionState.Done or MissionState.Failed or MissionState.Stopped or MissionState.Blocked))) return;
        quitWhenDone = false;
        _ = FinishAndQuit(quit);
    }

    private async Task FinishAndQuit(Action quit)
    {
        // The next wake is made before nth goes, so the entry is there for the next time.
        try { await wake.Sync(Scheduler.Duties.Select(v => v.Duty), stopping.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        o.OnMain(quit);
    }

    // ---- the first duty, the wake entries ----

    private void OnDutyAdded(Duty duty, bool first)
    {
        // The Morning digest is nth's own duty, not the owner's first one: it brings no card, and the first duty he makes still does (7.4).
        if (DigestDuty.Is(duty)) return;
        if (first || Scheduler.Duties.Count(v => !DigestDuty.Is(v.Duty)) == 1) FirstCard.FirstDutyMade();
        if (duty.WakePc) _ = ReadWakeTimers();
    }

    private void OnSchedulerChanged()
    {
        SyncWake();
        if (o.Mode.ForOneDuty) o.OnMain(QuitIfDone);
    }

    // Debounced: a burst of changes is one sync. It runs off the UI thread and never throws.
    private void SyncWake()
    {
        lock (gate)
        {
            if (disposed) return;
            if (wakeTimer is null) wakeTimer = o.Time.CreateTimer(_ => _ = RunWakeSync(), null, o.WakeSyncDelay, Timeout.InfiniteTimeSpan);
            else wakeTimer.Change(o.WakeSyncDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RunWakeSync()
    {
        try
        {
            await wake.Sync(Scheduler.Duties.Select(v => v.Duty), stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            o.Faulted?.Invoke("The wake entries could not be brought up to date: " + error.Message);
        }
    }

    // ---- the desktop gate ----

    private void NoteWaiting(Guid mission, string? line) => o.OnMain(() => controller.Runner.NoteWaiting(mission, line));

    private Task<bool> AskResume(Guid mission, string title, CancellationToken withdrawn)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        o.OnMain(() =>
        {
            var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question, $"Resume “{title}” now?")
            {
                Why = "The PC is unlocked and you are using it. The duty needs the desktop.",
                Rule = "Asks first while you are working",
                AssistantId = controller.Runner.Find(mission)?.Mission.AssistantId ?? "",
                MissionId = mission,
                Options = new ValueList<string>(["Resume now", "Not yet"]),
            };
            var raised = controller.Queue.Raise(card, (_, answer) => done.TrySetResult(answer.Text == "Resume now"));
            // The wait ended some other way (the owner went idle, or Stop): the question is taken back.
            withdrawn.Register(() => o.OnMain(() =>
            {
                if (controller.Queue.Withdraw(raised.Id, "The duty went on without waiting for an answer.")) done.TrySetResult(false);
            }));
        });
        return done.Task;
    }

    public void Dispose()
    {
        ITimer? timer;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            timer = wakeTimer;
            wakeTimer = null;
        }
        stopping.Cancel();
        timer?.Dispose();
        feedSubscription.Dispose();
        controller.Runner.Changed -= OnMission;
        controller.Hold.Changed -= OnHold;
        Scheduler.DutyAdded -= OnDutyAdded;
        Scheduler.Changed -= OnSchedulerChanged;
        tray?.Dispose();
        tray = null;
        Digests?.Dispose();
        Scheduler.Dispose();
        digestBook?.Dispose();
    }
}

/// <summary>The duty tools the controller is made with before the scheduler exists; the host binds the real ones when it starts.</summary>
public sealed class ForwardingDutyTools : IDutyTools
{
    private IDutyTools? inner;

    public void Bind(IDutyTools? tools) => Volatile.Write(ref inner, tools);

    public IReadOnlyList<DecisionInput>? Describe(AssistantToolCall call) => Volatile.Read(ref inner)?.Describe(call);

    public AssistantToolResult Create(AssistantToolCall call, Action<string> deliver) =>
        Volatile.Read(ref inner) is { } tools ? tools.Create(call, deliver) : AssistantToolResult.Failed("Not run: the duties are not running yet.");

    public DutyScope? ScopeOf(string dutyId) => Volatile.Read(ref inner)?.ScopeOf(dutyId) ?? new DutyScope { AllowedClasses = ValueList<ActionClass>.Empty };
}
