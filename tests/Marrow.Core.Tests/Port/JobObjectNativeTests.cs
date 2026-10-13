using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// Owed native check (never run by the unit agent): the whole tree of a mission agent ends with its job. A child starts a
// grandchild that would run for ever (ping -t); Terminate ends both, and so does closing the job (kill-on-close), so a
// build or dev server an agent started never outlives Stop or nth.
[Trait("Decision", "ED-NEW-dextra-as5-5")]
[Trait("Feature", "AS-5")]
public sealed class JobObjectNativeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static void Exited(ProcessLauncher launcher, string message) => Assert.True(Task.Run(launcher.WaitUntilExit).Wait(Bound), message);

    /// <summary>A PowerShell child that starts ping -t as a grandchild, says its id, and waits on it.</summary>
    private static (ProcessLauncher Child, int Grandchild) Tree(JobObject job)
    {
        var said = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var text = new System.Text.StringBuilder();
        var output = new ProcessPipe
        {
            ReadabilityHandler = chunk =>
            {
                lock (text)
                {
                    text.Append(System.Text.Encoding.ASCII.GetString(chunk));
                    var line = text.ToString();
                    if (line.Contains('\n', StringComparison.Ordinal) && int.TryParse(line.Trim(), out var pid)) said.TrySetResult(pid);
                }
            },
        };
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellScript(
                "$p = Start-Process -FilePath ping.exe -ArgumentList '-t','127.0.0.1' -NoNewWindow -PassThru -RedirectStandardOutput NUL; " +
                "[Console]::Out.WriteLine($p.Id); [Console]::Out.Flush(); Wait-Process -Id $p.Id"),
            StandardOutput = output,
            OuterJob = job,
        };
        launcher.Run();
        Assert.True(said.Task.Wait(Bound), "the grandchild did not start");
        Assert.True(SpinWait.SpinUntil(() => job.ActiveProcesses >= 2, Bound), "the grandchild is not in the job");
        return (launcher, said.Task.Result);
    }

    private static bool Ended(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
    }

    [Fact]
    public void Terminate_ends_the_child_and_its_grandchild()
    {
        using var job = JobObject.Create();
        var (child, grandchild) = Tree(job);
        job.Terminate();
        Exited(child, "the child outlived its job");
        Assert.True(SpinWait.SpinUntil(() => Ended(grandchild), Bound), "the grandchild outlived its job");
        Assert.Equal(0, job.ActiveProcesses);
    }

    [Fact]
    public void Closing_the_job_ends_the_whole_tree()
    {
        var job = JobObject.Create();
        var (child, grandchild) = Tree(job);
        job.Dispose();
        Exited(child, "the child outlived its closed job");
        Assert.True(SpinWait.SpinUntil(() => Ended(grandchild), Bound), "the grandchild outlived its closed job");
    }
}
