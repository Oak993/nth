using System.Text;
using Marrow.Core.Models;
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
// Part 2 adds commit and publish (Git.swift:156-227), the landing primitives (:267-291, :381-405), the checkpoint ref
// and capture (:409-429) and commitTree (:660-674). Forge CLIs are the scripted stand-ins of Fixtures/ForgeFixtures.cs
// (R-TEST-8); the index-path cache (PR-GIT-027) is GitSecondTests, in its own non-parallel collection.
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

    // ======== Part 2: commit and publish, forges, landing primitives, checkpoints ========

    // ---- workingChanges (Git.swift:156-161) ----

    [Fact]
    [Trait("Req", "PR-GIT-028")] // Services/Git/Git.swift:156-161
    public async Task WorkingChanges_is_status_short_and_the_diff_against_HEAD()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("a.txt", "a\nb\n");
        repository.Write("new.txt", "n\n");
        var (summary, patch) = await new GitValue(repository.Root).WorkingChanges();
        Assert.Equal(" M a.txt\n?? new.txt\n", summary);
        Assert.Equal(Prefixed(repository.Root, "diff", "HEAD", "--no-ext-diff"), patch);
        Assert.StartsWith("diff --git a/a.txt b/a.txt\n", patch, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-GIT-028")] // Services/Git/Git.swift:159
    public async Task An_ascii_patch_over_40000_bytes_keeps_its_first_40000_characters()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("a.txt", string.Concat(Enumerable.Repeat(new string('x', 99) + "\n", 600)));
        var full = Prefixed(repository.Root, "diff", "HEAD", "--no-ext-diff");
        Assert.True(Encoding.UTF8.GetByteCount(full) > 40_000);
        var (_, patch) = await new GitValue(repository.Root).WorkingChanges();
        Assert.Equal(full[..40_000], patch);
    }

    [Fact]
    [Trait("Req", "PR-GIT-028")] // Services/Git/Git.swift:159; porting/upstream_defects.md entry 75
    public async Task The_cut_is_tested_on_utf8_bytes_but_counted_in_characters()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        // U+00E9 LATIN SMALL LETTER E WITH ACUTE: two UTF-8 bytes, one Character.
        var e = "\U000000E9";
        // 30,000 Characters of content, 60,000 bytes: over the byte test, under the Character cut, so nothing is cut.
        repository.Write("a.txt", string.Concat(Enumerable.Repeat(string.Concat(Enumerable.Repeat(e, 99)) + "\n", 300)));
        var full = Prefixed(repository.Root, "diff", "HEAD", "--no-ext-diff");
        Assert.True(Encoding.UTF8.GetByteCount(full) > 40_000);
        Assert.True(new System.Globalization.StringInfo(full).LengthInTextElements < 40_000);
        var (_, patch) = await new GitValue(repository.Root).WorkingChanges();
        Assert.Equal(full, patch);
        Assert.True(Encoding.UTF8.GetByteCount(patch) > 40_000);
    }

    [Fact]
    [Trait("Req", "PR-GIT-028")] // Services/Git/Git.swift:159; porting/upstream_defects.md entry 75
    public async Task A_long_patch_of_combining_marks_keeps_40000_grapheme_clusters_not_40000_UTF16_units()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        // "e" + U+0301 COMBINING ACUTE ACCENT: one Character, two UTF-16 units, three UTF-8 bytes.
        var decomposed = "e\U00000301";
        repository.Write("a.txt", string.Concat(Enumerable.Repeat(string.Concat(Enumerable.Repeat(decomposed, 99)) + "\n", 500)));
        var full = Prefixed(repository.Root, "diff", "HEAD", "--no-ext-diff");
        var expected = new StringBuilder();
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(full);
        for (var count = 0; count < 40_000 && elements.MoveNext(); count++) expected.Append(elements.GetTextElement());
        var (_, patch) = await new GitValue(repository.Root).WorkingChanges();
        Assert.Equal(expected.ToString(), patch);
        Assert.True(Encoding.UTF8.GetByteCount(patch) > 100_000);
        // R-TEST-4: the obvious .NET cut counts UTF-16 units and keeps far fewer Characters.
        Assert.NotEqual(full[..40_000], patch);
    }

    [Fact]
    [Trait("Req", "PR-GIT-028")] // Services/Git/Git.swift:157-158
    public async Task WorkingChanges_outside_a_checkout_is_two_empty_strings()
    {
        using var runtime = new TestRuntime();
        using var folder = GitRepository.NotARepository();
        var (summary, patch) = await new GitValue(folder.Root).WorkingChanges();
        Assert.Equal("", summary);
        Assert.Equal("", patch);
    }

    // ---- commitAll (Git.swift:163-166) ----

    [Fact]
    [Trait("Req", "PR-GIT-029")] // Services/Git/Git.swift:163-166
    public async Task CommitAll_stages_everything_and_commits_the_message_from_stdin_in_the_users_own_identity()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("a.txt", "changed\n");
        repository.Write("dir/new file.txt", "n\n");
        // U+2014 EM DASH in the subject: the message goes to git as its UTF-8 bytes.
        await new GitValue(repository.Root).CommitAll(message: "Subject \U00002014 dash\n\nBody line");
        Assert.Equal("", repository.Git("status", "--porcelain"));
        Assert.Equal("Subject \U00002014 dash\n\nBody line\n", CommitMessage(repository.Root, "HEAD"));
        Assert.Equal("Port Test <port-test@localhost>\nPort Test <port-test@localhost>\n",
            repository.Git("log", "-1", "--format=%an <%ae>%n%cn <%ce>"));
        Assert.Equal("a.txt\ndir/new file.txt\n", repository.Git("-c", "core.quotepath=false", "ls-tree", "-r", "--name-only", "HEAD"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-029")] // Services/Git/Git.swift:165
    public async Task CommitAll_throws_gits_failure_when_there_is_nothing_to_commit()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var head = repository.Git("rev-parse", "HEAD");
        var error = await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).CommitAll(message: "m"));
        Assert.Contains("nothing to commit", error.ErrorDescription, StringComparison.Ordinal);
        Assert.Equal(head, repository.Git("rev-parse", "HEAD"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-029")] // Services/Git/Git.swift:164-165
    public async Task A_rejecting_pre_commit_hook_surfaces_as_the_hooks_stderr_after_everything_was_staged()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write(".git/hooks/pre-commit", "#!/bin/sh\necho 'hook says no' >&2\nexit 1\n");
        repository.Write("new.txt", "n\n");
        var error = await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).CommitAll(message: "m"));
        Assert.Equal("hook says no", error.ErrorDescription);
        Assert.Equal("A  new.txt\n", repository.Git("status", "--porcelain"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-029")] // Services/Git/Git.swift:165; porting/port_impacts.md entry 217
    public async Task The_commit_step_runs_at_the_120_second_default()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write(".git/hooks/pre-commit", "#!/bin/sh\ntouch .git/hook-started\nsleep 600\n");
        repository.Write("new.txt", "n\n");
        var commit = new GitValue(repository.Root).CommitAll(message: "m");
        Assert.True(GitWaits.Eventually(() => File.Exists(repository.Child(".git", "hook-started")) && runtime.Time.ActiveTimers == 1),
            "the hook never started under a deadline");
        await AssertBudget(runtime, commit, 120);
    }

    // ---- push, pushBranch, fetch, pullFastForward (Git.swift:168-170, :285-291, :403-405) ----

    [Fact]
    [Trait("Req", "PR-GIT-030")] // Services/Git/Git.swift:168-170
    public async Task Push_publishes_HEAD_to_origin_and_sets_its_upstream()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var remote = AddBareOrigin(repository);
        await new GitValue(repository.Root).Push();
        Assert.Equal(repository.Git("rev-parse", "HEAD"), GitRepository.GitIn(remote, null, "rev-parse", "refs/heads/main"));
        Assert.Equal("origin\n", repository.Git("config", "branch.main.remote"));
        Assert.Equal("refs/heads/main\n", repository.Git("config", "branch.main.merge"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-030")] // Services/Git/Git.swift:169
    public async Task A_rejected_push_throws_gits_text_and_nothing_is_forced()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var remote = AddBareOrigin(repository);
        repository.Git("push", "-q", "origin", "HEAD");
        repository.Write("a.txt", "remote side\n");
        var remoteHead = repository.CommitAll("remote side", 1_700_000_100);
        repository.Git("push", "-q", "origin", "HEAD");
        repository.Git("reset", "-q", "--hard", "HEAD~1");
        repository.Write("b.txt", "local side\n");
        repository.CommitAll("local side", 1_700_000_200);
        var error = await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).Push());
        Assert.Contains("rejected", error.ErrorDescription, StringComparison.Ordinal);
        Assert.Equal(remoteHead + "\n", GitRepository.GitIn(remote, null, "rev-parse", "refs/heads/main"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-030")] // Services/Git/Git.swift:169
    public async Task Push_has_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        AddBareOrigin(repository);
        repository.Git("config", "remote.origin.receivepack", "sleep 600 #");
        var push = new GitValue(repository.Root).Push();
        await AssertBudgetOnceStarted(runtime, push, 300);
    }

    [Fact]
    [Trait("Req", "PR-GIT-031")] // Services/Git/Git.swift:285-287
    public async Task PushBranch_publishes_the_named_branch_by_refspec_without_moving_HEAD()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var remote = AddBareOrigin(repository);
        repository.Git("switch", "-q", "-c", "topic/one");
        repository.Write("t.txt", "t\n");
        var topic = repository.CommitAll("topic", 1_700_000_100);
        repository.Git("switch", "-q", "main");
        await new GitValue(repository.Root).PushBranch("topic/one");
        Assert.Equal(topic + "\n", GitRepository.GitIn(remote, null, "rev-parse", "refs/heads/topic/one"));
        Assert.Equal("refs/heads/topic/one\n", GitRepository.GitIn(remote, null, "for-each-ref", "--format=%(refname)"));
        Assert.Equal("main\n", repository.Git("branch", "--show-current"));
        Assert.Equal("origin\n", repository.Git("config", "branch.topic/one.remote"));
        Assert.Equal("refs/heads/topic/one\n", repository.Git("config", "branch.topic/one.merge"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-031")] // Services/Git/Git.swift:286
    public async Task PushBranch_throws_for_a_branch_that_does_not_exist()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var remote = AddBareOrigin(repository);
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).PushBranch("absent"));
        Assert.Equal("", GitRepository.GitIn(remote, null, "for-each-ref"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-031")] // Services/Git/Git.swift:286
    public async Task PushBranch_has_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        AddBareOrigin(repository);
        repository.Git("config", "remote.origin.receivepack", "sleep 600 #");
        var push = new GitValue(repository.Root).PushBranch("main");
        await AssertBudgetOnceStarted(runtime, push, 300);
    }

    [Fact]
    [Trait("Req", "PR-GIT-032")] // Services/Git/Git.swift:289-291
    public async Task Fetch_updates_the_origin_tracking_refs()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var remote = AddBareOrigin(repository);
        var head = repository.Git("rev-parse", "HEAD").Trim();
        repository.Git("push", "-q", "origin", "HEAD");
        GitRepository.GitIn(remote, null, "update-ref", "refs/heads/side", head);
        Assert.False(GitRepository.Raw(repository.Root, null, "rev-parse", "--verify", "--quiet", "refs/remotes/origin/side").Succeeded);
        await new GitValue(repository.Root).Fetch();
        Assert.Equal(head + "\n", repository.Git("rev-parse", "refs/remotes/origin/side"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-032")] // Services/Git/Git.swift:290
    public async Task Fetch_throws_without_an_origin()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).Fetch());
    }

    [Fact]
    [Trait("Req", "PR-GIT-032")] // Services/Git/Git.swift:290
    public async Task Fetch_has_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        AddBareOrigin(repository);
        repository.Git("config", "remote.origin.uploadpack", "sleep 600 #");
        var fetch = new GitValue(repository.Root).Fetch();
        await AssertBudgetOnceStarted(runtime, fetch, 300);
    }

    [Fact]
    [Trait("Req", "PR-GIT-033")] // Services/Git/Git.swift:403-405
    public async Task PullFastForward_moves_a_checkout_that_is_behind_its_upstream()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var ahead = BehindUpstream(repository);
        await new GitValue(repository.Root).PullFastForward();
        Assert.Equal(ahead + "\n", repository.Git("rev-parse", "HEAD"));
        Assert.Equal("ahead\n", File.ReadAllText(repository.Child("b.txt")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-033")] // Services/Git/Git.swift:404
    public async Task PullFastForward_refuses_a_diverged_checkout_and_leaves_it_alone()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        BehindUpstream(repository);
        repository.Write("c.txt", "local\n");
        var local = repository.CommitAll("local", 1_700_000_300);
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).PullFastForward());
        Assert.Equal(local + "\n", repository.Git("rev-parse", "HEAD"));
        Assert.Equal("", repository.Git("status", "--porcelain"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-033")] // Services/Git/Git.swift:404
    public async Task PullFastForward_has_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        BehindUpstream(repository);
        repository.Git("config", "remote.origin.uploadpack", "sleep 600 #");
        var pull = new GitValue(repository.Root).PullFastForward();
        await AssertBudgetOnceStarted(runtime, pull, 300);
    }

    // ---- commitWork, commitTree (Git.swift:267-272, :660-674) ----

    [Fact]
    [Trait("Req", "PR-GIT-034")] // Services/Git/Git.swift:267-272; porting/port_impacts.md entry 232
    public async Task CommitWork_writes_a_one_parent_commit_in_the_users_identity_with_the_message_bytes_as_given()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var parent = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("b.txt", "b\n");
        repository.Git("add", "-A");
        var tree = repository.Git("write-tree").Trim();
        // No trailing newline: commit-tree -F - stores stdin as it is, so the bytes show nothing was added.
        var commit = await new GitValue(repository.Root).CommitWork(tree, parent: parent, message: "Land \U00002014 work");
        Assert.Matches("^[0-9a-f]{40}$", commit);
        var lines = repository.Git("cat-file", "commit", commit).Split('\n');
        Assert.Equal("tree " + tree, lines[0]);
        Assert.Equal("parent " + parent, lines[1]);
        Assert.StartsWith("author Port Test <port-test@localhost> ", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("committer Port Test <port-test@localhost> ", lines[3], StringComparison.Ordinal);
        Assert.Equal("", lines[4]);
        Assert.Equal("Land \U00002014 work", string.Join('\n', lines[5..]));
        Assert.Equal(parent + "\n", repository.Git("rev-parse", "HEAD"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-034")] // Services/Git/Git.swift:270
    public async Task CommitWork_throws_for_an_unknown_tree()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var parent = repository.Git("rev-parse", "HEAD").Trim();
        await Assert.ThrowsAsync<ShellError>(() =>
            new GitValue(repository.Root).CommitWork("0123456789012345678901234567890123456789", parent: parent, message: "m"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-035")] // Services/Git/Git.swift:660-674
    public async Task CommitTree_puts_the_tree_on_HEAD_in_the_apps_own_identity_with_no_ref_pointing_at_it()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var head = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("b.txt", "b\n");
        repository.Git("add", "-A");
        var tree = repository.Git("write-tree").Trim();
        var commit = await new GitValue(repository.Root).CommitTree(tree, message: "Start of the copy");
        var lines = repository.Git("cat-file", "commit", commit).Split('\n');
        Assert.Equal("tree " + tree, lines[0]);
        Assert.Equal("parent " + head, lines[1]);
        // DEVIATION OD-0012 F1: "Marrow" <marrow@localhost>, not "Droppy Code" <droppy-code@localhost>.
        Assert.StartsWith("author Nth <nth@localhost> ", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("committer Nth <nth@localhost> ", lines[3], StringComparison.Ordinal);
        Assert.Equal("\nStart of the copy\n", string.Join('\n', lines[4..]));
        Assert.Equal("", repository.Git("for-each-ref", "--points-at", commit));
        Assert.Equal(head + "\n", repository.Git("rev-parse", "HEAD"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-035")] // Services/Git/Git.swift:670
    public async Task CommitTree_in_an_empty_repository_is_parentless()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.Empty();
        repository.Write("a.txt", "a\n");
        repository.Git("add", "-A");
        var tree = repository.Git("write-tree").Trim();
        var commit = await new GitValue(repository.Root).CommitTree(tree, message: "m");
        var lines = repository.Git("cat-file", "commit", commit).Split('\n');
        Assert.Equal("tree " + tree, lines[0]);
        Assert.StartsWith("author Nth <nth@localhost> ", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain(lines, line => line.StartsWith("parent ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-GIT-035")] // Services/Git/Git.swift:672
    public async Task CommitTree_throws_for_an_unknown_tree()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() =>
            new GitValue(repository.Root).CommitTree("0123456789012345678901234567890123456789", message: "m"));
    }

    // ---- updateRef(_:to:expecting:) (Git.swift:278-283) ----

    [Fact]
    [Trait("Req", "PR-GIT-037")] // Services/Git/Git.swift:278-283
    public async Task UpdateRef_expecting_nil_creates_a_ref_that_does_not_exist()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var head = repository.Git("rev-parse", "HEAD").Trim();
        await new GitValue(repository.Root).UpdateRef("refs/heads/new", to: head, expecting: null);
        Assert.Equal(head + "\n", repository.Git("rev-parse", "refs/heads/new"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-037")] // Services/Git/Git.swift:282
    public async Task UpdateRef_expecting_nil_throws_for_a_ref_that_exists_and_leaves_it()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("b.txt", "b\n");
        var second = repository.CommitAll("second", 1_700_000_100);
        repository.Git("branch", "other", first);
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).UpdateRef("refs/heads/other", to: second, expecting: null));
        Assert.Equal(first + "\n", repository.Git("rev-parse", "refs/heads/other"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-037")] // Services/Git/Git.swift:282
    public async Task UpdateRef_moves_a_ref_only_while_it_still_points_at_the_expected_commit()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("b.txt", "b\n");
        var second = repository.CommitAll("second", 1_700_000_100);
        repository.Git("branch", "other", first);
        var git = new GitValue(repository.Root);
        await Assert.ThrowsAsync<ShellError>(() => git.UpdateRef("refs/heads/other", to: second, expecting: second));
        Assert.Equal(first + "\n", repository.Git("rev-parse", "refs/heads/other"));
        await git.UpdateRef("refs/heads/other", to: second, expecting: first);
        Assert.Equal(second + "\n", repository.Git("rev-parse", "refs/heads/other"));
    }

    // ---- resetSoft, resetIndex, checkoutPaths, removePaths (Git.swift:381-401) ----

    [Fact]
    [Trait("Req", "PR-GIT-038")] // Services/Git/Git.swift:381-384
    public async Task ResetSoft_moves_the_branch_and_leaves_index_and_working_tree()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("b.txt", "b\n");
        repository.CommitAll("second", 1_700_000_100);
        await new GitValue(repository.Root).ResetSoft(to: first);
        Assert.Equal(first + "\n", repository.Git("rev-parse", "refs/heads/main"));
        Assert.Equal("A  b.txt\n", repository.Git("status", "--porcelain"));
        Assert.Equal("b\n", File.ReadAllText(repository.Child("b.txt")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-038")] // Services/Git/Git.swift:383
    public async Task ResetSoft_throws_for_an_unknown_commit()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).ResetSoft(to: "no-such-commit"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-039")] // Services/Git/Git.swift:386-390
    public async Task ResetIndex_unstages_only_the_named_paths_from_NUL_separated_stdin()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        // U+00FC LATIN SMALL LETTER U WITH DIAERESIS in a name with a space.
        var odd = "dir/\U000000FC b.txt";
        repository.Write("a.txt", "changed\n");
        repository.Write(odd, "u\n");
        repository.Write("kept.txt", "k\n");
        repository.Git("add", "-A");
        await new GitValue(repository.Root).ResetIndex(paths: ["a.txt", odd]);
        Assert.Equal(" M a.txt\nA  kept.txt\n?? dir/\n", repository.Git("status", "--porcelain"));
        Assert.Equal("changed\n", File.ReadAllText(repository.Child("a.txt")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-039")] // Services/Git/Git.swift:388
    public async Task ResetIndex_of_no_paths_starts_no_process()
    {
        using var runtime = new TestRuntime();
        // Outside a checkout any git process fails, so returning without an error shows none ran.
        using var folder = GitRepository.NotARepository();
        var git = new GitValue(folder.Root);
        await git.ResetIndex(paths: []);
        await Assert.ThrowsAsync<ShellError>(() => git.ResetIndex(paths: ["a.txt"]));
    }

    [Fact]
    [Trait("Req", "PR-GIT-040")] // Services/Git/Git.swift:392-396
    public async Task CheckoutPaths_puts_the_refs_version_into_index_and_working_tree()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = repository.Git("rev-parse", "HEAD").Trim();
        repository.Write("a.txt", "second\n");
        repository.Write("b.txt", "b\n");
        repository.CommitAll("second", 1_700_000_100);
        repository.Write("b.txt", "dirty\n");
        await new GitValue(repository.Root).CheckoutPaths(from: first, paths: ["a.txt"]);
        Assert.Equal("a\n", File.ReadAllText(repository.Child("a.txt")));
        Assert.Equal("M  a.txt\n M b.txt\n", repository.Git("status", "--porcelain"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-040")] // Services/Git/Git.swift:395; porting/upstream_defects.md entry 78
    public async Task CheckoutPaths_passes_paths_as_patterns_without_literal_pathspecs()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("x1.txt", "1\n");
        repository.Write("x2.txt", "2\n");
        repository.CommitAll("xs", 1_700_000_100);
        repository.Write("x1.txt", "dirty\n");
        repository.Write("x2.txt", "dirty\n");
        await new GitValue(repository.Root).CheckoutPaths(from: "HEAD", paths: ["x*.txt"]);
        Assert.Equal("1\n", File.ReadAllText(repository.Child("x1.txt")));
        Assert.Equal("2\n", File.ReadAllText(repository.Child("x2.txt")));
        Assert.Equal("", repository.Git("status", "--porcelain"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-040")] // Services/Git/Git.swift:394-395
    public async Task CheckoutPaths_of_no_paths_starts_no_process_and_an_unmatched_pathspec_throws()
    {
        using var runtime = new TestRuntime();
        using var folder = GitRepository.NotARepository();
        await new GitValue(folder.Root).CheckoutPaths(from: "HEAD", paths: []);
        using var repository = GitRepository.WithCommit();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).CheckoutPaths(from: "HEAD", paths: ["a.txt", "absent.txt"]));
    }

    [Fact]
    [Trait("Req", "PR-GIT-041")] // Services/Git/Git.swift:398-401; porting/upstream_defects.md entry 78
    public async Task RemovePaths_deletes_from_disk_and_index_and_honours_glob_patterns()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("g1.txt", "1\n");
        repository.Write("g2.txt", "2\n");
        repository.Write("keep.txt", "k\n");
        repository.CommitAll("gs", 1_700_000_100);
        await new GitValue(repository.Root).RemovePaths(["a.txt", "g*.txt"]);
        Assert.False(File.Exists(repository.Child("a.txt")));
        Assert.False(File.Exists(repository.Child("g1.txt")));
        Assert.False(File.Exists(repository.Child("g2.txt")));
        Assert.Equal("D  a.txt\nD  g1.txt\nD  g2.txt\n", repository.Git("status", "--porcelain"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-041")] // Services/Git/Git.swift:399-400
    public async Task RemovePaths_of_no_paths_starts_no_process_and_an_untracked_path_throws()
    {
        using var runtime = new TestRuntime();
        using var folder = GitRepository.NotARepository();
        await new GitValue(folder.Root).RemovePaths([]);
        using var repository = GitRepository.WithCommit();
        repository.Write("untracked.txt", "u\n");
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).RemovePaths(["untracked.txt"]));
        Assert.True(File.Exists(repository.Child("untracked.txt")));
        Assert.True(File.Exists(repository.Child("a.txt")));
    }

    // ---- forge (Git.swift:199-206) ----

    [Theory]
    [Trait("Req", "PR-GIT-042")] // Services/Git/Git.swift:199-206; porting/port_impacts.md entry 226
    [InlineData("git@gitlab.com:group/project.git", MergeRequestLink.Forge.Gitlab)]
    [InlineData("https://github.com/owner/repo.git", MergeRequestLink.Forge.Github)]
    [InlineData("https://GitHub.Example.com/owner/repo", MergeRequestLink.Forge.Github)]
    [InlineData("https://GITLAB.acme.dev/g/p", MergeRequestLink.Forge.Gitlab)]
    [InlineData("https://gitlab.github.io/x/y", MergeRequestLink.Forge.Gitlab)]
    [InlineData("https://codeberg.org/owner/repo", MergeRequestLink.Forge.Gitea)]
    [InlineData("https://git.acme.com/owner/repo", MergeRequestLink.Forge.Gitea)]
    public async Task Forge_tests_the_lowercased_origin_for_gitlab_then_github_else_gitea(string origin, MergeRequestLink.Forge expected)
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Git("remote", "add", "origin", origin);
        Assert.Equal(expected, await new GitValue(repository.Root).Forge());
    }

    [Fact]
    [Trait("Req", "PR-GIT-042")] // Services/Git/Git.swift:202,205
    public async Task Forge_without_an_origin_is_the_gitea_family()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        Assert.Equal(MergeRequestLink.Forge.Gitea, await new GitValue(repository.Root).Forge());
        using var folder = GitRepository.NotARepository();
        Assert.Equal(MergeRequestLink.Forge.Gitea, await new GitValue(folder.Root).Forge());
    }

    // ---- createPullRequest (Git.swift:172-197) ----

    [Fact]
    [Trait("Req", "PR-GIT-043")] // Services/Git/Git.swift:177-181
    public async Task CreatePullRequest_on_GitLab_runs_glab_with_yes_before_the_branch_flags()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("git@gitlab.com:group/project.git");
        var script = new ForgeScript(repository.Root);
        var git = new GitValue(repository.Root);
        await git.CreatePullRequest(title: Title, body: Body);
        await git.CreatePullRequest(title: Title, body: Body, source: "feature/x");
        await git.CreatePullRequest(title: Title, body: Body, target: "main");
        await git.CreatePullRequest(title: Title, body: Body, source: "feature/x", target: "main");
        var calls = script.Calls();
        Assert.All(calls, call => Assert.Equal("glab.exe", call.Exe));
        string[] head = ["mr", "create", "--title", Title, "--description", Body, "--yes"];
        Assert.Equal(head, calls[0].Arguments);
        Assert.Equal([.. head, "--source-branch", "feature/x"], calls[1].Arguments);
        Assert.Equal([.. head, "--target-branch", "main"], calls[2].Arguments);
        Assert.Equal([.. head, "--source-branch", "feature/x", "--target-branch", "main"], calls[3].Arguments);
    }

    [Fact]
    [Trait("Req", "PR-GIT-044")] // Services/Git/Git.swift:182-186
    public async Task CreatePullRequest_on_GitHub_runs_gh_with_body_and_no_confirmation_flag()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://github.com/owner/repo.git");
        var script = new ForgeScript(repository.Root);
        var git = new GitValue(repository.Root);
        await git.CreatePullRequest(title: Title, body: Body);
        await git.CreatePullRequest(title: Title, body: Body, source: "feature/x", target: "main");
        var calls = script.Calls();
        Assert.All(calls, call => Assert.Equal("gh.exe", call.Exe));
        string[] head = ["pr", "create", "--title", Title, "--body", Body];
        Assert.Equal(head, calls[0].Arguments);
        Assert.Equal([.. head, "--head", "feature/x", "--base", "main"], calls[1].Arguments);
    }

    [Fact]
    [Trait("Req", "PR-GIT-045")] // Services/Git/Git.swift:187-191
    public async Task CreatePullRequest_on_the_gitea_family_runs_tea_pulls_create_with_description()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://codeberg.org/owner/repo.git");
        var script = new ForgeScript(repository.Root);
        var git = new GitValue(repository.Root);
        await git.CreatePullRequest(title: Title, body: Body);
        await git.CreatePullRequest(title: Title, body: Body, source: "feature/x");
        await git.CreatePullRequest(title: Title, body: Body, target: "main");
        var calls = script.Calls();
        Assert.All(calls, call => Assert.Equal("tea.exe", call.Exe));
        string[] head = ["pulls", "create", "--title", Title, "--description", Body];
        Assert.Equal(head, calls[0].Arguments);
        Assert.Equal([.. head, "--head", "feature/x"], calls[1].Arguments);
        Assert.Equal([.. head, "--base", "main"], calls[2].Arguments);
    }

    [Fact]
    [Trait("Req", "PR-GIT-045")] // Services/Git/Git.swift:187-191; porting/port_impacts.md entry 226
    public async Task An_enterprise_github_host_without_github_in_its_name_is_driven_with_tea()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://git.acme.com/owner/repo.git");
        var script = new ForgeScript(repository.Root);
        await new GitValue(repository.Root).CreatePullRequest(title: "t", body: "b");
        Assert.Equal("tea.exe", Assert.Single(script.Calls()).Exe);
    }

    [Theory]
    [Trait("Req", "PR-GIT-046")] // Services/Git/Git.swift:193-196
    [InlineData("Created!\nhttps://gitlab.com/g/p/-/merge_requests/7\n", "", "https://gitlab.com/g/p/-/merge_requests/7")]
    [InlineData("no address here\n", "see https://github.com/o/r/pull/3 now\n", "https://github.com/o/r/pull/3")]
    [InlineData("first https://one.example/a\n", "https://two.example/b\n", "https://one.example/a")]
    [InlineData("https://out.example/a", "tail", "https://out.example/a")]
    [InlineData("http://plain.example/a https://secure.example/b\tmore", "", "https://secure.example/b")]
    public async Task The_address_is_the_first_https_run_of_non_whitespace_in_stdout_newline_stderr(string stdout, string stderr, string expected)
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://github.com/owner/repo.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stdout: stdout, stderr: stderr);
        var url = await new GitValue(repository.Root).CreatePullRequest(title: "t", body: "b");
        Assert.NotNull(url);
        Assert.Equal(expected, url.AbsoluteString);
    }

    [Fact]
    [Trait("Req", "PR-GIT-046")] // Services/Git/Git.swift:195-196
    public async Task Output_with_no_https_address_answers_nil_and_counts_as_success()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://github.com/owner/repo.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stdout: "Opened http://insecure.example/1\n", stderr: "done\n");
        Assert.Null(await new GitValue(repository.Root).CreatePullRequest(title: "t", body: "b"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-046")] // Services/Git/Git.swift:193
    public async Task A_failed_forge_cli_throws_its_failure_message_before_any_address_is_read()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("git@gitlab.com:group/project.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stdout: "https://gitlab.com/g/p/-/merge_requests/1\n", stderr: "  glab: not authenticated  \n", status: "1");
        var error = await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).CreatePullRequest(title: "t", body: "b"));
        Assert.Equal("glab: not authenticated", error.ErrorDescription);
    }

    // ---- mergePullRequest (Git.swift:208-227) ----

    [Theory]
    [Trait("Req", "PR-GIT-047")] // Services/Git/Git.swift:211,216-218
    [InlineData("https://gitlab.com/group/project/-/merge_requests/42", "glab.exe", new[] { "mr", "merge", "42", "--yes", "--squash", "--remove-source-branch" })]
    [InlineData("https://github.com/owner/repo/pull/7", "gh.exe", new[] { "pr", "merge", "7", "--merge" })]
    [InlineData("https://codeberg.org/owner/repo/pulls/9", "tea.exe", new[] { "pulls", "merge", "9" })]
    public async Task MergePullRequest_runs_the_links_own_forge_cli_with_its_merge_vector(string address, string exe, string[] arguments)
    {
        using var runtime = new TestRuntime();
        // The origin names another forge: the link's forge decides, not the remote.
        using var repository = ForgeRepository("https://example.org/elsewhere.git");
        var script = new ForgeScript(repository.Root);
        await new GitValue(repository.Root).MergePullRequest(Link(address));
        var call = Assert.Single(script.Calls());
        Assert.Equal(exe, call.Exe);
        Assert.Equal(arguments, call.Arguments);
    }

    // PR-GIT-051 at the merge call site: a forge CLI missing from the PATH throws the exact sentence from the FIRST attempt, before any retry
    // sleep (Git.swift:234-247: runTool is called outside any catch, so the GitLab retry loop never sees it). GitLab is the only forge that retries.
    [Theory]
    [Trait("Req", "PR-GIT-051")] // Services/Git/Git.swift:231-248 with Core/Support/Shell.swift:129-130
    [InlineData("https://gitlab.com/group/project/-/merge_requests/42", "glab")]
    [InlineData("https://github.com/owner/repo/pull/7", "gh")]
    [InlineData("https://codeberg.org/owner/repo/pulls/9", "tea")]
    public async Task MergePullRequest_throws_the_not_found_sentence_before_any_retry_for_each_forge(string address, string tool)
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://example.org/elsewhere.git");
        using var emptyPath = EmptyLoginPath.Begin();

        var error = await Assert.ThrowsAsync<ShellError>(() => new GitValue(repository.Root).MergePullRequest(Link(address)).WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Equal(tool + " was not found on your PATH.", error.Message);
        Assert.Equal(0, runtime.Time.ActiveTimers); // no sleep was ever started: nothing was retried
    }

    [Fact]
    [Trait("Req", "PR-GIT-048")] // Services/Git/Git.swift:213-226; porting/upstream_defects.md entry 76
    public async Task A_gitlab_merge_that_keeps_answering_checking_is_tried_six_times_sleeping_3_5_7_9_11_and_a_wasted_13()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/g/p.git");
        var script = new ForgeScript(repository.Root);
        for (var call = 0; call < 6; call++)
            script.Reply(call, stderr: "Request is still checking (" + (call + 1) + ")\n", status: "1");
        var merge = new GitValue(repository.Root).MergePullRequest(Link("https://gitlab.com/g/p/-/merge_requests/5"));
        int[] delays = [3, 5, 7, 9, 11];
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            WaitForRetrySleep(runtime, script, attempt + 1, delays[attempt]);
            runtime.Advance(TimeSpan.FromSeconds(delays[attempt]) - TimeSpan.FromTicks(1));
            var seen = attempt + 1;
            Assert.False(SpinWait.SpinUntil(() => script.Count > seen, TimeSpan.FromMilliseconds(300)), "retried early");
            runtime.Advance(TimeSpan.FromTicks(1));
        }
        WaitForRetrySleep(runtime, script, 6, 13);
        // UPSTREAM DEFECT 76: the sixth failure still sleeps 3 + 5 * 2 seconds before the error is thrown.
        runtime.Advance(TimeSpan.FromSeconds(13) - TimeSpan.FromTicks(1));
        Assert.False(SpinWait.SpinUntil(() => merge.IsCompleted, TimeSpan.FromMilliseconds(300)), "threw before the last sleep ended");
        runtime.Advance(TimeSpan.FromTicks(1));
        var error = await Assert.ThrowsAsync<ShellError>(() => merge.WaitAsync(GitWaits.Bound));
        Assert.Equal("Request is still checking (6)", error.ErrorDescription);
        Assert.Equal(6, script.Count);
        Assert.Equal(TimeSpan.FromSeconds(48), runtime.Time.Elapsed);
    }

    [Fact]
    [Trait("Req", "PR-GIT-048")] // Services/Git/Git.swift:220,223-224
    public async Task A_gitlab_merge_that_is_not_allowed_yet_returns_once_a_retry_succeeds()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/g/p.git");
        var script = new ForgeScript(repository.Root);
        script.Reply(0, stderr: "405 Method Not Allowed\n", status: "1");
        script.Reply(1, stdout: "Merge NOT ALLOWED yet\n", status: "1");
        var merge = new GitValue(repository.Root).MergePullRequest(Link("https://gitlab.com/g/p/-/merge_requests/5"));
        WaitForRetrySleep(runtime, script, 1, 3);
        runtime.Advance(TimeSpan.FromSeconds(3));
        WaitForRetrySleep(runtime, script, 2, 5);
        runtime.Advance(TimeSpan.FromSeconds(5));
        await merge.WaitAsync(GitWaits.Bound);
        Assert.Equal(3, script.Count);
    }

    [Fact]
    [Trait("Req", "PR-GIT-048")] // Services/Git/Git.swift:222-223; porting/upstream_defects.md entry 77
    public async Task The_retry_test_reads_stdout_and_stderr_joined_with_no_separator_so_405_can_span_them()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/g/p.git");
        var script = new ForgeScript(repository.Root);
        script.Reply(0, stdout: "code 40", stderr: "5", status: "1");
        var merge = new GitValue(repository.Root).MergePullRequest(Link("https://gitlab.com/g/p/-/merge_requests/5"));
        WaitForRetrySleep(runtime, script, 1, 3);
        runtime.Advance(TimeSpan.FromSeconds(3));
        await merge.WaitAsync(GitWaits.Bound);
        Assert.Equal(2, script.Count);
    }

    [Fact]
    [Trait("Req", "PR-GIT-048")] // Services/Git/Git.swift:223,226
    public async Task A_gitlab_failure_without_a_retry_word_throws_at_once()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/g/p.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stderr: "merge conflicts\n", status: "1");
        var error = await Assert.ThrowsAsync<ShellError>(() =>
            new GitValue(repository.Root).MergePullRequest(Link("https://gitlab.com/g/p/-/merge_requests/5")).WaitAsync(GitWaits.Bound));
        Assert.Equal("merge conflicts", error.ErrorDescription);
        Assert.Equal(1, script.Count);
        Assert.Equal(TimeSpan.Zero, runtime.Time.Elapsed);
    }

    [Theory]
    [Trait("Req", "PR-GIT-048")] // Services/Git/Git.swift:223
    [InlineData("https://github.com/owner/repo/pull/7")]
    [InlineData("https://codeberg.org/owner/repo/pulls/9")]
    public async Task Only_gitlab_is_retried_even_when_the_output_says_405(string address)
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://example.org/x.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stderr: "HTTP 405: not allowed while checking\n", status: "1");
        var error = await Assert.ThrowsAsync<ShellError>(() =>
            new GitValue(repository.Root).MergePullRequest(Link(address)).WaitAsync(GitWaits.Bound));
        Assert.Equal("HTTP 405: not allowed while checking", error.ErrorDescription);
        Assert.Equal(1, script.Count);
    }

    [Fact]
    [Trait("Req", "PR-GIT-048")] // Services/Git/Git.swift:224; Core/Support/Shell.swift:69
    public async Task A_cancelled_retry_sleep_is_swallowed_and_the_next_attempt_ends_the_call_with_cancellation()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/g/p.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stderr: "405\n", status: "1");
        var git = new GitValue(repository.Root);
        var link = Link("https://gitlab.com/g/p/-/merge_requests/5");
        var handle = TaskHandle.DetachedThrowing(async () => { await git.MergePullRequest(link); return true; });
        WaitForRetrySleep(runtime, script, 1, 3);
        handle.Cancel();
        await Assert.ThrowsAsync<CancellationError>(() => handle.Value.WaitAsync(GitWaits.Bound));
        Assert.Equal(1, script.Count);
        Assert.Equal(TimeSpan.Zero, runtime.Time.Elapsed);
    }

    // ---- the forge CLIs' process (Git.swift:181,186,191,216-218) ----

    [Fact]
    [Trait("Req", "PR-GIT-050")] // Services/Git/Git.swift:181,186,191,216-218; Core/Support/Shell.swift:56,121-133
    public async Task Forge_clis_get_no_config_prefix_no_git_variables_and_stdin_at_the_null_device_in_the_directory()
    {
        using var runtime = new TestRuntime();
        Assert.False(LoginEnvironment.Current.ContainsKey("GIT_TERMINAL_PROMPT"));
        Assert.False(LoginEnvironment.Current.ContainsKey("GIT_OPTIONAL_LOCKS"));
        using var repository = ForgeRepository("git@gitlab.com:group/project.git");
        var script = new ForgeScript(repository.Root);
        var git = new GitValue(repository.Root);
        await git.CreatePullRequest(title: "t", body: "b");
        await git.MergePullRequest(Link("https://github.com/owner/repo/pull/7"));
        var calls = script.Calls();
        Assert.Equal(2, calls.Count);
        foreach (var call in calls)
        {
            Assert.NotEqual("-c", call.Arguments[0]);
            Assert.Equal("<unset>", call.TerminalPrompt);
            Assert.Equal("<unset>", call.OptionalLocks);
            Assert.Equal("char", call.Stdin);
            Assert.Equal(repository.Root, call.WorkingDirectory);
        }
    }

    [Fact]
    [Trait("Req", "PR-GIT-050")] // Services/Git/Git.swift:181
    public async Task CreatePullRequest_has_a_300_second_budget()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://github.com/owner/repo.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(status: "stall");
        var create = new GitValue(repository.Root).CreatePullRequest(title: "t", body: "b");
        Assert.True(GitWaits.Eventually(() => script.Count == 1 && runtime.Time.ActiveTimers == 1), "the stand-in never started");
        await AssertBudget(runtime, create, 300);
    }

    [Fact]
    [Trait("Req", "PR-GIT-050")] // Services/Git/Git.swift:216-218
    public async Task MergePullRequest_has_a_300_second_budget_per_attempt()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://example.org/x.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(status: "stall");
        var merge = new GitValue(repository.Root).MergePullRequest(Link("https://codeberg.org/owner/repo/pulls/9"));
        Assert.True(GitWaits.Eventually(() => script.Count == 1 && runtime.Time.ActiveTimers == 1), "the stand-in never started");
        await AssertBudget(runtime, merge, 300);
    }

    // ---- checkpoints (Git.swift:409-429) ----

    [Fact]
    [Trait("Req", "PR-GIT-056")] // Services/Git/Git.swift:409-411; porting/port_impacts.md entry 220
    public void CheckpointRef_is_the_droppy_code_namespace_with_the_lowercased_hyphenated_thread_id()
    {
        var thread = SwiftUUID.Parse("3F2EA2C1-9B4D-4A77-9A2E-77C0F2B9D001")!.Value;
        Assert.Equal("refs/droppy-code/checkpoints/3f2ea2c1-9b4d-4a77-9a2e-77c0f2b9d001/0-start",
            GitValue.CheckpointRef(thread: thread, turn: 0, phase: "start"));
        Assert.Equal("refs/droppy-code/checkpoints/3f2ea2c1-9b4d-4a77-9a2e-77c0f2b9d001/12-end",
            GitValue.CheckpointRef(thread: thread, turn: 12, phase: "end"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-058")] // Services/Git/Git.swift:416-429; porting/port_impacts.md entry 233
    public async Task CaptureCheckpoint_writes_a_parentless_commit_of_the_working_tree_in_the_apps_identity_and_points_the_ref_at_it()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("a.txt", "changed\n");
        repository.Write("new.txt", "n\n");
        repository.Write(".gitignore", "ignored.txt\n");
        repository.Write("ignored.txt", "i\n");
        var index = File.ReadAllBytes(repository.Child(".git", "index"));
        var status = repository.Git("status", "--porcelain");
        var head = repository.Git("rev-parse", "HEAD");
        var expectedTree = WorkingTree(repository);
        var @ref = GitValue.CheckpointRef(thread: Guid.NewGuid(), turn: 0, phase: "start");
        await new GitValue(repository.Root).CaptureCheckpoint(@ref);
        var commit = repository.Git("rev-parse", @ref).Trim();
        var lines = repository.Git("cat-file", "commit", commit).Split('\n');
        Assert.Equal("tree " + expectedTree, lines[0]);
        // DEVIATION OD-0012 F1, renamed by OD-0135: "Nth" <nth@localhost> and "Nth checkpoint". No parent line: lines[1] is the author.
        Assert.StartsWith("author Nth <nth@localhost> ", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("committer Nth <nth@localhost> ", lines[2], StringComparison.Ordinal);
        Assert.Equal("\nNth checkpoint\n", string.Join('\n', lines[3..]));
        Assert.Equal(head, repository.Git("rev-parse", "HEAD"));
        Assert.Equal(status, repository.Git("status", "--porcelain"));
        Assert.Equal(index, File.ReadAllBytes(repository.Child(".git", "index")));
    }

    [Fact]
    [Trait("Req", "PR-GIT-058")] // Services/Git/Git.swift:416-429
    public async Task CaptureCheckpoint_works_before_the_first_commit()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.Empty();
        repository.Write("a.txt", "a\n");
        var @ref = GitValue.CheckpointRef(thread: Guid.NewGuid(), turn: 1, phase: "end");
        await new GitValue(repository.Root).CaptureCheckpoint(@ref);
        var lines = repository.Git("cat-file", "commit", @ref).Split('\n');
        Assert.StartsWith("tree ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("author Nth <nth@localhost> ", lines[1], StringComparison.Ordinal);
        Assert.Equal("a\n", repository.Git("cat-file", "-p", @ref + ":a.txt"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-058")] // Services/Git/Git.swift:417-428
    public async Task A_tree_the_watch_already_holds_a_commit_for_reuses_that_commit_and_runs_no_commit_tree()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        repository.Write("a.txt", "changed\n");
        var git = new GitValue(repository.Root);
        var first = GitValue.CheckpointRef(thread: Guid.NewGuid(), turn: 0, phase: "start");
        await git.CaptureCheckpoint(first);
        var tree = WorkingTree(repository);
        Assert.Equal(tree, repository.Git("rev-parse", first + "^{tree}").Trim());
        // :420-421: a commit the watch remembers for this tree is taken as it is. HEAD's commit, which no checkpoint
        // could have produced (it has a parent and the test identity), stands in for it, so only reuse can put it on
        // the second ref.
        var head = repository.Git("rev-parse", "HEAD").Trim();
        var watch = WorkingTreeWatch.Shared(repository.Root, repository.Child(".git"));
        Assert.NotNull(watch);
        Assert.NotNull(watch.CommitOf(tree));
        watch.Remember(head, tree);
        var second = GitValue.CheckpointRef(thread: Guid.NewGuid(), turn: 1, phase: "end");
        await git.CaptureCheckpoint(second);
        Assert.Equal(head, repository.Git("rev-parse", second).Trim());
    }

    [Fact]
    [Trait("Req", "PR-GIT-058")] // Services/Git/Git.swift:417
    public async Task CaptureCheckpoint_throws_outside_a_checkout()
    {
        using var runtime = new TestRuntime();
        using var folder = GitRepository.NotARepository();
        await Assert.ThrowsAsync<ShellError>(() => new GitValue(folder.Root).CaptureCheckpoint("refs/droppy-code/checkpoints/x/0-start"));
    }

    // ---- part 2 helpers ----

    // A title and body with a quote, a space and a line break (U+2014 EM DASH too): each must arrive as one argv element.
    private const string Title = "Add \"quoted\" title";
    private const string Body = "Line one\nLine two \U00002014 done";

    /// <summary>What git answers with the app's two config pairs, to compare a whole patch byte for byte.</summary>
    private static string Prefixed(string directory, params string[] arguments) =>
        GitRepository.GitIn(directory, null, ["-c", "core.quotepath=false", "-c", "color.ui=false", .. arguments]);

    /// <summary>The message of <paramref name="commit"/>: the raw object after its first blank line.</summary>
    private static string CommitMessage(string directory, string commit)
    {
        var raw = GitRepository.GitIn(directory, null, "cat-file", "commit", commit);
        return raw[(raw.IndexOf("\n\n", StringComparison.Ordinal) + 2)..];
    }

    /// <summary>A bare repository beside the checkout, added as its origin; returns its path.</summary>
    private static string AddBareOrigin(GitRepository repository)
    {
        var remote = repository.Root + "-remote.git";
        GitRepository.GitIn(repository.Root, null, "init", "-q", "--bare", "-b", "main", remote);
        repository.Git("remote", "add", "origin", remote);
        return remote;
    }

    /// <summary>A checkout one commit behind origin/main, tracking it; returns the upstream's commit.</summary>
    private static string BehindUpstream(GitRepository repository)
    {
        AddBareOrigin(repository);
        repository.Write("b.txt", "ahead\n");
        var ahead = repository.CommitAll("ahead", 1_700_000_100);
        repository.Git("push", "-q", "-u", "origin", "HEAD");
        repository.Git("reset", "-q", "--hard", "HEAD~1");
        repository.Git("update-ref", "refs/remotes/origin/main", "HEAD");
        return ahead;
    }

    /// <summary>A checkout with <paramref name="origin"/> as its origin remote, for the forge stand-ins.</summary>
    private static GitRepository ForgeRepository(string origin)
    {
        var repository = GitRepository.WithCommit();
        repository.Git("remote", "add", "origin", origin);
        return repository;
    }

    private static MergeRequestLink Link(string address) => MergeRequestLink.Create(SwiftURL.Create(address)!)!;

    /// <summary>The working tree as git's own tree object, through a throwaway index seeded from HEAD.</summary>
    private static string WorkingTree(GitRepository repository)
    {
        var index = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = repository.Child(".git", "expected-index") };
        GitRepository.GitIn(repository.Root, index, "read-tree", "HEAD");
        GitRepository.GitIn(repository.Root, index, "add", "-A", "--", ".");
        var tree = GitRepository.GitIn(repository.Root, index, "write-tree").Trim();
        File.Delete(repository.Child(".git", "expected-index"));
        return tree;
    }

    /// <summary>Joins this attempt's retry deadline, rather than a process deadline or another test's executable.</summary>
    private static void WaitForRetrySleep(TestRuntime runtime, ForgeScript script, int calls, int seconds)
    {
        var due = runtime.Time.Elapsed + TimeSpan.FromSeconds(seconds);
        Assert.True(GitWaits.Eventually(() => script.Count == calls && runtime.Time.TimerDeadlines.Contains(due)),
            "attempt " + calls + " never reached its retry sleep");
    }

    /// <summary>Waits for the process's deadline to be armed, then checks the budget.</summary>
    private static async Task AssertBudgetOnceStarted(TestRuntime runtime, Task call, int seconds)
    {
        Assert.True(GitWaits.Eventually(() => runtime.Time.ActiveTimers == 1), "no deadline was armed");
        // Let the pack command start under the deadline before the clock moves.
        Assert.False(SpinWait.SpinUntil(() => call.IsCompleted, TimeSpan.FromMilliseconds(500)));
        await AssertBudget(runtime, call, seconds);
    }

    /// <summary>
    /// The armed deadline is <paramref name="seconds"/>: one tick short the call still runs, at the deadline the process
    /// is ended and the call throws the failure as a ShellError.
    /// </summary>
    private static async Task AssertBudget(TestRuntime runtime, Task call, int seconds)
    {
        runtime.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromTicks(1));
        Assert.False(SpinWait.SpinUntil(() => call.IsCompleted, TimeSpan.FromMilliseconds(300)), "ended before the budget");
        runtime.Advance(TimeSpan.FromTicks(1));
        for (var step = 0; step < 30 && !SpinWait.SpinUntil(() => call.IsCompleted, TimeSpan.FromMilliseconds(500)); step++)
            runtime.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ShellError>(() => call.WaitAsync(GitWaits.Bound));
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
