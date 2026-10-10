using Marrow.App.Port;
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Nth;

// OD-0136 amendment 2 (ED-NEW-nth-motion): the nth motion pack's GPU programs join the Graphics step once, draw every painter
// (both themes, moving and calm) without failing, and unregister cleanly.
[Collection("Headless")]
public sealed class NthMotionWarmupTests
{
    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_nth_programs_join_the_graphics_step_once_draw_and_unregister_cleanly()
    {
        var before = GpuWarmup.Registered.ToArray();
        try
        {
            NthMotionWarmup.Register(); NthMotionWarmup.Register();
            var added = Assert.Single(GpuWarmup.Registered.Except(before));
            Assert.Equal("nth-motion", added.Name);
            Assert.Equal(FirstRunSteps.Graphics, added.Step);
            added.Drawing(new GpuWarmContext(null, 1.5)); // the CPU path draws the same calls
        }
        finally { NthMotionWarmup.Unregister(); }
        Assert.Equal(before, GpuWarmup.Registered);
    }
}
