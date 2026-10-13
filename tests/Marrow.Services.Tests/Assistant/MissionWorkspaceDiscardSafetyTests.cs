using System.Diagnostics;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: final security gate S4 (ED-NEW-secfinal-22; DESIGN.md rev 5, 12.6a "Discard ... never touches the owner's tree"). Discard ran
// "git worktree remove --force <path>" and "branch -D", then always deleted the folder recursively; git's failure was not looked at, so a
// workspace record whose path had been changed (a folder that was never a worktree) lost that folder. Now the folder is deleted only for a
// worktree that git removed; a link in the folder is removed as a link and never followed. Real git, in throwaway folders under MARROW_PORT_DATA_DIR.
[Trait("Decision", "ED-NEW-secfinal-22")]
public sealed class MissionWorkspaceDiscardSafetyTests
{
    private static readonly Guid Id = Guid.Parse("0f3c9a2e-7b1d-4c55-8e6a-2d4f6b8a9c0d");

    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "mklink did not finish");
        Assert.True(process.ExitCode == 0, "mklink /J failed: " + output);
        Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), "the link is not a reparse point");
    }

    [Fact]
    public async Task A_folder_that_git_could_not_remove_as_a_worktree_is_left_where_it_is()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await MissionWorkspaces.Create(Id, "Discard", repository.Root, store.Child("worktrees", "d1"));
        var folder = store.Child("not-a-worktree");
        Directory.CreateDirectory(Path.Combine(folder, "deeper"));
        File.WriteAllText(Path.Combine(folder, "precious.txt"), "keep me");
        File.WriteAllText(Path.Combine(folder, "deeper", "also.txt"), "keep me too");

        // A record whose path is another folder of the same disk: git says it is not a working tree.
        await MissionWorkspaces.Discard(workspace with { Path = folder });

        Assert.Equal("keep me", File.ReadAllText(Path.Combine(folder, "precious.txt")));
        Assert.Equal("keep me too", File.ReadAllText(Path.Combine(folder, "deeper", "also.txt")));
        Assert.True(Directory.Exists(workspace.Path), "the real worktree was not the record's path, so it stays too");
    }

    [Fact]
    public async Task A_real_worktree_is_removed_with_its_branch_and_a_junction_in_it_is_not_followed()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await MissionWorkspaces.Create(Id, "Discard", repository.Root, store.Child("worktrees", "d2"));
        var outside = store.Child("outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "private.txt"), "not the mission's");
        File.WriteAllText(Path.Combine(workspace.Path, "work.txt"), "mission work");
        Junction(Path.Combine(workspace.Path, "linked"), outside);

        await MissionWorkspaces.Discard(workspace);

        Assert.False(Directory.Exists(workspace.Path));
        Assert.Equal("", repository.Git("branch", "--list", "nth/*").Trim());
        Assert.Equal("not the mission's", File.ReadAllText(Path.Combine(outside, "private.txt")));
    }

    [Fact]
    public async Task A_copy_that_holds_a_junction_is_deleted_without_following_it()
    {
        using var runtime = new TestRuntime();
        using var store = new ScratchFolder();
        var outside = store.Child("outside");
        var copy = store.Child("worktrees", "s1");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(copy);
        File.WriteAllText(Path.Combine(outside, "private.txt"), "not the mission's");
        File.WriteAllText(Path.Combine(copy, "work.txt"), "copy");
        Junction(Path.Combine(copy, "linked"), outside);

        await MissionWorkspaces.Discard(new MissionWorkspace(MissionWorkspaceKind.Snapshot, store.Child("owner"), copy));

        Assert.False(Directory.Exists(copy));
        Assert.Equal("not the mission's", File.ReadAllText(Path.Combine(outside, "private.txt")));
    }

    [Fact]
    public async Task A_workspace_path_that_is_itself_a_junction_loses_only_the_link()
    {
        using var runtime = new TestRuntime();
        using var store = new ScratchFolder();
        var outside = store.Child("outside");
        var link = store.Child("worktrees", "s2");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(store.Child("worktrees"));
        File.WriteAllText(Path.Combine(outside, "private.txt"), "not the mission's");
        Junction(link, outside);

        await MissionWorkspaces.Discard(new MissionWorkspace(MissionWorkspaceKind.Snapshot, store.Child("owner"), link));

        Assert.False(Directory.Exists(link));
        Assert.Equal("not the mission's", File.ReadAllText(Path.Combine(outside, "private.txt")));
    }
}
