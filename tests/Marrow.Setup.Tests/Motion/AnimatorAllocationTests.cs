using Marrow.Setup.Motion;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Motion;

// ED-NEW-smooth2-3: the one installer integrator must not feed the collector on every frame.
public sealed class AnimatorAllocationTests
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_warm_animator_allocates_nothing_per_frame()
    {
        var frames = new SingleSetupFrameSource();
        var animator = new Animator(frames);
        var ticks = 0;
        var motion = new CallbackSetupMotion(_ => { ticks++; return true; });
        animator.Start(motion);
        for (var i = 0; i < 1000; i++) frames.Frame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) frames.Frame();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(2000, ticks);
        Assert.True(bytes == 0, $"The animator allocated {bytes} bytes for 1000 frames ({bytes / 1000.0} B/frame).");
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_motion_started_during_a_frame_waits_for_the_next_frame_and_completed_motions_are_released()
    {
        var frames = new SingleSetupFrameSource();
        var animator = new Animator(frames);
        var order = new List<string>();
        var second = new CallbackSetupMotion(_ => { order.Add("second"); return false; });
        var first = new CallbackSetupMotion(_ => { order.Add("first"); animator.Start(second); animator.Start(second); return false; });
        animator.Start(first); animator.Start(first);
        frames.Frame();
        Assert.Equal(new[] { "first" }, order);
        Assert.True(frames.Pending);
        frames.Frame();
        Assert.Equal(new[] { "first", "second" }, order);
        Assert.True(animator.IsIdle);
        Assert.False(frames.Pending);
        // The reusable work list must release every reference after dispatch, even when a motion throws.
        foreach (var field in typeof(Animator).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            if (field.GetValue(animator) is List<IMotion> work) Assert.Empty(work);
        var failing = new CallbackSetupMotion(_ => throw new InvalidOperationException("fixture"));
        animator.Start(failing);
        Assert.Throws<InvalidOperationException>(() => frames.Frame());
        foreach (var field in typeof(Animator).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            if (field.Name != "moving" && field.GetValue(animator) is List<IMotion> work) Assert.Empty(work);
    }
}
