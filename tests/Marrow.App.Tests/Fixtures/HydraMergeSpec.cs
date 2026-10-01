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
        var changed = change(thread);
        f.Threads[f.Threads.FindIndex(candidate => candidate.Id == thread.Id)] = changed;
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
    public static Project AddRepositoryProject(HydraMergeProofFixture f, string name, string folder)
    {
        Directory.CreateDirectory(folder);
        var repo = AppModelGitRepo.WithCommit(folder);
        var project = AppModelSamples.Project(name, repo.Root);
        f.Projects.Add(project);
        f.Repos[project.Id] = repo;
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
}
