using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Updates;

// ED-NEW-providers-git-1: Git's failed row says what holds Git ("Git Bash is running (used by Claude Code). Close it and try again."), and an update or its Retry looks at what holds Git
// before it starts winget. No real process is read here: the look is a test function.
[Trait("Feature", "UE-3")]
[Trait("Decision", "ED-NEW-reltrace-1")] // ED-NEW-providers-git-1
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
    // ED-NEW-providersmerge-git-1
    public void An_agent_keeps_its_process_name_at_the_start_of_the_sentence()
    {
        Assert.Equal("ssh-agent is running. Close it and try again.", GitInUse.Message([new("ssh-agent.exe")]));
        Assert.Equal("gpg-agent and keyboxd are running. Close them and try again.", GitInUse.Message([new("keyboxd.exe"), new("gpg-agent.exe")]));
        Assert.Equal("dirmngr, gpg-agent and scdaemon are running (used by VS Code). Close them and try again.", GitInUse.Message([new("scdaemon.exe", "VS Code"), new("gpg-agent.exe"), new("dirmngr.exe")]));
        Assert.Equal("A Git command is running. Close it and try again.", GitInUse.Message([new("git.exe")]));
        Assert.Equal("A Git command and ssh-agent are running. Close them and try again.", GitInUse.Message([new("ssh-agent.exe"), new("git.exe")]));
        Assert.Equal("Git Bash and ssh-agent are running. Close them and try again.", GitInUse.Message([new("ssh-agent.exe"), new("bash.exe")]));
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
        var steps = new List<string>();
        var updater = new WingetUpdater("Git.Git", "Git", replay.Run, gitLocks: Look(() => { steps.Add("look"); return [new("bash.exe", "Claude Code", 4100, 77), new("tail.exe", "Claude Code", 4200, 78)]; }),
            pause: (wait, _) => { steps.Add("pause " + wait.TotalMilliseconds); return Task.CompletedTask; });
        var outcome = await updater.Update(new DevToolProgress(), default);
        Assert.False(outcome.Succeeded); Assert.Equal("2.54.0", outcome.NewVersion);
        Assert.Equal("Git Bash is running (used by Claude Code). Close it and try again.", outcome.Message);
        // ED-NEW-providersmerge-git-2: it looks, waits, and looks again; both looks see the same processes, so nothing is started
        Assert.Equal(["look", "pause 1750", "look"], steps); Assert.InRange(WingetUpdater.SecondLook.TotalSeconds, 1.5, 2);
        Assert.DoesNotContain(replay.Calls, c => c.Arguments[0] == "upgrade"); replay.Complete();
    }

    private static async Task<(UpdateOutcome Outcome, List<string> Order)> TwoLooks(IReadOnlyList<GitLockingProcess> first, IReadOnlyList<GitLockingProcess> second)
    {
        var order = new List<string>();
        var looks = 0;
        Task<DevToolCommandResult> Run(DevToolCommand command, CancellationToken _)
        {
            order.Add(command.Arguments[0]);
            return Task.FromResult(command.Arguments[0] == "upgrade" ? TrialHFixtures.Result("07-git-upgrade") : TrialHFixtures.Result("04-git-list"));
        }
        var updater = new WingetUpdater("Git.Git", "Git", Run, gitLocks: Look(() => { order.Add("look"); return looks++ == 0 ? first : second; }),
            pause: (_, _) => { order.Add("pause"); return Task.CompletedTask; });
        return (await updater.Update(new DevToolProgress(), default), order);
    }

    [Fact]
    // ED-NEW-providersmerge-git-2
    public async Task A_Git_process_that_is_gone_at_the_second_look_does_not_stop_the_update()
    {
        // nth's own source control ran git.exe, an agent's Bash tool ran bash.exe: both exit before the second look, and winget runs
        var (outcome, order) = await TwoLooks([new("git.exe", "nth", 5100, 900), new("bash.exe", "Claude Code", 5200, 901)], []);
        Assert.Equal(["list", "look", "pause", "look", "upgrade", "list"], order);
        Assert.DoesNotContain("running", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    // ED-NEW-providersmerge-git-2
    public async Task Only_a_process_seen_at_both_looks_holds_Git()
    {
        // the first git.exe exited and a new one reused its id (another start time); a bash.exe started after the first look: neither held Git the whole time
        var (outcome, order) = await TwoLooks([new("git.exe", "nth", 5100, 900)], [new("git.exe", "nth", 5100, 950), new("bash.exe", "Claude Code", 5300, 960)]);
        Assert.Contains("upgrade", order);
        Assert.DoesNotContain("running", outcome.Message, StringComparison.Ordinal);
        // the same ssh-agent at both looks (its host is no longer seen) holds Git: only it is named, and winget never starts
        (outcome, order) = await TwoLooks([new("ssh-agent.exe", "Windows Terminal", 6100, 10), new("git.exe", "nth", 6200, 11)], [new("ssh-agent.exe", null, 6100, 10)]);
        Assert.Equal(["list", "look", "pause", "look"], order);
        Assert.False(outcome.Succeeded);
        Assert.Equal("ssh-agent is running. Close it and try again.", outcome.Message);
    }

    [Fact]
    // ED-NEW-providersmerge-git-2
    public void The_same_process_is_the_same_id_start_time_and_executable()
    {
        GitLockingProcess a = new("bash.exe", "Claude Code", 42, 1000);
        Assert.True(a.Same(new("BASH.EXE", null, 42, 1000)));
        Assert.False(a.Same(new("bash.exe", "Claude Code", 42, 1001)));
        Assert.False(a.Same(new("bash.exe", "Claude Code", 43, 1000)));
        Assert.False(a.Same(new("sh.exe", "Claude Code", 42, 1000)));
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
