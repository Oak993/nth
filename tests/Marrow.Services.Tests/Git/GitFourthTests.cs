using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using GitValue = Marrow.Services.Git.Git;

namespace Marrow.Services.Tests.Git;

// Part 4 of Services.Git.Git: the in-app GitLab merge asks GitLab to remove the source branch (Git.swift:216), while
// the agent-facing merge prompt (Core/Models/MergeRequestLink.swift:50) still does not. glab is the compiled stand-in
// of Fixtures/ForgeFixtures.cs, which records its argv and answers as scripted; no real forge CLI runs. Every test
// installs a TestRuntime, so Shell.run's deadlines and the retry sleeps of Git.swift:224 are on the manual clock.
public sealed class GitFourthTests
{
    private static readonly string[] GitlabMergeVector = ["mr", "merge", "42", "--yes", "--squash", "--remove-source-branch"];

    [Fact]
    [Trait("Req", "PR-GIT-160")] // Services/Git/Git.swift:216
    public async Task The_gitlab_merge_passes_remove_source_branch_as_the_last_argument()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/g/p.git");
        var script = new ForgeScript(repository.Root);
        await new GitValue(repository.Root).MergePullRequest(Link("https://gitlab.com/g/p/-/merge_requests/42"));
        var call = Assert.Single(script.Calls());
        Assert.Equal("glab.exe", call.Exe);
        Assert.Equal(GitlabMergeVector, call.Arguments);
        Assert.Equal("--remove-source-branch", call.Arguments[^1]);
        Assert.Single(call.Arguments, argument => argument == "--remove-source-branch");
    }

    [Fact]
    [Trait("Req", "PR-GIT-160")] // Services/Git/Git.swift:213-224
    public async Task Every_gitlab_retry_keeps_remove_source_branch_last()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/g/p.git");
        var script = new ForgeScript(repository.Root);
        script.Reply(0, stderr: "405 Method Not Allowed\n", status: "1");
        script.Reply(1, stderr: "Request is still checking\n", status: "1");
        var merge = new GitValue(repository.Root).MergePullRequest(Link("https://gitlab.com/g/p/-/merge_requests/42"));
        WaitForRetrySleep(runtime, script, 1);
        runtime.Advance(TimeSpan.FromSeconds(3));
        WaitForRetrySleep(runtime, script, 2);
        runtime.Advance(TimeSpan.FromSeconds(5));
        await merge.WaitAsync(GitWaits.Bound);
        var calls = script.Calls();
        Assert.Equal(3, calls.Count);
        foreach (var call in calls)
        {
            Assert.Equal("glab.exe", call.Exe);
            Assert.Equal(GitlabMergeVector, call.Arguments);
            Assert.Equal("--remove-source-branch", call.Arguments[^1]);
        }
    }

    [Fact]
    [Trait("Req", "PR-GIT-160")] // Services/Git/Git.swift:216,220-223,226
    public async Task A_gitlab_failure_outside_the_retry_test_throws_its_failure_message_after_one_attempt()
    {
        using var runtime = new TestRuntime();
        using var repository = ForgeRepository("https://gitlab.com/g/p.git");
        var script = new ForgeScript(repository.Root);
        script.ReplyAlways(stderr: "  Merge request has conflicts  \n", status: "1");
        var error = await Assert.ThrowsAsync<ShellError>(() =>
            new GitValue(repository.Root).MergePullRequest(Link("https://gitlab.com/g/p/-/merge_requests/42")).WaitAsync(GitWaits.Bound));
        Assert.Equal("Merge request has conflicts", error.ErrorDescription);
        var call = Assert.Single(script.Calls());
        Assert.Equal(GitlabMergeVector, call.Arguments);
    }

    [Fact]
    [Trait("Req", "PR-GIT-160")] // Core/Models/MergeRequestLink.swift:50; Services/Git/Git.swift:216
    public void The_agent_facing_gitlab_prompt_still_names_neither_squash_nor_remove_source_branch()
    {
        var prompt = Link("https://gitlab.com/g/p/-/merge_requests/42").Prompt;
        Assert.Contains(
            "Use glab from this directory: `glab mr merge 42 --yes`. If GitLab answers that the request is still being checked (a 405), wait a few seconds and try again; if it reports conflicts, stop and say so instead of resolving them.",
            prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--squash", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--remove-source-branch", prompt, StringComparison.Ordinal);
    }

    // ---- helpers ----

    private static GitRepository ForgeRepository(string origin)
    {
        var repository = GitRepository.WithCommit();
        repository.Git("remote", "add", "origin", origin);
        return repository;
    }

    private static MergeRequestLink Link(string address) => MergeRequestLink.Create(SwiftURL.Create(address)!)!;

    /// <summary>Waits until the given attempt has exited and the one retry sleep of Git.swift:224 is armed.</summary>
    private static void WaitForRetrySleep(TestRuntime runtime, ForgeScript script, int calls)
    {
        bool Ready() => script.Count == calls && !ForgeStandIns.IsRunning("glab") && runtime.Time.ActiveTimers == 1;
        Assert.True(GitWaits.Eventually(() => Ready() && !SpinWait.SpinUntil(() => !Ready(), TimeSpan.FromMilliseconds(200))),
            "attempt " + calls + " never reached its retry sleep");
    }
}
