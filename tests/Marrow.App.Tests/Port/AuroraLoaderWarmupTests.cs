using Marrow.App.Port;
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Port;

// OD-0113 / OD-0108: each fixed-gradient/path variant is drawn before its first visible use.
[Collection("Headless")]
public sealed class AuroraLoaderWarmupTests
{
    [Fact]
    [Trait("Decision", "ED-252")]
    public void All_three_loaders_join_the_graphics_step_once_and_unregister_cleanly()
    {
        var before = GpuWarmup.Registered.ToArray();
        try
        {
            AuroraLoaderWarmup.Register(); AuroraLoaderWarmup.Register();
            var added = GpuWarmup.Registered.Except(before).ToArray();
            Assert.Equal(["aurora-curtain", "aurora-flow", "aurora-sheen"], added.Select(p => p.Name));
            Assert.All(added, p => Assert.Equal(FirstRunSteps.Graphics, p.Step));
        }
        finally { AuroraLoaderWarmup.Unregister(); }
        Assert.Equal(before, GpuWarmup.Registered);
    }
}
