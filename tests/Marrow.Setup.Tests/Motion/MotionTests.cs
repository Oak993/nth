using Marrow.Setup.Motion;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Motion;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, "Motion", and 3b, "one per-frame integrator"; unit U1
// (ED-NEW-inst-u1-1). Springs are set by response and damping and are interruptible: a new target keeps the current value
// and velocity, so a motion turns around from where it is. Timed curves are CSS cubic-beziers. One loop advances
// everything, asks for frames only while something moves, and caps a frame's step so a stall never throws a spring.
public sealed class MotionTests
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_critically_damped_spring_settles_on_its_target_without_overshoot()
    {
        var spring = new Spring(0.24, 1, 0, 0.002);
        spring.Retarget(1);
        var highest = 0.0;
        var frames = 0;
        while (spring.Advance(1.0 / 60) && frames < 600)
        {
            highest = Math.Max(highest, spring.Value);
            frames++;
        }

        Assert.Equal(1, spring.Value);
        Assert.Equal(0, spring.Velocity);
        Assert.True(highest <= 1.0005, $"overshot to {highest}");
        Assert.InRange(frames, 10, 60);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_retarget_mid_flight_keeps_the_value_and_velocity()
    {
        var spring = new Spring(0.3, 1);
        spring.Retarget(10);
        for (var i = 0; i < 6; i++) spring.Advance(1.0 / 60);
        var value = spring.Value;
        var velocity = spring.Velocity;
        Assert.True(velocity > 0);

        spring.Retarget(0);

        Assert.Equal(value, spring.Value);
        Assert.Equal(velocity, spring.Velocity);
        spring.Advance(1.0 / 60);
        Assert.True(spring.Value > value - 0.5, "the spring jumped instead of turning around");
        Assert.True(Math.Abs(spring.Value - value) < 1, "a frame moved too far");
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_screen_change_curve_is_cubic_bezier_2_8_2_1()
    {
        var curve = CubicBezier.ScreenChange;
        Assert.Equal(0, curve.Evaluate(0), 6);
        Assert.Equal(1, curve.Evaluate(1), 6);
        var previous = 0.0;
        for (var t = 0.01; t <= 1; t += 0.01)
        {
            var value = curve.Evaluate(t);
            Assert.True(value >= previous - 1e-9, $"not monotonic at {t}");
            previous = value;
        }
        Assert.True(curve.Evaluate(0.25) > 0.6, "the curve front-loads its motion");
        Assert.Equal(0.5, CubicBezier.Linear.Evaluate(0.5), 6);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_tween_waits_its_delay_runs_its_time_and_retargets_from_where_it_is()
    {
        var tween = new Tween(0);
        tween.Retarget(1, 0.3, CubicBezier.Linear, delay: 0.06);

        tween.Advance(0.05);
        Assert.Equal(0, tween.Value);
        tween.Advance(0.16);
        Assert.Equal(0.5, tween.Value, 6);

        tween.Retarget(0, 0.2, CubicBezier.Linear);
        Assert.Equal(0.5, tween.Value, 6);
        tween.Advance(0.1);
        Assert.Equal(0.25, tween.Value, 6);
        Assert.False(tween.Advance(0.2));
        Assert.Equal(0, tween.Value);
        Assert.True(tween.IsAtRest);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_loop_asks_for_frames_only_while_something_moves_and_caps_a_stalled_frame()
    {
        var frames = new ManualFrameSource();
        var animator = new Animator(frames);
        var spring = new Spring(0.3, 1);
        var motion = new SpringMotion(spring);
        Assert.True(animator.IsIdle);
        Assert.False(frames.HasPendingFrame);

        spring.Retarget(100);
        animator.Start(motion);
        animator.Start(motion);
        Assert.True(frames.HasPendingFrame);
        frames.Frame(1.0 / 60);
        Assert.Equal(1, motion.Calls);
        frames.Frame(5); // a 5 s stall is advanced as 0.1 s
        Assert.Equal(0.1, motion.LastStep, 6);
        Assert.True(spring.Value < 100);

        frames.Run(3);
        Assert.True(animator.IsIdle);
        Assert.False(frames.HasPendingFrame);
        Assert.Equal(100, spring.Value);
    }
}
