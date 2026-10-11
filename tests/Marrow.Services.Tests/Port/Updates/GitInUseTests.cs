using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Updates;

// ED-NEW-providers-git-1: Git's failed row says what holds Git ("Git Bash is running (used by Claude Code). Close it and try again."), and an update or its Retry looks at what holds Git
// before it starts winget. No real process is read here: the look is a test function.
[Trait("Feature", "UE-3")]
[Trait("Decision", "ED-NEW-providers-git-1")]
public sealed class GitInUseTests
{
    private static Func<CancellationToken, Task<IReadOnlyList<GitLockingProcess>>> Look(Func<IReadOnlyList<GitLockingProcess>> now) => _ => Task.FromResult(now());

    [Fact]
    public void The_message_names_what_runs_and_the_apps_that_started_it()
    {
        Assert.Equal(GitInUse.Fallback, GitInUse.Message([]));
        Assert.Equal("Git Bash is running (used by Claude Code). Close it and try again.", GitInUse.Message([new("bash.exe", "Claude Code"), new("tail.exe", "Claude Code"), new("grep.exe", "Claude Code")]));
        Assert.Equal("Git Bash is running. Close it and try again.", GitInUse.Message([new("mintty.exe"), new("bash.exe")]));
        Assert.Equal("Git Bash is running (used by Claude Code and VS Code). Close it and try again.", GitInUse.Message([new("bash.exe", "Claude Code"), new("bash.exe", "VS Code"), new("sh.exe", "claude code")]));
        Assert.Equal("A Git command and ssh-agent are running. Close them and try again.", GitInUse.Message([new("ssh-agent.exe"), new("git-remote-https.exe"), new("git.exe")]));
        Assert.Equal("Git Bash and a Git command are running (used by Cursor). Close them and try again.", GitInUse.Message([new("git.exe", "Cursor"), new("bash.exe", "Cursor")]));
    }

    [Fact]
    public void The_installers_own_list_is_read_from_its_log()
    {
        var log = TrialHFixtures.Result("09-git-installer-log").Stdout;
        Assert.Equal(["tail.exe", "bash.exe", "grep.exe", "timeout.exe", "sleep.exe"], GitInUse.NamesFromLog(log));
        Assert.Empty(GitInUse.NamesFromLog("Installer failed with exit code: 1"));
    }

    [Fact]
    public async Task While_Git_is_held_the_update_says_what_and_never_starts_the_installer()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list");
        var looks = 0;
        var updater = new WingetUpdater("Git.Git", "Git", replay.Run, gitLocks: Look(() => { looks++; return [new("bash.exe", "Claude Code"), new("tail.exe", "Claude Code")]; }));
        var outcome = await updater.Update(new DevToolProgress(), default);
        Assert.False(outcome.Succeeded); Assert.Equal("2.54.0", outcome.NewVersion);
        Assert.Equal("Git Bash is running (used by Claude Code). Close it and try again.", outcome.Message);
        Assert.Equal(1, looks); Assert.DoesNotContain(replay.Calls, c => c.Arguments[0] == "upgrade"); replay.Complete();
    }

    [Fact]
    public async Task Retry_looks_again_before_it_starts_winget_and_runs_it_once_Git_is_free()
    {
        var order = new List<string>();
        var upgrade = TrialHFixtures.Result("07-git-upgrade") with { InstallerLog = TrialHFixtures.Result("09-git-installer-log").Stdout };
        Task<DevToolCommandResult> Run(DevToolCommand command, CancellationToken _)
        {
            order.Add(command.Arguments[0]);
            return Task.FromResult(command.Arguments[0] == "upgrade" ? upgrade : TrialHFixtures.Result("04-git-list"));
        }
        var holding = true;
        var runner = new UpdateOrchestrator([new WingetUpdater("Git.Git", "Git", Run, gitLocks: Look(() => { order.Add("look"); return holding ? [new("bash.exe", "Claude Code")] : []; }))]);
        await runner.UpdateEverything();
        var row = Assert.Single(runner.Rows);
        Assert.Equal((UpdateRowState.Failed, "Git Bash is running (used by Claude Code). Close it and try again."), (row.State, row.Message));
        Assert.DoesNotContain("upgrade", order);
        holding = false; order.Clear();
        await runner.Retry(row.Id);
        Assert.Equal(1, order.Count(step => step == "upgrade"));
        Assert.True(order.IndexOf("look") >= 0 && order.IndexOf("look") < order.IndexOf("upgrade"), string.Join(",", order));
        // The installer still refused (its log lists Git Bash); the live look sees nothing now, so the installer's own list names it.
        row = Assert.Single(runner.Rows);
        Assert.Equal((UpdateRowState.Failed, "Git Bash is running. Close it and try again."), (row.State, row.Message));
    }
}
