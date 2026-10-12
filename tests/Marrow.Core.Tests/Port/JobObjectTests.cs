using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// Dextra AS-5 (DESIGN.md rev 5, 12.6, 15.4; ED-NEW-dextra-as5-5): a mission agent's kill-on-close Job Object. A child the
// launcher starts with OuterJob runs inside it from its first instruction; a job that was terminated or closed refuses any
// new launch, so a mission agent never runs outside its job. Children are where.exe only (R-TEST-10).
[Trait("Decision", "ED-NEW-dextra-as5-5")]
[Trait("Feature", "AS-5")]
public sealed class JobObjectTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static void Exited(ProcessLauncher launcher, string message) => Assert.True(Task.Run(launcher.WaitUntilExit).Wait(Bound), message);

    [Fact]
    public void A_new_job_is_open_and_empty_and_terminate_or_dispose_closes_it()
    {
        using var job = JobObject.Create();
        Assert.False(job.IsClosed);
        Assert.Equal(0, job.ActiveProcesses);
        job.Terminate();
        Assert.True(job.IsClosed);
        Assert.Equal(0, job.ActiveProcesses);
        job.Terminate();

        var other = JobObject.Create();
        other.Dispose();
        Assert.True(other.IsClosed);
        Assert.Equal(0, other.ActiveProcesses);
        other.Terminate();
        other.Dispose();
    }

    [Fact]
    public void A_child_runs_inside_the_agents_job_and_leaves_it_empty_when_it_exits()
    {
        using var job = JobObject.Create();
        var output = new ProcessPipe();
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.Where,
            Arguments = ["/Q", "where.exe"],
            CurrentDirectoryURL = Environment.SystemDirectory,
            StandardOutput = output,
            OuterJob = job,
        };
        launcher.Run();
        Exited(launcher, "the child did not exit in time");
        Assert.Equal(0, launcher.TerminationStatus);
        Assert.True(SpinWait.SpinUntil(() => job.ActiveProcesses == 0, Bound));
        Assert.False(job.IsClosed);
    }

    [Fact]
    public void A_terminated_or_closed_job_refuses_the_launch()
    {
        var terminated = JobObject.Create();
        terminated.Terminate();
        var closed = JobObject.Create();
        closed.Dispose();
        foreach (var job in new[] { terminated, closed })
        {
            var launcher = new ProcessLauncher
            {
                ExecutableURL = SystemTools.Where,
                Arguments = ["/Q", "where.exe"],
                OuterJob = job,
            };
            var refused = Assert.Throws<InvalidOperationException>(launcher.Run);
            Assert.Equal("The agent's job has ended; nothing more starts in it.", refused.Message);
            Assert.False(launcher.IsRunning);
            Assert.Equal(0, launcher.ProcessIdentifier);
        }
        terminated.Dispose();
    }

    [Fact]
    public void Without_an_outer_job_the_launch_is_as_before()
    {
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Where, Arguments = ["/Q", "where.exe"], CurrentDirectoryURL = Environment.SystemDirectory };
        Assert.Null(launcher.OuterJob);
        launcher.Run();
        Exited(launcher, "the child did not exit in time");
        Assert.Equal(0, launcher.TerminationStatus);
    }
}
