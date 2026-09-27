using System.Diagnostics;
using System.Globalization;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// ED-194 (the B3 amendment of ED-047 (b) and ED-126): since B3, StdioProcess's own child sits in a Job Object from
// spawn (src/Marrow.Core/Support/StdioProcess.cs:17 `new() { UsesJobObject = true }`), with no limits and no
// KILL_ON_JOB_CLOSE, so ProcessControl.KillTree() can end the whole tree at quit. But the Stdio stop ladder
// (TerminationLadder.Stdio, ProcessLauncherLadderTests.cs) still ends with KillProcess on the pid alone; it never
// uses the job. So the job's mere presence must not change what the two callers see:
//   - stop (a caller running the Stdio ladder to its end, or MCPProxy/a provider session closing one child) still
//     calls KillProcess: a grandchild the child started survives, exactly as it did before ED-194 with no job at all.
//   - quit (MCPLiveProcesses.terminateAll, called through ProcessControl.KillTree at AppDelegate.swift:54, the
//     TerminateAll ladder shape of ProcessLauncherLadderTests.cs) calls KillTree: TerminateJobObject ends the child
//     and every grandchild in its job.
// This is the ProcessLauncherSpawnTests.StartChildWithGrandchild/StartAndKillChildWithoutJob shape (a harmless
// powershell.exe starting a second powershell.exe, never an AI CLI, R-TEST-10), driven through the real
// Marrow.Core.Support.StdioProcess a provider session actually constructs, not a bare ProcessLauncher.
public sealed class StdioProcessJobTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("Decision", "ED-194")]
    public async Task A_StdioProcess_childs_grandchild_survives_KillProcess_at_stop_but_dies_when_KillTree_runs_at_quit()
    {
        // ---- Stop: KillProcess is the pid alone, even though ED-194 now puts the child in a job. ----------------
        var (stopped, spared) = StartWithGrandchild("stdio job stop");
        try
        {
            Assert.True(stopped.IsRunning);
            stopped.ProcessControl.KillProcess(); // the Stdio ladder's forced step (StdioProcess.swift:126, ED-126)
            await stopped.WaitForExit().WaitAsync(Bound);
            Assert.False(stopped.IsRunning);
            spared.Refresh();
            Assert.False(spared.HasExited, "the grandchild died from KillProcess, although ED-194 says it never touches the job");
        }
        finally
        {
            Cleanup(spared);
        }

        // ---- Quit: KillTree on a still-running child is TerminateJobObject, and it takes the grandchild too. -----
        var (running, doomed) = StartWithGrandchild("stdio job quit");
        try
        {
            Assert.True(running.IsRunning);
            running.ProcessControl.KillTree(); // MCPLiveProcesses.terminateAll at quit (AppDelegate.swift:54)
            await running.WaitForExit().WaitAsync(Bound);
            Assert.True(doomed.WaitForExit(Bound), "the grandchild survived quit's KillTree, although a job is present (ED-194)");
        }
        finally
        {
            Cleanup(doomed);
        }
    }

    /// <summary>
    /// A real <see cref="StdioProcess"/> (powershell.exe) that starts a second powershell.exe (no new window, so it
    /// inherits the ED-194 job), writes that grandchild's pid to a file and signals a named event, then itself
    /// sleeps 60 s. Returns once the grandchild's pid is read; the event handle is disposed inside this method (its
    /// job, waiting, is done once the pid file is read).
    /// </summary>
    private static (StdioProcess Child, Process Grandchild) StartWithGrandchild(string label)
    {
        var folder = ProcessTestFolders.Create(label);
        var pidFile = Path.Join(folder, "grandchild.pid");
        var eventName = "Marrow-StdioProcess-" + Guid.NewGuid().ToString("N");
        var child = new StdioProcess(
            SystemTools.PowerShell,
            SystemTools.PowerShellEncoded(
                "$p = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep 60' -NoNewWindow -PassThru\n" +
                "[IO.File]::WriteAllText('" + pidFile + "', [string]$p.Id)\n" +
                "$null = ([Threading.EventWaitHandle]::OpenExisting('" + eventName + "')).Set()\n" +
                "Start-Sleep 60\n"),
            folder,
            InheritedEnvironment());
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        child.Start();
        try
        {
            Assert.True(ready.WaitOne(Bound), "the StdioProcess child never reported its grandchild");
            var grandchild = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile).Trim(), CultureInfo.InvariantCulture));
            Assert.False(grandchild.HasExited);
            // Retain the native handle so ExitCode/HasExited stay available for this attached Process (P5 pattern).
            _ = grandchild.SafeHandle;
            return (child, grandchild);
        }
        catch
        {
            if (child.IsRunning) child.ProcessControl.KillTree();
            throw;
        }
    }

    private static IReadOnlyDictionary<string, string> InheritedEnvironment() =>
        System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase);

    private static void Cleanup(Process grandchild)
    {
        try
        {
            if (!grandchild.HasExited) grandchild.Kill();
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        grandchild.Dispose();
    }
}
