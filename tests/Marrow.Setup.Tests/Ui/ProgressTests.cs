using Avalonia.Automation;
using Marrow.Setup.Motion;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Ui;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 and 8a, "Installing", unit U2 (ED-NEW-inst-u2-1). Progress bar B: it
// follows its target on a critically damped spring (k = 110) and never goes back, its percentage counts whole numbers to
// 100 %, Calm jumps; the indeterminate segment sweeps left to right; the steps cross from waiting to working to done in
// 0.2 s each with Narrator names that say the state; spinner B's dash obeys its curve (at least 4 % long) and Calm holds
// it still at 30 %.
[Collection("SetupHeadless")]
public sealed class ProgressTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_bar_follows_its_target_without_overshoot_and_never_goes_back()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var bar = new ProgressBarB(() => false);
            bar.Attach(new Animator(frames));
            bar.SetTarget(40);
            var shown = new List<double>();
            for (var i = 0; i < 90; i++)
            {
                frames.Frame();
                shown.Add(bar.Shown);
            }
            Assert.All(shown.Zip(shown.Skip(1)), p => Assert.True(p.Second >= p.First - 1e-9, "the bar went back"));
            Assert.All(shown, v => Assert.True(v <= 40.001, "the bar overshot"));
            Assert.Equal(40, bar.Shown, 2);
            Assert.Equal(40, bar.Percent);
            Assert.Equal("40%", bar.PercentText);

            bar.SetTarget(25);
            frames.Run(.5);
            Assert.Equal(40, bar.Target);
            Assert.Equal(40, bar.Shown, 2);

            bar.SetTarget(100);
            frames.Run(1.5);
            Assert.Equal("100%", bar.PercentText);
            Assert.Equal("100%", AutomationProperties.GetName(bar));
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_bar_gets_half_way_to_a_new_target_in_about_a_tenth_of_a_second()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var bar = new ProgressBarB(() => false);
            bar.Attach(new Animator(frames));
            bar.SetTarget(100);
            frames.Run(.1);
            // a critically damped spring with k = 110 (ω = 10.5/s) is 1 − e^(−ωt)(1 + ωt) of the way at t
            var w = Math.Sqrt(ProgressBarB.Stiffness);
            var expected = 100 * (1 - Math.Exp(-w * .1) * (1 + w * .1));
            Assert.InRange(bar.Shown, expected - 3, expected + 3);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Calm_jumps_the_bar_and_Reset_starts_a_new_run()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var bar = new ProgressBarB(() => true);
            bar.Attach(new Animator(frames));
            bar.SetTarget(63.7);
            Assert.Equal(63.7, bar.Shown);
            Assert.Equal("63%", bar.PercentText);
            bar.Reset();
            Assert.Equal(0, bar.Shown);
            Assert.Equal("0%", bar.PercentText);
            bar.SetTarget(10);
            Assert.Equal(10, bar.Target);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_indeterminate_segment_sweeps_from_left_to_right_and_Calm_breathes_in_place()
    {
        var start = ProgressBarB.Segment(0, calm: false);
        Assert.Equal((0, 0), (start.Left, start.Width));
        var middle = ProgressBarB.Segment(ProgressBarB.SegmentCycle * .5, false);
        Assert.True(middle.Width > .3);
        var end = ProgressBarB.Segment(ProgressBarB.SegmentCycle * .999, false);
        Assert.True(end.Left > .95);
        var lastLeft = -1.0;
        for (var t = 0.0; t < ProgressBarB.SegmentCycle - .02; t += 1.0 / 60)
        {
            var s = ProgressBarB.Segment(t, false);
            Assert.True(s.Left >= lastLeft - 1e-9);
            Assert.InRange(s.Left + s.Width, 0, 1.000001);
            lastLeft = s.Left;
        }
        for (var t = 0.0; t < 5; t += .1)
        {
            var calm = ProgressBarB.Segment(t, true);
            Assert.Equal((.35, .30), (calm.Left, calm.Width));
            Assert.InRange(calm.Opacity, .55, 1);
        }
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Steps_cross_from_waiting_to_working_to_done_in_0_2_s_with_their_state_in_their_name()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var list = new StepList(() => false);
            list.Attach(new Animator(frames));
            list.SetSteps([new StepSpec("Checking this PC"), new StepSpec("Copying nth", "420 of 420 MB"), new StepSpec("Adding nth to Start")]);
            Assert.All(list.Rows, r => Assert.Equal(StepState.Waiting, r.State));
            Assert.Equal("Checking this PC, waiting", AutomationProperties.GetName(list.Rows[0]));

            list.Advance(1);
            Assert.Equal([StepState.Done, StepState.Working, StepState.Waiting], list.Rows.Select(r => r.State).ToArray());
            Assert.Equal("Checking this PC, done", AutomationProperties.GetName(list.Rows[0]));
            Assert.Equal("Copying nth, in progress", AutomationProperties.GetName(list.Rows[1]));
            frames.Run(.1);
            var half = list.Rows[1].Slot.Opacities;
            // half way through the 0.2 s cross-fade on the canvas's ease curve
            var eased = CubicBezier.Ease.Evaluate(.5);
            Assert.Equal(eased, half.Spinner, 2);
            Assert.Equal(1 - eased, half.Ring, 2);
            frames.Run(.15);
            Assert.Equal((0, 1, 0), list.Rows[1].Slot.Opacities);
            Assert.Equal((0, 0, 1), list.Rows[0].Slot.Opacities);
            Assert.Equal(1, list.Rows[1].DetailOpacity, 3);

            list.Advance(3);
            frames.Run(.3);
            Assert.All(list.Rows, r => Assert.Equal((0, 0, 1), r.Slot.Opacities));
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_steps_detail_keeps_the_width_of_its_longest_form()
    {
        headless.OnUiThread(() =>
        {
            var row = new StepRow(new StepSpec("Copying nth", "420 of 420 MB"), () => false);
            row.SetDetail("1 of 420 MB");
            row.Measure(new Avalonia.Size(420, double.PositiveInfinity));
            var narrow = row.DesiredSize;
            row.SetDetail("420 of 420 MB");
            row.InvalidateMeasure();
            row.Measure(new Avalonia.Size(420, double.PositiveInfinity));
            Assert.Equal(narrow, row.DesiredSize);
            row.Arrange(new Avalonia.Rect(row.DesiredSize));
            var label = row.LabelBlock.Bounds;
            row.SetDetail("7 of 420 MB");
            row.InvalidateMeasure();
            row.Measure(new Avalonia.Size(420, double.PositiveInfinity));
            row.Arrange(new Avalonia.Rect(row.DesiredSize));
            Assert.Equal(label, row.LabelBlock.Bounds);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_spinner_dash_follows_its_curve_and_Calm_holds_it_at_30_percent()
    {
        for (var t = 0.0; t < 10; t += 1.0 / 60)
        {
            var (start, length) = StepSlot.DashAt(t, calm: false);
            Assert.InRange(length, 4, 100);
            Assert.InRange(start, 0, 100);
        }
        Assert.Equal(4, StepSlot.DashAt(0, false).Length, 6);
        Assert.True(StepSlot.DashAt(StepSlot.Lap * .5, false).Length > 30);
        Assert.Equal((0, 30), StepSlot.DashAt(0, true));
        Assert.Equal((0, 30), StepSlot.DashAt(7.3, true));
    }
}
