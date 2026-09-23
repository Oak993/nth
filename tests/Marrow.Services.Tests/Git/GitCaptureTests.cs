using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using GitValue = Marrow.Services.Git.Git;

namespace Marrow.Services.Tests.Git;

// Part 3 of Services.Git.Git: the watch-backed captures (Git.swift:413-468) with the OD-0008 A3 ruling, which says a
// capture that cannot prove it covered the whole tree throws instead of returning a tree. The two routes BLK-0003
// names are exercised directly: a teardown of the watch (WorkingTreeWatch.swift:159-170, :271) and a changed-path list
// that misses a changed directory (the early release of WorkingTreeWatch.swift:191-192). The scratch capture of
// Git.swift:445-463 is private in the Swift; the port makes it internal so these tests reach it (R-NAME-6).
// Expectations are the system git's own trees, built through an index of the test's own (GitProbes.IndexTree), in real
// folders under MARROW_PORT_DATA_DIR (R-TEST-9, R-TEST-10). The tests share the process-wide watch registry, so they
// run in their own non-parallel collection (Fixtures/GitCaptureCollection.cs).
[Collection(GitCaptureCollection.Name)]
public sealed class GitCaptureTests
{
    // ---- captureCheckpoint's commit memo (Git.swift:416-429) ----

    [Fact]
    [Trait("Req", "PR-GIT-059")] // Services/Git/Git.swift:419-427; Services/Git/WorkingTreeWatch.swift:339-352
    public async Task A_checkpoint_of_an_unchanged_tree_takes_the_commit_the_watch_remembers_and_runs_only_update_ref()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("a.txt", "changed\n");
        var git = new GitValue(repository.Root);
        var first = GitValue.CheckpointRef(thread: Guid.NewGuid(), turn: 0, phase: "start");
        await git.CaptureCheckpoint(first);
        var commit = repository.Git("rev-parse", first).Trim();
        var tree = WorkingTree(repository);
        var watch = WorkingTreeWatch.Shared(repository.Root, repository.Child(".git"));
        Assert.NotNull(watch);
        // :426 the commit just made is remembered against the tree it holds.
        Assert.Equal(commit, watch.CommitOf(tree));
        // :420-421 a remembered commit is taken as it is: HEAD's commit (which no commit-tree of this tree could make,
        // having a parent and the test identity) stands in, so only the reuse can put it on the next ref.
        var head = repository.Git("rev-parse", "HEAD").Trim();
        watch.Remember(head, tree);
        var second = GitValue.CheckpointRef(thread: Guid.NewGuid(), turn: 1, phase: "end");
        await git.CaptureCheckpoint(second);
        Assert.Equal(head, repository.Git("rev-parse", second).Trim());
    }

    [Fact]
    [Trait("Req", "PR-GIT-059")] // Services/Git/WorkingTreeWatch.swift:339-352
    public async Task Remember_for_a_tree_the_last_snapshot_does_not_hold_is_dropped_and_the_next_checkpoint_keeps_its_own_commit()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("a.txt", "changed\n");
        var git = new GitValue(repository.Root);
        var first = GitValue.CheckpointRef(thread: Guid.NewGuid(), turn: 0, phase: "start");
        await git.CaptureCheckpoint(first);
        var commit = repository.Git("rev-parse", first).Trim();
        var tree = WorkingTree(repository);
        var watch = WorkingTreeWatch.Shared(repository.Root, repository.Child(".git"))!;
        var head = repository.Git("rev-parse", "HEAD").Trim();
        var otherTree = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        Assert.NotEqual(tree, otherTree);
        watch.Remember(head, otherTree);
        Assert.Null(watch.CommitOf(otherTree));
        Assert.Equal(commit, watch.CommitOf(tree));
        var second = GitValue.CheckpointRef(thread: Guid.NewGuid(), turn: 1, phase: "end");
        await git.CaptureCheckpoint(second);
        Assert.Equal(commit, repository.Git("rev-parse", second).Trim());
    }

    [Fact]
    [Trait("Req", "PR-GIT-059")] // Services/Git/Git.swift:418-427
    [Trait("Req", "PR-GIT-068")] // Services/Git/Git.swift:433-438; Services/Git/WorkingTreeWatch.swift:290-294
    public async Task A_project_below_its_repository_root_has_a_watch_without_a_stream_captures_fresh_and_remembers_nothing()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("sub/x.txt", "x\n");
        repository.CommitAll("sub", 1_700_000_100);
        repository.Write("sub/x.txt", "x2\n");
        var sub = repository.Child("sub");
        var @ref = GitValue.CheckpointRef(thread: Guid.NewGuid(), turn: 0, phase: "start");
        await new GitValue(sub).CaptureCheckpoint(@ref);
        var tree = WorkingTree(repository);
        Assert.Equal(tree, repository.Git("rev-parse", @ref + "^{tree}").Trim());
        // The watch of a folder whose git directory lies outside it has no stream (WorkingTreeWatch.swift:151-154), so
        // the capture got no scratch (:293), the private index was thrown away, and no snapshot holds the tree.
        var watch = WorkingTreeWatch.Shared(sub, repository.Child(".git"))!;
        Assert.False(watch.HasStream);
        Assert.False(File.Exists(watch.ScratchPath));
        Assert.Null(watch.CommitOf(tree));
    }

    // ---- currentTree (Git.swift:431-439) ----

    [Fact]
    [Trait("Req", "PR-GIT-068")] // Services/Git/Git.swift:433-438
    public async Task CurrentTree_at_a_checkout_root_goes_through_the_watch_whose_scratch_index_stays_between_captures()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("a.txt", "changed\n");
        var git = new GitValue(repository.Root);
        var tree = await git.CurrentTree();
        Assert.Equal(WorkingTree(repository), tree);
        var watch = WorkingTreeWatch.Shared(repository.Root, repository.Child(".git"))!;
        Assert.True(watch.HasStream);
        Assert.True(File.Exists(watch.ScratchPath));
        Assert.Equal(tree, await git.CurrentTree());
        repository.Write("dir/b.txt", "b\n");
        Assert.Equal(WorkingTree(repository), await git.CurrentTree());
        Assert.True(File.Exists(watch.ScratchPath));
    }

    // ---- the scratch capture and the A3 proof (Git.swift:441-463) ----

    [Fact]
    [Trait("Req", "PR-GIT-067")] // Services/Git/Git.swift:447-451; OD-0008 A3
    public async Task A_fast_path_whose_named_directories_hold_every_change_builds_on_the_scratch_and_returns_its_tree()
    {
        using var runtime = new TestRuntime();
        using var repository = Layout();
        var git = new GitValue(repository.Root);
        var watch = Watch(repository);
        Assert.Equal(WorkingTree(repository), await git.CaptureTree(watch.ScratchPath, null, watch));
        // An ignored file in the scratch alone: only a capture that builds on the scratch can keep it.
        ForceIntoScratch(repository, watch, "ignored.txt");
        repository.Write("sub/x.txt", "x2\n");
        repository.Write("sub/new.txt", "n\n");
        var tree = await git.CaptureTree(watch.ScratchPath, ["sub"], watch);
        var expected = GitProbes.IndexTree(repository, null, ["read-tree", "HEAD"], ["add", "-A", "--", "."], ["add", "-f", "ignored.txt"]);
        Assert.Equal(expected, tree);
        Assert.True(File.Exists(watch.ScratchPath));
    }

    [Fact]
    [Trait("Req", "PR-GIT-067")] // Services/Git/Git.swift:447-451; Services/Git/WorkingTreeWatch.swift:191-192; OD-0008 A3
    public async Task A_fast_path_whose_named_directories_miss_a_change_cannot_prove_the_whole_tree_and_throws()
    {
        using var runtime = new TestRuntime();
        using var repository = Layout();
        var git = new GitValue(repository.Root);
        var watch = Watch(repository);
        await git.CaptureTree(watch.ScratchPath, null, watch);
        repository.Write("sub/x.txt", "x2\n");
        // A change the changed-path list does not name, as an early release by a foreign cookie leaves it.
        repository.Write("other/y.txt", "y2\n");
        repository.Write("other/created.txt", "c\n");
        var error = await Assert.ThrowsAsync<CocoaError>(() => git.CaptureTree(watch.ScratchPath, ["sub"], watch));
        Assert.Equal(CocoaError.ErrorCode.FileReadUnknown, error.ErrorCodeValue);
    }

    [Fact]
    [Trait("Req", "PR-GIT-067")] // Services/Git/Git.swift:445-463; Services/Git/WorkingTreeWatch.swift:159-170,271; OD-0008 A3
    public async Task A_capture_through_a_watch_that_was_torn_down_throws_on_the_fast_path_and_on_the_rebuild()
    {
        using var runtime = new TestRuntime();
        using var repository = Layout();
        var git = new GitValue(repository.Root);
        var watch = Watch(repository);
        await git.CaptureTree(watch.ScratchPath, null, watch);
        repository.Write("sub/x.txt", "x2\n");
        watch.TearDown();
        await watch.TeardownCompleted.WaitAsync(GitWaits.Bound);
        await Assert.ThrowsAsync<CocoaError>(() => git.CaptureTree(watch.ScratchPath, ["sub"], watch));
        await Assert.ThrowsAsync<CocoaError>(() => git.CaptureTree(watch.ScratchPath, null, watch));
    }

    [Theory]
    [Trait("Req", "PR-GIT-067")] // Services/Git/Git.swift:447,453-462
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_changed_paths_the_scratch_is_deleted_and_rebuilt_from_a_copy_of_the_real_index(bool emptyList)
    {
        using var runtime = new TestRuntime();
        using var repository = Layout();
        var git = new GitValue(repository.Root);
        var watch = Watch(repository);
        await git.CaptureTree(watch.ScratchPath, null, watch);
        ForceIntoScratch(repository, watch, "ignored.txt");
        // The real index holds an ignored file the working tree walk would never add: only a copy of it keeps it.
        repository.Write("forced.txt", "f\n");
        repository.Write(".gitignore", "ignored.txt\nforced.txt\n");
        repository.Git("add", "-f", "forced.txt");
        repository.Write("sub/x.txt", "x2\n");
        var tree = await git.CaptureTree(watch.ScratchPath, emptyList ? [] : null, watch);
        var expected = GitProbes.IndexTree(repository, repository.Child(".git", "index"), ["add", "-A", "--", "."]);
        Assert.Equal(expected, tree);
        Assert.Equal("f\n", repository.Git("cat-file", "-p", tree + ":forced.txt"));
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", tree + ":ignored.txt").Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-GIT-067")] // Services/Git/Git.swift:447,453-462
    public async Task A_scratch_missing_from_disk_is_rebuilt_whole()
    {
        using var runtime = new TestRuntime();
        using var repository = Layout();
        var git = new GitValue(repository.Root);
        var watch = Watch(repository);
        await git.CaptureTree(watch.ScratchPath, null, watch);
        File.Delete(watch.ScratchPath);
        repository.Write("sub/x.txt", "x2\n");
        repository.Write("other/y.txt", "y2\n");
        Assert.Equal(WorkingTree(repository), await git.CaptureTree(watch.ScratchPath, ["sub"], watch));
        Assert.True(File.Exists(watch.ScratchPath));
    }

    [Fact]
    [Trait("Req", "PR-GIT-067")] // Services/Git/Git.swift:448,453-462
    public async Task A_pathspec_add_that_fails_rebuilds_the_scratch_whole()
    {
        using var runtime = new TestRuntime();
        using var repository = Layout();
        var git = new GitValue(repository.Root);
        var watch = Watch(repository);
        await git.CaptureTree(watch.ScratchPath, null, watch);
        ForceIntoScratch(repository, watch, "ignored.txt");
        repository.Write("other/y.txt", "y2\n");
        // "gone" names nothing in the index or on disk, so the pathspec add exits non-zero.
        var tree = await git.CaptureTree(watch.ScratchPath, ["gone"], watch);
        Assert.Equal(WorkingTree(repository), tree);
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", tree + ":ignored.txt").Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-GIT-067")] // Services/Git/Git.swift:454-458
    public async Task Without_a_real_index_the_rebuild_reads_HEAD_before_adding_the_whole_tree()
    {
        using var runtime = new TestRuntime();
        using var repository = Layout();
        // HEAD holds an ignored file: read-tree HEAD keeps it, an index started empty would not.
        repository.Write("kept.log", "k\n");
        repository.Write(".gitignore", "ignored.txt\n*.log\n");
        repository.Git("add", "-f", "kept.log", ".gitignore");
        repository.CommitAll("kept", 1_700_000_200);
        var git = new GitValue(repository.Root);
        var watch = Watch(repository);
        File.Delete(repository.Child(".git", "index"));
        File.Delete(repository.Child("a.txt"));
        repository.Write("b.txt", "b\n");
        var tree = await git.CaptureTree(watch.ScratchPath, null, watch);
        Assert.Equal(WorkingTree(repository), tree);
        Assert.Equal("k\n", repository.Git("cat-file", "-p", tree + ":kept.log"));
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", tree + ":a.txt").Succeeded);
    }

    // ---- helpers ----

    /// <summary>A checkout with a.txt, sub/x.txt, other/y.txt committed and ignored.txt present but ignored.</summary>
    private static GitRepository Layout()
    {
        var repository = GitRepository.WithCommit();
        repository.Write("sub/x.txt", "x\n");
        repository.Write("other/y.txt", "y\n");
        repository.Write(".gitignore", "ignored.txt\n");
        repository.CommitAll("layout", 1_700_000_100);
        repository.Write("ignored.txt", "i\n");
        return repository;
    }

    private static WorkingTreeWatch Watch(GitRepository repository)
    {
        var watch = WorkingTreeWatch.Shared(repository.Root, repository.Child(".git"));
        Assert.NotNull(watch);
        Assert.True(watch.HasStream);
        return watch;
    }

    /// <summary>Force-adds <paramref name="path"/> into the watch's scratch index, and nowhere else.</summary>
    private static void ForceIntoScratch(GitRepository repository, WorkingTreeWatch watch, string path) =>
        GitRepository.GitIn(repository.Root, new Dictionary<string, string> { ["GIT_INDEX_FILE"] = watch.ScratchPath }, "add", "-f", path);

    /// <summary>The working tree as git's own tree object, through a throwaway index seeded from HEAD.</summary>
    private static string WorkingTree(GitRepository repository) =>
        GitProbes.IndexTree(repository, null, ["read-tree", "HEAD"], ["add", "-A", "--", "."]);
}
