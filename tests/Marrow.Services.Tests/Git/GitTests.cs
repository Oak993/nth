using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using GitValue = Marrow.Services.Git.Git;

namespace Marrow.Services.Tests.Git;

// Part 1 of Services.Git.Git: the process wrapper (Git.swift:30-59) and the repository, branch, remote, landing and
// path queries of Git.swift:63-152, :233-242, :259-265, :293-379 and :517-524. Every expectation is read from the
// Swift and git's documented output; the git in these tests is the system git (R-TEST-10) over real folders under
// MARROW_PORT_DATA_DIR (R-TEST-9). Every test installs a TestRuntime, so Shell.run's deadline is on the manual clock.
public sealed class GitTests
{
    // ---- Git.run, output, check (Git.swift:30-59) ----

    [Fact]
    [Trait("Req", "PR-GIT-001")] // Services/Git/Git.swift:30-49
    public async Task Run_puts_the_two_config_pairs_before_the_callers_arguments_and_nothing_else_on_the_command_line()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var result = await new GitValue(repository.Root).Run(["config", "--list", "--show-scope"]);
        Assert.True(result.Succeeded);
        // "command" scope lists exactly the -c pairs git was given, in the order given.
        var commandLine = SwiftString.Split(result.Output, "\n").Where(line => line.StartsWith("command\t", StringComparison.Ordinal));
        Assert.Equal(new[] { "command\tcore.quotepath=false", "command\tcolor.ui=false" }, commandLine);
    }

    [Fact]
    [Trait("Req", "PR-GIT-001")] // Services/Git/Git.swift:43-44
    public async Task Run_starts_git_in_the_values_own_directory_not_the_repository_root()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        Directory.CreateDirectory(repository.Child("sub", "deeper"));
        var result = await new GitValue(repository.Child("sub", "deeper")).Run(["rev-parse", "--show-prefix"]);
        Assert.True(result.Succeeded);
        Assert.Equal("sub/deeper/\n", result.Output);
    }

    [Fact]
    [Trait("Req", "PR-GIT-001")] // Services/Git/Git.swift:43; porting/extracts/git-command-vectors.md section 1
    public async Task Percent_caret_and_brace_arguments_reach_git_unexpanded()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var git = new GitValue(repository.Root);
        var tree = GitRepository.GitIn(repository.Root, null, "cat-file", "-p", "HEAD").Split('\n')[0];
        var viaPeel = await git.Run(["rev-parse", "HEAD^{tree}"]);
        Assert.Equal("tree " + viaPeel.TrimmedOutput, tree);
        var viaFormat = await git.Run(["for-each-ref", "--format=%(refname)%09%(objecttype)", "refs/heads"]);
        Assert.Equal("refs/heads/main\tcommit\n", viaFormat.Output);
    }

    [Fact]
    [Trait("Req", "PR-GIT-002")] // Services/Git/Git.swift:37-40
    public async Task Run_gives_git_the_login_environment_with_prompts_and_optional_locks_off()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var result = await new GitValue(repository.Root).Run(
            ["-c", "alias.probe=!printenv GIT_TERMINAL_PROMPT GIT_OPTIONAL_LOCKS OS LANG", "probe"]);
        Assert.True(result.Succeeded, result.FailureMessage);
        var login = LoginEnvironment.Current;
        Assert.Equal("0\n0\n" + login["OS"] + "\n" + login["LANG"] + "\n", result.Output);
    }

    [Fact]
    [Trait("Req", "PR-GIT-002")] // Services/Git/Git.swift:40; porting/port_impacts.md entry 229
    public async Task Caller_extras_are_merged_last_and_win_over_the_forced_variables()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var result = await new GitValue(repository.Root).Run(
            ["-c", "alias.probe=!printenv GIT_TERMINAL_PROMPT GIT_OPTIONAL_LOCKS MARROW_GIT_EXTRA", "probe"],
            environment: new Dictionary<string, string> { ["GIT_OPTIONAL_LOCKS"] = "1", ["MARROW_GIT_EXTRA"] = "extra" });
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal("0\n1\nextra\n", result.Output);
    }

    [Fact]
    [Trait("Decision", "ED-128")]
    public async Task An_extra_in_another_case_keeps_the_first_spelling_and_takes_the_extra_value()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var result = await new GitValue(repository.Root).Run(
            ["-c", "alias.probe=!printenv GIT_TERMINAL_PROMPT", "probe"],
            environment: new Dictionary<string, string> { ["git_terminal_prompt"] = "7" });
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal("7\n", result.Output);
    }

    [Fact]
    [Trait("Req", "PR-GIT-003")] // Services/Git/Git.swift:35
    public async Task Run_defaults_to_a_120_second_budget_and_a_timeout_is_a_failed_result_not_a_thrown_error()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var run = new GitValue(repository.Root).Run(["-c", "alias.stall=!sleep 600", "stall"]);
        Assert.True(GitWaits.Eventually(() => runtime.Time.ActiveTimers == 1), "no deadline was armed");
        runtime.Advance(TimeSpan.FromSeconds(120) - TimeSpan.FromTicks(1));
        Assert.False(SpinWait.SpinUntil(() => run.IsCompleted, TimeSpan.FromMilliseconds(300)));
        runtime.Advance(TimeSpan.FromTicks(1));
        var result = await Finish(runtime, run);
        Assert.False(result.Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-GIT-003")] // Services/Git/Git.swift:35,47
    public async Task Run_passes_a_call_sites_own_budget_through_to_the_process()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var run = new GitValue(repository.Root).Run(["-c", "alias.stall=!sleep 600", "stall"], timeout: 300);
        Assert.True(GitWaits.Eventually(() => runtime.Time.ActiveTimers == 1), "no deadline was armed");
        runtime.Advance(TimeSpan.FromSeconds(120));
        Assert.False(SpinWait.SpinUntil(() => run.IsCompleted, TimeSpan.FromMilliseconds(300)));
        runtime.Advance(TimeSpan.FromSeconds(180) - TimeSpan.FromTicks(1));
        Assert.False(SpinWait.SpinUntil(() => run.IsCompleted, TimeSpan.FromMilliseconds(300)));
        runtime.Advance(TimeSpan.FromTicks(1));
        var result = await Finish(runtime, run);
        Assert.False(result.Succeeded);
    }

    [Fact]
    [Trait("Req", "PR-GIT-003")] // Services/Git/Git.swift:35
    public void The_default_budget_parameter_is_120_seconds()
    {
        var parameters = typeof(GitValue).GetMethod(nameof(GitValue.Run))!.GetParameters();
        Assert.Equal(new[] { "arguments", "environment", "input", "timeout" }, parameters.Select(p => p.Name));
        Assert.Equal(120.0, parameters[3].DefaultValue);
    }

    [Fact]
    [Trait("Req", "PR-GIT-005")] // Services/Git/Git.swift:51-55
    public async Task Output_returns_stdout_untrimmed()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var head = GitRepository.GitIn(repository.Root, null, "rev-parse", "HEAD");
        Assert.EndsWith("\n", head, StringComparison.Ordinal);
        Assert.Equal(head, await new GitValue(repository.Root).Output(["rev-parse", "HEAD"]));
    }

    [Fact]
    [Trait("Req", "PR-GIT-005")] // Services/Git/Git.swift:51-59
    public async Task Output_throws_a_ShellError_carrying_the_failure_message_on_a_non_zero_exit()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var expected = GitRepository.Raw(repository.Root, null, "-c", "core.quotepath=false", "-c", "color.ui=false",
            "rev-parse", "--verify", "no-such-ref").FailureMessage;
        var error = await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).Output(["rev-parse", "--verify", "no-such-ref"]));
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    [Trait("Req", "PR-GIT-005")] // Services/Git/Git.swift:51-55
    public async Task Output_runs_at_the_120_second_default_and_throws_when_the_budget_ends_it()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var output = new GitValue(repository.Root).Output(["-c", "alias.stall=!sleep 600", "stall"]);
        Assert.True(GitWaits.Eventually(() => runtime.Time.ActiveTimers == 1), "no deadline was armed");
        runtime.Advance(TimeSpan.FromSeconds(120) - TimeSpan.FromTicks(1));
        Assert.False(SpinWait.SpinUntil(() => output.IsCompleted, TimeSpan.FromMilliseconds(300)));
        runtime.Advance(TimeSpan.FromTicks(1));
        Finish(runtime, output);
        await Assert.ThrowsAsync<ShellError>(() => output);
    }

    [Fact]
    [Trait("Req", "PR-GIT-005")] // Services/Git/Git.swift:51
    public void Output_takes_the_arguments_alone_with_no_timeout_or_environment_parameter()
    {
        var parameters = typeof(GitValue).GetMethod(nameof(GitValue.Output))!.GetParameters();
        Assert.Equal(new[] { "arguments" }, parameters.Select(p => p.Name));
    }

    // ---- Repository (Git.swift:63-78) ----

    [Fact]
    [Trait("Req", "PR-GIT-007")] // Services/Git/Git.swift:63-65
    public async Task IsRepository_is_true_in_a_checkout_and_in_a_folder_below_its_root()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        Directory.CreateDirectory(repository.Child("sub"));
        Assert.True(await new GitValue(repository.Root).IsRepository());
        Assert.True(await new GitValue(repository.Child("sub")).IsRepository());
    }

    [Fact]
    [Trait("Req", "PR-GIT-007")] // Services/Git/Git.swift:63-65
    public async Task IsRepository_is_false_outside_a_checkout_and_inside_the_git_directory_where_git_answers_false()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var outside = GitRepository.NotARepository();
        Assert.False(await new GitValue(outside.Root).IsRepository());
        // Inside .git, rev-parse exits 0 and prints "false": the answer comes from the text, not the status.
        var inside = GitRepository.Raw(repository.Child(".git"), null, "rev-parse", "--is-inside-work-tree");
        Assert.True(inside.Succeeded);
        Assert.Equal("false", inside.TrimmedOutput);
        Assert.False(await new GitValue(repository.Child(".git")).IsRepository());
    }

    [Fact]
    [Trait("Req", "PR-GIT-008")] // Services/Git/Git.swift:67-74
    public async Task RepositoryRoot_drops_one_component_per_prefix_segment_from_the_values_own_path()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        Directory.CreateDirectory(repository.Child("sub", "deeper"));
        Assert.Equal(repository.Root, await new GitValue(repository.Child("sub", "deeper")).RepositoryRoot());
        Assert.Equal(repository.Root, await new GitValue(repository.Child("sub")).RepositoryRoot());
        Assert.Equal(repository.Root, await new GitValue(repository.Root).RepositoryRoot());
    }

    [Fact]
    [Trait("Req", "PR-GIT-008")] // Services/Git/Git.swift:67-74; porting/upstream_defects.md entry 86
    public async Task RepositoryRoot_keeps_the_callers_spelling_and_never_asks_git_for_the_toplevel()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        Directory.CreateDirectory(repository.Child("sub"));
        // An upper-cased spelling of the same folder: git's toplevel would come back in git's own form.
        var shouted = repository.Root[..^"repo".Length] + "REPO";
        var root = await new GitValue(shouted + "\\SUB").RepositoryRoot();
        Assert.Equal(shouted, root);
        var toplevel = GitRepository.GitIn(shouted + "\\SUB", null, "rev-parse", "--show-toplevel").Trimmed();
        Assert.NotEqual(toplevel, root);
    }

    [Fact]
    [Trait("Req", "PR-GIT-008")] // Services/Git/Git.swift:68
    public async Task RepositoryRoot_throws_outside_a_checkout()
    {
        using var runtime = new TestRuntime();
        using var outside = GitRepository.NotARepository();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(outside.Root).RepositoryRoot());
    }

    [Fact]
    [Trait("Req", "PR-GIT-009")] // Services/Git/Git.swift:76-78
    public async Task HasCommits_is_false_before_the_first_commit_and_true_after()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.Empty();
        var git = new GitValue(repository.Root);
        Assert.False(await git.HasCommits());
        repository.Write("a.txt", "a\n");
        repository.CommitAll("first", 1_700_000_000);
        Assert.True(await git.HasCommits());
    }

    [Fact]
    [Trait("Req", "PR-GIT-009")] // Services/Git/Git.swift:76-78
    public async Task HasCommits_is_false_outside_a_checkout()
    {
        using var runtime = new TestRuntime();
        using var outside = GitRepository.NotARepository();
        Assert.False(await new GitValue(outside.Root).HasCommits());
    }

    // ---- status (Git.swift:80-98, :719-722) ----

    [Fact]
    [Trait("Req", "PR-GIT-010")] // Services/Git/Git.swift:80-98
    public async Task Status_reads_branch_upstream_ahead_behind_and_counts_every_non_header_line()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("branch", "base");
        repository.Git("switch", "-q", "base");
        repository.Write("b.txt", "b\n");
        repository.CommitAll("on base", 1_700_000_100);
        repository.Git("switch", "-q", "main");
        repository.Write("c.txt", "c\n");
        repository.CommitAll("on main 1", 1_700_000_200);
        repository.Write("d.txt", "d\n");
        repository.CommitAll("on main 2", 1_700_000_300);
        repository.Git("branch", "--set-upstream-to=base");
        repository.Write("a.txt", "changed\n");
        repository.Write("untracked.txt", "u\n");
        var status = await new GitValue(repository.Root).Status();
        Assert.Equal(new GitStatus { Branch = "main", Upstream = "base", Ahead = 2, Behind = 1, ChangedFiles = 2 }, status);
    }

    [Fact]
    [Trait("Req", "PR-GIT-010")] // Services/Git/Git.swift:84-85
    public async Task A_detached_head_reads_as_no_branch()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("switch", "-q", "--detach");
        var status = await new GitValue(repository.Root).Status();
        Assert.Equal(new GitStatus(), status);
        Assert.Null(status!.Branch);
    }

    [Fact]
    [Trait("Req", "PR-GIT-010")] // Services/Git/Git.swift:80-98
    public async Task A_clean_checkout_without_an_upstream_has_zero_counts()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.Empty();
        var status = await new GitValue(repository.Root).Status();
        Assert.Equal(new GitStatus { Branch = "main" }, status);
    }

    [Fact]
    [Trait("Req", "PR-GIT-010")] // Services/Git/Git.swift:81
    public async Task Status_is_nil_outside_a_checkout()
    {
        using var runtime = new TestRuntime();
        using var outside = GitRepository.NotARepository();
        Assert.Null(await new GitValue(outside.Root).Status());
    }

    // ---- branches, switch, create (Git.swift:100-119) ----

    [Fact]
    [Trait("Req", "PR-GIT-011")] // Services/Git/Git.swift:100-111
    public async Task Branches_lists_heads_newest_commit_first_with_current_and_upstream()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("switch", "-q", "-c", "feature");
        repository.Write("f.txt", "f\n");
        repository.CommitAll("feature", 1_700_000_500);
        repository.Git("branch", "--set-upstream-to=main");
        repository.Git("switch", "-q", "-c", "older", "main");
        repository.Git("switch", "-q", "feature");
        var branches = await new GitValue(repository.Root).Branches();
        Assert.Equal(
            new[] { new GitBranch("feature", true, "main"), new GitBranch("main", false, null), new GitBranch("older", false, null) },
            branches.Take(1).Concat(branches.Skip(1).OrderBy(b => b.Name, StringComparer.Ordinal)));
        Assert.Equal("feature", branches[0].Id);
    }

    [Fact]
    [Trait("Req", "PR-GIT-011")] // Services/Git/Git.swift:102-104
    public async Task Branches_is_empty_outside_a_checkout()
    {
        using var runtime = new TestRuntime();
        using var outside = GitRepository.NotARepository();
        Assert.Empty(await new GitValue(outside.Root).Branches());
    }

    [Fact]
    [Trait("Req", "PR-GIT-012")] // Services/Git/Git.swift:113-115
    public async Task SwitchBranch_moves_the_checkout_to_the_named_branch()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("branch", "other");
        await new GitValue(repository.Root).SwitchBranch("other");
        Assert.Equal("other\n", repository.Git("branch", "--show-current"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-012")] // Services/Git/Git.swift:113-115
    public async Task SwitchBranch_throws_gits_failure_message()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var expected = GitRepository.Raw(repository.Root, null, "-c", "core.quotepath=false", "-c", "color.ui=false",
            "switch", "missing").FailureMessage;
        var error = await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).SwitchBranch("missing"));
        Assert.Equal(expected, error.Message);
        Assert.Equal("main\n", repository.Git("branch", "--show-current"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-013")] // Services/Git/Git.swift:117-119
    public async Task CreateBranch_creates_the_branch_and_moves_head_to_it()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await new GitValue(repository.Root).CreateBranch("new-work");
        Assert.Equal("new-work\n", repository.Git("branch", "--show-current"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-013")] // Services/Git/Git.swift:117-119
    public async Task CreateBranch_throws_gits_failure_message_for_an_existing_name()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("branch", "taken");
        var expected = GitRepository.Raw(repository.Root, null, "-c", "core.quotepath=false", "-c", "color.ui=false",
            "switch", "-c", "taken").FailureMessage;
        var error = await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).CreateBranch("taken"));
        Assert.Equal(expected, error.Message);
    }

    // ---- remotes (Git.swift:131-147) ----

    [Fact]
    [Trait("Req", "PR-GIT-014")] // Services/Git/Git.swift:131-134
    public async Task RemoteURL_is_the_trimmed_origin_address()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("remote", "add", "origin", "git@example.com:team/project.git");
        Assert.Equal("git@example.com:team/project.git", await new GitValue(repository.Root).RemoteURL());
    }

    [Fact]
    [Trait("Req", "PR-GIT-014")] // Services/Git/Git.swift:132
    public async Task RemoteURL_is_nil_without_an_origin()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var outside = GitRepository.NotARepository();
        Assert.Null(await new GitValue(repository.Root).RemoteURL());
        Assert.Null(await new GitValue(outside.Root).RemoteURL());
    }

    [Theory]
    [Trait("Req", "PR-GIT-015")] // Services/Git/Git.swift:136-147
    [InlineData("git@github.com:team/project.git", "https://github.com/team/project")]
    [InlineData("git@gitlab.com:group/sub/project", "https://gitlab.com/group/sub/project")]
    [InlineData("ssh://git@gitlab.example.com/team/project.git", "https://gitlab.example.com/team/project")]
    [InlineData("ssh://git@host.example:2222/team/project.git", "https://host.example:2222/team/project")]
    [InlineData("https://github.com/team/project.git", "https://github.com/team/project")]
    [InlineData("http://git.example.com/team/project", "http://git.example.com/team/project")]
    public async Task RemoteWebURL_rewrites_the_origin_into_an_https_page(string origin, string expected)
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("remote", "add", "origin", origin);
        var url = await new GitValue(repository.Root).RemoteWebURL();
        Assert.NotNull(url);
        Assert.Equal(expected, url.AbsoluteString);
    }

    [Theory]
    [Trait("Req", "PR-GIT-015")] // Services/Git/Git.swift:146
    [InlineData("C:\\repos\\project")]
    [InlineData("git://example.com/team/project.git")]
    [InlineData("file:///C:/repos/project.git")]
    [InlineData("git@host-without-colon")]
    [InlineData("ssh://user@example.com/team/project.git")]
    public async Task RemoteWebURL_is_nil_for_anything_that_does_not_end_up_starting_http(string origin)
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("remote", "add", "origin", origin);
        Assert.Null(await new GitValue(repository.Root).RemoteWebURL());
    }

    [Fact]
    [Trait("Req", "PR-GIT-015")] // Services/Git/Git.swift:138
    public async Task RemoteWebURL_is_nil_without_an_origin()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        Assert.Null(await new GitValue(repository.Root).RemoteWebURL());
    }

    // ---- listFiles (Git.swift:149-152) ----

    [Fact]
    [Trait("Req", "PR-GIT-016")] // Services/Git/Git.swift:149-152
    public async Task ListFiles_gives_tracked_and_untracked_files_unquoted_with_forward_slashes_and_skips_ignored_ones()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write(".gitignore", "*.log\n");
        repository.CommitAll("ignore", 1_700_000_100);
        repository.Write("dir/new.txt", "n\n");
        repository.Write("caf\U000000E9.txt", "e\n"); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        repository.Write("noise.log", "x\n");
        var files = await new GitValue(repository.Root).ListFiles();
        Assert.Equal(4, files.Count);
        Assert.Equal(
            new HashSet<string>([".gitignore", "a.txt", "dir/new.txt", "caf\U000000E9.txt"], StringComparer.Ordinal),
            files.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-GIT-016")] // Services/Git/Git.swift:150
    public async Task ListFiles_is_empty_outside_a_checkout()
    {
        using var runtime = new TestRuntime();
        using var outside = GitRepository.NotARepository();
        Assert.Empty(await new GitValue(outside.Root).ListFiles());
    }

    // ---- defaultBranch (Git.swift:233-242) ----

    [Fact]
    [Trait("Req", "PR-GIT-017")] // Services/Git/Git.swift:233-242
    public async Task DefaultBranch_is_main_with_no_remote_at_all()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var outside = GitRepository.NotARepository();
        Assert.Equal("main", await new GitValue(repository.Root).DefaultBranch());
        Assert.Equal("main", await new GitValue(outside.Root).DefaultBranch());
    }

    [Fact]
    [Trait("Req", "PR-GIT-017")] // Services/Git/Git.swift:234-237
    public async Task DefaultBranch_prefers_origin_head_with_its_origin_prefix_removed()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("update-ref", "refs/remotes/origin/trunk", "HEAD");
        repository.Git("update-ref", "refs/remotes/origin/main", "HEAD");
        repository.Git("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/trunk");
        Assert.Equal("trunk", await new GitValue(repository.Root).DefaultBranch());
    }

    [Fact]
    [Trait("Req", "PR-GIT-017")] // Services/Git/Git.swift:238-240
    public async Task DefaultBranch_falls_back_to_origin_main_before_origin_master()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("update-ref", "refs/remotes/origin/master", "HEAD");
        Assert.Equal("master", await new GitValue(repository.Root).DefaultBranch());
        repository.Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Assert.Equal("main", await new GitValue(repository.Root).DefaultBranch());
    }

    // ---- treeHash, commitHash (Git.swift:259-265) ----

    [Fact]
    [Trait("Req", "PR-GIT-024")] // Services/Git/Git.swift:259-261
    public async Task TreeHash_peels_a_ref_to_its_tree_trimmed()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var tree = GitRepository.GitIn(repository.Root, null, "cat-file", "-p", "HEAD").Split('\n')[0]["tree ".Length..];
        Assert.Equal(tree, await new GitValue(repository.Root).TreeHash("HEAD"));
        Assert.Equal(tree, await new GitValue(repository.Root).TreeHash("main"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-024")] // Services/Git/Git.swift:259-261
    public async Task TreeHash_throws_for_an_unknown_ref()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).TreeHash("no-such-ref"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-025")] // Services/Git/Git.swift:263-265
    public async Task CommitHash_defaults_to_HEAD_and_resolves_a_named_ref_trimmed()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").TrimEnd('\n');
        repository.Git("branch", "first");
        repository.Write("b.txt", "b\n");
        var second = repository.CommitAll("second", 1_700_000_100);
        var git = new GitValue(repository.Root);
        Assert.Equal(second, await git.CommitHash());
        Assert.Equal(first, await git.CommitHash("first"));
        Assert.Equal("HEAD", typeof(GitValue).GetMethod(nameof(GitValue.CommitHash))!.GetParameters()[0].DefaultValue);
    }

    [Fact]
    [Trait("Req", "PR-GIT-025")] // Services/Git/Git.swift:263-265
    public async Task CommitHash_throws_for_an_unknown_ref()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).CommitHash("no-such-ref"));
    }

    // ---- isAncestor, changedPaths (Git.swift:293-301) ----

    [Fact]
    [Trait("Req", "PR-GIT-018")] // Services/Git/Git.swift:293-295
    public async Task IsAncestor_answers_from_the_exit_status()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").TrimEnd('\n');
        repository.Write("b.txt", "b\n");
        var second = repository.CommitAll("second", 1_700_000_100);
        var git = new GitValue(repository.Root);
        Assert.True(await git.IsAncestor(first, second));
        Assert.False(await git.IsAncestor(second, first));
        Assert.False(await git.IsAncestor("no-such-ref", second));
    }

    [Fact]
    [Trait("Req", "PR-GIT-019")] // Services/Git/Git.swift:297-301
    public async Task ChangedPaths_splits_on_NUL_and_keeps_names_raw()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").TrimEnd('\n');
        repository.Write("dir/with space.txt", "s\n");
        repository.Write("caf\U000000E9.txt", "e\n"); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        repository.Write("a.txt", "changed\n");
        var second = repository.CommitAll("second", 1_700_000_100);
        var paths = await new GitValue(repository.Root).ChangedPaths(first, second);
        Assert.Equal(new[] { "a.txt", "caf\U000000E9.txt", "dir/with space.txt" }, paths);
    }

    [Fact]
    [Trait("Req", "PR-GIT-019")] // Services/Git/Git.swift:300
    public async Task ChangedPaths_is_empty_between_equal_trees_and_throws_for_an_unknown_ref()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var git = new GitValue(repository.Root);
        Assert.Empty(await git.ChangedPaths("HEAD", "HEAD"));
        await Assert.ThrowsAsync<ShellError>(() => git.ChangedPaths("no-such-ref", "HEAD"));
    }

    // ---- dirtyPaths, ignoredPaths, presentPaths (Git.swift:303-361) ----

    [Fact]
    [Trait("Req", "PR-GIT-020")] // Services/Git/Git.swift:303-319
    public async Task DirtyPaths_lists_modified_renamed_with_its_origin_and_every_untracked_file()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("old.txt", "o\n");
        repository.CommitAll("old", 1_700_000_100);
        repository.Write("a.txt", "changed\n");
        repository.Git("mv", "old.txt", "renamed.txt");
        repository.Write("dir/deep/new.txt", "n\n");
        var paths = await new GitValue(repository.Root).DirtyPaths();
        // porcelain v1 -z: " M a.txt\0R  renamed.txt\0old.txt\0?? dir/deep/new.txt\0"
        Assert.Equal(new[] { "a.txt", "renamed.txt", "old.txt", "dir/deep/new.txt" }, paths);
    }

    [Fact]
    [Trait("Req", "PR-GIT-020")] // Services/Git/Git.swift:305
    public async Task DirtyPaths_is_empty_for_a_clean_checkout_and_outside_one()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var outside = GitRepository.NotARepository();
        Assert.Empty(await new GitValue(repository.Root).DirtyPaths());
        Assert.Empty(await new GitValue(outside.Root).DirtyPaths());
    }

    [Fact]
    [Trait("Req", "PR-GIT-021")] // Services/Git/Git.swift:321-325
    public async Task IgnoredPaths_returns_the_ignored_subset()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write(".gitignore", "*.log\nbuild/\n");
        repository.Write("build/out.bin", "b\n");
        var ignored = await new GitValue(repository.Root).IgnoredPaths(["a.txt", "x.log", "dir/y.log", "build/out.bin", "keep.txt"]);
        Assert.Equal(new HashSet<string>(["x.log", "dir/y.log", "build/out.bin"], StringComparer.Ordinal),
            ignored.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-GIT-021")] // Services/Git/Git.swift:323
    public async Task IgnoredPaths_accepts_exit_status_one_when_nothing_matches()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write(".gitignore", "*.log\n");
        var raw = GitRepository.Raw(repository.Root, null, "check-ignore", "a.txt");
        Assert.Equal(1, raw.Status);
        Assert.Empty(await new GitValue(repository.Root).IgnoredPaths(["a.txt", "b.txt"]));
    }

    [Fact]
    [Trait("Req", "PR-GIT-021")] // Services/Git/Git.swift:323
    public async Task IgnoredPaths_is_empty_for_no_paths_and_for_any_other_exit_status()
    {
        using var runtime = new TestRuntime();
        using var outside = GitRepository.NotARepository();
        var raw = GitRepository.Raw(outside.Root, null, "check-ignore", "x.log");
        Assert.NotEqual(0, raw.Status);
        Assert.NotEqual(1, raw.Status);
        Assert.Empty(await new GitValue(outside.Root).IgnoredPaths(["x.log"]));
        Assert.Empty(await new GitValue(outside.Root).IgnoredPaths([]));
    }

    [Fact]
    [Trait("Req", "PR-GIT-022")] // Services/Git/Git.swift:333-354
    public async Task PresentPaths_answers_which_paths_a_ref_holds_blobs_and_trees_alike()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("dir/b.txt", "b\n");
        repository.CommitAll("second", 1_700_000_100);
        repository.Write("untracked.txt", "u\n");
        var git = new GitValue(repository.Root);
        string[] among = ["a.txt", "missing.txt", "dir/b.txt", "dir", "untracked.txt"];
        var expected = new HashSet<string>(["a.txt", "dir/b.txt", "dir"], StringComparer.Ordinal);
        Assert.Equal(expected, (await git.PresentPaths(among, "HEAD")).ToHashSet(StringComparer.Ordinal));
        Assert.Equal(expected, (await git.TrackedPaths(among)).ToHashSet(StringComparer.Ordinal));
        Assert.Equal(new HashSet<string>(["a.txt"], StringComparer.Ordinal),
            (await git.PresentPaths(among, "HEAD~1")).ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-GIT-022")] // Services/Git/Git.swift:342,347
    public async Task PresentPaths_is_empty_for_no_paths_an_unknown_ref_and_a_failed_process()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var outside = GitRepository.NotARepository();
        var git = new GitValue(repository.Root);
        Assert.Empty(await git.PresentPaths([], "HEAD"));
        Assert.Empty(await git.PresentPaths(["a.txt"], "no-such-ref"));
        Assert.Empty(await new GitValue(outside.Root).PresentPaths(["a.txt"], "HEAD"));
        Assert.Empty(await new GitValue(outside.Root).TrackedPaths(["a.txt"]));
    }

    // ---- hasOperationInProgress (Git.swift:372-379) ----

    [Fact]
    [Trait("Req", "PR-GIT-023")] // Services/Git/Git.swift:372-379
    public async Task HasOperationInProgress_is_false_for_a_quiet_checkout()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        Assert.False(await new GitValue(repository.Root).HasOperationInProgress());
    }

    [Theory]
    [Trait("Req", "PR-GIT-023")] // Services/Git/Git.swift:375-377
    [InlineData("rebase-merge", true)]
    [InlineData("rebase-apply", true)]
    [InlineData("MERGE_HEAD", false)]
    [InlineData("CHERRY_PICK_HEAD", false)]
    [InlineData("REVERT_HEAD", false)]
    [InlineData("BISECT_LOG", false)]
    public async Task HasOperationInProgress_is_true_when_any_of_the_six_markers_exists(string marker, bool isDirectory)
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var path = repository.Child(".git", marker);
        if (isDirectory) Directory.CreateDirectory(path);
        else File.WriteAllBytes(path, Encoding.UTF8.GetBytes(repository.Git("rev-parse", "HEAD")));
        Assert.True(await new GitValue(repository.Root).HasOperationInProgress());
    }

    [Fact]
    [Trait("Req", "PR-GIT-023")] // Services/Git/Git.swift:375
    public async Task HasOperationInProgress_ignores_any_other_file_in_the_git_directory()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        File.WriteAllBytes(repository.Child(".git", "ORIG_HEAD"), Encoding.UTF8.GetBytes(repository.Git("rev-parse", "HEAD")));
        File.WriteAllBytes(repository.Child(".git", "AUTO_MERGE"), Encoding.UTF8.GetBytes(repository.Git("rev-parse", "HEAD")));
        Assert.False(await new GitValue(repository.Root).HasOperationInProgress());
    }

    [Fact]
    [Trait("Req", "PR-GIT-023")] // Services/Git/Git.swift:373-374
    public async Task HasOperationInProgress_counts_a_folder_git_cannot_answer_for_as_busy()
    {
        using var runtime = new TestRuntime();
        using var outside = GitRepository.NotARepository();
        Assert.True(await new GitValue(outside.Root).HasOperationInProgress());
    }

    [Fact]
    [Trait("Req", "PR-GIT-023")] // Services/Git/Git.swift:365-367,373
    public async Task HasOperationInProgress_tests_a_linked_worktrees_own_git_directory()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var linked = repository.Root + "-linked";
        repository.Git("worktree", "add", "-q", "--detach", linked);
        try
        {
            var gitDirectory = GitRepository.GitIn(linked, null, "rev-parse", "--path-format=absolute", "--git-dir").Trimmed();
            File.WriteAllBytes(gitDirectory + "/MERGE_HEAD", Encoding.UTF8.GetBytes(repository.Git("rev-parse", "HEAD")));
            Assert.True(await new GitValue(linked).HasOperationInProgress());
            Assert.False(await new GitValue(repository.Root).HasOperationInProgress());
        }
        finally
        {
            repository.Git("worktree", "remove", "--force", linked);
        }
    }

    // ---- indexPath fast path (Git.swift:517-524) ----

    [Fact]
    [Trait("Req", "PR-GIT-026")] // Services/Git/Git.swift:517-524
    public async Task IndexPath_of_a_plain_checkout_is_its_git_directory_plus_slash_index()
    {
        using var runtime = new TestRuntime();
        Assert.False(LoginEnvironment.Current.ContainsKey("GIT_DIR"));
        Assert.False(LoginEnvironment.Current.ContainsKey("GIT_INDEX_FILE"));
        using var repository = GitRepository.WithCommit();
        Assert.Equal(repository.Root + "\\.git/index", await new GitValue(repository.Root).IndexPath());
    }

    [Fact]
    [Trait("Req", "PR-GIT-026")] // Services/Git/Git.swift:521-523
    public async Task IndexPath_answers_from_the_folder_alone_without_asking_git()
    {
        using var runtime = new TestRuntime();
        // An empty .git folder is no repository git would answer for, so only the stat can produce this answer.
        using var folder = GitRepository.NotARepository();
        Directory.CreateDirectory(folder.Child(".git"));
        Assert.Equal(folder.Root + "\\.git/index", await new GitValue(folder.Root).IndexPath());
    }

    // ---- helpers ----

    /// <summary>After the deadline: steps the manual clock a second at a time (the kill ladder and the drain run on it)
    /// until the process result lands.</summary>
    private static async Task<ShellResult> Finish(TestRuntime runtime, Task<ShellResult> run)
    {
        for (var step = 0; step < 30 && !SpinWait.SpinUntil(() => run.IsCompleted, TimeSpan.FromMilliseconds(500)); step++)
            runtime.Advance(TimeSpan.FromSeconds(1));
        return await run.WaitAsync(GitWaits.Bound);
    }

    private static void Finish(TestRuntime runtime, Task<string> output)
    {
        for (var step = 0; step < 30 && !SpinWait.SpinUntil(() => output.IsCompleted, TimeSpan.FromMilliseconds(500)); step++)
            runtime.Advance(TimeSpan.FromSeconds(1));
        Assert.True(SpinWait.SpinUntil(() => output.IsCompleted, GitWaits.Bound));
    }
}
