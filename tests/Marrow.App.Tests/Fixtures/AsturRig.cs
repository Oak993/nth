using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-16: the controller over fake chats with a real team of Asturs in a throwaway store, the real classifier and policy engine, the home view's
/// seam as a fake, and the nth-dextra tools driven through the real runner (so the engine decides every call). Delegations are saved to
/// <see cref="Saved"/> and read from <c>delegations</c>. Open it with <see cref="OpenAsync"/>: Dextra's own profile is made first.
/// </summary>
internal sealed class AsturRig : IDisposable
{
    public static readonly InjectionReport Pass = new(1, 1, ValueList<string>.Empty);

    private readonly AssistantTempRoot temp = new();

    private AsturRig(bool autoAnswer, JsonObject? delegations, ReserveSettings? budgets, bool withTeam)
    {
        Store = AssistantStore.Open(temp.Roots);
        Budgets = budgets ?? new ReserveSettings();
        Team = AssistantTeam.Open(Store, new AssistantTeamOptions { Budgets = Budgets });
        Project = new MissionProject(MissionRig.Project, "App", Path.Combine(temp.Folder, "owner"));
        Projects.Add(Project);
        Workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees"));
        Homes = new FakeHomes(this);
        Switch = autoAnswer ? AutoAnswerSwitch.AfterCorpus(() => Pass) : AutoAnswerSwitch.Off;
        Assert.True(MissionRig.Within(Switch.Ready));
        Loaded = delegations;
        WithTeam = withTeam;
    }

    public static async Task<AsturRig> OpenAsync(bool autoAnswer = true, JsonObject? delegations = null, ReserveSettings? budgets = null, bool withTeam = true)
    {
        var rig = new AsturRig(autoAnswer, delegations, budgets, withTeam);
        rig.Dextra = (await rig.Team.EnsureRootAsync()).Profile!;
        rig.Controller = rig.NewController();
        return rig;
    }

    public AssistantStore Store { get; }

    public ReserveSettings Budgets { get; }

    public AssistantTeam Team { get; }

    public AssistantProfile Dextra { get; private set; } = null!;

    public MissionProject Project { get; }

    /// <summary>The projects the owner has: the one by default; a test may add more.</summary>
    public List<MissionProject> Projects { get; } = [];

    public FakeWorkspaces Workspaces { get; }

    public FakeMissionAgents Agents { get; } = new();

    public FakeAgentJobFactory JobFactory { get; } = new();

    public FakeHomes Homes { get; }

    public AutoAnswerSwitch Switch { get; }

    public Dictionary<Guid, ChatThread> Chats { get; } = [];

    public List<AuditEntry> Audit { get; } = [];

    public JsonObject? Loaded { get; set; }

    /// <summary>The delegations document each time the controller saved it.</summary>
    public List<JsonObject> Saved { get; } = [];

    /// <summary>The same saves, each with how many messages had been sent to the home chats when it was saved.</summary>
    public List<(JsonObject Doc, int Sent)> SavedWhen { get; } = [];

    /// <summary>When set, saving the delegations fails.</summary>
    public bool SaveFails { get; set; }

    public AssistantController Controller { get; private set; } = null!;

    /// <summary>Whether the controller has a team and a home seam (a build without them says the tools are not part of it).</summary>
    public bool WithTeam { get; }

    /// <summary>A second controller over the same store and team, as the next start of nth would make (the missions are loaded by the caller).</summary>
    public AssistantController NewController()
    {
        var controller = new AssistantController(new AssistantControllerOptions
        {
            Agents = Agents,
            Thread = id => Chats.GetValueOrDefault(id),
            Projects = () => [.. Projects],
            Profiles = () => Team.Profiles,
            Jobs = new AgentJobs(JobFactory),
            Workspaces = Workspaces,
            Audit = e =>
            {
                lock (Audit) Audit.Add(e);
            },
            Memory = new FakeMemory(FakeMemory.Plenty),
            Model = "model-a",
            AutoAnswer = Switch,
            Asturs = WithTeam
                ? new AsturSetup
                {
                    Team = Team,
                    Homes = Homes,
                    LoadDelegations = () => Loaded,
                    SaveDelegations = body =>
                    {
                        if (SaveFails) throw new IOException("The store's disk is full.");
                        lock (Saved)
                        {
                            var clone = (JsonObject)body.DeepClone();
                            Saved.Add(clone);
                            SavedWhen.Add((clone, Agents.Chats.ToArray().Where(c => c.Launch.Mission == Guid.Empty).Sum(c => c.Sent.Count)));
                        }
                    },
                }
                : null,
            HomeChats = () => [.. Chats.Values.Where(c => c.AssistantID is not null && c.MissionID is null).Select(c => c.Id)],
            ScanOn = work => work(),
        });
        return controller;
    }

    public List<AuditEntry> Logged()
    {
        lock (Audit) return [.. Audit];
    }

    /// <summary>An assistant's home chat: its own, in no mission, known to the fake chats so a message to it is recorded.</summary>
    public ChatThread AddHomeChat(AssistantProfile who)
    {
        var chat = MissionRig.NewChat(() => new ChatThread(Project.Id, ProviderKind.Claude, "model-a", null, RuntimeMode.AutoAcceptEdits)
        {
            AssistantID = Guid.Parse(who.Id),
            Title = who.Name,
        });
        Chats[chat.Id] = chat;
        Agents.Restore(chat.Id, new AgentLaunch(Guid.Empty, who.Id, who.Name, "", Project.Id, Project.Path, ProviderKind.Claude));
        return chat;
    }

    /// <summary>Dextra's home chat.</summary>
    public ChatThread DextraHome() => AddHomeChat(Dextra);

    /// <summary>An Astur, made through the team the way the owner's card makes one.</summary>
    public async Task<AssistantProfile> MakeAsync(string name, AsturShape shape, Func<AsturDraft, AsturDraft>? change = null)
    {
        var draft = new AsturDraft { Name = name, Shape = shape, Area = name + " looks after its area.", Budget = new(15) };
        var made = await Team.CreateAsync(change?.Invoke(draft) ?? draft);
        Assert.True(made.Ok, made.Problem);
        return made.Profile!;
    }

    /// <summary>The nth-dextra tools for a chat, through the real runner: the engine decides every call.</summary>
    public AssistantToolRunner Runner(ChatThread chat)
    {
        using var bound = AssistantMcpBridge.Host.Override(Controller);
        var registration = AssistantMcpBridge.Register(BuiltInServerFixtures.Config(ProviderKind.Claude, chat.Id))
            ?? throw new InvalidOperationException("The chat is not one Dextra serves.");
        return new AssistantToolRunner(registration);
    }

    public Task<AssistantToolResult> Call(AssistantToolRunner runner, string tool, string json) =>
        runner.Call(tool, JSONValue.Parse(json), AssistantRoute.Mcp, CancellationToken.None);

    /// <summary>A mission's lead chat as the app would have it, so the classifier and the taint tracker know it.</summary>
    public ChatThread Lead(MissionRecord record)
    {
        var thread = record.LeadThread!.Value;
        return Chats[thread] = MissionRig.NewChat(() => new ChatThread(Project.Id, ProviderKind.Claude, "model-a", null, SealedProviderConfig.CappedMode)
        {
            Id = thread,
            MissionID = record.Id,
            AssistantID = Guid.Parse(record.Mission.AssistantId),
            Title = "Lead",
        });
    }

    public MissionRecord Now(MissionRecord record) => Controller.Runner.Find(record.Id)!;

    /// <summary>The missions the controller's runner has, newest last.</summary>
    public IReadOnlyList<MissionRecord> Missions => Controller.Runner.Missions;

    /// <summary>The chat's tool call starts and ends, with what it returned.</summary>
    public void Read(Guid thread, string url, string output)
    {
        var id = "t" + Guid.NewGuid().ToString("N")[..8];
        Controller.OnProviderEvent(thread, new ProviderEvent.ToolStarted(id, new ToolCall { ToolCallKind = ToolCall.Kind.Web, Title = url }));
        Controller.OnProviderEvent(thread, new ProviderEvent.ToolOutput(id, output));
        Controller.OnProviderEvent(thread, new ProviderEvent.ToolUpdated(id, new ToolUpdate { Status = ToolCall.Status.Completed }));
    }

    /// <summary>Waits until the mission's lead is running (the agent gate let it on), and gives its chat.</summary>
    public MissionRecord Started(MissionRecord record)
    {
        MissionRig.Until(() => Controller.Runner.Find(record.Id)?.LeadThread is not null, "the lead to start");
        var started = Now(record);
        Lead(started);
        return started;
    }

    /// <summary>The lead ends its turn with <paramref name="report"/>, the reviewer passes it, and the mission is Done.</summary>
    public void Complete(MissionRecord record, string report)
    {
        var lead = Started(record).LeadThread!.Value;
        Agents.Finish(lead, report);
        Controller.Runner.OnTurnFinished(lead, TurnStatus.Completed, continues: false);
        MissionRig.Until(() => Now(record).ReviewerThread is not null, "the reviewer to start");
        var reviewer = Now(record).ReviewerThread!.Value;
        Agents.Finish(reviewer, "I read the work.\nVERDICT: PASS");
        Controller.Runner.OnTurnFinished(reviewer, TurnStatus.Completed, continues: false);
        MissionRig.Until(() => Now(record).Mission.State == MissionState.Done, "the mission to be done");
    }

    /// <summary>The owner stops one mission: its turn is cut, its processes end, and it is Stopped.</summary>
    public void StopMission(MissionRecord record)
    {
        Assert.Null(Controller.Controls.Stop(record.Id));
        EndStopped(record);
    }

    /// <summary>Stop all (the header's button): every mission is cut and Stopped.</summary>
    public void StopEverything()
    {
        Controller.Stopper.Press(StopSource.Header);
        foreach (var record in Missions.Where(m => m.LeadThread is not null).ToList()) EndStopped(record);
    }

    private void EndStopped(MissionRecord record)
    {
        if (Now(record).LeadThread is { } lead)
        {
            Agents.Finish(lead, null);
            Controller.Runner.OnTurnFinished(lead, TurnStatus.Interrupted, continues: false);
        }
        foreach (var job in JobFactory.Made) job.LiveProcesses = 0;
        Controller.Runner.CheckStopping();
        MissionRig.Until(() => Now(record).Mission.State == MissionState.Stopped, "the mission to stop");
    }

    /// <summary>The reviewer fails the work three times: the mission is Failed.</summary>
    public void Fail(MissionRecord record)
    {
        var lead = Started(record).LeadThread!.Value;
        for (var round = 1; round <= 3; round++)
        {
            Agents.Finish(lead, "Done, round " + round + ".");
            Controller.Runner.OnTurnFinished(lead, TurnStatus.Completed, continues: false);
            MissionRig.Until(() => Now(record).ReviewerThread is not null, "the reviewer to start");
            var reviewer = Now(record).ReviewerThread!.Value;
            Agents.Finish(reviewer, "VERDICT: FAIL not good enough");
            Controller.Runner.OnTurnFinished(reviewer, TurnStatus.Completed, continues: false);
        }
        MissionRig.Until(() => Now(record).Mission.State == MissionState.Failed, "the mission to fail");
    }

    /// <summary>What a chat was sent, in order.</summary>
    public List<string> SentTo(Guid thread) => [.. Agents.Chats.Where(c => c.Id == thread).SelectMany(c => c.Sent)];

    public void Dispose()
    {
        Controller.Dispose();
        Team.Dispose();
        Store.Dispose();
        temp.Dispose();
    }
}
