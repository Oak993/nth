using System.Text;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 12.6a, 12.6b "Revert changes" and "Resume or Restart a stopped task"; ED-NEW-dextra-as6-6): Revert
// takes the stopped task's changes out of its worktree and keeps the branch tip as a hidden ref the Activity log can restore for
// 7 days; the owner's own tree is never touched; a copied folder is set aside beside itself the same way; Restore puts the work
// back; Restart gets a fresh branch beside the failed one. Real git, in throwaway folders under MARROW_PORT_DATA_DIR.
[Trait("Decision", "ED-NEW-dextra-as6-6")]
[Trait("Feature", "AS-6")]
public sealed class MissionWorkspaceRevertTests
{
    private static readonly Guid Id = Guid.Parse("0f3c9a2e-7b1d-4c55-8e6a-2d4f6b8a9c01");
    private static readonly DateTimeOffset At = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

    private static string Read(string path) => File.ReadAllText(path, Encoding.UTF8);

    private static string OwnerState(GitRepository repository)
    {
        var state = new StringBuilder();
        state.Append(repository.Git("rev-parse", "HEAD")).Append(repository.Git("status", "--porcelain=v1", "--untracked-files=all"));
        foreach (var file in Directory.EnumerateFiles(repository.Root, "*", SearchOption.AllDirectories).Where(f => !f.Contains("\\.git", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            state.Append(file).Append('=').Append(Read(file)).Append('\n');
        return state.ToString();
    }

    private static async Task<MissionWorkspace> WorkedIn(GitRepository repository, ScratchFolder store, string name = "m1", int attempt = 1)
    {
        var workspace = await MissionWorkspaces.Create(Id, "Fix the login page", repository.Root, store.Child("worktrees", name), attempt: attempt);
        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "a\nthe mission's edit\n");
        File.WriteAllText(Path.Combine(workspace.Path, "c.txt"), "mission file\n");
        return workspace;
    }

    [Fact]
    public async Task Revert_keeps_the_work_as_a_hidden_ref_removes_the_worktree_and_branch_and_leaves_the_owners_tree_exactly_as_it_was()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        repository.Write("a.txt", "a\nthe owner's uncommitted edit\n");
        repository.Write("untracked.txt", "his\n");
        var before = OwnerState(repository);
        var workspace = await WorkedIn(repository, store);

        var reverted = await MissionWorkspaces.Revert(workspace, Id, At, changedFiles: 2);

        // The work is gone from the workspace: no worktree, no branch.
        Assert.False(Directory.Exists(workspace.Path));
        Assert.Equal("", repository.Git("branch", "--list", "nth/*").Trim());
        Assert.Single(repository.Git("worktree", "list", "--porcelain").Split('\n'), line => line.StartsWith("worktree ", StringComparison.Ordinal));
        // It is kept as a hidden ref (never under branches or tags), holding the mission's files.
        Assert.StartsWith(MissionWorkspaces.RevertedRefRoot + Id.ToString("N") + "/20261012T093000000Z", reverted.Ref, StringComparison.Ordinal);
        Assert.Equal(reverted.Commit, repository.Git("rev-parse", reverted.Ref).Trim());
        Assert.Equal("mission file\n", repository.Git("show", reverted.Ref + ":c.txt"));
        Assert.Equal("a\nthe mission's edit\n", repository.Git("show", reverted.Ref + ":a.txt"));
        Assert.Equal("", repository.Git("branch", "-a", "--list", "*reverted*").Trim());
        Assert.Equal("", repository.Git("tag", "--list").Trim());
        Assert.Equal(2, reverted.ChangedFiles);
        Assert.Equal(At.AddDays(7), reverted.Expires);
        // Nothing of the owner's moved.
        Assert.Equal(before, OwnerState(repository));
    }

    [Fact]
    public async Task Restore_puts_the_worktree_back_on_its_branch_at_the_saved_commit_and_a_second_restore_finds_nothing()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await WorkedIn(repository, store);
        var reverted = await MissionWorkspaces.Revert(workspace, Id, At, 2);
        var before = OwnerState(repository);

        var back = await MissionWorkspaces.Restore(reverted);

        Assert.Equal(workspace.Path, back.Path);
        Assert.Equal(workspace.Branch, back.Branch);
        Assert.NotNull(back.GitDir);
        Assert.Equal("mission file\n", Read(Path.Combine(back.Path, "c.txt")));
        Assert.Equal("a\nthe mission's edit\n", Read(Path.Combine(back.Path, "a.txt")));
        Assert.Equal(reverted.Commit, repository.Git("rev-parse", workspace.Branch!).Trim());
        // The ref goes once the branch holds the work; the restored workspace reviews like any other.
        Assert.Equal("", repository.Git("for-each-ref", MissionWorkspaces.RevertedRefRoot).Trim());
        var review = await MissionWorkspaces.Review(back);
        Assert.Equal(["a.txt", "c.txt"], review.Changes.Select(c => c.Path).Order(StringComparer.Ordinal));
        Assert.Equal(before, OwnerState(repository));
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Restore(reverted));
    }

    [Fact]
    public async Task Restore_is_refused_when_the_branch_or_the_place_is_taken_again_and_nothing_is_lost()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await WorkedIn(repository, store);
        var reverted = await MissionWorkspaces.Revert(workspace, Id, At, 2);

        repository.Git("branch", workspace.Branch!);
        var branchTaken = await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Restore(reverted));
        Assert.Contains("exists again", branchTaken.Message, StringComparison.Ordinal);
        repository.Git("branch", "-D", workspace.Branch!);

        Directory.CreateDirectory(workspace.Path);
        var placeTaken = await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Restore(reverted));
        Assert.Contains("place is taken", placeTaken.Message, StringComparison.Ordinal);
        Directory.Delete(workspace.Path);

        // Still restorable after both refusals.
        Assert.Equal(reverted.Commit, repository.Git("rev-parse", reverted.Ref).Trim());
        var back = await MissionWorkspaces.Restore(reverted);
        Assert.Equal("mission file\n", Read(Path.Combine(back.Path, "c.txt")));
    }

    [Fact]
    public async Task Reverted_work_is_due_after_seven_days_and_dropping_it_removes_its_ref_for_good()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await WorkedIn(repository, store);
        var reverted = await MissionWorkspaces.Revert(workspace, Id, At, 2);

        Assert.False(MissionWorkspaces.RevertedDue(reverted, At.AddDays(6.9)));
        Assert.True(MissionWorkspaces.Restorable(reverted, At.AddDays(6.9)));
        Assert.True(MissionWorkspaces.RevertedDue(reverted, At.AddDays(7)));
        Assert.False(MissionWorkspaces.Restorable(reverted, At.AddDays(7)));

        await MissionWorkspaces.DropReverted(reverted);
        Assert.Equal("", repository.Git("for-each-ref", MissionWorkspaces.RevertedRefRoot).Trim());
        await MissionWorkspaces.DropReverted(reverted); // safe to repeat
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Restore(reverted));
    }

    [Fact]
    public async Task A_revert_nth_ended_in_is_found_by_its_ref_and_finished_and_one_that_did_nothing_is_not()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await WorkedIn(repository, store);
        Assert.Null(await MissionWorkspaces.FindReverted(workspace, Id)); // nothing was done: the workspace is as it was
        Assert.True(Directory.Exists(workspace.Path));

        // nth ended after the work was committed and its ref made, before the worktree went.
        var review = await MissionWorkspaces.Review(workspace);
        var kept = MissionWorkspaces.RevertedRefRoot + Id.ToString("N") + "/20261012T093000000Z";
        repository.Git("update-ref", kept, review.Commit!);

        var found = await MissionWorkspaces.FindReverted(workspace, Id);
        Assert.NotNull(found);
        Assert.Equal((kept, review.Commit, At), (found!.Ref, found.Commit, found.At));
        Assert.False(Directory.Exists(workspace.Path)); // finished off
        Assert.Equal("", repository.Git("branch", "--list", "nth/*").Trim());
        var back = await MissionWorkspaces.Restore(found);
        Assert.Equal("mission file\n", Read(Path.Combine(back.Path, "c.txt")));
        // A ref of another mission is not this one's.
        repository.Git("update-ref", MissionWorkspaces.RevertedRefRoot + Guid.NewGuid().ToString("N") + "/20261012T093000000Z", review.Commit!);
        await MissionWorkspaces.Discard(back);
        Assert.Null(await MissionWorkspaces.FindReverted(back, Id));
    }

    [Fact]
    public async Task A_copy_set_aside_before_nth_ended_is_found_beside_its_place()
    {
        using var runtime = new TestRuntime();
        using var source = GitRepository.NotARepository();
        using var store = new ScratchFolder();
        Directory.CreateDirectory(source.Child("owner"));
        File.WriteAllText(source.Child("owner", "a.txt"), "a\n");
        var workspace = await MissionWorkspaces.Create(Id, "Copy work", source.Child("owner"), store.Child("worktrees", "s1"));
        Assert.Null(await MissionWorkspaces.FindReverted(workspace, Id));
        var reverted = await MissionWorkspaces.Revert(workspace, Id, At, 1);

        var found = await MissionWorkspaces.FindReverted(workspace, Id);
        Assert.Equal((reverted.Ref, At), (found!.Ref, found.At));
        // The place is taken again (a restart made a fresh copy): the old set-aside copy is no revert of this workspace.
        Directory.CreateDirectory(workspace.Path);
        Assert.Null(await MissionWorkspaces.FindReverted(workspace, Id));
    }

    [Fact]
    public async Task A_copied_folder_is_set_aside_beside_itself_restored_and_dropped()
    {
        using var runtime = new TestRuntime();
        using var source = GitRepository.NotARepository();
        using var store = new ScratchFolder();
        Directory.CreateDirectory(source.Child("owner"));
        File.WriteAllText(source.Child("owner", "a.txt"), "a\n");
        var workspace = await MissionWorkspaces.Create(Id, "Copy work", source.Child("owner"), store.Child("worktrees", "s1"));
        Assert.Equal(MissionWorkspaceKind.Snapshot, workspace.Kind);
        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "changed\n");

        var reverted = await MissionWorkspaces.Revert(workspace, Id, At, 1);
        Assert.False(Directory.Exists(workspace.Path));
        Assert.True(Directory.Exists(reverted.Ref));
        Assert.StartsWith(workspace.Path + ".reverted-", reverted.Ref, StringComparison.Ordinal);
        Assert.Equal("changed\n", Read(Path.Combine(reverted.Ref, "a.txt")));
        Assert.Equal("a\n", Read(source.Child("owner", "a.txt")));

        var back = await MissionWorkspaces.Restore(reverted);
        Assert.Equal("changed\n", Read(Path.Combine(back.Path, "a.txt")));
        Assert.False(Directory.Exists(reverted.Ref));

        var again = await MissionWorkspaces.Revert(back, Id, At.AddMinutes(1), 1);
        await MissionWorkspaces.DropReverted(again);
        Assert.False(Directory.Exists(again.Ref));
        Assert.Equal("a\n", Read(source.Child("owner", "a.txt")));
    }

    [Fact]
    public async Task A_restart_gets_a_fresh_branch_and_folder_beside_the_failed_one()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        Assert.Equal("nth/mission-0f3c9a2e-fix-the-login-page", MissionWorkspaces.BranchName(Id, "Fix the login page"));
        Assert.Equal("nth/mission-0f3c9a2e-fix-the-login-page", MissionWorkspaces.BranchName(Id, "Fix the login page", 1));
        Assert.Equal("nth/mission-0f3c9a2e-fix-the-login-page-r2", MissionWorkspaces.BranchName(Id, "Fix the login page", 2));
        var failed = await WorkedIn(repository, store);
        var fresh = await MissionWorkspaces.Create(Id, "Fix the login page", repository.Root, store.Child("worktrees", "m1-2"), attempt: 2);

        Assert.Equal("nth/mission-0f3c9a2e-fix-the-login-page-r2", fresh.Branch);
        Assert.NotEqual(failed.Path, fresh.Path);
        // The failed branch is still there for review, with its files; the fresh one starts from the owner's branch.
        Assert.Equal(2, repository.Git("branch", "--list", "nth/*").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal("mission file\n", Read(Path.Combine(failed.Path, "c.txt")));
        Assert.False(File.Exists(Path.Combine(fresh.Path, "c.txt")));
        await MissionWorkspaces.Discard(failed);
        await MissionWorkspaces.Discard(fresh);
    }

    [Fact]
    public void A_path_is_acted_on_only_strictly_under_the_root_fully_resolved_and_with_no_link_on_the_way()
    {
        using var store = new ScratchFolder();
        var root = store.Child("worktrees");
        Directory.CreateDirectory(Path.Combine(root, "m1"));
        Assert.True(MissionWorkspaces.StrictlyUnder(root, Path.Combine(root, "m1")));
        Assert.True(MissionWorkspaces.StrictlyUnder(root, Path.Combine(root, "m1", "deeper", "x")));
        Assert.True(MissionWorkspaces.StrictlyUnder(root, Path.Combine(root, "M1").ToUpperInvariant())); // case does not matter on Windows
        Assert.False(MissionWorkspaces.StrictlyUnder(root, root));
        Assert.False(MissionWorkspaces.StrictlyUnder(root, root + "2\\m1")); // a sibling that only starts like it
        Assert.False(MissionWorkspaces.StrictlyUnder(root, Path.Combine(root, "m1", "..", "..", "elsewhere")));
        Assert.False(MissionWorkspaces.StrictlyUnder(root, store.Child("elsewhere")));
        Assert.False(MissionWorkspaces.StrictlyUnder(root, @"\\?\" + Path.Combine(root, "m1")));
        Assert.False(MissionWorkspaces.StrictlyUnder(root, ""));
        // A link inside the root that leads out of it is not walked into. Creating one needs the symbolic-link privilege (Developer
        // Mode or an elevated run), so without it only the plain-path rules above are exercised here.
        var outside = store.Child("outside");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(root, "link");
        try { Directory.CreateSymbolicLink(link, outside); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException) { return; }
        Assert.False(MissionWorkspaces.StrictlyUnder(root, link));
        Assert.False(MissionWorkspaces.StrictlyUnder(root, Path.Combine(link, "m1")));
    }
}
