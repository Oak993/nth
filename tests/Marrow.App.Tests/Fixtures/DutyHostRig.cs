using Marrow.App.Assistant;
using Marrow.App.Tray;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-10): a duty host over a real controller, a real assistant store in a temporary folder, fake provider chats, a manual
/// clock, a fake Task Scheduler, a fake Run key and a fake desktop. Duties fire by moving the clock; their missions are real missions
/// of the runner, finished by ending the fake chats' turns. Helper code only (R-TEST-3).
/// </summary>
internal sealed class DutyHostRig : IDisposable
{
    public const string Exe = @"C:\Users\me\AppData\Local\nth\current\nth.exe";

    private readonly AssistantTempRoot temp = new();
    private readonly Dictionary<Guid, ChatThread> chats = [];

    public DutyHostRig(StartupMode? mode = null, Func<string, bool>? folderGranted = null, bool tray = true, DutyHealthBook? book = null, DigestSetup? digest = null, ReserveSetup? reserve = null)
    {
        Book = book ?? new DutyHealthBook();
        Clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero));
        Project = new MissionProject(MissionRig.Project, "App", Path.Combine(temp.Folder, "owner"));
        Directory.CreateDirectory(Project.Path);
        Workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees"));
        Store = AssistantStore.Open(temp.Roots);
        Events = new AssistantEventBus(Clock);
        Gate = new ForwardingDutyTools();
        Controller = new AssistantController(new AssistantControllerOptions
        {
            Agents = Agents,
            Thread = id => chats.GetValueOrDefault(id),
            Projects = () => [Project],
            Profiles = () => [new AssistantProfile("dextra", "Dextra")],
            Jobs = new AgentJobs(new FakeAgentJobFactory()),
            Workspaces = Workspaces,
            Audit = entry =>
            {
                lock (Audit) Audit.Add(entry);
            },
            Memory = new FakeMemory(FakeMemory.Plenty),
            Time = Clock,
            Duties = Gate,
            DutyHealth = Book,
            Reserve = reserve,
        });
        Tray = tray ? new AppTray(TrayHost, () => true, open: () => { }) : null;
        Host = new DutyHost(new DutyHostOptions
        {
            Store = Store,
            Controller = Controller,
            Events = Events,
            AssistantIds = () => ["dextra"],
            OnMain = work => work(),
            Projects = () => [Project],
            FolderGranted = folderGranted ?? (_ => false),
            ExePath = () => Exe,
            Time = Clock,
            Mode = mode ?? StartupMode.Normal,
            Tray = Tray,
            RunKey = RunKey,
            TaskHost = Tasks,
            Processes = Processes,
            Desktop = Desktop,
            Quit = () => Quits++,
            Faulted = message =>
            {
                lock (Faults) Faults.Add(message);
            },
            WakeSyncDelay = TimeSpan.FromSeconds(2),
            Digest = digest,
        });
        Gate.Bind(Host.Tools);
    }

    public ManualTimeProvider Clock { get; }

    /// <summary>The failure breaker's book (AS-15); a test shares one between two rigs to stand for a restart.</summary>
    public DutyHealthBook Book { get; }

    public MissionProject Project { get; }

    public FakeMissionAgents Agents { get; } = new();

    public FakeWorkspaces Workspaces { get; }

    public AssistantStore Store { get; }

    public AssistantEventBus Events { get; }

    public ForwardingDutyTools Gate { get; }

    public AssistantController Controller { get; }

    public FakeTrayHost TrayHost { get; } = new();

    public AppTray? Tray { get; }

    public FakeTaskSchedulerHost Tasks { get; } = new();

    public FakeRunKey RunKey { get; } = new();

    public FakeDesktop Desktop { get; } = new();

    public FakeProcessRunner Processes { get; } = new((_, _) => new ProcessOutcome(0, "Current AC Power Setting Index: 0x00000001\nCurrent DC Power Setting Index: 0x00000001\n", ""));

    public DutyHost Host { get; }

    public List<AuditEntry> Audit { get; } = [];

    public List<string> Faults { get; } = [];

    public int Quits { get; private set; }

    public string Folder => temp.Folder;

    public static Duty Daily(string id, int hour, string instructions = "Look at the numbers.") =>
        new(id, "Duty " + id, "dextra", new TimeTrigger(TimeSchedule.Daily(new TimeOnly(hour, 0)), "UTC"), instructions) { Since = new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero) };

    public static Duty Every(string id, int minutes, string instructions = "Look at the numbers.") =>
        new(id, "Duty " + id, "dextra", new TimeTrigger(TimeSchedule.Every(minutes), "UTC"), instructions) { Since = new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero) };

    /// <summary>The owner's Save of a duty (his Save confirms it), then the host starts.</summary>
    public Duty Save(Duty duty)
    {
        var (saved, problem) = Host.Save(duty);
        Assert.Null(problem);
        return saved!;
    }

    public MissionRecord MissionOf(Duty duty)
    {
        MissionRig.Until(() => Controller.Runner.Missions.Any(m => m.DutyId == duty.Id), "the duty's mission");
        return Controller.Runner.Missions.First(m => m.DutyId == duty.Id);
    }

    public MissionRecord Now(MissionRecord record) => Controller.Runner.Find(record.Id)!;

    /// <summary>The mission's lead is running (its chat started behind the gate).</summary>
    public Guid LeadOf(MissionRecord record)
    {
        MissionRig.Until(() => Now(record).LeadThread is not null, "the lead to start");
        var lead = Now(record).LeadThread!.Value;
        // The app knows the lead's chat (its folder places a command the lead asks to run); without it every command is unplaced and unknown.
        lock (chats)
        {
            if (!chats.ContainsKey(lead))
                chats[lead] = MissionRig.NewChat(() => new ChatThread(Project.Id, ProviderKind.Claude, "model-a", null, SealedProviderConfig.CappedMode) { Id = lead, MissionID = record.Id, Title = "Lead" });
        }
        return lead;
    }

    /// <summary>The lead answers and the reviewer passes it: the mission ends Done.</summary>
    public void Complete(MissionRecord record, string report)
    {
        var lead = LeadOf(record);
        Agents.Finish(lead, report);
        Controller.OnTurnFinished(lead, TurnStatus.Completed, false);
        MissionRig.Until(() => Now(record).ReviewerThread is not null, "the reviewer to start");
        var reviewer = Now(record).ReviewerThread!.Value;
        Agents.Finish(reviewer, "VERDICT: PASS");
        Controller.OnTurnFinished(reviewer, TurnStatus.Completed, false);
        MissionRig.Until(() => Now(record).Mission.State == MissionState.Done, "Done");
    }

    public void Dispose()
    {
        Host.Dispose();
        Tray?.Dispose();
        Controller.Dispose();
        Events.Dispose();
        Store.Dispose();
        temp.Dispose();
    }
}
