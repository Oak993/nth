using System.Runtime.InteropServices;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// F.ProcessLauncher, part 1: the graceful step never reaches the app itself (ED-047, probe P6).
//
// Terminate() sends CTRL_BREAK_EVENT. A console control event goes to a process GROUP on a CONSOLE: sent to group 0,
// or from a process that shares the app's console, it reaches the app (and, under `dotnet test`, the test host and
// the runner). P6 created each pipe child with CREATE_NEW_PROCESS_GROUP and its own hidden console
// (CREATE_NO_WINDOW), and sent the event from a separate helper process that attached to the child's console
// (FreeConsole, AttachConsole(child), GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, child), FreeConsole); its runner
// never gave up its own console (deviation 4: "AttachConsole is process-wide state"), and the helper's guard bit
// showed the event never reached the helper. This test holds the same for the launcher, for a child with a job and
// one without (ED-047 (a) and (b); since ED-194 the StdioProcess child is in a job too, which Terminate never
// uses): the test host registers handlers for CTRL_BREAK_EVENT (PosixSignal.SIGQUIT on Windows) and CTRL_C_EVENT
// (SIGINT), cancelling them so a wrong event cannot end the run, and they must never fire; the host's console
// window handle must be unchanged. Registering the handlers is a process-wide change, so the test runs in the
// non-parallel "Process signals" collection (R-TEST-5). Limit: when the test host has no console at all, a
// GenerateConsoleCtrlEvent sent wrongly from the host fails silently and is not seen here; the Ctrl+Break tests in
// ProcessLauncherSpawnTests still require the child's own handler to run.
[Collection("Process signals")]
public sealed class ProcessLauncherSignalTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Terminate_signals_only_the_childs_own_process_group_and_never_the_app_that_launched_it()
    {
        using var signalled = new ManualResetEventSlim();
        var breaks = 0;
        var interrupts = 0;
        using var onBreak = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, context =>
        {
            Interlocked.Increment(ref breaks);
            context.Cancel = true;
            signalled.Set();
        });
        using var onInterrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            Interlocked.Increment(ref interrupts);
            context.Cancel = true;
            signalled.Set();
        });
        var console = HostConsole.Window;
        foreach (var usesJobObject in (bool[])[true, false])
        {
            var output = new ProcessPipe();
            var stdout = new PipeRecorder(output);
            var launcher = new ProcessLauncher
            {
                ExecutableURL = SystemTools.PowerShell,
                Arguments = SystemTools.PowerShellEncoded(ProcessScripts.CtrlBreakCooperating()),
                StandardInput = usesJobObject ? null : new ProcessPipe(),
                StandardOutput = output,
                UsesJobObject = usesJobObject,
            };
            launcher.Run();
            try
            {
                Assert.True(stdout.WaitForText(ProcessScripts.Armed, Bound), "the child never armed its Ctrl+Break handler");
                launcher.Terminate();
                WaitForExit(launcher);
                Assert.Equal(ProcessScripts.BreakExitCode, launcher.TerminationStatus);
            }
            finally
            {
                if (launcher.IsRunning) launcher.KillTree();
            }
        }
        // A control event reaches every process in the group at about the same time; the child has already run its
        // handler and exited, so a host that was signalled too is signalled by now. The bounded wait covers the
        // dispatch of the host's own handler thread.
        Assert.False(signalled.Wait(TimeSpan.FromMilliseconds(1_000)), "the test host received a console control event");
        Assert.Equal(0, Volatile.Read(ref breaks));
        Assert.Equal(0, Volatile.Read(ref interrupts));
        Assert.Equal(console, HostConsole.Window);
    }

    private static void WaitForExit(ProcessLauncher launcher) =>
        Assert.True(Task.Run(launcher.WaitUntilExit).Wait(Bound), "the child did not exit in time");
}
