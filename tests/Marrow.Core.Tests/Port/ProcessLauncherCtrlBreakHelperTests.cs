using System.Diagnostics;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// F.ProcessLauncher, part 4: the CTRL_BREAK helper never outlives its work (ED-242).
//
// Terminate() on a pipe child launches a helper process (Windows PowerShell running P6's four kernel32 calls) and
// returns at once. 52 such helpers were found alive on this PC, orphaned and idle, the oldest a day old. Two causes,
// each reproduced (2026-10-02):
// (1) Created inside CreateProcessW but never resumed: when the creating process ends between the creation of the
//     process and the resume (a test host exiting while a background thread launched a helper), the helper stays
//     suspended with no parent for ever: one thread, Wait/Suspended, about 2 MB, no CPU. Reproduced by a process that
//     launched helpers on four threads and exited under them (29 orphans in 20 runs), and seen once from a Services
//     suite run (its helper was created at 16:14:44, the second its test host ended).
// (2) A CTRL_BREAK that reaches a helper while Windows PowerShell is starting on its hidden console puts it in the
//     script debugger ("Entering debug mode", a [DBG] prompt read from that console) for ever, or ends it with
//     STATUS_CONTROL_C_EXIT. It reached the helper because the helper led a process group of its own: a helper sent for
//     a stale pid that the system had given to a new helper attaches to that helper's console and sends CTRL_BREAK to
//     the group of that pid. Reproduced with a sender attaching 80 to 160 ms into a helper's start (3 of 15 stranded,
//     6 killed); the same sender against a helper that leads no group: 30 of 30 untouched. Stale pids are common: in
//     one Services suite run 159 of 194 helpers were sent for a child that had already exited.
// The tests compose the helper's real pieces: CtrlBreakHelper.Create (kill-on-close job, no group of its own, the
// target's handle held), CompleteLaunch (the limit cleared; Start is the two), and Release (the bounded wait) or
// Abandon (the handles closed without a wait, as the system closes them when the creator ends).
public sealed class ProcessLauncherCtrlBreakHelperTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    /// <summary>Longer than any kill-on-close or TerminateProcess takes; shorter than the stand-in's 60 s sleep.</summary>
    private static readonly TimeSpan EndBound = TimeSpan.FromSeconds(15);

    /// <summary>The exit code of the exited child whose pid a helper reserves; no helper here exits with it.</summary>
    private const int ExitedChildCode = 5;

    [Fact]
    [Trait("Decision", "ED-242")]
    public void A_helper_whose_creator_ends_before_its_launch_completes_ends_with_the_creators_handles()
    {
        // The orphans were suspended inside CreateProcessW; a job ends its processes running or suspended alike, and
        // the scan of ProcessLauncherTests keeps CREATE_SUSPENDED out of src, so the helper here is running.
        var helper = CtrlBreakHelper.Create(ProcessScripts.StrandedHelper);
        using var observed = Observe(helper);
        try
        {
            Assert.False(observed.HasExited);
            helper.Abandon();
            Assert.True(observed.WaitForExit(EndBound), "a helper outlived its creator's handles before its launch completed");
        }
        finally
        {
            KillIfRunning(observed);
        }
    }

    [Fact]
    [Trait("Decision", "ED-242")]
    public void A_helper_that_does_not_end_by_itself_is_ended_at_its_lifetime()
    {
        var helper = CtrlBreakHelper.Start(ProcessScripts.StrandedHelper);
        using var observed = Observe(helper);
        try
        {
            var released = Stopwatch.StartNew();
            helper.Release(TimeSpan.FromSeconds(1));
            Assert.True(observed.WaitForExit(EndBound), "a stranded helper outlived its lifetime");
            Assert.True(released.Elapsed >= TimeSpan.FromMilliseconds(900), "the helper was ended before its lifetime");
        }
        finally
        {
            KillIfRunning(observed);
        }
    }

    [Fact]
    [Trait("Decision", "ED-242")]
    public void A_CTRL_BREAK_sent_to_the_group_of_a_starting_helpers_pid_reaches_no_process()
    {
        var helper = CtrlBreakHelper.Start(ProcessScripts.StrandedHelper);
        using var observed = Observe(helper);
        try
        {
            var probe = RunToExit(ProcessScripts.BreakProbe(helper.ProcessId));
            // 87, ERROR_INVALID_PARAMETER: the helper's console holds no process group of that id.
            Assert.Equal(ProcessScripts.BreakProbeSendFailed + 87, probe.TerminationStatus);
            Assert.False(observed.WaitForExit(1_000), "the helper received the CTRL_BREAK");
        }
        finally
        {
            helper.Release(TimeSpan.Zero);
            if (!observed.WaitForExit(EndBound)) KillIfRunning(observed);
        }
    }

    [Fact]
    [Trait("Decision", "ED-242")]
    public void The_target_pid_stays_the_exited_childs_until_its_helper_is_let_go()
    {
        var (pid, reserved) = ExitedChildWithReservedHandle();
        // The launcher, and with it its own handle to the child, is gone: only the reservation keeps the pid.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var helper = CtrlBreakHelper.Start(ProcessScripts.StrandedHelper, reserved);
        using var observed = Observe(helper);
        try
        {
            Assert.Equal(ExitedChildCode, ProcessIdentity.ExitCode(pid));
            helper.Release(TimeSpan.Zero);
            Assert.True(observed.WaitForExit(EndBound));
            Assert.True(SpinWait.SpinUntil(() => ProcessIdentity.ExitCode(pid) != ExitedChildCode, EndBound),
                "the reservation outlived the helper");
        }
        finally
        {
            KillIfRunning(observed);
        }
    }

    [Fact]
    [Trait("Decision", "ED-242")]
    public void A_launched_helper_still_finishes_its_call_when_its_creator_lets_go_of_it()
    {
        // After the launch the kill-on-close limit is cleared: a helper launched just before the app quits still sends
        // its CTRL_BREAK, as the helper did before ED-242.
        var helper = CtrlBreakHelper.Start(ProcessScripts.SlowHelper);
        using var observed = Observe(helper);
        try
        {
            helper.Abandon();
            Assert.True(observed.WaitForExit(EndBound), "the helper did not finish");
            Assert.Equal(ProcessScripts.SlowHelperExitCode, observed.ExitCode);
        }
        finally
        {
            KillIfRunning(observed);
        }
    }

    [Fact]
    [Trait("Decision", "ED-242")]
    [Trait("Decision", "ED-047")]
    public void A_cooperating_child_still_receives_CTRL_BREAK_and_its_helper_ends_by_itself()
    {
        var (launcher, stdout) = StartCooperatingChild();
        try
        {
            Assert.True(stdout.WaitForText(ProcessScripts.Armed, Bound), "the child never armed its Ctrl+Break handler");
            var helper = CtrlBreakHelper.Start(ProcessNative.CtrlBreakScript(launcher.ProcessIdentifier));
            using var observed = Observe(helper);
            helper.Release(CtrlBreakHelper.Lifetime);
            WaitForExit(launcher);
            Assert.True(stdout.WaitForText(ProcessScripts.BreakSeen, Bound));
            Assert.Equal(ProcessScripts.BreakExitCode, launcher.TerminationStatus);
            AssertEndsByItself(observed, expectedExitCode: 0);
        }
        finally
        {
            if (launcher.IsRunning) launcher.KillTree();
        }
    }

    [Fact]
    [Trait("Decision", "ED-242")]
    public void A_helper_sent_to_a_child_that_has_already_exited_ends_by_itself()
    {
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellScript("exit 0"),
        };
        launcher.Run();
        WaitForExit(launcher);
        // The launcher still holds the child's handle, so the pid is the exited child's: AttachConsole fails (exit 1).
        var helper = CtrlBreakHelper.Start(ProcessNative.CtrlBreakScript(launcher.ProcessIdentifier));
        using var observed = Observe(helper);
        helper.Release(CtrlBreakHelper.Lifetime);
        AssertEndsByItself(observed, expectedExitCode: 1);
        GC.KeepAlive(launcher);
    }

    [Fact]
    [Trait("Decision", "ED-242")]
    public void A_helper_whose_child_is_killed_while_the_helper_starts_ends_by_itself()
    {
        // TerminationLadder.TerminateAll's shape: the CTRL_BREAK, then the tree killed at once.
        var (launcher, stdout) = StartCooperatingChild();
        try
        {
            Assert.True(stdout.WaitForText(ProcessScripts.Armed, Bound), "the child never armed its Ctrl+Break handler");
            var helper = CtrlBreakHelper.Start(ProcessNative.CtrlBreakScript(launcher.ProcessIdentifier));
            using var observed = Observe(helper);
            helper.Release(CtrlBreakHelper.Lifetime);
            launcher.KillTree();
            WaitForExit(launcher);
            Assert.True(observed.WaitForExit(CtrlBreakHelper.Lifetime + EndBound), "the helper did not end");
            Assert.NotEqual(9, observed.ExitCode); // ended by itself (1: AttachConsole failed; 0: sent first), never at the bound
        }
        finally
        {
            if (launcher.IsRunning) launcher.KillTree();
        }
    }

    [Fact]
    [Trait("Decision", "ED-242")]
    public void A_helper_sent_to_a_child_that_ignores_CTRL_BREAK_ends_by_itself_and_the_child_lives_on()
    {
        // A Windows PowerShell child with no handler of its own: its own break handling keeps it alive.
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellScript("Start-Sleep 60"),
        };
        launcher.Run();
        try
        {
            var helper = CtrlBreakHelper.Start(ProcessNative.CtrlBreakScript(launcher.ProcessIdentifier));
            using var observed = Observe(helper);
            helper.Release(CtrlBreakHelper.Lifetime);
            AssertEndsByItself(observed, expectedExitCode: 0);
            Assert.True(launcher.IsRunning);
        }
        finally
        {
            if (launcher.IsRunning) launcher.KillTree();
        }
    }

    /// <summary>A Process of the helper, opened while the helper's creator still holds it (so the pid is the helper's).</summary>
    private static Process Observe(CtrlBreakHelper helper)
    {
        var process = Process.GetProcessById(helper.ProcessId);
        _ = process.SafeHandle; // its own handle, so ExitCode stays readable after the helper ends
        return process;
    }

    private static void AssertEndsByItself(Process observed, int expectedExitCode)
    {
        Assert.True(observed.WaitForExit(CtrlBreakHelper.Lifetime), "the helper did not end by itself within its lifetime");
        Assert.Equal(expectedExitCode, observed.ExitCode);
    }

    /// <summary>Runs a PowerShell script as a pipe child of the launcher and waits for it to exit.</summary>
    private static ProcessLauncher RunToExit(string script)
    {
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellEncoded(script),
        };
        launcher.Run();
        try
        {
            WaitForExit(launcher);
        }
        finally
        {
            if (launcher.IsRunning) launcher.KillTree();
        }
        return launcher;
    }

    /// <summary>
    /// A pipe child that exited with <see cref="ExitedChildCode"/>, and a handle of the test's own to
    /// it (the reservation Terminate makes), taken while the launcher still held the child; nothing returned keeps the
    /// launcher reachable.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (int Pid, Microsoft.Win32.SafeHandles.SafeProcessHandle Reserved) ExitedChildWithReservedHandle()
    {
        var child = RunToExit("exit " + ExitedChildCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(ExitedChildCode, child.TerminationStatus);
        return (child.ProcessIdentifier, ProcessIdentity.Open(child.ProcessIdentifier));
    }

    private static void WaitForExit(ProcessLauncher launcher) =>
        Assert.True(Task.Run(launcher.WaitUntilExit).Wait(Bound), "the process did not exit in time");

    private static void KillIfRunning(Process observed)
    {
        if (!observed.HasExited) observed.Kill();
    }

    private static (ProcessLauncher Launcher, PipeRecorder Stdout) StartCooperatingChild()
    {
        var output = new ProcessPipe();
        var stdout = new PipeRecorder(output);
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellEncoded(ProcessScripts.CtrlBreakCooperating()),
            StandardOutput = output,
        };
        launcher.Run();
        return (launcher, stdout);
    }
}
