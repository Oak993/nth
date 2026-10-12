using Marrow.Setup.Install;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Install;

// C7 independent review: only fakes and disposable folders; no installer or app process.
public sealed class C7U3ReviewReproTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Retry_does_not_count_the_previous_attempt_before_the_new_engine_writes()
    {
        using var first = new InstallScenario(VelopackScenarios.Build("crashMidCopy"), update: false).Play();
        Assert.NotNull(first.Run.Failure);
        Assert.True(first.Run.Evidence.CopiedBytes > 0);
        // Retry starts the same root. The fake has started but has not written or renamed anything.
        var runner = new FakeVelopackRunner(new VelopackTimeline("retry-before-rename"), first.Hive, first.Runner.StartMenu);
        var retry = new InstallRun(first.Request, runner, first.Disk, first.After, first.Time);
        retry.Start();
        var frame = retry.Tick();
        Assert.Equal(0, retry.Evidence.CopiedBytes);
        Assert.Equal(4, frame.Target);
    }

}
