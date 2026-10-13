using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-7: the controller over fake chats with the real classifier, engine and privacy check, and an auto-answer
/// switch that has settled (passed unless the test says otherwise). "dotnet test" is on the owner's allowlist. It
/// records the audit lines and the instructions found, and makes provider events for the chats it knows. The home chats'
/// taint is saved to <see cref="Saved"/> and read from <c>load</c>; untrusted output is read for instructions at once, or
/// held in <see cref="HeldScans"/> while <see cref="HoldScans"/> is set.
/// </summary>
internal sealed class DextraTaintRig : IDisposable
{
    public static readonly InjectionReport Pass = new(1, 1, ValueList<string>.Empty);

    private readonly AssistantTempRoot temp = new();

    public DextraTaintRig(bool autoAnswer = true, Func<InjectionReport>? check = null, Func<JsonObject?>? load = null,
        IEnumerable<ChatThread>? chats = null, TimeProvider? time = null)
    {
        foreach (var chat in chats ?? []) Chats[chat.Id] = chat;
        Project = new MissionProject(MissionRig.Project, "App", Path.Combine(temp.Folder, "owner"));
        Workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees"));
        Switch = autoAnswer ? AutoAnswerSwitch.AfterCorpus(check ?? (() => Pass)) : AutoAnswerSwitch.Off;
        Assert.True(MissionRig.Within(Switch.Ready));
        Controller = new AssistantController(new AssistantControllerOptions
        {
            Agents = Agents,
            Thread = id => ThreadFails?.Invoke(id) == true ? throw new IOException("The chat store could not be read.") : Chats.GetValueOrDefault(id),
            Projects = () => [Project],
            Profiles = () => [new AssistantProfile("dextra", "Dextra") { Connections = new(["calendar"]) }],
            Jobs = new AgentJobs(new FakeAgentJobFactory()),
            Workspaces = Workspaces,
            Audit = e =>
            {
                if (AuditFails?.Invoke(e) == true) throw new IOException("The activity log's disk is full.");
                lock (Audit) Audit.Add(e);
            },
            Memory = new FakeMemory(FakeMemory.Plenty),
            Model = "model-a",
            AutoAnswer = Switch,
            Allowlist = () => ["dotnet test"],
            PersonalDataRecipients = () => [.. Recipients],
            InstructionFound = (thread, source, finding) =>
            {
                lock (Found) Found.Add((thread, source, finding));
            },
            LoadHomeTaint = load ?? (() => null),
            SaveHomeTaint = body =>
            {
                if (SaveFails) throw new IOException("The store's disk is full.");
                lock (Saved) Saved.Add((JsonObject)body.DeepClone());
            },
            HomeChats = () => [.. Chats.Values.Where(c => c.AssistantID is not null && c.MissionID is null).Select(c => c.Id)],
            ScanOn = work =>
            {
                if (HoldScans) lock (HeldScans) HeldScans.Add(work);
                else work();
            },
            Time = time ?? TimeProvider.System,
        });
    }

    /// <summary>The home chats' taint as the controller saved it, in order.</summary>
    public List<JsonObject> Saved { get; } = [];

    /// <summary>When set, saving the home chats' taint fails.</summary>
    public bool SaveFails { get; set; }

    /// <summary>When set, reading untrusted output for instructions waits in <see cref="HeldScans"/> (off the main thread).</summary>
    public bool HoldScans { get; set; }

    public List<Action> HeldScans { get; } = [];

    /// <summary>Runs the held reads, in order.</summary>
    public void RunScans()
    {
        List<Action> work;
        lock (HeldScans)
        {
            work = [.. HeldScans];
            HeldScans.Clear();
        }
        foreach (var item in work) item();
    }

    /// <summary>A head of a chat as Hydra makes one: Dextra's when its lead is, in the lead's mission if any.</summary>
    public ChatThread HeadOf(ChatThread lead)
    {
        var head = MissionRig.NewChat(() => new ChatThread(Project.Id, ProviderKind.Claude, "model-a", null, lead.RuntimeMode)
        {
            ParentThreadID = lead.Id,
            IsInPanel = true,
            Title = "Head",
            Hydra = new HydraHeadInfo(1, "Head", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated),
            MissionID = lead.MissionID,
            AssistantID = lead.AssistantID,
        });
        Chats[head.Id] = head;
        return head;
    }

    public MissionProject Project { get; }

    public FakeWorkspaces Workspaces { get; }

    public FakeMissionAgents Agents { get; } = new();

    public Dictionary<Guid, ChatThread> Chats { get; } = [];

    public List<AuditEntry> Audit { get; } = [];

    /// <summary>The audit lines so far (the auto-answer line arrives from the thread pool, so it is read under the lock).</summary>
    public List<AuditEntry> Logged()
    {
        lock (Audit) return [.. Audit];
    }

    public List<string> Recipients { get; } = [];

    /// <summary>When it says so for an entry, writing that entry fails (the log's disk is full).</summary>
    public Func<AuditEntry, bool>? AuditFails { get; set; }

    /// <summary>When it says so for a chat, reading that chat fails (nth cannot place what it does).</summary>
    public Func<Guid, bool>? ThreadFails { get; set; }

    public List<(Guid Thread, TaintSource Source, InstructionFinding Finding)> Found { get; } = [];

    public AutoAnswerSwitch Switch { get; }

    public AssistantController Controller { get; }

    /// <summary>A mission started through the runner, its lead chat on record in the mission's own folder.</summary>
    public MissionRecord Mission(string title = "Fix the login timeout", bool readers = false, bool team = false)
    {
        var start = Controller.Runner.Start(new MissionRequest(title, "Make it 30 s.", "dextra", Project.Id, Project.Path, ProviderKind.Claude)
        {
            Readers = readers,
            Team = team,
        });
        Assert.True(MissionRig.Within(start.Launched));
        var record = Controller.Runner.Find(start.Record.Id)!;
        Chat(record.LeadThread!.Value, record, "Lead");
        return record;
    }

    /// <summary>A chat of the mission as the app would have it (in the mission's own folder).</summary>
    public ChatThread Chat(Guid thread, MissionRecord record, string title) =>
        Chats[thread] = MissionRig.NewChat(() => new ChatThread(Project.Id, ProviderKind.Claude, "model-a", null, SealedProviderConfig.CappedMode)
        {
            Id = thread,
            MissionID = record.Id,
            Title = title,
            WorktreePath = Controller.Runner.Find(record.Id)?.Workspace?.WorkingFolder,
        });

    /// <summary>Dextra's home chat: Dextra's, in no mission.</summary>
    public ChatThread Home()
    {
        var chat = MissionRig.NewChat(() => new ChatThread(Project.Id, ProviderKind.Claude, "model-a", null, RuntimeMode.AutoAcceptEdits)
        {
            AssistantID = Guid.NewGuid(),
            Title = "Dextra",
        });
        Chats[chat.Id] = chat;
        return chat;
    }

    public MissionRecord Now(MissionRecord record) => Controller.Runner.Find(record.Id)!;

    /// <summary>The chat's tool call starts and ends, with what it returned.</summary>
    public void Tool(Guid thread, ToolCall.Kind kind, string title, string? output = null, string? detail = null)
    {
        var id = "t" + Guid.NewGuid().ToString("N")[..8];
        Controller.OnProviderEvent(thread, new ProviderEvent.ToolStarted(id, new ToolCall { ToolCallKind = kind, Title = title, Detail = detail }));
        if (output is not null) Controller.OnProviderEvent(thread, new ProviderEvent.ToolOutput(id, output));
        Controller.OnProviderEvent(thread, new ProviderEvent.ToolUpdated(id, new ToolUpdate { Status = ToolCall.Status.Completed }));
    }

    /// <summary>A command approval of the chat, as Claude asks it.</summary>
    public bool Ask(Guid thread, string id, string command) => Controller.OnApproval(thread, MissionRig.Approval(id, command));

    /// <summary>Whether the chat's approval was answered, and how; null while it waits.</summary>
    public bool? Answer(Guid thread, string id) =>
        Agents.Resolutions.Where(r => r.Thread == thread && r.Request == id).Select(r => (bool?)r.Approved).LastOrDefault();

    public static AssistantChat AsChat(ChatThread chat) =>
        new(chat.Id, "dextra", chat.MissionID, chat.MissionID is null ? AssistantChatRole.Home : AssistantChatRole.Mission, ProviderKind.Claude, "model-a");

    public void Dispose()
    {
        Controller.Dispose();
        temp.Dispose();
    }
}
