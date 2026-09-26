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

// B3 delta of Services.Git.Git (lane A9/L2d, B3 plan). Covers the B3-only additions over the B2 file: createPullRequest's
// existing-request fallback and existingRequestURL(in:web:) (Git.swift:192-218, PR-GIT-161/162), the new
// captureTree(paths:onto:from:) with its blobData/fileMode helpers (:280-359, PR-GIT-163/164/165/166), pushBranch's
// force parameter (:387-392, PR-GIT-031) and the two-argument updateRef(_:to:) (:376-378, PR-GIT-036), no longer dead
// code at B3. Real-git expectations follow the same conventions as the rest of this unit's tests: GitRepository over
// real folders under MARROW_PORT_DATA_DIR (R-TEST-9, R-TEST-10), TestRuntime for every call's manual clock, and
// ForgeScript (Fixtures/ForgeFixtures.cs) for the forge-CLI stand-ins.
public sealed class GitFifthTests
{
    // ---- updateRef(_:to:) (Git.swift:376-378, PR-GIT-036) ----

    [Fact]
    [Trait("Req", "PR-GIT-036")] // Services/Git/Git.swift:376-378
    public async Task UpdateRef_two_argument_moves_a_ref_unconditionally()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var head = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("b.txt", "b\n");
        var second = repository.CommitAll("second", 1_700_000_100);
        repository.Git("branch", "topic", head);
        var git = new GitValue(repository.Root);
        await git.UpdateRef("refs/heads/topic", second);
        Assert.Equal(second + "\n", repository.Git("rev-parse", "refs/heads/topic"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-036")] // Services/Git/Git.swift:377
    public async Task UpdateRef_two_argument_throws_gits_own_message_for_a_bad_ref_name()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var head = repository.Git("rev-parse", "HEAD").Trim();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).UpdateRef("not a valid ref", head));
    }

    [Fact]
    [Trait("Req", "PR-GIT-036")] // Services/Git/Git.swift:376
    public void UpdateRef_two_argument_overload_exists_beside_the_compare_and_swap_one()
    {
        var overloads = typeof(GitValue).GetMethods().Where(m => m.Name == nameof(GitValue.UpdateRef)).ToList();
        Assert.Contains(overloads, m => m.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "ref", "to" }));
        Assert.Contains(overloads, m => m.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "ref", "to", "expecting" }));
    }

    // ---- pushBranch(_:force:) (Git.swift:387-392, PR-GIT-031) ----

    [Fact]
    [Trait("Req", "PR-GIT-031")] // Services/Git/Git.swift:390-392 (B3)
    public void PushBranch_defaults_force_to_false()
    {
        var parameters = typeof(GitValue).GetMethod(nameof(GitValue.PushBranch))!.GetParameters();
        Assert.Equal(new[] { "name", "force" }, parameters.Select(p => p.Name));
        Assert.Equal(false, parameters[1].DefaultValue);
    }

    [Fact]
    [Trait("Req", "PR-GIT-031")] // Services/Git/Git.swift:390-392 (B3)
    public async Task PushBranch_with_force_overwrites_a_branch_the_remote_moved_on_without()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var remote = AddBareOrigin(repository);
        repository.Git("switch", "-q", "-c", "topic/one");
        repository.Write("t.txt", "t1\n");
        repository.CommitAll("topic v1", 1_700_000_100);
        await new GitValue(repository.Root).PushBranch("topic/one");
        var stale = GitRepository.GitIn(remote, null, "rev-parse", "refs/heads/topic/one").Trim();
        // The job's own branch is rebuilt on a newer base and no longer shares history with what was pushed.
        repository.Git("reset", "-q", "--hard", "HEAD~1");
        repository.Write("t.txt", "t2\n");
        var rebuilt = repository.CommitAll("topic v2", 1_700_000_200);
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).PushBranch("topic/one"));
        Assert.Equal(stale, GitRepository.GitIn(remote, null, "rev-parse", "refs/heads/topic/one").Trim());
        await new GitValue(repository.Root).PushBranch("topic/one", force: true);
        Assert.Equal(rebuilt, GitRepository.GitIn(remote, null, "rev-parse", "refs/heads/topic/one").Trim());
    }

    // ---- createPullRequest's existing-request fallback (Git.swift:192-200, PR-GIT-161) ----

    [Fact]
    [Trait("Req", "PR-GIT-161")] // Services/Git/Git.swift:192-196
    [Trait("Req", "PR-GIT-043")] // Services/Git/Git.swift:172-200 (B3)
    public async Task CreatePullRequest_returns_the_existing_requests_url_instead_of_throwing_on_gitlab()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/group/project.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stderr: "ERROR: A merge request already exists for this branch: !9\n", status: "1");
        var url = await new GitValue(repository.Root).CreatePullRequest(title: "t", body: "b");
        Assert.NotNull(url);
        Assert.Equal("https://gitlab.com/group/project/-/merge_requests/9", url.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-GIT-161")] // Services/Git/Git.swift:192-196
    public async Task CreatePullRequest_returns_the_existing_requests_url_instead_of_throwing_on_github()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://github.com/owner/repo.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stdout: "a pull request for branch \"topic\" already exists: https://github.com/owner/repo/pull/4\n", status: "1");
        var url = await new GitValue(repository.Root).CreatePullRequest(title: "t", body: "b");
        Assert.NotNull(url);
        Assert.Equal("https://github.com/owner/repo/pull/4", url.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-GIT-161")] // Services/Git/Git.swift:196 (Self.check still runs)
    public async Task CreatePullRequest_still_throws_when_the_failure_does_not_name_an_existing_request()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://github.com/owner/repo.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stderr: "gh: not authenticated\n", status: "1");
        var error = await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).CreatePullRequest(title: "t", body: "b"));
        Assert.Equal("gh: not authenticated", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-GIT-161")] // Services/Git/Git.swift:193
    public async Task CreatePullRequest_does_not_consult_the_fallback_on_a_successful_exit()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/group/project.git");
        var script = new ForgeScript(repository.Root);
        // A successful exit that happens to mention "already exists" in its own text is read for an https address
        // as it always was (:197-199), never through the fallback.
        script.ReplyAlways(stdout: "already exists at https://gitlab.com/group/project/-/merge_requests/1\n", status: "0");
        var url = await new GitValue(repository.Root).CreatePullRequest(title: "t", body: "b");
        Assert.Equal("https://gitlab.com/group/project/-/merge_requests/1", url!.AbsoluteString);
    }

    // ---- existingRequestURL(in:web:) (Git.swift:202-218, PR-GIT-162) ----

    [Theory]
    [Trait("Req", "PR-GIT-162")] // Services/Git/Git.swift:207-210
    [InlineData("A pull request already exists: https://github.com/o/r/pull/4\n", "", null, "https://github.com/o/r/pull/4")]
    [InlineData("", "already exists: https://codeberg.org/o/r/pulls/9\n", null, "https://codeberg.org/o/r/pulls/9")]
    [InlineData("Already Exists (mixed case)\nhttps://github.com/o/r/pull/2\n", "", null, "https://github.com/o/r/pull/2")]
    public void ExistingRequestURL_returns_the_forges_own_https_address_for_github_and_gitea(
        string stdout, string stderr, string? web, string expected)
    {
        var result = new ShellResult(1, Encoding.UTF8.GetBytes(stdout), Encoding.UTF8.GetBytes(stderr));
        var url = GitValue.ExistingRequestURL(result, web is null ? null : SwiftURL.Create(web));
        Assert.NotNull(url);
        Assert.Equal(expected, url.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-GIT-162")] // Services/Git/Git.swift:211-217
    public void ExistingRequestURL_for_gitlabs_bang_number_appends_merge_requests_path_to_the_web_url()
    {
        var result = new ShellResult(1, [], Encoding.UTF8.GetBytes("A merge request already exists for this branch: !12\n"));
        var url = GitValue.ExistingRequestURL(result, SwiftURL.Create("https://gitlab.com/group/project"));
        Assert.Equal("https://gitlab.com/group/project/-/merge_requests/12", url!.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-GIT-162")] // Services/Git/Git.swift:212-216
    public void ExistingRequestURL_strips_embedded_credentials_from_the_web_url_before_appending()
    {
        var result = new ShellResult(1, [], Encoding.UTF8.GetBytes("merge request already exists: !3\n"));
        var url = GitValue.ExistingRequestURL(result, SwiftURL.Create("https://oauth2:secrettoken@gitlab.com/group/project"));
        Assert.Equal("https://gitlab.com/group/project/-/merge_requests/3", url!.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-GIT-162")] // Services/Git/Git.swift:207 (nil case 1)
    public void ExistingRequestURL_is_nil_with_no_already_exist_substring()
    {
        var result = new ShellResult(1, [], Encoding.UTF8.GetBytes("merge request !3 was rejected\n"));
        Assert.Null(GitValue.ExistingRequestURL(result, SwiftURL.Create("https://gitlab.com/g/p")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-162")] // Services/Git/Git.swift:211 (nil case 2)
    public void ExistingRequestURL_is_nil_when_already_exist_names_neither_a_url_nor_a_bang_number()
    {
        var result = new ShellResult(1, [], Encoding.UTF8.GetBytes("a request already exists somewhere\n"));
        Assert.Null(GitValue.ExistingRequestURL(result, SwiftURL.Create("https://gitlab.com/g/p")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-162")] // Services/Git/Git.swift:211 (nil case 3)
    public void ExistingRequestURL_is_nil_for_a_bang_number_with_no_web_url_given()
    {
        var result = new ShellResult(1, [], Encoding.UTF8.GetBytes("merge request already exists: !3\n"));
        Assert.Null(GitValue.ExistingRequestURL(result, null));
    }

    [Fact]
    [Trait("Req", "PR-GIT-162")] // Services/Git/Git.swift:206 stdout, "\n", then stderr
    public void ExistingRequestURL_reads_stdout_then_stderr_joined_by_one_newline()
    {
        var result = new ShellResult(1, Encoding.UTF8.GetBytes("no address on this line"), Encoding.UTF8.GetBytes("already exists: https://github.com/o/r/pull/1"));
        Assert.Equal("https://github.com/o/r/pull/1", GitValue.ExistingRequestURL(result, null)!.AbsoluteString);
    }

    // ---- blobData, fileMode (Git.swift:347-359, PR-GIT-166) ----

    [Fact]
    [Trait("Req", "PR-GIT-166")] // Services/Git/Git.swift:347-351
    public async Task BlobData_reads_a_paths_bytes_at_a_commit_and_is_nil_where_the_commit_has_no_such_file()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("later.txt", "later\n");
        var second = repository.CommitAll("second", 1_700_000_100);
        var git = new GitValue(repository.Root);
        Assert.Equal("a\n", Encoding.UTF8.GetString((await git.BlobData(first, "a.txt"))!));
        Assert.Null(await git.BlobData(first, "later.txt"));
        Assert.Equal("later\n", Encoding.UTF8.GetString((await git.BlobData(second, "later.txt"))!));
        Assert.Null(await git.BlobData(first, "missing.txt"));
        Assert.Null(await git.BlobData("0123456789012345678901234567890123456789", "a.txt"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-166")] // Services/Git/Git.swift:353-359
    public async Task FileMode_reads_a_paths_mode_at_a_commit_and_is_nil_where_it_has_none()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("exe.txt", "e\n");
        repository.Git("add", "-A");
        repository.Git("update-index", "--chmod=+x", "exe.txt");
        var second = repository.CommitAll("second", 1_700_000_100);
        var git = new GitValue(repository.Root);
        Assert.Equal("100644", await git.FileMode(first, "a.txt"));
        Assert.Equal("100755", await git.FileMode(second, "exe.txt"));
        Assert.Null(await git.FileMode(first, "exe.txt"));
        Assert.Null(await git.FileMode(first, "missing.txt"));
    }

    // ---- captureTree(paths:onto:from:) (Git.swift:280-345, PR-GIT-163/164/165) ----

    [Fact]
    [Trait("Req", "PR-GIT-003")] // Services/Git/Git.swift:301-303 (B3)
    public async Task CaptureTreeOnto_gives_its_clean_add_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var head = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("clean.txt", "clean\n");
        var marker = GitProbes.InstallStallFilter(repository);
        var git = new GitValue(repository.Root);
        await GitProbes.AssertBudgetOnceStalled(runtime, git.CaptureTree(["clean.txt"], onto: head, from: head), 300, marker);
    }

    [Fact]
    [Trait("Req", "PR-GIT-163")] // Services/Git/Git.swift:297-303
    public async Task CaptureTreeOnto_with_no_contested_paths_stages_every_path_from_the_working_tree()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        repository.Write("clean.txt", "clean-v2-team\n");
        var git = new GitValue(repository.Root);
        var result = await git.CaptureTree(["clean.txt"], onto: @base, from: head);
        Assert.NotNull(result);
        Assert.Equal("clean-v2-team\n", repository.Git("cat-file", "-p", result.Value.Tree + ":clean.txt"));
        Assert.Empty(result.Value.Merged);
    }

    [Fact]
    [Trait("Req", "PR-GIT-164")] // Services/Git/Git.swift:322
    public async Task CaptureTreeOnto_skips_a_contested_path_whose_working_tree_already_matches_the_remote()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        repository.Write("skip.txt", "skip-v2-remote\n");
        var git = new GitValue(repository.Root);
        var result = await git.CaptureTree(["skip.txt"], onto: @base, from: head);
        Assert.NotNull(result);
        Assert.Equal("skip-v2-remote\n", repository.Git("cat-file", "-p", result.Value.Tree + ":skip.txt"));
        Assert.Empty(result.Value.Merged);
    }

    [Fact]
    [Trait("Req", "PR-GIT-164")] // Services/Git/Git.swift:311-313
    public async Task CaptureTreeOnto_skips_a_contested_path_deleted_on_both_sides()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        // remotedelete.txt is already gone at base (Scenario()); deleting it from the working tree too puts it in
        // the true (nil, nil) branch, distinct from the skip test above (remote == working, both present).
        File.Delete(repository.Child("remotedelete.txt"));
        var git = new GitValue(repository.Root);
        var result = await git.CaptureTree(["remotedelete.txt"], onto: @base, from: head);
        Assert.NotNull(result);
        Assert.Empty(result.Value.Merged);
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", result.Value.Tree + ":remotedelete.txt").Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-GIT-163")] // Services/Git/Git.swift:298 ("--no-renames")
    public async Task CaptureTreeOnto_treats_a_remote_rename_as_a_delete_of_the_old_path()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        // The team is still editing the pre-rename path; with "--no-renames" the diff between head and base names
        // rename_old.txt as changed (deleted), so it lands in contested with remote nil and working non-nil (:318-320),
        // and the call refuses. Without "--no-renames" git's own rename detector would pair the delete with
        // rename_new.txt's add and never mention rename_old.txt, so it would be treated as clean and staged as-is.
        repository.Write("rename_old.txt", "RN-1\nRN-2\nRN-3\nTEAM-RN-4\nRN-5\n");
        var git = new GitValue(repository.Root);
        Assert.Null(await git.CaptureTree(["rename_old.txt"], onto: @base, from: head));
    }

    [Fact]
    [Trait("Req", "PR-GIT-164")] // Services/Git/Git.swift:314-317
    public async Task CaptureTreeOnto_accepts_a_team_deletion_when_the_remote_only_changed_the_mode()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        File.Delete(repository.Child("teamdelete_ok.txt"));
        var git = new GitValue(repository.Root);
        var result = await git.CaptureTree(["teamdelete_ok.txt"], onto: @base, from: head);
        Assert.NotNull(result);
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", result.Value.Tree + ":teamdelete_ok.txt").Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-GIT-164")] // Services/Git/Git.swift:314-317
    public async Task CaptureTreeOnto_refuses_a_team_deletion_when_the_remote_changed_the_content()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        File.Delete(repository.Child("teamdelete_bad.txt"));
        var git = new GitValue(repository.Root);
        Assert.Null(await git.CaptureTree(["teamdelete_bad.txt"], onto: @base, from: head));
    }

    [Fact]
    [Trait("Req", "PR-GIT-164")] // Services/Git/Git.swift:318-320
    public async Task CaptureTreeOnto_refuses_when_the_remote_deleted_a_file_the_team_kept()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        repository.Write("remotedelete.txt", "remotedelete-v1-edited-by-team\n");
        var git = new GitValue(repository.Root);
        Assert.Null(await git.CaptureTree(["remotedelete.txt"], onto: @base, from: head));
    }

    [Fact]
    [Trait("Req", "PR-GIT-164")] // Services/Git/Git.swift:323-324
    public async Task CaptureTreeOnto_refuses_an_add_add_with_no_shared_ancestor()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        repository.Write("addadd.txt", "added-by-team\n");
        var git = new GitValue(repository.Root);
        Assert.Null(await git.CaptureTree(["addadd.txt"], onto: @base, from: head));
    }

    [Fact]
    [Trait("Req", "PR-GIT-165")] // Services/Git/Git.swift:325-339
    public async Task CaptureTreeOnto_three_way_merges_a_path_both_sides_changed_and_stages_the_remotes_mode()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        repository.Write("merge.txt", "L1\nL2\nL3\nL4\nTEAM-L5\n");
        var git = new GitValue(repository.Root);
        var result = await git.CaptureTree(["merge.txt"], onto: @base, from: head);
        Assert.NotNull(result);
        Assert.Equal("REMOTE-L1\nL2\nL3\nL4\nTEAM-L5\n", repository.Git("cat-file", "-p", result.Value.Tree + ":merge.txt"));
        Assert.Equal("REMOTE-L1\nL2\nL3\nL4\nTEAM-L5\n", Encoding.UTF8.GetString(Assert.Single(result.Value.Merged).Value));
        // The mode staged is the remote's (100755, PR-GIT-165), not the working tree's or a "100644" default.
        var listing = repository.Git("ls-tree", result.Value.Tree, "merge.txt");
        Assert.StartsWith("100755 blob", listing, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-GIT-165")] // Services/Git/Git.swift:331-333
    public async Task CaptureTreeOnto_returns_null_on_a_genuine_conflict()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        repository.Write("conflict.txt", "L1\nL2\nTEAM-L3\nL4\nL5\n");
        var git = new GitValue(repository.Root);
        Assert.Null(await git.CaptureTree(["conflict.txt"], onto: @base, from: head));
    }

    [Fact]
    [Trait("Req", "PR-GIT-163")] // Services/Git/Git.swift:280-345
    public async Task CaptureTreeOnto_combines_clean_skip_delete_and_merged_paths_in_one_successful_call()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        repository.Write("clean.txt", "clean-v2-team\n");
        repository.Write("skip.txt", "skip-v2-remote\n");
        File.Delete(repository.Child("teamdelete_ok.txt"));
        repository.Write("merge.txt", "L1\nL2\nL3\nL4\nTEAM-L5\n");
        var git = new GitValue(repository.Root);
        var result = await git.CaptureTree(["clean.txt", "skip.txt", "teamdelete_ok.txt", "merge.txt"], onto: @base, from: head);
        Assert.NotNull(result);
        var tree = result.Value.Tree;
        Assert.Equal("clean-v2-team\n", repository.Git("cat-file", "-p", tree + ":clean.txt"));
        Assert.Equal("skip-v2-remote\n", repository.Git("cat-file", "-p", tree + ":skip.txt"));
        Assert.False(GitRepository.Raw(repository.Root, null, "cat-file", "-e", tree + ":teamdelete_ok.txt").Succeeded);
        Assert.Equal("REMOTE-L1\nL2\nL3\nL4\nTEAM-L5\n", repository.Git("cat-file", "-p", tree + ":merge.txt"));
        Assert.Equal(new[] { "merge.txt" }, result.Value.Merged.Keys);
        // Paths never named are untouched: the base's own content stands (read-tree seeded the scratch from it).
        Assert.Equal("bad-v2-remote\n", repository.Git("cat-file", "-p", tree + ":teamdelete_bad.txt"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-070")] // Services/Git/Git.swift:291-293 (B3)
    public async Task CaptureTreeOnto_names_a_scratch_directory_holding_an_index_and_removes_it_on_success_null_and_throw()
    {
        using var runtime = new TestRuntime();
        var scenario = Scenario();
        using var repository = scenario.Repository;
        var head = scenario.Head;
        var @base = scenario.Base;
        var git = new GitValue(repository.Root);
        var before = ScratchDirectories();

        // Success: nail the scratch directory's own shape (a directory, not a file) and that its GIT_INDEX_FILE is
        // "<scratch>\index" inside it, the way GitThirdTests does for droppy-code-index-<UUID>.
        var log = GitProbes.InstallIndexProbe(repository);
        repository.Write("clean.txt", "clean-v2-team\n");
        Assert.NotNull(await git.CaptureTree(["clean.txt"], onto: @base, from: head));
        var index = Assert.Single(GitProbes.ProbedIndexFiles(log).Distinct());
        Assert.Matches(RebaseIndex, index);
        Assert.False(Directory.Exists(System.IO.Path.GetDirectoryName(index)));
        Assert.Equal(before, ScratchDirectories());

        // A null return (a genuine merge conflict) still runs the deferred cleanup.
        repository.Write("conflict.txt", "L1\nL2\nTEAM-L3\nL4\nL5\n");
        Assert.Null(await git.CaptureTree(["conflict.txt"], onto: @base, from: head));
        Assert.Equal(before, ScratchDirectories());

        // A thrown ShellError (read-tree fails on a bad "onto") still runs the deferred cleanup: the scratch
        // directory the call created before failing does not survive it.
        await Assert.ThrowsAsync<ShellError>(() => git.CaptureTree(["clean.txt"], onto: "not-a-commit", from: head));
        Assert.Equal(before, ScratchDirectories());
    }

    // ---- helpers ----

    private static readonly Regex RebaseScratch = new(
        "\\Adroppy-code-rebase-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\\z", RegexOptions.CultureInvariant);

    /// <summary>A scratch directory's own GIT_INDEX_FILE: "&lt;TempPath&gt;droppy-code-rebase-&lt;UPPER-UUID&gt;\index"
    /// (PR-GIT-070), the same convention GitThirdTests.TemporaryIndex uses for droppy-code-index-&lt;UUID&gt;.</summary>
    private static readonly Regex RebaseIndex = new(
        "\\A" + Regex.Escape(System.IO.Path.GetTempPath().TrimEnd('\\')) +
        "\\\\droppy-code-rebase-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\\\\index\\z",
        RegexOptions.CultureInvariant);

    /// <summary>The droppy-code-rebase-&lt;UUID&gt; directories currently under the system temporary folder (PR-GIT-070).</summary>
    private static IReadOnlyList<string> ScratchDirectories() =>
        Directory.GetDirectories(System.IO.Path.GetTempPath())
            .Select(path => System.IO.Path.GetFileName(path)!)
            .Where(name => RebaseScratch.IsMatch(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// A checkout at <c>head</c> (a commit holding a.txt plus the scenario files below) with a divergent
    /// <c>base</c> commit built in a throwaway detached worktree, so the checkout's own HEAD and index never move:
    /// the checkout's working tree is free for each test to edit as "the team"'s uncommitted work. Built once per
    /// test; disposing the tuple's repository removes the whole checkout.
    ///
    /// clean.txt: untouched between head and base. skip.txt: base changes it to "skip-v2-remote". teamdelete_ok.txt:
    /// base changes only its mode (chmod +x), content unchanged. teamdelete_bad.txt: base changes its content.
    /// remotedelete.txt: base deletes it. addadd.txt: absent at head, base adds "added-by-remote". merge.txt: base
    /// changes line 1 (and sets the executable bit); a team edit to a different line merges cleanly. conflict.txt:
    /// base changes line 3; a team edit to the same line conflicts. rename_old.txt: base renames it to
    /// rename_new.txt with a small edit, so its own default (no "--no-renames") diff would pair the two as one
    /// rename entry naming only rename_new.txt.
    /// </summary>
    private static (GitRepository Repository, string Head, string Base) Scenario()
    {
        var repository = GitRepository.WithCommit();
        repository.Write("clean.txt", "clean-v1\n");
        repository.Write("skip.txt", "skip-v1\n");
        repository.Write("teamdelete_ok.txt", "same-content\n");
        repository.Write("teamdelete_bad.txt", "bad-v1\n");
        repository.Write("remotedelete.txt", "remotedelete-v1\n");
        repository.Write("merge.txt", "L1\nL2\nL3\nL4\nL5\n");
        repository.Write("conflict.txt", "L1\nL2\nL3\nL4\nL5\n");
        repository.Write("rename_old.txt", "RN-1\nRN-2\nRN-3\nRN-4\nRN-5\n");
        var head = repository.CommitAll("shared", 1_700_000_100);

        var scratch = repository.Root + "-remote-build";
        repository.Git("worktree", "add", "--quiet", "--detach", scratch, head);
        try
        {
            File.WriteAllText(scratch + "\\skip.txt", "skip-v2-remote\n");
            File.WriteAllText(scratch + "\\teamdelete_bad.txt", "bad-v2-remote\n");
            File.Delete(scratch + "\\remotedelete.txt");
            File.WriteAllText(scratch + "\\addadd.txt", "added-by-remote\n");
            File.WriteAllText(scratch + "\\merge.txt", "REMOTE-L1\nL2\nL3\nL4\nL5\n");
            File.WriteAllText(scratch + "\\conflict.txt", "L1\nL2\nREMOTE-L3\nL4\nL5\n");
            File.Delete(scratch + "\\rename_old.txt");
            File.WriteAllText(scratch + "\\rename_new.txt", "RN-1\nRN-2\nRN-3\nRENAMED-RN-4\nRN-5\n");
            GitRepository.GitIn(scratch, null, "add", "-A");
            GitRepository.GitIn(scratch, null, "update-index", "--chmod=+x", "teamdelete_ok.txt");
            GitRepository.GitIn(scratch, null, "update-index", "--chmod=+x", "merge.txt");
            GitRepository.GitIn(scratch, new Dictionary<string, string>
            {
                ["GIT_COMMITTER_DATE"] = "1700000200 +0000", ["GIT_AUTHOR_DATE"] = "1700000200 +0000",
            }, "commit", "-q", "-m", "remote tip");
            var @base = GitRepository.GitIn(scratch, null, "rev-parse", "HEAD").Trim();
            return (repository, head, @base);
        }
        finally
        {
            repository.Git("worktree", "remove", "--force", scratch);
        }
    }

    /// <summary>A checkout with <paramref name="origin"/> as its origin remote, for the forge stand-ins.</summary>
    private static GitRepository ForgeRepository(string origin)
    {
        var repository = GitRepository.WithCommit();
        repository.Git("remote", "add", "origin", origin);
        return repository;
    }

    /// <summary>A bare repository beside the checkout, added as its origin; returns its path.</summary>
    private static string AddBareOrigin(GitRepository repository)
    {
        var remote = repository.Root + "-remote.git";
        GitRepository.GitIn(repository.Root, null, "init", "-q", "--bare", "-b", "main", remote);
        repository.Git("remote", "add", "origin", remote);
        return remote;
    }
}
