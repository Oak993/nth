using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-5: fake provider chats for the mission runner. Each started chat is a scripted provider session: the test
/// says when its turn runs and ends, what it answered, and which approvals and questions it raises; the fake records
/// every message, answer, interrupt and stop the runner sends it. Everything runs inline on the test's thread.
/// </summary>
internal sealed class FakeMissionAgents : IMissionAgents
{
    internal sealed class Chat(Guid id, AgentLaunch launch)
    {
        public Guid Id { get; } = id;
        public AgentLaunch Launch { get; } = launch;
        public bool Running { get; set; } = true;
        public string? Final { get; set; }
        public List<string> Sent { get; } = [];
        public int Interrupts { get; set; }
        public int SessionStops { get; set; }
    }

    private readonly object gate = new();

    public List<Chat> Chats { get; } = [];

    public List<(Guid Thread, string Request, bool Approved)> Resolutions { get; } = [];

    public List<(Guid Thread, string Request, ValueMap<string, ValueList<string>> Answers)> Answers { get; } = [];

    public Dictionary<Guid, List<AgentHead>> HeadsOf { get; } = [];

    public List<Guid> HeadsStopped { get; } = [];

    public List<Guid> HeadStopped { get; } = [];

    public List<string> ModelList { get; set; } = ["model-a", "model-b"];

    /// <summary>Messages a busy or missing chat refused.</summary>
    public List<(Guid Thread, string Text)> Refused { get; } = [];

    public Func<AgentLaunch, Exception?>? FailStart { get; set; }

    /// <summary>When set, posted work waits until <see cref="RunPosted"/> (the app's main-thread queue, held still).</summary>
    public bool Defer { get; set; }

    private readonly List<Action> posted = [];

    public void Post(Action work)
    {
        if (!Defer)
        {
            work();
            return;
        }
        lock (gate) posted.Add(work);
    }

    /// <summary>Runs the work posted while <see cref="Defer"/> held it, in order.</summary>
    public void RunPosted()
    {
        List<Action> work;
        lock (gate)
        {
            work = [.. posted];
            posted.Clear();
        }
        foreach (var item in work) item();
    }

    public Guid Start(AgentLaunch launch)
    {
        if (FailStart?.Invoke(launch) is { } error) throw error;
        var chat = new Chat(Guid.NewGuid(), launch);
        chat.Sent.Add(launch.Brief);
        lock (gate) Chats.Add(chat);
        return chat.Id;
    }

    /// <summary>A chat of a mission nth loaded from its journal: it exists, and no turn of it runs.</summary>
    public Chat Restore(Guid thread, AgentLaunch launch)
    {
        var chat = new Chat(thread, launch) { Running = false };
        lock (gate) Chats.Add(chat);
        return chat;
    }

    public Chat Of(Guid thread)
    {
        lock (gate) return Chats.Single(c => c.Id == thread);
    }

    public Chat Lead(MissionRecord record) => Of(record.LeadThread!.Value);

    public bool Send(Guid thread, string text)
    {
        lock (gate)
        {
            var chat = Chats.FirstOrDefault(c => c.Id == thread);
            if (chat is null || chat.Running)
            {
                Refused.Add((thread, text));
                return false;
            }
            chat.Sent.Add(text);
            chat.Running = true;
            return true;
        }
    }

    public bool TurnRunning(Guid thread)
    {
        lock (gate) return Chats.FirstOrDefault(c => c.Id == thread)?.Running ?? false;
    }

    public void Interrupt(Guid thread)
    {
        lock (gate)
        {
            if (Chats.FirstOrDefault(c => c.Id == thread) is { } chat) chat.Interrupts++;
        }
    }

    public void StopHeads(Guid lead) => HeadsStopped.Add(lead);

    public void StopHead(Guid head) => HeadStopped.Add(head);

    public void StopSession(Guid thread)
    {
        lock (gate)
        {
            if (Chats.FirstOrDefault(c => c.Id == thread) is { } chat) chat.SessionStops++;
        }
    }

    public void Resolve(Guid thread, ApprovalRequest request, bool approve) => Resolutions.Add((thread, request.Id, approve));

    public void Answer(Guid thread, QuestionRequest request, ValueMap<string, ValueList<string>> answers) => Answers.Add((thread, request.Id, answers));

    public string? FinalMessage(Guid thread)
    {
        lock (gate) return Chats.FirstOrDefault(c => c.Id == thread)?.Final;
    }

    public IReadOnlyList<AgentHead> Heads(Guid lead) => HeadsOf.TryGetValue(lead, out var heads) ? heads : [];

    public IReadOnlyList<string> Models(ProviderKind provider) => ModelList;

    /// <summary>The lines nth showed in chats (AS-7: an approval it answered for the owner).</summary>
    public List<(Guid Thread, string Text)> Notes { get; } = [];

    public void Note(Guid thread, string text)
    {
        lock (gate) Notes.Add((thread, text));
    }

    /// <summary>The moves of a live chat to another account that were asked of it (AS-14).</summary>
    public List<(Guid Thread, Guid Account)> AccountMoves { get; } = [];

    /// <summary>What a move answers; null: it is done at once. A test returns an unfinished task to make a move take time.</summary>
    public Func<Guid, Guid, Task<bool>>? MoveAnswer { get; set; }

    public Task<bool> MoveAccount(Guid thread, Guid account)
    {
        lock (gate) AccountMoves.Add((thread, account));
        return MoveAnswer?.Invoke(thread, account) ?? Task.FromResult(true);
    }

    /// <summary>What each chat holds as context (AS-6: a cold start reads it again); unknown when absent.</summary>
    public Dictionary<Guid, long> Context { get; } = [];

    public long? ContextTokens(Guid thread) => Context.TryGetValue(thread, out var tokens) ? tokens : null;

    /// <summary>What each chat says it is doing (AS-6: the "Still pausing" note names it).</summary>
    public Dictionary<Guid, string> Doing { get; } = [];

    public string? Activity(Guid thread) => Doing.TryGetValue(thread, out var text) ? text : null;

    /// <summary>The heads whose finish a Pause held and a Resume gave back (AS-6).</summary>
    public List<(Guid Head, TurnStatus Status)> HeldFinished { get; } = [];

    public void FinishHeld(Guid head, TurnStatus status) => HeldFinished.Add((head, status));

    /// <summary>The stopped heads whose chats were started again, and what they were told (AS-6).</summary>
    public List<(Guid Head, string Text)> HeadsRestarted { get; } = [];

    public bool RestartHead(Guid head, string text)
    {
        lock (gate)
        {
            HeadsRestarted.Add((head, text));
            if (Chats.FirstOrDefault(c => c.Id == head) is { } chat)
            {
                chat.Sent.Add(text);
                chat.Running = true;
            }
        }
        return true;
    }

    /// <summary>The chat's turn ends with this answer.</summary>
    public void Finish(Guid thread, string? answer)
    {
        lock (gate)
        {
            var chat = Chats.Single(c => c.Id == thread);
            chat.Running = false;
            chat.Final = answer;
        }
    }
}

/// <summary>Workspaces without git: what Create, Review, Merge and Discard were asked, and what they answer.</summary>
internal sealed class FakeWorkspaces(string root) : IMissionWorkspaces
{
    public List<string> Calls { get; } = [];

    public MergeReview Changes { get; set; } = new(new ValueList<WorkspaceChange>([new WorkspaceChange("src/login.cs", WorkspaceChangeKind.Modified) { Insertions = 30, Deletions = 8 }]), "diff --git a/src/login.cs");

    public MergeResult MergeResult { get; set; } = new(MergeOutcome.Merged);

    public Exception? CreateFails { get; set; }

    /// <summary>What Revert was asked to do with each workspace (AS-6).</summary>
    public List<MissionWorkspace> Reverted { get; } = [];

    public Exception? RevertFails { get; set; }

    /// <summary>What an interrupted revert is found to have done (AS-6): null when it did nothing.</summary>
    public RevertedWork? FoundReverted { get; set; }

    public Task<RevertedWork?> FindReverted(MissionWorkspace workspace, Guid mission)
    {
        Calls.Add("find reverted");
        return Task.FromResult(FoundReverted);
    }

    public Task<RevertedWork> Revert(MissionWorkspace workspace, Guid mission, DateTimeOffset now, int changedFiles)
    {
        Calls.Add("revert");
        if (RevertFails is { } error) throw error;
        Reverted.Add(workspace);
        return Task.FromResult(new RevertedWork(workspace, "refs/nth/reverted/" + mission.ToString("N") + "/stamp", "c0ffee", now, changedFiles));
    }

    public Task<MissionWorkspace> Restore(RevertedWork reverted, Guid mission)
    {
        Calls.Add("restore");
        return Task.FromResult(reverted.Workspace);
    }

    public Task DropReverted(RevertedWork reverted, Guid mission)
    {
        Calls.Add("drop");
        return Task.CompletedTask;
    }

    public Task<MissionWorkspace> Create(Guid mission, string title, string source, int attempt = 1)
    {
        Calls.Add("create " + source);
        if (CreateFails is { } error) throw error;
        var path = Path.Combine(root, mission.ToString("N") + (attempt > 1 ? "-" + attempt : ""));
        return Task.FromResult(new MissionWorkspace(MissionWorkspaceKind.Worktree, source, path)
        {
            WorkingFolder = path,
            Repository = source,
            Branch = MissionWorkspaces.BranchName(mission, title, attempt),
            BaseBranch = "main",
            BaseCommit = "0000000000000000000000000000000000000000",
        });
    }

    public Task<MergeReview> Review(MissionWorkspace workspace)
    {
        Calls.Add("review");
        return Task.FromResult(Changes);
    }

    /// <summary>The review each Merge was asked to apply.</summary>
    public List<MergeReview> Merged { get; } = [];

    public Task<MergeResult> Merge(MissionWorkspace workspace, MergeReview reviewed)
    {
        Calls.Add("merge");
        Merged.Add(reviewed);
        return Task.FromResult(MergeResult);
    }

    public Task<MergeResult> RebaseFirst(MissionWorkspace workspace)
    {
        Calls.Add("rebase");
        return Task.FromResult(new MergeResult(MergeOutcome.Merged));
    }

    public Task Discard(MissionWorkspace workspace)
    {
        Calls.Add("discard");
        return Task.CompletedTask;
    }
}

/// <summary>An agent job whose processes the test ends.</summary>
internal sealed class FakeAgentJob : IAgentJob
{
    public JobObject? Native => null;

    public int LiveProcesses { get; set; } = 1;

    public int Terminations { get; private set; }

    public bool Disposed { get; private set; }

    public bool EndsOnTerminate { get; set; } = true;

    public void Terminate()
    {
        Terminations++;
        if (EndsOnTerminate) LiveProcesses = 0;
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeAgentJobFactory : IAgentJobFactory
{
    public List<FakeAgentJob> Made { get; } = [];

    public bool EndOnTerminate { get; set; } = true;

    public Exception? Fails { get; set; }

    public IAgentJob Create()
    {
        if (Fails is { } error) throw error;
        var job = new FakeAgentJob { EndsOnTerminate = EndOnTerminate };
        Made.Add(job);
        return job;
    }
}

internal sealed class FakeMemory(MemoryReading? reading) : IMemoryReader
{
    public MemoryReading? Reading { get; set; } = reading;

    public MemoryReading? Read() => Reading;

    public static MemoryReading Plenty => new(32UL << 30, 20UL << 30);
}

/// <summary>A runner over fakes, with the policy the test sets per request title.</summary>
internal sealed class MissionRig
{
    public MissionRig(string root, Func<ApprovalRequest, ApprovalVerdict>? policy = null, MissionLimits? limits = null, MemoryReading? memory = null,
        Action<MissionRecord>? save = null, MissionRigControls? controls = null)
    {
        Root = root;
        Workspaces = new FakeWorkspaces(Path.Combine(root, "worktrees"));
        Memory = new FakeMemory(memory ?? FakeMemory.Plenty);
        Gate = new AgentGate(() => limits ?? MissionLimits.Default, Memory);
        Jobs = new AgentJobs(JobFactory);
        Policy = policy ?? (r => new ApprovalVerdict(new Classification(ActionClass.Work, "work"), new PolicyDecision(Verdict.Allow, PolicyRule.Alone, "Work runs alone")));
        Runner = new MissionRunner(new MissionRunnerOptions
        {
            Agents = Agents,
            Queue = Queue,
            Gate = Gate,
            Jobs = Jobs,
            Workspaces = Workspaces,
            Policy = (_, _, request) => Policy(request),
            Limits = () => limits ?? MissionLimits.Default,
            Save = r =>
            {
                lock (Saved) Saved.Add(r);
                save?.Invoke(r);
            },
            Audit = e =>
            {
                if (AuditFails?.Invoke(e) == true) throw new IOException("The activity log's disk is full.");
                lock (Audit) Audit.Add(e);
            },
            // Dextra AS-6: the controls' seams, when a test asks for them.
            StopKey = controls?.StopKey,
            Hold = controls?.Hold,
            PcStreams = controls?.PcStreams,
            ReserveCard = controls?.ReserveCard,
            CurrentInputs = controls?.CurrentInputs,
            Time = controls?.Time ?? TimeProvider.System,
        });
    }

    public string Root { get; }

    public FakeMissionAgents Agents { get; } = new();

    public DecisionQueue Queue { get; } = new();

    public FakeWorkspaces Workspaces { get; }

    public FakeMemory Memory { get; }

    public AgentGate Gate { get; }

    public FakeAgentJobFactory JobFactory { get; } = new();

    public AgentJobs Jobs { get; }

    public Func<ApprovalRequest, ApprovalVerdict> Policy { get; set; }

    public MissionRunner Runner { get; }

    public List<MissionRecord> Saved { get; } = [];

    public List<AuditEntry> Audit { get; } = [];

    /// <summary>When it says so for an entry, writing that entry fails (the log's disk is full).</summary>
    public Func<AuditEntry, bool>? AuditFails { get; set; }

    public static readonly Guid Project = Guid.Parse("70000000-0000-4000-8000-000000000007");

    public MissionRequest Request(string title = "Fix the login timeout", ProviderKind provider = ProviderKind.Claude, bool team = false) =>
        new(title, "Make the login wait 30 s, not 5 s, and keep the tests green.", "dextra", Project, Path.Combine(Root, "owner"), provider) { Team = team, Model = "model-a" };

    /// <summary>Starts a mission and waits until its lead runs.</summary>
    public MissionRecord Started(MissionRequest? request = null)
    {
        var start = Runner.Start(request ?? Request());
        Assert.True(start.Launched.Wait(TimeSpan.FromSeconds(10)));
        return Runner.Find(start.Record.Id)!;
    }

    public MissionRecord Now(MissionRecord record) => Runner.Find(record.Id)!;

    public static ApprovalRequest Approval(string id, string command) =>
        new(id, ApprovalRequest.Kind.Command, command, new ValueList<ApprovalRequest.Option>([
            new("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new("always", "Always allow", ApprovalRequest.Option.Role.ApproveAlways),
            new("deny", "Deny", ApprovalRequest.Option.Role.Decline),
        ]));

    /// <summary>Waits (bounded) for a condition the runner reaches off the test's thread (a gate wait, a review start).</summary>
    public static void Until(Func<bool> condition, string what)
    {
        Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)), "Timed out waiting for " + what);
    }

    /// <summary>A chat as the app makes it: ChatThread reads the Hydra default from the settings store, so one is bound
    /// (in memory) while it is made.</summary>
    public static ChatThread NewChat(Func<ChatThread> make)
    {
        using var defaults = UserDefaults.Standard.Override(new Marrow.Testing.InMemorySettingsStore());
        return make();
    }

    /// <summary>True when the task completes within the bound (a gate admit, a launch).</summary>
    public static bool Within(Task task, double seconds = 10) => task.Wait(TimeSpan.FromSeconds(seconds));
}
