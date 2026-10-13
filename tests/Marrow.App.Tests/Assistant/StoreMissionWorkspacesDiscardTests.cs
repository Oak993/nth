using System.Diagnostics;
using Marrow.App.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: final security gate S4 (ED-NEW-secfinal-22; DESIGN.md rev 5, 12.6a; the lean rules, item 10). Revert, Restore, FindReverted and
// DropReverted act only on a workspace strictly under the store's worktrees folder, fully resolved, with no link on the way; Discard did not,
// and it is what the owner's Discard, the 7-day sweep, the earlier-attempts sweep and a dry run call. A mission record whose workspace path was
// changed (any process of the same user can edit the journal) lost an unrelated folder, seven days later and unattended. Discard now refuses
// such a path the way the others do, and nothing is deleted.
[Trait("Decision", "ED-NEW-secfinal-22")]
public sealed class StoreMissionWorkspacesDiscardTests
{
    private static readonly Guid Mission = Guid.Parse("5c1f0e7a-3b42-4d19-9a6e-7f2b8d4c1e03");

    private static string Base()
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
        return Path.Combine(data, "DiscardGuard", Guid.NewGuid().ToString("N"));
    }

    // The app's folder rule: worktrees\<mission>, and <mission>-2, -3 for a Restart's fresh folder.
    private static StoreMissionWorkspaces Workspaces(string store) =>
        new((mission, attempt) => Path.Combine(store, "worktrees", mission.ToString("N") + (attempt > 1 ? "-" + attempt : "")));

    // The mission's project folder as its record holds it; these cases are decided by the worktrees-folder rule or concern a copy, which has no repository.
    private const string Owner = @"C:\owner";

    private static MissionWorkspace Copy(string path) => new(MissionWorkspaceKind.Snapshot, @"C:\owner", path);

    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "mklink did not finish");
        Assert.True(process.ExitCode == 0, "mklink /J failed: " + output);
    }

    private static void Clean(string folder, params string[] links)
    {
        foreach (var link in links)
        {
            try { if (Directory.Exists(link)) Directory.Delete(link, recursive: false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task A_record_whose_path_was_changed_to_a_folder_outside_the_worktrees_folder_loses_nothing()
    {
        var store = Base();
        try
        {
            var documents = Path.Combine(store, "Documents");
            Directory.CreateDirectory(Path.Combine(documents, "Taxes"));
            File.WriteAllText(Path.Combine(documents, "Taxes", "2026.txt"), "important");
            Directory.CreateDirectory(Path.Combine(store, "worktrees"));

            foreach (var tampered in new[] { documents, Path.Combine(store, "worktrees", "..", "Documents"), Path.Combine(store, "worktrees"), store })
            {
                await Assert.ThrowsAsync<MissionWorkspaceError>(() => Workspaces(store).Discard(Copy(tampered), Owner));
                // A worktree record with the same path is refused before git or any delete.
                await Assert.ThrowsAsync<MissionWorkspaceError>(() => Workspaces(store).Discard(new MissionWorkspace(MissionWorkspaceKind.Worktree, @"C:\owner", tampered) { Repository = store, Branch = "nth/mission-x" }, Owner));
            }

            Assert.Equal("important", File.ReadAllText(Path.Combine(documents, "Taxes", "2026.txt")));
            Assert.True(Directory.Exists(Path.Combine(store, "worktrees")));
        }
        finally { Clean(store); }
    }

    [Fact]
    public async Task A_workspace_folder_under_the_worktrees_folder_is_still_discarded()
    {
        var store = Base();
        try
        {
            var folder = Path.Combine(store, "worktrees", Mission.ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "src"));
            File.WriteAllText(Path.Combine(folder, "src", "a.txt"), "mission copy");
            var second = Path.Combine(store, "worktrees", Mission.ToString("N") + "-2");
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(second, "b.txt"), "second attempt");

            await Workspaces(store).Discard(Copy(folder), Owner);
            await Workspaces(store).Discard(Copy(second), Owner);

            Assert.False(Directory.Exists(folder));
            Assert.False(Directory.Exists(second));
            Assert.True(Directory.Exists(Path.Combine(store, "worktrees")));
        }
        finally { Clean(store); }
    }

    [Fact]
    public async Task A_workspace_path_that_is_a_junction_out_of_the_worktrees_folder_is_refused_and_its_target_kept()
    {
        var store = Base();
        var link = Path.Combine(store, "worktrees", Mission.ToString("N"));
        try
        {
            var documents = Path.Combine(store, "Documents");
            Directory.CreateDirectory(documents);
            Directory.CreateDirectory(Path.Combine(store, "worktrees"));
            File.WriteAllText(Path.Combine(documents, "keep.txt"), "important");
            Junction(link, documents);

            await Assert.ThrowsAsync<MissionWorkspaceError>(() => Workspaces(store).Discard(Copy(link), Owner));

            Assert.Equal("important", File.ReadAllText(Path.Combine(documents, "keep.txt")));
            Assert.True(Directory.Exists(link));
        }
        finally { Clean(store, link); }
    }

    [Fact]
    public async Task A_junction_inside_a_discarded_workspace_is_not_followed_out_of_it()
    {
        var store = Base();
        var folder = Path.Combine(store, "worktrees", Mission.ToString("N"));
        var link = Path.Combine(folder, "linked");
        try
        {
            var documents = Path.Combine(store, "Documents");
            Directory.CreateDirectory(documents);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(documents, "keep.txt"), "important");
            File.WriteAllText(Path.Combine(folder, "work.txt"), "mission copy");
            Junction(link, documents);

            // A link inside the folder is not a link in the way down to it, so the folder is discarded; the link goes as a link.
            await Workspaces(store).Discard(Copy(folder), Owner);

            Assert.False(Directory.Exists(folder));
            Assert.Equal("important", File.ReadAllText(Path.Combine(documents, "keep.txt")));
        }
        finally { Clean(store, link); }
    }
}
