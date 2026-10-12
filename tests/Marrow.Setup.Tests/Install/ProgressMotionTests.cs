using Marrow.Setup.Motion;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 4 ("The bar follows a critically damped spring and never goes
// backwards") and the canvas's Progress runner (stiffness 110, 100 shown past 99.6); unit U3 (ED-NEW-inst-u3-1). The drawn
// value never goes back, never passes its target, shows 100 once the target is 100 and it is past 99.6, and the
// percentage is rounded down. Calm shows the target at once. Frames are driven by hand.
public sealed class ProgressMotionTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_spring_has_the_canvas_s_stiffness()
    {
        Assert.Equal(110, ProgressMotion.Stiffness);
        Assert.Equal(2 * Math.PI / Math.Sqrt(110), ProgressMotion.Response, 12);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void It_rises_toward_its_target_without_passing_it_and_settles_there()
    {
        var (bar, frames) = Rig();
        bar.SetTarget(40);
        var values = new List<double>();
        frames.Run(0.2, () => values.Add(bar.Value));
        Assert.InRange(values[^1], 5, 39.9);
        frames.Run(3, () => values.Add(bar.Value));
        Assert.All(values.Zip(values.Skip(1)), pair => Assert.True(pair.Second >= pair.First, $"went back from {pair.First} to {pair.Second}"));
        Assert.All(values, v => Assert.True(v <= 40, $"passed its target at {v}"));
        Assert.Equal(40, bar.Value, 2);
        Assert.False(frames.HasPendingFrame, "a settled bar asks for no frames");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_lower_target_is_ignored_and_the_bar_never_goes_back()
    {
        var (bar, frames) = Rig();
        bar.SetTarget(60);
        frames.Run(0.25);
        var reached = bar.Value;
        bar.SetTarget(20);
        Assert.Equal(60, bar.Target);
        frames.Run(0.1);
        Assert.True(bar.Value >= reached);
        frames.Run(3);
        bar.SetTarget(10);
        frames.Run(1);
        Assert.Equal(60, bar.Value, 2);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_new_higher_target_mid_flight_keeps_going_without_a_jump()
    {
        var (bar, frames) = Rig();
        bar.SetTarget(30);
        frames.Run(0.15);
        var before = bar.Value;
        bar.SetTarget(80);
        frames.Frame();
        Assert.InRange(bar.Value - before, 0, 8);
        var previous = bar.Value;
        frames.Run(4, () =>
        {
            Assert.True(bar.Value >= previous);
            Assert.True(bar.Value - previous < 8, "a frame jumped");
            previous = bar.Value;
        });
        Assert.Equal(80, bar.Value, 2);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Past_99_6_with_a_target_of_100_it_shows_100_and_stops()
    {
        var (bar, frames) = Rig();
        bar.SetTarget(99.7);
        frames.Run(4);
        Assert.InRange(bar.Value, 99.6, 99.7);
        Assert.Equal(99, bar.Percent);
        bar.SetTarget(100);
        frames.Frame();
        Assert.Equal(100, bar.Value);
        Assert.Equal(100, bar.Percent);
        Assert.False(frames.HasPendingFrame);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_percentage_is_the_value_rounded_down()
    {
        var bar = new ProgressMotion(() => true);
        bar.SetTarget(41.99);
        Assert.Equal(41, bar.Percent);
        bar.SetTarget(42);
        Assert.Equal(42, bar.Percent);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Calm_and_an_unattached_bar_show_the_target_at_once()
    {
        var frames = new ManualFrameSource();
        var calm = new ProgressMotion(() => true);
        calm.Attach(new Animator(frames));
        calm.SetTarget(55);
        Assert.Equal(55, calm.Value);
        Assert.False(frames.HasPendingFrame);
        var loose = new ProgressMotion(() => false);
        loose.SetTarget(70);
        Assert.Equal(70, loose.Value);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Each_drawn_change_is_announced_and_Now_starts_again_at_rest()
    {
        var (bar, frames) = Rig();
        var changes = 0;
        bar.Changed += () => changes++;
        bar.SetTarget(50);
        frames.Run(0.5);
        Assert.InRange(changes, 20, 31);
        bar.Now(0);
        Assert.Equal(0, bar.Value);
        Assert.Equal(0, bar.Target);
        bar.SetTarget(10);
        frames.Run(2);
        Assert.Equal(10, bar.Value, 2);
    }

    private static (ProgressMotion Bar, ManualFrameSource Frames) Rig()
    {
        var frames = new ManualFrameSource();
        var bar = new ProgressMotion(() => false);
        bar.Attach(new Animator(frames));
        return (bar, frames);
    }
}
