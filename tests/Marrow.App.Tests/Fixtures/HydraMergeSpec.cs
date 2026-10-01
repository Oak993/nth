using System.Globalization;
using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test helpers for the Req-tagged AppModelHydraMerge*Tests of AppModel+HydraMerge.swift. Expected strings are
// written here from the Swift source (:144-147 strayBody, :374-385 the success note), never read back from the port.
public static class HydraMergeSpec
{
    /// <summary>A folder no sidebar project holds and that is no scratch location (not under TEMP, APPDATA or a home dot-folder).</summary>
    public const string Foreign = @"C:\hydramerge-spec";

    /// <summary>The forge's answer to "create": the request every success flow merges.</summary>
    public const string RequestURL = HydraMergeReviewFixture.Url;

    /// <summary>Count paths that lie under no sidebar project, already in the order the merge sorts them.</summary>
    public static string[] Outside(int count) =>
        Enumerable.Range(1, count).Select(index => Foreign + @"\outside\f" + index.ToString("00", CultureInfo.InvariantCulture) + ".txt").ToArray();

    /// <summary>AppModel+HydraMerge.swift:144-147, written out for a sorted list of stray paths.</summary>
    public static string StrayBody(IReadOnlyList<string> paths)
    {
        var shown = string.Join(", ", paths.Take(6).Select(path => "`" + path + "`"));
        if (paths.Count > 6) shown += " and " + (paths.Count - 6) + " more";
        var lead = paths.Count == 1 ? "1 file was" : paths.Count + " files were";
        return lead + " changed outside every project: " + shown + ". Add the folder that holds them to the sidebar and ask for the merge again.";
    }

    /// <summary>The Hydra notes of a lead's timeline as (item id, "title\nbody") pairs, oldest first.</summary>
    public static (string ID, string Text)[] NoteEntries(HydraMergeProofFixture f, ChatThread lead) => f.H.Main.OnMain(() =>
        f.Runtime(lead).Entries
            .Where(entry => entry.Item.TimelineItemContent is TimelineItem.Content.User { Value.HydraHeads.Count: 0 })
            .Select(entry => (entry.Item.Id, ((TimelineItem.Content.User)entry.Item.TimelineItemContent).Value.Text))
            .ToArray());

    /// <summary>The lead's merge records.</summary>
    public static HydraMergeRecord[] Records(HydraMergeProofFixture f, ChatThread lead) =>
        f.H.Main.OnMain(() => f.Runtime(lead).HydraMerges.ToArray());

    /// <summary>Swaps a thread already added to the fixture for a changed copy (the fixture builds its library from the list).</summary>
    public static ChatThread Change(HydraMergeProofFixture f, ChatThread thread, Func<ChatThread, ChatThread> change)
    {
        var index = f.Threads.FindIndex(candidate => candidate.Id == thread.Id);
        var changed = change(f.Threads[index]); // the current copy, so that several changes of one thread add up
        f.Threads[index] = changed;
        return changed;
    }

    /// <summary>A project at a path that need not exist (its folder is only ever compared as text), added to the library.</summary>
    public static Project AddFakeProject(HydraMergeProofFixture f, string name, string path)
    {
        var project = AppModelSamples.Project(name, path);
        f.Projects.Add(project);
        return project;
    }

    /// <summary>A real git checkout at <paramref name="folder"/> (created here, so it may sit inside another project's folder), added as a project.</summary>
    public static Project AddRepositoryProject(HydraMergeProofFixture f, string name, string folder, bool origin = false)
    {
        Directory.CreateDirectory(folder);
        var repo = AppModelGitRepo.WithCommit(folder);
        var project = AppModelSamples.Project(name, repo.Root);
        f.Projects.Add(project);
        f.Repos[project.Id] = repo;
        if (origin)
        {
            var bare = f.H.NewFolder(name + "-origin.git");
            repo.Git("init", "--bare", "-b", "main", bare);
            repo.Git("remote", "add", "origin", bare);
            repo.Git("push", "-u", "origin", "main");
            f.Origins[project.Id] = bare;
        }
        return project;
    }

    /// <summary>The lead's merge-note timeline texts ("title\nbody") of one run, with the stage words the pill showed at every shell call.</summary>
    public static (string Verb, string Directory, string? Stage, HydraMergeStage? Step)[] StageTrail(List<(ShellInvocation Call, string? Stage, HydraMergeStage? Step)> trail) =>
        trail.Select(entry => (Verb(entry.Call), entry.Call.Directory ?? "", entry.Stage, entry.Step)).ToArray();

    /// <summary>The git verb of a call (the argument after the port's leading -c pairs), or the tool's file name for a forge CLI.</summary>
    public static string Verb(ShellInvocation call)
    {
        var name = Path.GetFileNameWithoutExtension(call.Executable);
        if (!name.Equals("git", StringComparison.OrdinalIgnoreCase)) return name + " " + string.Join(' ', call.Arguments.Take(2));
        return string.Join(' ', HydraMergeReviewFixture.GitArgs(call).Take(1));
    }

    /// <summary>Observes every shell call of a run together with the pill's stage words at that moment.</summary>
    public static Action<ShellInvocation> Trail(HydraMergeProofFixture f, ChatThread lead, List<(ShellInvocation Call, string? Stage, HydraMergeStage? Step)> into) =>
        call =>
        {
            var runtime = f.Runtime(lead);
            into.Add((call, runtime.HydraMergeStage, runtime.HydraMergeStep));
        };

    /// <summary>Private IsLoadingHistory switch, the way a lead that took up work again looks to the readiness test.</summary>
    public static void SetLoadingHistory(ThreadRuntime runtime, bool value) =>
        typeof(ThreadRuntime).GetProperty("IsLoadingHistory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(runtime, value);

    /// <summary>
    /// A lead working in its own copy of <paramref name="project"/>'s checkout (lead.worktreePath), on a branch of its own, with
    /// the project's origin. The copy is a clone, not a linked worktree: the offline forge stand-in keeps its script under the
    /// working directory's .git folder, and a linked worktree's .git is a file.
    /// </summary>
    public static (ChatThread Lead, string Copy) AddWorktreeLead(HydraMergeProofFixture f, Project project, string title, string[] touched,
        string branch = "wt-branch")
    {
        var clone = f.Clone(project);
        clone.Git("checkout", "-q", "-b", branch);
        var lead = Change(f, f.AddLead(project, title, touched), t => t with { WorktreePath = clone.Root });
        return (lead, clone.Root);
    }

    /// <summary>Answers the forge CLI's first call in <paramref name="checkout"/> with the request every success flow merges.</summary>
    public static Marrow.Services.Tests.Fixtures.ForgeScript Forge(string checkout, string url = RequestURL)
    {
        var script = new Marrow.Services.Tests.Fixtures.ForgeScript(checkout);
        script.Reply(0, stdout: url);
        return script;
    }

    /// <summary>An assistant message of the lead's timeline, belonging to <paramref name="turnID"/>.</summary>
    public static void AddAssistant(HydraMergeProofFixture f, ChatThread lead, Guid turnID, string text)
    {
        var document = f.Documents[lead.Id];
        var item = new TimelineItem(turnID, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text }));
        f.Documents[lead.Id] = document with { Items = new ValueList<TimelineItem>(document.Items.Append(item)) };
    }

    /// <summary>The lines of a merge note's body (everything after the title line).</summary>
    public static string[] BodyLines((string ID, string Text) note) => note.Text[(note.Text.IndexOf((char)10, StringComparison.Ordinal) + 1)..].Split((char)10);

    /// <summary>The title of a merge note (its first line).</summary>
    public static string Title((string ID, string Text) note) => note.Text[..note.Text.IndexOf((char)10, StringComparison.Ordinal)];

    /// <summary>HydraWork for the pass over <paramref name="pass"/>, every argument explicit (AppModel+HydraMerge.swift:533).</summary>
    public static object WorkAt(HydraMergeProofFixture f, ChatThread lead, Project pass, Project leadProject, string leadCheckout, bool sweep = false)
    {
        object result = null!;
        f.H.Main.Loop.Run(async () =>
        {
            var task = (Task)HydraMergeProofFixture.Call("HydraWorkFor", f.Model, lead.Id, f.Runtime(lead), pass.Path, new Git(pass.Path), pass.Id,
                leadCheckout, leadProject.Id, sweep)!;
            await task;
            result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        }, AppModelHarness.Bound);
        return result;
    }

    public static string[] PathsOf(object work) => HydraMergeProofFixture.Property<ValueList<string>>(work, "Paths").ToArray();

    public static string[] OutsideOf(object work) => HydraMergeProofFixture.Property<ValueList<string>>(work, "Outside").ToArray();

    public static Guid[] TurnIDsOf(object work) => HydraMergeProofFixture.Property<ValueList<Guid>>(work, "TurnIDs").ToArray();

    public static Guid[] HeadIDsOf(object work) => HydraMergeProofFixture.Property<ValueList<Guid>>(work, "HeadIDs").ToArray();

    /// <summary>The heads entries of a work (HydraMergeHead: name, roster index, one-line task, files among the pass's paths).</summary>
    public static (string Name, long Index, string Task, string[] Files)[] HeadsOf(object work) =>
        HydraMergeProofFixture.Property<System.Collections.IEnumerable>(work, "Heads").Cast<object>().Select(head => (
            HydraMergeProofFixture.Property<string>(head, "Name"), HydraMergeProofFixture.Property<long>(head, "Index"),
            HydraMergeProofFixture.Property<string>(head, "Task"), HydraMergeProofFixture.Property<ValueList<string>>(head, "Files").ToArray())).ToArray();

    /// <summary>Writes files (relative to <paramref name="folder"/>) and commits them, so the checkout has them tracked.</summary>
    public static void Commit(AppModelGitRepo repo, params (string Name, string Text)[] files)
    {
        foreach (var (name, text) in files)
        {
            var full = Path.Combine(repo.Root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "more files");
    }

    /// <summary>A tool call that edited <paramref name="paths"/>, in the lead's timeline under <paramref name="turnID"/> (null: under no turn).</summary>
    public static void AddTool(HydraMergeProofFixture f, ChatThread thread, Guid? turnID, params string[] paths)
    {
        var document = f.Documents[thread.Id];
        var item = new TimelineItem(turnID, new TimelineItem.Content.Tool(new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Edit, Title = "Edit", ToolCallStatus = ToolCall.Status.Completed,
            Edits = new ValueList<FileEdit>(paths.Select(path => new FileEdit(path))),
        }));
        f.Documents[thread.Id] = document with { Items = new ValueList<TimelineItem>(document.Items.Append(item)) };
    }

    /// <summary>A checkpoint of the working tree as the app takes one before a turn (Git.captureTree).</summary>
    public static string Checkpoint(string checkout) => Task.Run(() => new Git(checkout).CaptureTree()).GetAwaiter().GetResult();
}
