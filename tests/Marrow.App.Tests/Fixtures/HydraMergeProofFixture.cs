using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: real local git, offline forge replies, manual main actor/clock; no provider CLI can escape ED-211.
public sealed class HydraMergeProofFixture : IDisposable
{
    public AppModelHarness H { get; } = new();
    public List<Project> Projects { get; } = [];
    public List<ChatThread> Threads { get; } = [];
    public Dictionary<Guid, ThreadDocument> Documents { get; } = [];
    public Dictionary<Guid, AppModelGitRepo> Repos { get; } = [];
    public Dictionary<Guid, string> Origins { get; } = [];
    public AppModel Model { get; private set; } = null!;

    public Project AddProject(string name, bool origin = true)
    {
        var repo = AppModelGitRepo.WithCommit(H.NewFolder(name));
        var project = AppModelSamples.Project(name, repo.Root);
        Projects.Add(project);
        Repos[project.Id] = repo;
        if (origin)
        {
            var bare = H.NewFolder(name + "-origin.git");
            repo.Git("init", "--bare", "-b", "main", bare);
            repo.Git("remote", "add", "origin", bare);
            repo.Git("push", "-u", "origin", "main");
            Origins[project.Id] = bare;
        }
        return project;
    }

    public ChatThread AddLead(Project project, string title, string[] paths, Guid? id = null, TurnStatus status = TurnStatus.Completed,
        bool archived = false, bool hydra = true, DateTimeOffset? updated = null, string? checkpoint = null)
    {
        var lead = AppModelSamples.Thread(project) with { Id = id ?? Guid.NewGuid(), Title = title, HydraEnabled = hydra,
            UpdatedAt = updated ?? SwiftDate.Now, IsArchived = archived, LastStatus = status };
        Threads.Add(lead);
        var turn = new TurnRecord { Id = Guid.NewGuid(), Index = 1, Status = status, CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>(paths), BaseCheckpoint = checkpoint };
        Documents[lead.Id] = new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) };
        return lead;
    }

    public ChatThread AddHead(ChatThread parent, string[] paths, HydraHeadInfo.Status status = HydraHeadInfo.Status.Completed,
        Project? project = null, bool landed = true, long index = 1, string task = "task", string? copy = null)
    {
        var head = AppModelSamples.Thread(project ?? Projects.Single(p => p.Id == parent.ProjectID)) with
        {
            ParentThreadID = parent.Id, WorktreePath = copy, Title = "Head",
            Hydra = new HydraHeadInfo(index, task, HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with
            {
                HydraHeadInfoStatus = status,
                Landing = new HydraLanding { PatchPath = landed ? null : "pending.patch", Files = new ValueList<HydraLanding.File>(paths.Select(p =>
                    new HydraLanding.File { Path = p, Additions = 1, Deletions = 0 })) },
            },
        };
        Threads.Add(head);
        Documents[head.Id] = new ThreadDocument(head.Id);
        return head;
    }

    // Timeline.swift:4-21,151-179,202-207: tool edits are FileEdit paths in a tool item attached to a turn.
    public void Tool(ChatThread thread, params string[] paths)
    {
        var doc = Documents[thread.Id];
        var item = new TimelineItem(doc.Turns.LastOrDefault()?.Id, new TimelineItem.Content.Tool(new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Edit, Title = "Edit", ToolCallStatus = ToolCall.Status.Completed,
            Edits = new ValueList<FileEdit>(paths.Select(p => new FileEdit(p))),
        }));
        Documents[thread.Id] = doc with { Items = new ValueList<TimelineItem>(doc.Items.Append(item)) };
    }

    public void Start(params ChatThread[] live)
    {
        H.WriteLibrary(Projects, Threads);
        foreach (var doc in Documents.Values) DiskWriter.Shared.WriteSynchronously(doc, Storage.ThreadURL(doc.ThreadID));
        Model = H.MakeModel();
        H.PrepareBootstrap(Model);
        H.Main.OnMain(() => { Model.Settings.HydraEnabled = true; Model.Settings.HydraAutoMerge = true; });
        foreach (var thread in live) H.Main.Loop.Run(() => Model.Runtime(thread.Id).EnsureLoaded(), AppModelHarness.Bound);
    }

    public ThreadRuntime Runtime(ChatThread thread) => Model.ExistingRuntime(thread.Id)!;
    public void Run(ChatThread lead, Action<ShellInvocation>? observer = null) => H.Main.Loop.Run(async () =>
    {
        using var scope = ShellObserver.Observe(observer ?? (_ => { }));
        await Model.AutoMergeHydraWork(lead.Id);
    }, AppModelHarness.Bound);
    public string[] Notes(ChatThread lead) => H.Main.OnMain(() => Runtime(lead).Entries.Select(e => e.Item.TimelineItemContent)
        .OfType<TimelineItem.Content.User>().Where(u => u.Value.HydraHeads is { Count: 0 }).Select(u => u.Value.Text).ToArray());
    public string Branch(ChatThread lead) => (string)Call("HydraBranchName", null, lead)!;
    public void RemoveRuntime(ChatThread lead) => ((Dictionary<Guid, ThreadRuntime>)typeof(AppModel)
        .GetField("runtimes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Model)!).Remove(lead.Id);
    public void RemoveThread(ChatThread lead) => typeof(AppModel).GetProperty("Threads")!
        .SetValue(Model, new ValueList<ChatThread>(Model.Threads.Where(t => t.Id != lead.Id)));
    public static object? Call(string name, AppModel? model, params object?[] args) => typeof(AppModel)
        .GetMethod(name, BindingFlags.NonPublic | (model is null ? BindingFlags.Static : BindingFlags.Instance))!.Invoke(model, args);
    public object Work(ChatThread lead, Project project, bool sweep = false)
    {
        object result = null!;
        H.Main.Loop.Run(async () =>
        {
            var task = (Task)Call("HydraWorkFor", Model, lead.Id, Runtime(lead), project.Path, new Git(project.Path),
                project.Id, Projects.Single(p => p.Id == lead.ProjectID).Path, lead.ProjectID, sweep)!;
            await task;
            result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        }, AppModelHarness.Bound);
        return result;
    }
    public string Pass(ChatThread lead, Project project, bool several = false, Action<ShellInvocation>? observer = null)
    {
        var work = Work(lead, project);
        string result = "";
        H.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(observer ?? (_ => { }));
            var task = (Task)Call("MergeHydraProject", Model, lead.Id, lead, project, project.Path, work, Runtime(lead), several)!;
            await task;
            result = task.GetType().GetProperty("Result")!.GetValue(task)!.ToString()!;
        }, AppModelHarness.Bound);
        return result;
    }
    public static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
    public (Dictionary<string,string> Reserved, Dictionary<string,string> Blocking) Owned(ChatThread lead, Project project) =>
        H.Main.OnMain(() => ((Dictionary<string,string>, Dictionary<string,string>))Call("SiblingOwnedPaths", Model,
            project.Path, project.Id, lead.Id)!);
    public AppModelGitRepo Clone(Project project)
    {
        var path = H.NewFolder("remote-clone");
        Repos[project.Id].Git("clone", Origins[project.Id], path);
        var clone = AppModelGitRepo.At(path);
        clone.Git("config", "user.name", "Port Test");
        clone.Git("config", "user.email", "port-test@localhost");
        clone.Git("config", "commit.gpgsign", "false");
        clone.Git("config", "core.autocrlf", "false");
        return clone;
    }
    public ForgeScript Forge(Project project, string url = HydraMergeReviewFixture.Url)
    {
        var script = new ForgeScript(project.Path);
        script.Reply(0, stdout: url);
        return script;
    }
    public void Dispose() => H.Dispose();
}
