using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// F.ProcessLauncher, part 1: the three termination shapes of probe P6 (porting/probes/p6/RESULT.md, "Use three
// shapes, not one"), driven under the manual clock (R-TEST-5). Each is transcribed from the Swift:
//
//   Shell      Shell.swift:110-118 (identical at MCPHub.swift:286-295 and MCPProbe.swift:227-236)
//              guard process.isRunning else { return }
//              process.terminate()
//              DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 1) {
//                  guard process.isRunning else { return }
//                  kill(getpgid(pid) == pid ? -pid : pid, SIGKILL)          the whole group
//              }
//   Stdio      StdioProcess.swift:119-128
//              writeQueue.async { try? handle.close() }                       stdin first, running or not
//              guard process.isRunning else { return }
//              process.terminate()
//              DispatchQueue.global().asyncAfter(deadline: .now() + 3) {
//                  if process.isRunning { kill(process.processIdentifier, SIGKILL) }   the pid alone
//              }
//   TerminateAll  MCPHub.swift:547-556 (MCPLiveProcesses.terminateAll, called at quit, AppDelegate.swift:54)
//              let running = processes.filter(\.isRunning)
//              for process in running { process.terminate() }
//              for process in running where process.isRunning { kill(group or pid, SIGKILL) }   no gap
//
// Windows forms (ED-047, ED-027, PA-0007): terminate() is the graceful step (CTRL_BREAK, or 0x03 into a pty);
// the Shell and TerminateAll kill is TerminateJobObject (the tree, the -pid group kill); the Stdio kill is
// TerminateProcess on that pid alone. R-PROC-1: the delays go through GlobalQueue.AsyncAfter, so SwiftClock drives
// them and a test moves time with TestRuntime.Advance.
//
// CONFLICT, reported to the orchestrator (STOP F.ProcessLauncher #1): for the Stdio graceful step ED-027 says
// "Step 2 becomes Node's subprocess.kill(), which on Windows is TerminateProcess with exit code 1", while ED-047 (b)
// says "graceful step = CTRL_BREAK_EVENT to that child only". These tests follow ED-047 because (1) ED-047 is the
// decision on the termination ladders themselves (Q127 [TERM]) and names StdioProcess.terminate line by line;
// (2) ED-027 predates ADR-0002 and names a Node API, and IMPLEMENTING.md 1.6 keeps only the intent of such
// decisions: ED-027's stated intent is "preserve all three steps and the 3-second timing", which a
// TerminateProcess step 2 defeats (step 3 becomes a no-op); (3) probe P6 measured CTRL_BREAK ending a cooperating
// pipe child (M3) and the callers depend on the graceful step being graceful (CommandCodeSession.swift:232-234).
// The orchestrator is asked to confirm, or to amend one of the two decisions. The fake here cannot tell the two
// readings apart (its Terminate is one call); ProcessLauncherSpawnTests and ProcessLauncherSignalTests can.
//
// The surface these tests assume, in namespace Marrow.Core.Port:
//   interface IProcessControl { bool IsRunning { get; } void Terminate(); void KillTree(); void KillProcess(); }
//       (ProcessLauncher implements it; FakeProcessControl in Fixtures/ProcessSamples.cs stands in here)
//   static TerminationLadder.Shell(IProcessControl process)
//   static TerminationLadder.Stdio(IProcessControl process, Action closeInput)
//   static TerminationLadder.TerminateAll(IReadOnlyList<IProcessControl> processes)
public sealed class ProcessLauncherLadderTests
{
    private static readonly TimeSpan PoolWait = TimeSpan.FromSeconds(10);

    // ---- Shell: 1 s, then the tree -------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Shell_ladder_leaves_a_process_that_is_not_running_alone_and_arms_no_timer()
    {
        using var runtime = new TestRuntime();
        var process = FakeProcessControl.Exited();
        TerminationLadder.Shell(process); // Shell.swift:111
        Assert.Equal(["isRunning"], process.Calls);
        Assert.Equal(0, runtime.Time.ActiveTimers);
        runtime.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(["isRunning"], process.Calls);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Shell_ladder_signals_at_once_and_kills_the_whole_tree_exactly_one_second_later()
    {
        using var runtime = new TestRuntime();
        var process = new FakeProcessControl();
        TerminationLadder.Shell(process);
        // Shell.swift:112: terminate() runs inline, before the ladder returns; :113 arms one timer.
        Assert.Equal(["isRunning", "terminate"], process.Calls);
        Assert.Equal(1, runtime.Time.ActiveTimers);
        runtime.Advance(TimeSpan.FromMilliseconds(999));
        // ManualTimeProvider drops a timer synchronously when it fires, so a count of one proves the kill timer has
        // not fired at 999 ms, even though a fired timer's work might still be queued on the pool (Shell.swift:113).
        Assert.Equal(1, runtime.Time.ActiveTimers);
        Assert.Equal(["isRunning", "terminate"], process.Calls);
        runtime.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(process.WaitFor("killTree", 1, PoolWait));
        // Shell.swift:114-116: guard isRunning, then the group kill (ED-047 (a): TerminateJobObject), never the pid alone.
        Assert.Equal(["isRunning", "terminate", "isRunning", "killTree"], process.Calls);
        Assert.Equal(0, process.Count("killProcess"));
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Shell_ladder_does_not_kill_a_process_that_exited_within_the_second()
    {
        using var runtime = new TestRuntime();
        var process = new FakeProcessControl();
        TerminationLadder.Shell(process);
        runtime.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(1, runtime.Time.ActiveTimers);
        process.Exit();
        runtime.Advance(TimeSpan.FromMilliseconds(600));
        Assert.True(process.WaitFor("isRunning", 2, PoolWait)); // the guard at Shell.swift:114 ran
        Assert.Equal(["isRunning", "terminate", "isRunning"], process.Calls);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Shell_ladder_ends_a_process_that_honours_the_graceful_signal_without_a_kill()
    {
        using var runtime = new TestRuntime();
        var process = new FakeProcessControl { ExitsOnTerminate = true };
        TerminationLadder.Shell(process);
        runtime.Advance(TimeSpan.FromSeconds(1));
        Assert.True(process.WaitFor("isRunning", 2, PoolWait));
        Assert.Equal(["isRunning", "terminate", "isRunning"], process.Calls);
    }

    // ---- Stdio: stdin first, 3 s, then the pid alone ---------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-027")]
    [Trait("Decision", "ED-047")]
    public void Stdio_ladder_closes_stdin_even_when_the_process_already_exited_and_arms_no_timer()
    {
        using var runtime = new TestRuntime();
        var process = FakeProcessControl.Exited();
        TerminationLadder.Stdio(process, () => process.Note("closeInput"));
        // StdioProcess.swift:120-122: the close is queued before the guard, so it happens either way.
        Assert.Equal(["closeInput", "isRunning"], process.Calls);
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-027")]
    [Trait("Decision", "ED-047")]
    public void Stdio_ladder_closes_stdin_then_signals_then_kills_only_the_pid_exactly_three_seconds_later()
    {
        using var runtime = new TestRuntime();
        var process = new FakeProcessControl();
        TerminationLadder.Stdio(process, () => process.Note("closeInput"));
        Assert.Equal(["closeInput", "isRunning", "terminate"], process.Calls); // ED-027: in that order
        Assert.Equal(1, runtime.Time.ActiveTimers);
        runtime.Advance(TimeSpan.FromMilliseconds(2_999));
        Assert.Equal(1, runtime.Time.ActiveTimers); // not fired at 2,999 ms (StdioProcess.swift:125 is .now() + 3)
        Assert.Equal(["closeInput", "isRunning", "terminate"], process.Calls);
        runtime.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(process.WaitFor("killProcess", 1, PoolWait));
        // StdioProcess.swift:126: kill(pid, SIGKILL), no group (ED-047 (b): TerminateProcess, no job, grandchildren live).
        Assert.Equal(["closeInput", "isRunning", "terminate", "isRunning", "killProcess"], process.Calls);
        Assert.Equal(0, process.Count("killTree"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-027")]
    public void Stdio_ladder_does_not_kill_a_process_that_exited_within_the_three_seconds()
    {
        using var runtime = new TestRuntime();
        var process = new FakeProcessControl();
        TerminationLadder.Stdio(process, () => process.Note("closeInput"));
        runtime.Advance(TimeSpan.FromMilliseconds(1_000)); // past the Shell gap: the Stdio gap is 3 s, not 1 s
        Assert.Equal(1, runtime.Time.ActiveTimers);         // so the timer has not fired: a 1 s gap would have
        Assert.Equal(["closeInput", "isRunning", "terminate"], process.Calls);
        process.Exit();
        runtime.Advance(TimeSpan.FromMilliseconds(2_000));
        Assert.True(process.WaitFor("isRunning", 2, PoolWait));
        Assert.Equal(["closeInput", "isRunning", "terminate", "isRunning"], process.Calls);
    }

    // ---- TerminateAll: no gap ------------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Terminate_all_signals_every_running_process_then_kills_the_tree_of_each_still_running_with_no_gap()
    {
        using var runtime = new TestRuntime();
        var journal = new ProcessJournal();
        var stubborn = new FakeProcessControl("a", journal);
        var polite = new FakeProcessControl("b", journal) { ExitsOnTerminate = true };
        var gone = FakeProcessControl.Exited("c", journal);
        TerminationLadder.TerminateAll([stubborn, polite, gone]);
        // MCPHub.swift:548 filter, :549-551 terminate each, :552-555 kill each still running: all before returning.
        Assert.Equal(
            ["a:isRunning", "b:isRunning", "c:isRunning", "a:terminate", "b:terminate", "a:isRunning", "a:killTree", "b:isRunning"],
            journal.Entries);
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Assert.Equal(0, stubborn.Count("killProcess"));
    }
}
