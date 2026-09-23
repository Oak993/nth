using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using GitValue = Marrow.Services.Git.Git;

namespace Marrow.Services.Tests.Git;

// Part 3 of Services.Git.Git: worktrees (Git.swift:121-129, :676-685), the path-scoped capture (:244-257), the fresh
// capture (:470-488), diffs (:540-573), revert preview and restore (:575-648, with the OD-0008 A4 ruling), checkpoint
// deletion (:650-656), apply (:687-716) and the GitLab merge vector (:216). Every expectation is read from the Swift
// and from the system git's own answers (R-TEST-10) in real folders under MARROW_PORT_DATA_DIR (R-TEST-9); what the
// code under test did inside git is read back through the probes of Fixtures/GitProbeFixtures.cs. Every test installs
// a TestRuntime, so Shell.run's deadlines and FileOps' retry waits are on the manual clock.
public sealed class GitThirdTests
{
    // ---- worktrees (Git.swift:121-129, :676-685) ----

    [Fact]
    [Trait("Req", "PR-GIT-106")] // Services/Git/Git.swift:121-125
    public async Task AddWorktree_creates_the_branch_at_HEAD_and_checks_it_out_at_the_path()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var head = repository.Git("rev-parse", "HEAD").Trim();
        var path = repository.Root + "-feature";
        await new GitValue(repository.Root).AddWorktree(at: path, branch: "feature", @base: null);
        Assert.Equal("feature\n", GitRepository.GitIn(path, null, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal(head + "\n", GitRepository.GitIn(path, null, "rev-parse", "HEAD"));
        Assert.Equal(head + "\n", repository.Git("rev-parse", "refs/heads/feature"));
        Assert.Equal("a\n", File.ReadAllText(path + "\\a.txt"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-106")] // Services/Git/Git.swift:122-123
    public async Task AddWorktree_with_a_base_starts_the_new_branch_at_that_commit()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("b.txt", "b\n");
        repository.CommitAll("second", 1_700_000_100);
        var path = repository.Root + "-based";
        await new GitValue(repository.Root).AddWorktree(at: path, branch: "based", @base: first);
        Assert.Equal(first + "\n", repository.Git("rev-parse", "refs/heads/based"));
        Assert.False(File.Exists(path + "\\b.txt"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-106")] // Services/Git/Git.swift:124
    public async Task AddWorktree_for_a_branch_that_exists_throws_gits_failure()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var error = await Assert.ThrowsAsync<ShellError>(() =>
            new GitValue(repository.Root).AddWorktree(at: repository.Root + "-main", branch: "main", @base: null));
        Assert.Contains("'main' already exists", error.ErrorDescription, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-GIT-106")] // Services/Git/Git.swift:124; porting/extracts/git-command-vectors.md section 0.1
    public async Task AddWorktree_has_a_120_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var marker = GitProbes.InstallStallPostCheckout(repository);
        var add = new GitValue(repository.Root).AddWorktree(at: repository.Root + "-slow", branch: "slow", @base: null);
        await GitProbes.AssertBudgetOnceStalled(runtime, add, 120, marker);
    }

    [Fact]
    [Trait("Req", "PR-GIT-107")] // Services/Git/Git.swift:127-129
    public async Task RemoveWorktree_forces_the_removal_of_a_worktree_with_uncommitted_changes()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var path = repository.Root + "-dirty";
        repository.Git("worktree", "add", "-q", "-b", "dirty", path);
        File.WriteAllText(path + "\\a.txt", "edited\n");
        File.WriteAllText(path + "\\untracked.txt", "u\n");
        await new GitValue(repository.Root).RemoveWorktree(at: path);
        Assert.False(Directory.Exists(path));
        Assert.DoesNotContain(GitProbes.ShellPath(path), repository.Git("worktree", "list", "--porcelain"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Req", "PR-GIT-107")] // Services/Git/Git.swift:128
    public async Task RemoveWorktree_of_a_folder_that_is_no_worktree_throws()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        Directory.CreateDirectory(repository.Root + "-plain");
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).RemoveWorktree(at: repository.Root + "-plain"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-108")] // Services/Git/Git.swift:676-680
    public async Task AddDetachedWorktree_checks_out_the_commit_with_no_branch()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("b.txt", "b\n");
        repository.CommitAll("second", 1_700_000_100);
        var branches = repository.Git("for-each-ref", "refs/heads");
        var path = repository.Root + "-copy";
        await new GitValue(repository.Root).AddDetachedWorktree(at: path, commit: first);
        Assert.Equal(first + "\n", GitRepository.GitIn(path, null, "rev-parse", "HEAD"));
        Assert.False(GitRepository.Raw(path, null, "symbolic-ref", "-q", "HEAD").Succeeded);
        Assert.Equal(branches, repository.Git("for-each-ref", "refs/heads"));
        Assert.False(File.Exists(path + "\\b.txt"));
        Assert.Equal("", GitRepository.GitIn(path, null, "status", "--porcelain"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-108")] // Services/Git/Git.swift:679
    public async Task AddDetachedWorktree_has_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var head = repository.Git("rev-parse", "HEAD").Trim();
        var marker = GitProbes.InstallStallPostCheckout(repository);
        var add = new GitValue(repository.Root).AddDetachedWorktree(at: repository.Root + "-slow", commit: head);
        await GitProbes.AssertBudgetOnceStalled(runtime, add, 300, marker);
    }

    [Fact]
    [Trait("Req", "PR-GIT-108")] // Services/Git/Git.swift:679
    public async Task AddDetachedWorktree_of_an_unknown_commit_throws()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() =>
            new GitValue(repository.Root).AddDetachedWorktree(at: repository.Root + "-none", commit: "0123456789abcdef0123456789abcdef01234567"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-109")] // Services/Git/Git.swift:682-685
    public async Task PruneWorktrees_forgets_a_worktree_whose_folder_is_gone()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var path = repository.Root + "-gone";
        repository.Git("worktree", "add", "-q", "--detach", path);
        Directory.Delete(path, recursive: true);
        Assert.Contains(GitProbes.ShellPath(path), repository.Git("worktree", "list", "--porcelain"), StringComparison.OrdinalIgnoreCase);
        await new GitValue(repository.Root).PruneWorktrees();
        Assert.DoesNotContain(GitProbes.ShellPath(path), repository.Git("worktree", "list", "--porcelain"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Req", "PR-GIT-109")] // Services/Git/Git.swift:684
    public async Task PruneWorktrees_outside_a_checkout_fails_silently()
    {
        using var runtime = new TestRuntime();
        using var folder = GitRepository.NotARepository();
        await new GitValue(folder.Root).PruneWorktrees();
    }

    // ---- checkpoint deletion (Git.swift:650-656) ----

    [Fact]
    [Trait("Req", "PR-GIT-062")] // Services/Git/Git.swift:650-656
    public async Task DeleteCheckpoints_removes_every_ref_under_the_threads_prefix_in_one_unconditional_transaction()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var thread = Guid.NewGuid();
        var otherThread = Guid.NewGuid();
        var mine = new[] { GitValue.CheckpointRef(thread: thread, turn: 0, phase: "start"), GitValue.CheckpointRef(thread: thread, turn: 1, phase: "end") };
        var other = GitValue.CheckpointRef(thread: otherThread, turn: 0, phase: "start");
        // Only the trailing slash of :651 keeps a ref whose folder name merely starts with the thread's id.
        var lookalike = "refs/droppy-code/checkpoints/" + SwiftString.Lowercased(SwiftUUID.UuidString(thread)) + "-x/0-start";
        foreach (var @ref in mine.Append(other).Append(lookalike)) repository.Git("update-ref", @ref, "HEAD");
        var log = GitProbes.InstallTransactionLog(repository);
        await new GitValue(repository.Root).DeleteCheckpoints(thread: thread);
        var remaining = repository.Git("for-each-ref", "--format=%(refname)", "refs/droppy-code").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { other, lookalike }.Order(StringComparer.Ordinal), remaining.Order(StringComparer.Ordinal));
        var committed = Assert.Single(GitProbes.Transactions(log), transaction => transaction.State == "committed");
        // "delete <ref>" with no old value: the hook sees a zero old value, so nothing was checked against.
        var zero = new string('0', 40);
        Assert.Equal(mine.Select(@ref => zero + " " + zero + " " + @ref).Order(StringComparer.Ordinal), committed.Lines.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-GIT-062")] // Services/Git/Git.swift:655; porting/upstream_defects.md entry 85
    public async Task A_batch_that_fails_leaves_the_refs_and_reports_nothing()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var thread = Guid.NewGuid();
        var @ref = GitValue.CheckpointRef(thread: thread, turn: 0, phase: "start");
        repository.Git("update-ref", @ref, "HEAD");
        var log = GitProbes.InstallTransactionLog(repository, refusePrepared: true);
        await new GitValue(repository.Root).DeleteCheckpoints(thread: thread);
        Assert.Equal(repository.Git("rev-parse", "HEAD"), repository.Git("rev-parse", @ref));
        Assert.Contains(GitProbes.Transactions(log), transaction => transaction.State == "prepared");
        Assert.DoesNotContain(GitProbes.Transactions(log), transaction => transaction.State == "committed");
    }

    [Fact]
    [Trait("Req", "PR-GIT-062")] // Services/Git/Git.swift:652-654
    public async Task A_thread_with_no_checkpoints_runs_no_update_ref_and_outside_a_checkout_nothing_throws()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var log = GitProbes.InstallTransactionLog(repository);
        await new GitValue(repository.Root).DeleteCheckpoints(thread: Guid.NewGuid());
        Assert.Empty(GitProbes.Transactions(log));
        using var folder = GitRepository.NotARepository();
        await new GitValue(folder.Root).DeleteCheckpoints(thread: Guid.NewGuid());
    }

    // ---- captureTree() (Git.swift:470-488) ----

    [Fact]
    [Trait("Req", "PR-GIT-065")] // Services/Git/Git.swift:470-488
    public async Task CaptureTree_is_the_whole_working_tree_over_a_copy_of_the_real_index_which_it_leaves_alone()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write(".gitignore", "*.log\n");
        repository.Write("forced.log", "f\n");
        repository.Git("add", "-f", "forced.log");
        repository.Write("a.txt", "changed\n");
        repository.Write("new.txt", "n\n");
        repository.Write("skipped.log", "s\n");
        var index = File.ReadAllBytes(repository.Child(".git", "index"));
        var tree = await new GitValue(repository.Root).CaptureTree();
        // A staged ignored file stays because the real index was the seed; the unstaged ignored one is left out.
        Assert.Equal(GitProbes.IndexTree(repository, repository.Child(".git", "index"), ["add", "-A", "--", "."]), tree);
        Assert.Equal("f\n", repository.Git("cat-file", "-p", tree + ":forced.log"));
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", tree + ":skipped.log").Succeeded);
        Assert.Equal(index, File.ReadAllBytes(repository.Child(".git", "index")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-065")] // Services/Git/Git.swift:479-483
    public async Task CaptureTree_without_a_real_index_reads_HEAD_and_records_deletions()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write(".gitignore", "*.log\n");
        repository.Write("kept.log", "k\n");
        repository.Git("add", "-f", "kept.log", ".gitignore");
        repository.CommitAll("kept", 1_700_000_100);
        File.Delete(repository.Child(".git", "index"));
        File.Delete(repository.Child("a.txt"));
        repository.Write("b.txt", "b\n");
        var tree = await new GitValue(repository.Root).CaptureTree();
        Assert.Equal(GitProbes.IndexTree(repository, null, ["read-tree", "HEAD"], ["add", "-A", "--", "."]), tree);
        Assert.Equal("k\n", repository.Git("cat-file", "-p", tree + ":kept.log"));
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", tree + ":a.txt").Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-GIT-065")] // Services/Git/Git.swift:474-476
    [Trait("Req", "PR-GIT-070")] // Services/Git/Git.swift:474-475
    public async Task CaptureTree_points_git_at_a_droppy_code_index_uuid_in_the_temporary_folder_and_removes_it()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var log = GitProbes.InstallIndexProbe(repository);
        repository.Write("new.txt", "n\n");
        await new GitValue(repository.Root).CaptureTree();
        var indexes = GitProbes.ProbedIndexFiles(log);
        Assert.NotEmpty(indexes);
        var index = Assert.Single(indexes.Distinct());
        Assert.Matches(TemporaryIndex("droppy-code-index-"), index);
        Assert.False(File.Exists(index));
    }

    [Fact]
    [Trait("Req", "PR-GIT-065")] // Services/Git/Git.swift:484
    public async Task CaptureTree_gives_its_add_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("new.txt", "n\n");
        var marker = GitProbes.InstallStallFilter(repository);
        await GitProbes.AssertBudgetOnceStalled(runtime, new GitValue(repository.Root).CaptureTree(), 300, marker);
    }

    [Fact]
    [Trait("Req", "PR-GIT-070")] // Services/Git/Git.swift:475
    [Trait("Req", "PR-GIT-065")] // Services/Git/Git.swift:484
    public async Task A_capture_whose_add_fails_throws_and_still_removes_its_index()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var log = GitProbes.InstallIndexProbe(repository);
        repository.Write("b.txt", "b\n");
        repository.Write("z.txt", "z\n");
        Task<string> capture;
        using (new FileStream(repository.Child("z.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            capture = new GitValue(repository.Root).CaptureTree();
            await Assert.ThrowsAsync<ShellError>(() => GitProbes.Drive(runtime, capture));
        }
        var index = Assert.Single(GitProbes.ProbedIndexFiles(log).Distinct());
        Assert.Matches(TemporaryIndex("droppy-code-index-"), index);
        Assert.False(File.Exists(index));
    }

    // ---- captureTree(paths:) (Git.swift:244-257) ----

    [Fact]
    [Trait("Req", "PR-GIT-066")] // Services/Git/Git.swift:244-257
    public async Task CaptureTree_of_paths_is_HEAD_with_only_those_paths_taken_from_disk_deletions_included()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("b.txt", "b\n");
        repository.Write("c/d.txt", "d\n");
        repository.CommitAll("more", 1_700_000_100);
        repository.Write("a.txt", "a2\n");
        File.Delete(repository.Child("b.txt"));
        repository.Write("n.txt", "n\n");
        repository.Write("c/d.txt", "d2\n");
        repository.Write("u.txt", "u\n");
        // Staged in the real index, but the index here starts from HEAD, never from the real one.
        repository.Git("add", "u.txt");
        var index = File.ReadAllBytes(repository.Child(".git", "index"));
        var tree = await new GitValue(repository.Root).CaptureTree(paths: ["a.txt", "b.txt", "n.txt"]);
        Assert.Equal(GitProbes.IndexTree(repository, null, ["read-tree", "HEAD"], ["add", "-A", "--", "a.txt", "b.txt", "n.txt"]), tree);
        Assert.Equal("a2\n", repository.Git("cat-file", "-p", tree + ":a.txt"));
        Assert.Equal("d\n", repository.Git("cat-file", "-p", tree + ":c/d.txt"));
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", tree + ":b.txt").Succeeded);
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", tree + ":u.txt").Succeeded);
        Assert.Equal(index, File.ReadAllBytes(repository.Child(".git", "index")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-066")] // Services/Git/Git.swift:252
    public async Task CaptureTree_of_paths_before_the_first_commit_reads_no_HEAD()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.Empty();
        repository.Write("a.txt", "a\n");
        repository.Write("b.txt", "b\n");
        var tree = await new GitValue(repository.Root).CaptureTree(paths: ["a.txt"]);
        Assert.Equal(GitProbes.IndexTree(repository, null, ["add", "-A", "--", "a.txt"]), tree);
    }

    [Fact]
    [Trait("Req", "PR-GIT-066")] // Services/Git/Git.swift:253; porting/upstream_defects.md entry 79
    public async Task CaptureTree_of_paths_adds_with_ignore_errors_so_an_unreadable_file_fails_without_the_fatal_line()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("n.txt", "n\n");
        repository.Write("m.txt", "m\n");
        using var held = new FileStream(repository.Child("n.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var capture = new GitValue(repository.Root).CaptureTree(paths: ["m.txt", "n.txt"]);
        var error = await Assert.ThrowsAsync<ShellError>(() => GitProbes.Drive(runtime, capture));
        // git add exits 1 with --ignore-errors and 128 with "fatal: adding files failed" without it.
        Assert.Contains("unable to index file 'n.txt'", error.ErrorDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("fatal: adding files failed", error.ErrorDescription, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-GIT-066")] // Services/Git/Git.swift:253
    public async Task CaptureTree_of_a_path_in_neither_HEAD_nor_the_working_tree_throws()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).CaptureTree(paths: ["missing.txt"]));
    }

    [Fact]
    [Trait("Req", "PR-GIT-066")] // Services/Git/Git.swift:249-250
    [Trait("Req", "PR-GIT-070")] // Services/Git/Git.swift:249-250
    public async Task CaptureTree_of_paths_uses_its_own_droppy_code_index_uuid_and_removes_it()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var log = GitProbes.InstallIndexProbe(repository);
        repository.Write("n.txt", "n\n");
        await new GitValue(repository.Root).CaptureTree(paths: ["n.txt"]);
        var index = Assert.Single(GitProbes.ProbedIndexFiles(log).Distinct());
        Assert.Matches(TemporaryIndex("droppy-code-index-"), index);
        Assert.False(File.Exists(index));
    }

    [Fact]
    [Trait("Req", "PR-GIT-066")] // Services/Git/Git.swift:253
    public async Task CaptureTree_of_paths_gives_its_add_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("n.txt", "n\n");
        var marker = GitProbes.InstallStallFilter(repository);
        await GitProbes.AssertBudgetOnceStalled(runtime, new GitValue(repository.Root).CaptureTree(paths: ["n.txt"]), 300, marker);
    }

    // ---- diff (Git.swift:540-573) ----

    [Fact]
    [Trait("Req", "PR-GIT-080")] // Services/Git/Git.swift:547-561
    public async Task Diff_without_paths_is_gits_whole_tree_patch_with_rename_detection()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("big.txt", string.Concat(Enumerable.Range(0, 40).Select(line => "line " + line + "\n")));
        repository.CommitAll("big", 1_700_000_100);
        var from = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        File.Move(repository.Child("big.txt"), repository.Child("moved.txt"));
        repository.Write("a.txt", "a2\n");
        var to = WorkingTree(repository);
        var patch = await new GitValue(repository.Root).Diff(from: from, to: to);
        Assert.Equal(Prefixed(repository, "diff", "--no-ext-diff", "-M", from, to), patch);
        Assert.Contains("rename from big.txt\nrename to moved.txt\n", patch, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-GIT-080")] // Services/Git/Git.swift:552,557
    public async Task Diff_with_paths_puts_literal_pathspecs_before_the_subcommand_so_a_bracket_is_a_character()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("[ab].txt", "x\n");
        repository.Write("b.txt", "b\n");
        repository.CommitAll("names", 1_700_000_100);
        var from = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        repository.Write("[ab].txt", "x2\n");
        repository.Write("a.txt", "a2\n");
        repository.Write("b.txt", "b2\n");
        var to = WorkingTree(repository);
        var patch = await new GitValue(repository.Root).Diff(from: from, to: to, paths: ["[ab].txt"]);
        Assert.Equal(Prefixed(repository, "--literal-pathspecs", "diff", "--no-ext-diff", "-M", from, to, "--", "[ab].txt"), patch);
        Assert.StartsWith("diff --git a/[ab].txt b/[ab].txt\n", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git a/a.txt", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("diff --git a/b.txt", patch, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-GIT-080")] // Services/Git/Git.swift:554
    public async Task Diff_with_binary_carries_whole_binary_blobs()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        File.WriteAllBytes(repository.Child("x.bin"), [0, 1, 2, 3, 0, 255]);
        repository.CommitAll("bin", 1_700_000_100);
        var from = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        File.WriteAllBytes(repository.Child("x.bin"), [0, 9, 8, 7, 0, 254, 1]);
        var to = WorkingTree(repository);
        var git = new GitValue(repository.Root);
        var whole = await git.Diff(from: from, to: to, binary: true);
        Assert.Equal(Prefixed(repository, "diff", "--no-ext-diff", "-M", "--binary", from, to), whole);
        Assert.Contains("GIT binary patch", whole, StringComparison.Ordinal);
        var plain = await git.Diff(from: from, to: to);
        Assert.Contains("Binary files a/x.bin and b/x.bin differ", plain, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-GIT-080")] // Services/Git/Git.swift:558-559
    public async Task Diff_of_an_unknown_ref_throws_gits_failure()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).Diff(from: "HEAD", to: "no-such-ref"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-080")] // Services/Git/Git.swift:558
    public async Task Diff_has_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var from = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        repository.Write("a.txt", "a2\n");
        var to = WorkingTree(repository);
        var marker = GitProbes.InstallStallTextconv(repository);
        await GitProbes.AssertBudgetOnceStalled(runtime, new GitValue(repository.Root).Diff(from: from, to: to), 300, marker);
    }

    [Fact]
    [Trait("Req", "PR-GIT-081")] // Services/Git/Git.swift:541,556-572; porting/port_impacts.md entry 222
    public async Task Exactly_400_paths_are_one_diff_and_401_are_two_joined_in_order()
    {
        using var runtime = new TestRuntime();
        using var repository = ManyFiles();
        var from = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        foreach (var name in Numbered(400)) repository.Write(name, name + " changed\n");
        repository.Write("a.txt", "a2\n");
        var to = WorkingTree(repository);
        var git = new GitValue(repository.Root);
        // 400 paths, a.txt last: one git diff, which prints a.txt first.
        string[] four = [.. Numbered(400).Skip(1), "a.txt"];
        var single = await git.Diff(from: from, to: to, paths: four);
        Assert.Equal(Prefixed(repository, ["--literal-pathspecs", "diff", "--no-ext-diff", "-M", from, to, "--", .. four]), single);
        Assert.StartsWith("diff --git a/a.txt b/a.txt\n", single, StringComparison.Ordinal);
        // 401 paths: the first 400, then a chunk of a.txt alone, printed after them.
        string[] fourOne = [.. Numbered(400), "a.txt"];
        var chunked = await git.Diff(from: from, to: to, paths: fourOne);
        var first = Prefixed(repository, ["--literal-pathspecs", "diff", "--no-ext-diff", "-M", from, to, "--", .. Numbered(400)]);
        var second = Prefixed(repository, "--literal-pathspecs", "diff", "--no-ext-diff", "-M", from, to, "--", "a.txt");
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.Equal(first + second, chunked);
    }

    [Fact]
    [Trait("Req", "PR-GIT-081")] // Services/Git/Git.swift:568
    public async Task A_chunk_with_no_output_adds_no_newline_to_the_joined_patch()
    {
        using var runtime = new TestRuntime();
        using var repository = ManyFiles();
        var from = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        repository.Write("a.txt", "a2\n");
        var to = WorkingTree(repository);
        string[] paths = [.. Numbered(400), "a.txt"];
        var patch = await new GitValue(repository.Root).Diff(from: from, to: to, paths: paths);
        Assert.Equal(Prefixed(repository, "--literal-pathspecs", "diff", "--no-ext-diff", "-M", from, to, "--", "a.txt"), patch);
        Assert.StartsWith("diff --git", patch, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-GIT-082")] // Services/Git/Git.swift:547-561; porting/upstream_defects.md entry 83
    public async Task An_empty_path_list_ends_the_command_with_a_bare_separator_and_diffs_the_whole_tree()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("b.txt", "b\n");
        repository.CommitAll("b", 1_700_000_100);
        var from = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        repository.Write("a.txt", "a2\n");
        repository.Write("b.txt", "b2\n");
        var to = WorkingTree(repository);
        var patch = await new GitValue(repository.Root).Diff(from: from, to: to, paths: []);
        Assert.Equal(Prefixed(repository, "--literal-pathspecs", "diff", "--no-ext-diff", "-M", from, to, "--"), patch);
        Assert.Equal(Prefixed(repository, "diff", "--no-ext-diff", "-M", from, to), patch);
        Assert.Contains("diff --git a/a.txt", patch, StringComparison.Ordinal);
        Assert.Contains("diff --git a/b.txt", patch, StringComparison.Ordinal);
    }

    // ---- previewRestore (Git.swift:575-597), blobs and workingBlobs (:623-648) ----

    [Fact]
    [Trait("Req", "PR-GIT-097")] // Services/Git/Git.swift:582
    public async Task PreviewRestore_of_no_paths_is_empty_and_asks_git_nothing()
    {
        using var runtime = new TestRuntime();
        using var folder = GitRepository.NotARepository();
        var preview = await new GitValue(folder.Root).PreviewRestore(@base: "HEAD", end: null, paths: []);
        Assert.Empty(preview.Files);
    }

    [Fact]
    [Trait("Req", "PR-GIT-097")] // Services/Git/Git.swift:581-597
    [Trait("Req", "PR-GIT-102")] // Services/Git/Git.swift:623-636
    [Trait("Req", "PR-GIT-103")] // Services/Git/Git.swift:638-648
    public async Task PreviewRestore_lists_the_named_files_that_differ_and_marks_those_changed_since_the_end_as_kept()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("b.txt", "b\n");
        repository.CommitAll("b", 1_700_000_100);
        var git = new GitValue(repository.Root);
        var @base = await git.CaptureTree();
        repository.Write("a.txt", "a2\nmore\n");
        repository.Write("n.txt", "n\n");
        repository.Write("b.txt", "b2\n");
        var end = await git.CaptureTree();
        repository.Write("a.txt", "a3\n");
        var preview = await git.PreviewRestore(@base: @base, end: end, paths: ["a.txt", "n.txt"]);
        Assert.Equal(
            new[] { new RevertPreview.File("a.txt", 2, 1, false, IsKept: true), new RevertPreview.File("n.txt", 1, 0, false, IsKept: false) },
            preview.Files);
    }

    [Fact]
    [Trait("Req", "PR-GIT-097")] // Services/Git/Git.swift:584
    public async Task PreviewRestore_without_an_end_tree_compares_with_the_named_files_as_they_are_now()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var git = new GitValue(repository.Root);
        var @base = await git.CaptureTree();
        repository.Write("a.txt", "a2\n");
        repository.Write("other.txt", "o\n");
        var preview = await git.PreviewRestore(@base: @base, end: null, paths: ["a.txt"]);
        Assert.Equal(new[] { new RevertPreview.File("a.txt", 1, 1, false, IsKept: false) }, preview.Files);
    }

    [Fact]
    [Trait("Req", "PR-GIT-097")] // Services/Git/Git.swift:586-593
    [Trait("Req", "PR-GIT-103")] // Services/Git/Git.swift:641-642
    public async Task A_file_the_end_deleted_is_kept_only_once_something_has_put_it_back_on_disk()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("d.txt", "d\n");
        repository.CommitAll("d", 1_700_000_100);
        var git = new GitValue(repository.Root);
        var @base = await git.CaptureTree();
        File.Delete(repository.Child("d.txt"));
        var end = await git.CaptureTree();
        // Missing on disk and missing from the end tree: neither side has a blob, so the two agree (no hash-object).
        var gone = await git.PreviewRestore(@base: @base, end: end, paths: ["d.txt"]);
        Assert.Equal(new[] { new RevertPreview.File("d.txt", 0, 1, false, IsKept: false) }, gone.Files);
        repository.Write("d.txt", "back\n");
        var back = await git.PreviewRestore(@base: @base, end: end, paths: ["d.txt"]);
        Assert.Equal(new[] { new RevertPreview.File("d.txt", 0, 1, false, IsKept: true) }, back.Files);
    }

    [Fact]
    [Trait("Req", "PR-GIT-097")] // Services/Git/Git.swift:586
    public async Task PreviewRestore_detects_no_renames_so_a_moved_file_is_a_deletion_and_an_addition()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var git = new GitValue(repository.Root);
        var @base = await git.CaptureTree();
        File.Move(repository.Child("a.txt"), repository.Child("r.txt"));
        var end = await git.CaptureTree();
        var preview = await git.PreviewRestore(@base: @base, end: end, paths: ["a.txt", "r.txt"]);
        Assert.Equal(
            new[] { new RevertPreview.File("a.txt", 0, 1, false, IsKept: false), new RevertPreview.File("r.txt", 1, 0, false, IsKept: false) },
            preview.Files);
    }

    [Fact]
    [Trait("Req", "PR-GIT-097")] // Services/Git/Git.swift:586
    public async Task PreviewRestore_with_a_bad_base_throws()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).PreviewRestore(@base: "no-such-ref", end: null, paths: ["a.txt"]));
    }

    [Theory]
    [Trait("Req", "PR-GIT-097")] // Services/Git/Git.swift:586-590; OD-0008 A4
    [Trait("Req", "PR-GIT-098")] // Services/Git/Git.swift:575-576,586-590,625
    [Trait("Req", "PR-GIT-102")] // Services/Git/Git.swift:625
    [InlineData(false)]
    [InlineData(true)]
    public async Task In_a_project_below_the_repository_root_the_preview_names_repository_relative_paths_whatever_diff_relative_says(bool relative)
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("x.txt", "root\n");
        repository.Write("sub/x.txt", "x\n");
        repository.CommitAll("sub", 1_700_000_100);
        if (relative) repository.Git("config", "diff.relative", "true");
        var @base = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        repository.Write("sub/x.txt", "x2\n");
        var end = WorkingTree(repository);
        // The root file of the same name changes after the end: a lookup that landed on it would read "kept".
        repository.Write("x.txt", "root2\n");
        var preview = await new GitValue(repository.Child("sub")).PreviewRestore(@base: @base, end: end, paths: ["x.txt"]);
        Assert.Equal(new[] { new RevertPreview.File("sub/x.txt", 1, 1, false, IsKept: false) }, preview.Files);
        repository.Write("sub/x.txt", "x3\n");
        var kept = await new GitValue(repository.Child("sub")).PreviewRestore(@base: @base, end: end, paths: ["x.txt"]);
        Assert.Equal(new[] { new RevertPreview.File("sub/x.txt", 1, 1, false, IsKept: true) }, kept.Files);
    }

    // ---- restore (Git.swift:599-621) ----

    [Fact]
    [Trait("Req", "PR-GIT-101")] // Services/Git/Git.swift:599-612
    [Trait("Req", "PR-GIT-158")] // Services/Git/Git.swift:613-620
    [Trait("Req", "PR-GIT-102")] // Services/Git/Git.swift:604,625-635
    public async Task Restore_puts_held_paths_back_in_the_working_tree_and_index_and_deletes_the_ones_the_ref_lacks()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("b.txt", "b\n");
        repository.CommitAll("b", 1_700_000_100);
        var git = new GitValue(repository.Root);
        var @base = await git.CaptureTree();
        repository.Write("a.txt", "changed\n");
        repository.Git("add", "a.txt");
        repository.Write("n.txt", "new\n");
        repository.Git("add", "n.txt");
        repository.Write("loose.txt", "l\n");
        repository.Write("b.txt", "b2\n");
        repository.Write("keep.txt", "k\n");
        await git.Restore(["a.txt", "n.txt", "loose.txt"], from: @base);
        Assert.Equal("a\n", File.ReadAllText(repository.Child("a.txt")));
        Assert.Equal(repository.Git("rev-parse", @base + ":a.txt").Trim(), repository.Git("rev-parse", ":a.txt").Trim());
        Assert.False(File.Exists(repository.Child("n.txt")));
        Assert.False(File.Exists(repository.Child("loose.txt")));
        Assert.Equal("", repository.Git("ls-files", "--", "n.txt"));
        // Nothing outside the paths is touched.
        Assert.Equal("b2\n", File.ReadAllText(repository.Child("b.txt")));
        Assert.Equal("k\n", File.ReadAllText(repository.Child("keep.txt")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-101")] // Services/Git/Git.swift:602
    public async Task Restore_of_no_paths_asks_git_nothing()
    {
        using var runtime = new TestRuntime();
        using var folder = GitRepository.NotARepository();
        await new GitValue(folder.Root).Restore([], from: "HEAD");
    }

    [Fact]
    [Trait("Req", "PR-GIT-101")] // Services/Git/Git.swift:607-611
    [Trait("Req", "PR-GIT-158")] // Services/Git/Git.swift:613
    public async Task A_restore_git_refuses_throws_before_any_created_file_is_deleted()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var git = new GitValue(repository.Root);
        var @base = await git.CaptureTree();
        repository.Write("a.txt", "changed\n");
        repository.Write("n.txt", "new\n");
        using (new FileStream(repository.Child("a.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var restore = git.Restore(["a.txt", "n.txt"], from: @base);
            await Assert.ThrowsAsync<ShellError>(() => GitProbes.Drive(runtime, restore));
        }
        Assert.True(File.Exists(repository.Child("n.txt")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-158")] // Services/Git/Git.swift:614-620
    public async Task A_created_file_that_cannot_be_deleted_is_left_on_disk_and_the_restore_still_succeeds()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var git = new GitValue(repository.Root);
        var @base = await git.CaptureTree();
        repository.Write("held.txt", "h\n");
        repository.Write("free.txt", "f\n");
        repository.Git("add", "held.txt", "free.txt");
        using (new FileStream(repository.Child("held.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            await GitProbes.Drive(runtime, git.Restore(["free.txt", "held.txt"], from: @base));
        Assert.True(File.Exists(repository.Child("held.txt")));
        Assert.False(File.Exists(repository.Child("free.txt")));
        // The quiet `rm --cached` still took both out of the index.
        Assert.Equal("", repository.Git("ls-files", "--", "held.txt", "free.txt"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-158")] // Services/Git/Git.swift:603,613-620; OD-0008 A4
    [Trait("Req", "PR-GIT-101")] // Services/Git/Git.swift:603-611
    public async Task In_a_project_below_the_repository_root_restore_joins_its_repository_relative_paths_onto_the_root()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("x.txt", "root\n");
        repository.Write("sub/x.txt", "x\n");
        repository.CommitAll("sub", 1_700_000_100);
        var @base = repository.Git("rev-parse", "HEAD^{tree}").Trim();
        repository.Write("x.txt", "root2\n");
        repository.Write("sub/x.txt", "x2\n");
        repository.Write("sub/n.txt", "n\n");
        repository.Write("n.txt", "root n\n");
        await new GitValue(repository.Child("sub")).Restore(["sub/x.txt", "sub/n.txt"], from: @base);
        Assert.Equal("x\n", File.ReadAllText(repository.Child("sub", "x.txt")));
        Assert.False(File.Exists(repository.Child("sub", "n.txt")));
        Assert.Equal("root2\n", File.ReadAllText(repository.Child("x.txt")));
        Assert.Equal("root n\n", File.ReadAllText(repository.Child("n.txt")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-069")] // Services/Git/Git.swift:356-361,607-609,615-617,643
    [Trait("Req", "PR-GIT-066")] // Services/Git/Git.swift:253
    public async Task Paths_with_spaces_and_non_ascii_letters_survive_all_three_stdin_encodings()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        const string held = "dir with space/\U000000E9t\U000000E9.txt"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        const string created = "dir with space/n\U000000E9e.txt"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        repository.Write(held, "one\n");
        repository.CommitAll("names", 1_700_000_100);
        var git = new GitValue(repository.Root);
        var @base = await git.CaptureTree();
        repository.Write(held, "two\n");
        repository.Write(created, "c\n");
        // nulSeparated, through captureTree(paths:).
        var end = await git.CaptureTree(paths: [held, created]);
        Assert.Equal("two\n", repository.Git("cat-file", "-p", end + ":" + held));
        Assert.Equal("c\n", repository.Git("cat-file", "-p", end + ":" + created));
        // The newline-joined hash-object payload, through workingBlobs: nothing changed since the end.
        var preview = await git.PreviewRestore(@base: @base, end: end, paths: [held, created]);
        Assert.Equal(
            new[] { new RevertPreview.File(created, 1, 0, false, IsKept: false), new RevertPreview.File(held, 1, 1, false, IsKept: false) },
            preview.Files);
        // The two NUL-joined payloads, through restore and rm --cached.
        repository.Git("add", "--", created);
        await git.Restore([held, created], from: @base);
        Assert.Equal("one\n", File.ReadAllText(repository.Child("dir with space", "\U000000E9t\U000000E9.txt")));
        Assert.False(File.Exists(repository.Child("dir with space", "n\U000000E9e.txt")));
        Assert.Equal("", repository.Git("ls-files", "--", created));
    }

    // ---- apply (Git.swift:687-716) ----

    [Fact]
    [Trait("Req", "PR-GIT-104")] // Services/Git/Git.swift:691-694
    public async Task A_patch_that_applies_as_it_is_returns_no_conflicts_stages_nothing_and_builds_no_index()
    {
        using var runtime = new TestRuntime();
        using var repository = TenLines();
        var patch = PatchChanging(repository, 8, "patched");
        var log = GitProbes.InstallIndexProbe(repository);
        var index = File.ReadAllBytes(repository.Child(".git", "index"));
        var conflicts = await new GitValue(repository.Root).Apply(patch);
        Assert.Empty(conflicts);
        Assert.Equal(Lines(("L8", "patched")), File.ReadAllText(repository.Child("f.txt")));
        Assert.Equal(index, File.ReadAllBytes(repository.Child(".git", "index")));
        Assert.DoesNotContain(GitProbes.ProbedIndexFiles(log), path => path.Contains("droppy-code-apply-", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-GIT-104")] // Services/Git/Git.swift:696-709
    [Trait("Req", "PR-GIT-070")] // Services/Git/Git.swift:699-700
    public async Task A_hunk_that_no_longer_fits_is_merged_three_way_through_a_throwaway_droppy_code_apply_index()
    {
        using var runtime = new TestRuntime();
        using var repository = TenLines();
        var patch = PatchChanging(repository, 8, "patched");
        // A change inside the hunk's context: the plain apply fails, the three-way merge settles it.
        File.WriteAllText(repository.Child("f.txt"), Lines(("L6", "mine")));
        var log = GitProbes.InstallIndexProbe(repository);
        var index = File.ReadAllBytes(repository.Child(".git", "index"));
        var conflicts = await new GitValue(repository.Root).Apply(patch);
        Assert.Empty(conflicts);
        Assert.Equal(Lines(("L6", "mine"), ("L8", "patched")), File.ReadAllText(repository.Child("f.txt")));
        Assert.Equal(index, File.ReadAllBytes(repository.Child(".git", "index")));
        var probed = GitProbes.ProbedIndexFiles(log).Where(path => path.Length > 0).Distinct().ToList();
        var throwaway = Assert.Single(probed);
        Assert.Matches(TemporaryIndex("droppy-code-apply-"), throwaway);
        Assert.False(File.Exists(throwaway));
    }

    [Fact]
    [Trait("Req", "PR-GIT-105")] // Services/Git/Git.swift:709-715
    [Trait("Req", "PR-GIT-104")] // Services/Git/Git.swift:708
    public async Task A_three_way_merge_that_cannot_settle_a_file_returns_it_and_leaves_conflict_markers()
    {
        using var runtime = new TestRuntime();
        using var repository = TenLines();
        var patch = PatchChanging(repository, 8, "patched");
        File.WriteAllText(repository.Child("f.txt"), Lines(("L8", "mine")));
        var index = File.ReadAllBytes(repository.Child(".git", "index"));
        var conflicts = await new GitValue(repository.Root).Apply(patch);
        Assert.Equal(new[] { "f.txt" }, conflicts);
        var text = File.ReadAllText(repository.Child("f.txt"));
        Assert.Contains("<<<<<<<", text, StringComparison.Ordinal);
        Assert.Contains(">>>>>>>", text, StringComparison.Ordinal);
        Assert.Equal(index, File.ReadAllBytes(repository.Child(".git", "index")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-105")] // Services/Git/Git.swift:714
    public async Task A_patch_that_cannot_be_applied_at_all_throws()
    {
        using var runtime = new TestRuntime();
        using var repository = TenLines();
        const string patch = "diff --git a/ghost.txt b/ghost.txt\nindex 1234567..89abcde 100644\n--- a/ghost.txt\n+++ b/ghost.txt\n@@ -1 +1 @@\n-old\n+new\n";
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).Apply(patch));
        Assert.False(File.Exists(repository.Child("ghost.txt")));
    }

    // ---- the GitLab merge vector (Git.swift:216) ----

    [Fact]
    [Trait("Req", "PR-GIT-159")] // Services/Git/Git.swift:216; App/AppModel+HydraMerge.swift:372-373
    public async Task The_gitlab_merge_passes_squash_fifth_on_every_attempt()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("remote", "add", "origin", "https://gitlab.com/g/p.git");
        var script = new ForgeScript(repository.Root);
        script.Reply(0, stderr: "405 Method Not Allowed\n", status: "1");
        var merge = new GitValue(repository.Root).MergePullRequest(MergeRequestLink.Create(SwiftURL.Create("https://gitlab.com/g/p/-/merge_requests/42")!)!);
        bool Sleeping() => script.Count == 1 && !ForgeStandIns.IsRunning("glab") && runtime.Time.ActiveTimers == 1;
        Assert.True(GitWaits.Eventually(() => Sleeping() && !SpinWait.SpinUntil(() => !Sleeping(), TimeSpan.FromMilliseconds(200))),
            "the first attempt never reached its retry sleep");
        runtime.Advance(TimeSpan.FromSeconds(3));
        await merge.WaitAsync(GitWaits.Bound);
        var calls = script.Calls();
        Assert.Equal(2, calls.Count);
        foreach (var call in calls)
        {
            Assert.Equal("glab.exe", call.Exe);
            Assert.Equal(new[] { "mr", "merge", "42", "--yes", "--squash", "--remove-source-branch" }, call.Arguments);
            Assert.Equal("--squash", call.Arguments[4]);
        }
    }

    // ---- helpers ----

    /// <summary>A pattern for a private index in the temporary folder: the prefix, then an uppercase UUID.</summary>
    private static Regex TemporaryIndex(string prefix) =>
        new("\\A" + Regex.Escape(System.IO.Path.GetTempPath().TrimEnd('\\') + "\\" + prefix) +
            "[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\\z", RegexOptions.CultureInvariant);

    /// <summary>What git answers with the app's two config pairs, to compare a whole patch byte for byte.</summary>
    private static string Prefixed(GitRepository repository, params string[] arguments) =>
        GitRepository.GitIn(repository.Root, null, ["-c", "core.quotepath=false", "-c", "color.ui=false", .. arguments]);

    /// <summary>The working tree as git's own tree object, through a throwaway index seeded from HEAD.</summary>
    private static string WorkingTree(GitRepository repository) =>
        GitProbes.IndexTree(repository, null, ["read-tree", "HEAD"], ["add", "-A", "--", "."]);

    /// <summary>p000.txt to p(count-1).txt, zero-padded so they sort as numbered.</summary>
    private static string[] Numbered(int count) =>
        [.. Enumerable.Range(0, count).Select(n => "p" + n.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".txt")];

    /// <summary>A checkout holding a.txt and p000.txt to p399.txt in one commit.</summary>
    private static GitRepository ManyFiles()
    {
        var repository = GitRepository.WithCommit();
        foreach (var name in Numbered(400)) repository.Write(name, name + "\n");
        repository.CommitAll("many", 1_700_000_100);
        return repository;
    }

    /// <summary>f.txt holding L1 to L10, one per line, with the given lines replaced.</summary>
    private static string Lines(params (string Line, string Text)[] replaced) =>
        string.Concat(Enumerable.Range(1, 10).Select(n =>
        {
            var name = "L" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var replacement = replaced.FirstOrDefault(r => r.Line == name).Text;
            return (replacement ?? name) + "\n";
        }));

    private static GitRepository TenLines()
    {
        var repository = GitRepository.WithCommit();
        repository.Write("f.txt", Lines());
        repository.CommitAll("ten", 1_700_000_100);
        return repository;
    }

    /// <summary>git's own patch of f.txt with line <paramref name="line"/> replaced; the working tree is put back.</summary>
    private static string PatchChanging(GitRepository repository, int line, string text)
    {
        File.WriteAllText(repository.Child("f.txt"), Lines(("L" + line.ToString(System.Globalization.CultureInfo.InvariantCulture), text)));
        var patch = repository.Git("diff", "--no-ext-diff", "--binary");
        repository.Git("checkout", "--", "f.txt");
        return patch;
    }
}
