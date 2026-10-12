using System.Text;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 12.6a; ED-NEW-dextra-as5-4): each mission works in its own git worktree on its own
// branch, made from the owner's current branch; the merge card shows the branch's changes; Merge happens only on the
// owner's tap and a conflict leaves everything as it was; Discard removes the worktree and the branch and never touches
// the owner's tree. A folder that is no repository gets a snapshot copy, copied back all or none. Real git, in
// throwaway folders under MARROW_PORT_DATA_DIR.
public sealed class MissionWorkspaceTests
{
    private static readonly Guid Id = Guid.Parse("0f3c9a2e-7b1d-4c55-8e6a-2d4f6b8a9c01");

    private static string Read(string path) => File.ReadAllText(path, Encoding.UTF8);

    /// <summary>What the owner's checkout looks like: HEAD, the status and every file's text.</summary>
    private static string OwnerState(GitRepository repository)
    {
        var state = new StringBuilder();
        state.Append(repository.Git("rev-parse", "HEAD")).Append(repository.Git("status", "--porcelain=v1", "--untracked-files=all"));
        foreach (var file in Directory.EnumerateFiles(repository.Root, "*", SearchOption.AllDirectories).Where(f => !f.Contains("\\.git", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            state.Append(file).Append('=').Append(Read(file)).Append('\n');
        return state.ToString();
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-4")]
    public void The_branch_name_is_the_mission_id_and_a_safe_slug_of_its_title()
    {
        Assert.Equal("nth/mission-0f3c9a2e-fix-the-login-page", MissionWorkspaces.BranchName(Id, "Fix the login page!"));
        Assert.Equal("work", MissionWorkspaces.Slug("..::.."));
        Assert.Equal("caf-r-sum", MissionWorkspaces.Slug("Caf\u00e9 r\u00e9sum\u00e9"));
        Assert.Equal(32, MissionWorkspaces.Slug(new string('a', 80)).Length);
        Assert.Equal("a-b", MissionWorkspaces.Slug("a ~^: b"));
        Assert.True(MissionWorkspaces.Due(DateTimeOffset.UnixEpoch, kept: false, DateTimeOffset.UnixEpoch.AddDays(7)));
        Assert.False(MissionWorkspaces.Due(DateTimeOffset.UnixEpoch, kept: false, DateTimeOffset.UnixEpoch.AddDays(6.9)));
        Assert.False(MissionWorkspaces.Due(DateTimeOffset.UnixEpoch, kept: true, DateTimeOffset.UnixEpoch.AddDays(30)));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-4")]
    public async Task A_mission_works_in_its_own_worktree_and_its_merge_card_shows_the_branch_while_the_owners_tree_stays_put()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        repository.Write("mine.txt", "the owner's own work in progress\n");
        var before = OwnerState(repository);

        var workspace = await MissionWorkspaces.Create(Id, "Fix the login page", repository.Root, store.Child("worktrees", Id.ToString("N")));
        Assert.Equal(MissionWorkspaceKind.Worktree, workspace.Kind);
        Assert.Equal(("nth/mission-0f3c9a2e-fix-the-login-page", "main"), (workspace.Branch, workspace.BaseBranch));
        Assert.Equal(repository.Git("rev-parse", "HEAD").Trim(), workspace.BaseCommit);
        Assert.Equal(workspace.Path, workspace.WorkingFolder);
        Assert.Equal("a\n", Read(Path.Combine(workspace.Path, "a.txt")));
        Assert.False(File.Exists(Path.Combine(workspace.Path, "mine.txt")));
        Assert.Contains("nth/mission-0f3c9a2e-fix-the-login-page", repository.Git("branch", "--list", "nth/*"), StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "a\nfixed\n");
        File.WriteAllText(Path.Combine(workspace.Path, "b.txt"), "new\n");
        var review = await MissionWorkspaces.Review(workspace);
        Assert.Equal(new[] { ("a.txt", WorkspaceChangeKind.Modified), ("b.txt", WorkspaceChangeKind.Added) }, review.Changes.Select(c => (c.Path, c.Kind)));
        Assert.Equal("2 files changed, 2 insertions, 0 deletions", review.Summary);
        Assert.False(review.BaseMoved);
        Assert.Contains("+fixed", review.Patch, StringComparison.Ordinal);
        Assert.Equal(before, OwnerState(repository));

        var refused = await MissionWorkspaces.Merge(workspace);
        Assert.Equal(MergeOutcome.Merged, refused.Outcome); // untracked files of the owner do not block a merge that does not touch them
        Assert.Equal("a\nfixed\n", Read(repository.Child("a.txt")));
        Assert.Equal("new\n", Read(repository.Child("b.txt")));
        Assert.Equal("the owner's own work in progress\n", Read(repository.Child("mine.txt")));
        Assert.Equal(MergeOutcome.NothingToMerge, (await MissionWorkspaces.Merge(workspace)).Outcome);
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-4")]
    public async Task Discard_removes_the_worktree_and_the_branch_and_leaves_the_owners_tree_exactly_as_it_was()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        repository.Write("a.txt", "a\nthe owner's uncommitted edit\n");
        repository.Write("untracked.txt", "his\n");
        var before = OwnerState(repository);
        var workspace = await MissionWorkspaces.Create(Id, "Throwaway", repository.Root, store.Child("worktrees", "m1"));
        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "the mission's version\n");
        File.WriteAllText(Path.Combine(workspace.Path, "c.txt"), "mission file\n");
        await MissionWorkspaces.Review(workspace); // commits the mission's work on its own branch

        await MissionWorkspaces.Discard(workspace);
        Assert.False(Directory.Exists(workspace.Path));
        Assert.Equal("", repository.Git("branch", "--list", "nth/*").Trim());
        Assert.Single(repository.Git("worktree", "list", "--porcelain").Split('\n'), line => line.StartsWith("worktree ", StringComparison.Ordinal));
        Assert.Equal(before, OwnerState(repository));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-4")]
    public async Task A_merge_never_runs_over_the_owners_changes_and_a_conflict_leaves_everything_as_it_was()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await MissionWorkspaces.Create(Id, "Edit a", repository.Root, store.Child("worktrees", "m2"));
        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "mission\n");

        repository.Write("a.txt", "owner uncommitted\n");
        var dirty = await MissionWorkspaces.Merge(workspace);
        Assert.Equal(MergeOutcome.Refused, dirty.Outcome);
        Assert.StartsWith("Commit or stash your changes in ", dirty.Refused, StringComparison.Ordinal);
        Assert.Equal("owner uncommitted\n", Read(repository.Child("a.txt")));

        repository.CommitAll("owner moves on", 1_700_000_100);
        var before = OwnerState(repository);
        var review = await MissionWorkspaces.Review(workspace);
        Assert.True(review.BaseMoved);
        var conflict = await MissionWorkspaces.Merge(workspace);
        Assert.Equal(MergeOutcome.Conflict, conflict.Outcome);
        Assert.Equal(new[] { "a.txt" }, conflict.Conflicts);
        Assert.Equal(before, OwnerState(repository));
        Assert.False(File.Exists(repository.Child(".git", "MERGE_HEAD")));

        var mission = repository.Git("rev-parse", workspace.Branch!);
        var rebase = await MissionWorkspaces.RebaseFirst(workspace);
        Assert.Equal(MergeOutcome.Conflict, rebase.Outcome);
        Assert.Equal(mission, repository.Git("rev-parse", workspace.Branch!));
        Assert.Equal("mission\n", Read(Path.Combine(workspace.Path, "a.txt")));

        repository.Git("switch", "-q", "-c", "elsewhere");
        var elsewhere = await MissionWorkspaces.Merge(workspace);
        Assert.Equal((MergeOutcome.Refused, $"Switch {workspace.Repository} to main first; nothing was merged."), (elsewhere.Outcome, elsewhere.Refused));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-4")]
    public async Task Rebase_first_replays_the_branch_onto_the_owners_new_commits()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await MissionWorkspaces.Create(Id, "Add b", repository.Root, store.Child("worktrees", "m3"));
        File.WriteAllText(Path.Combine(workspace.Path, "b.txt"), "b\n");
        repository.Write("c.txt", "c\n");
        var ownerTip = repository.CommitAll("owner adds c", 1_700_000_200);
        Assert.True((await MissionWorkspaces.Review(workspace)).BaseMoved);
        Assert.Equal(MergeOutcome.Merged, (await MissionWorkspaces.RebaseFirst(workspace)).Outcome);
        Assert.Equal(ownerTip, repository.Git("merge-base", "main", workspace.Branch!).Trim());
        Assert.True(File.Exists(Path.Combine(workspace.Path, "c.txt")));
        Assert.Equal(new[] { "b.txt" }, (await MissionWorkspaces.Review(workspace)).Changes.Select(c => c.Path));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-4")]
    public async Task A_project_in_a_subfolder_works_in_the_same_subfolder_of_the_worktree()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        repository.Write("app/main.txt", "main\n");
        repository.CommitAll("app", 1_700_000_300);
        var workspace = await MissionWorkspaces.Create(Id, "App", repository.Child("app"), store.Child("worktrees", "m4"));
        Assert.Equal(Path.Combine(workspace.Path, "app"), workspace.WorkingFolder);
        Assert.Equal("main\n", Read(Path.Combine(workspace.WorkingFolder, "main.txt")));
        Assert.Equal(repository.Root.Replace('\\', '/'), workspace.Repository!.Replace('\\', '/'), ignoreCase: true);
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-4")]
    public async Task A_folder_that_is_no_repository_gets_a_snapshot_copied_back_all_or_none_and_discard_deletes_only_the_copy()
    {
        using var runtime = new TestRuntime();
        using var owner = new ScratchFolder();
        using var store = new ScratchFolder();
        var source = owner.Child("notes");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "keep.txt"), "keep\n");
        File.WriteAllText(Path.Combine(source, "edit.txt"), "old\n");
        File.WriteAllText(Path.Combine(source, "sub", "gone.txt"), "gone\n");

        var workspace = await MissionWorkspaces.Create(Id, "Notes", source, store.Child("worktrees", "m5"));
        Assert.Equal(MissionWorkspaceKind.Snapshot, workspace.Kind);
        Assert.Equal(new[] { "edit.txt", "keep.txt", "sub/gone.txt" }, workspace.Manifest.Select(e => e.Path));
        File.WriteAllText(Path.Combine(workspace.Path, "edit.txt"), "new\n");
        File.WriteAllText(Path.Combine(workspace.Path, "added.txt"), "added\n");
        File.Delete(Path.Combine(workspace.Path, "sub", "gone.txt"));

        var review = await MissionWorkspaces.Review(workspace);
        Assert.Equal(new[] { ("added.txt", WorkspaceChangeKind.Added), ("edit.txt", WorkspaceChangeKind.Modified), ("sub/gone.txt", WorkspaceChangeKind.Deleted) },
            review.Changes.Select(c => (c.Path, c.Kind)));
        Assert.Empty(review.Conflicts);
        Assert.Equal("old\n", Read(Path.Combine(source, "edit.txt")));

        // The owner edits a file the mission changed: nothing at all is copied back.
        File.WriteAllText(Path.Combine(source, "edit.txt"), "owner's\n");
        var conflict = await MissionWorkspaces.Merge(workspace);
        Assert.Equal(MergeOutcome.Conflict, conflict.Outcome);
        Assert.Equal(new[] { "edit.txt" }, conflict.Conflicts);
        Assert.False(File.Exists(Path.Combine(source, "added.txt")));
        Assert.True(File.Exists(Path.Combine(source, "sub", "gone.txt")));

        File.WriteAllText(Path.Combine(source, "edit.txt"), "old\n");
        File.WriteAllText(Path.Combine(source, "keep.txt"), "the owner's later edit of a file the mission left alone\n");
        Assert.Equal(MergeOutcome.Merged, (await MissionWorkspaces.Merge(workspace)).Outcome);
        Assert.Equal(("new\n", "added\n"), (Read(Path.Combine(source, "edit.txt")), Read(Path.Combine(source, "added.txt"))));
        Assert.False(File.Exists(Path.Combine(source, "sub", "gone.txt")));
        Assert.Equal("the owner's later edit of a file the mission left alone\n", Read(Path.Combine(source, "keep.txt")));
        Assert.Equal(MergeOutcome.Refused, (await MissionWorkspaces.RebaseFirst(workspace)).Outcome);

        await MissionWorkspaces.Discard(workspace);
        Assert.False(Directory.Exists(workspace.Path));
        Assert.Equal(new[] { "added.txt", "edit.txt", "keep.txt" }, Directory.GetFiles(source).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-4")]
    public async Task A_folder_too_large_to_copy_is_refused_whole_and_leaves_no_half_copy()
    {
        using var runtime = new TestRuntime();
        using var owner = new ScratchFolder();
        using var store = new ScratchFolder();
        var source = owner.Child("big");
        Directory.CreateDirectory(source);
        for (var i = 0; i < 5; i++) File.WriteAllText(Path.Combine(source, $"f{i}.txt"), new string('x', 100));
        var folder = store.Child("worktrees", "m6");
        var files = await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Create(Id, "Big", source, folder, fileCap: 4));
        Assert.StartsWith("The folder is too large to copy for a mission", files.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Create(Id, "Big", source, folder, byteCap: 450));
        Assert.False(Directory.Exists(folder));
        Assert.Equal(MissionWorkspaceKind.Snapshot, (await MissionWorkspaces.Create(Id, "Big", source, folder, byteCap: 500, fileCap: 5)).Kind);
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Create(Id, "Again", source, folder));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-4")]
    public void A_workspace_round_trips_through_the_mission_journal()
    {
        var worktree = new MissionWorkspace(MissionWorkspaceKind.Worktree, "C:/src/app", "C:/store/worktrees/m")
        {
            WorkingFolder = "C:/store/worktrees/m/app", Repository = "C:/src", Branch = "nth/mission-1-x", BaseBranch = "main", BaseCommit = "abc",
        };
        Assert.Equal(worktree, MissionWorkspaces.FromJson(MissionWorkspaces.ToJson(worktree)));
        var snapshot = new MissionWorkspace(MissionWorkspaceKind.Snapshot, "C:/notes", "C:/store/worktrees/n")
        {
            WorkingFolder = "C:/store/worktrees/n", Manifest = new([new SnapshotEntry("a.txt", "00ff", 3)]),
        };
        var back = MissionWorkspaces.FromJson(MissionWorkspaces.ToJson(snapshot))!;
        Assert.Equal(snapshot with { Manifest = back.Manifest }, back);
        Assert.Equal(snapshot.Manifest.ToArray(), back.Manifest.ToArray());
        Assert.Null(MissionWorkspaces.FromJson(new System.Text.Json.Nodes.JsonObject { ["kind"] = "other", ["source"] = "x", ["path"] = "y" }));
    }
}
