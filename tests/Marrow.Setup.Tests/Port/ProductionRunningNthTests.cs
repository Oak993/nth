using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a, unit U4 (ED-NEW-inst-u4-1). The handshake as the screens will make it, on the real process
// list and the real pipe name, aimed at a folder no nth runs in (so nothing is asked of any nth the person may have open), and at a temporary
// folder where a copy of ping.exe called nth.exe runs and has no control pipe at all (an older nth), asked with short waits and never ended.
public sealed class ProductionRunningNthTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task With_no_nth_in_the_root_the_real_handshake_is_clear_and_asks_nothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "nth-production-test-" + Guid.NewGuid().ToString("N"), "nth");
        var nth = ProductionRunningNth.Create(root);

        var status = await nth.AskAsync();

        Assert.Equal(RunningNthState.Clear, status.State);
        Assert.True(status.CanProceed);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_program_in_the_root_that_has_no_control_pipe_gets_the_question_and_is_left_running_until_the_person_says_yes()
    {
        using var install = new TempNthInstall();
        using var process = install.Start(@"current\nth.exe");
        var timings = RunningNthTimings.Default with { StartupPatience = TimeSpan.FromMilliseconds(600), AskRetry = TimeSpan.FromMilliseconds(100) };
        var nth = new RunningNthHandshake(install.Root, new PipeNthControlChannel(FakeNthPipeServer.NewName(), TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(150)), new WindowsRunningNthProcesses(), timings: timings);

        var status = await nth.AskAsync();

        Assert.Equal(RunningNthState.NotResponding, status.State);
        Assert.Equal("no-answer", status.Reason);
        Assert.False(process.HasExited); // nothing is forced

        var closed = await nth.EndItAsync();

        Assert.Equal(RunningNthState.Closed, closed.State);
        Assert.True(process.WaitForExit(2000));
    }
}
