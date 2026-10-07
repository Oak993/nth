using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-3.5.2 (compaction): the stage folds as the page scrolls, 212 down to 120, the conversation text above the composer fading and lifting; a step of a wheel or a drag is followed exactly, a jump is eased
// with the time constant 0.06 s on the frame clock, and under reduce motion everything is at once. Frames are sampled at 60 Hz.
[Collection("Headless")]
public sealed class StickyStageCompactionTests(HeadlessSession headless)
{
    private const double Frame = 1000.0 / 60;

    // the clock moves in whole ticks of 100 ns, so a frame of 1000 / 60 ms is this many seconds to the clock
    private static readonly double FrameSeconds = TimeSpan.FromMilliseconds(Frame).TotalSeconds;

    // what the page shows of the stage now: the applied compaction and the three things it drives, read off the controls
    private static (double Col, double SurfaceHeight, double Opacity, double Lift, double BlockBottom) Shown(StickyStageProbes p)
    {
        var stage = p.Rig.Page.Stage!;
        return p.Rig.Get(() =>
        {
            var surfaceTop = p.Top(stage.Surface);
            return (stage.Compaction, stage.Surface.Height, stage.Chat.Opacity, p.Top(stage.ChatHeading) - surfaceTop - ChatHeadingRest, p.Top(stage.Hairline) + 1);
        });
    }

    // the conversation heading sits 18 below the stage's top at rest (CZ-4.2); a lift moves it up from there
    private const double ChatHeadingRest = CustomizationStage.ChatTop;

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T61, CZ-3.5.2: steps of a wheel or a drag are followed exactly, in both directions, within the same frame (no lag, no easing)
    public void A_wheel_scroll_is_followed_exactly() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        foreach (var y in new[] { 0.0, 10, 30, 60, 92, 150, 400, 92, 60, 45, 10, 0 })
        {
            p.Rig.ScrollTo(y);
            var shown = Shown(p);
            Assert.Equal(Math.Min(92, y), shown.Col, 6); // at once: no frame has run
            Assert.Equal(212 - shown.Col, shown.SurfaceHeight, 6);
        }
        // and a step of 72 or less from where it stands, at any place on the way
        p.Rig.ScrollTo(0);
        p.Rig.ScrollTo(72);
        Assert.Equal(72, Shown(p).Col, 6);
        p.Rig.ScrollTo(0);
        Assert.Equal(0, Shown(p).Col, 6);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T61, CZ-3.5.2, OD-0076: a 300 point jump glides: the stage follows the exponential with tau 0.06 s frame by frame, every frame a little less than the one before, to the end, never a cut
    public void A_jump_eases_with_the_time_constant_and_never_cuts() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        p.Rig.ScrollTo(300);
        Assert.Equal(0, Shown(p).Col, 6); // the jump itself does not move the stage
        var expected = new Compaction(0, false);
        expected = StickyStage.Scrolled(expected, 300, false);
        var samples = new List<double> { 0 };
        var last = 0.0;
        for (var frame = 1; frame <= 60; frame++)
        {
            p.Rig.Advance(Frame);
            expected = StickyStage.Frame(expected, 300, FrameSeconds, false);
            var shown = Shown(p);
            Assert.Equal(expected.Value, shown.Col, 6);
            Assert.Equal(212 - shown.Col, shown.SurfaceHeight, 6);
            Assert.Equal(StickyStage.ChatOpacity(shown.Col), shown.Opacity, 6);
            Assert.Equal(StickyStage.ChatOffset(shown.Col), shown.Lift, 1); // layout puts the heading on whole device pixels
            Assert.True(shown.Col >= last, "never backwards");
            samples.Add(shown.Col);
            last = shown.Col;
        }
        Assert.Equal(92, last);
        // the first frame is the exponential from rest (the design's own first frame) and no later frame is bigger: it only slows
        Assert.Equal(92 * (1 - Math.Exp(-FrameSeconds / 0.06)), samples[1], 6);
        var steps = samples.Zip(samples.Skip(1), (a, b) => b - a).ToArray();
        Assert.True(steps.Max() <= steps[0] + 1e-9);
        // it is the exponential with tau 0.06 s, not a fixed-length ramp: 56.6 % of the way after 3 frames (0.05 s), 67.1 % after 4, and not yet at the end after 7 (0.117 s, about two tau)
        Assert.InRange(samples[3] / 92, 0.55, 0.58);
        Assert.InRange(samples[4] / 92, 0.66, 0.68);
        Assert.True(samples[7] < 92 - 1);
        // once there it stays: a frame with nothing to ease moves nothing
        p.Rig.Advance(Frame * 10);
        Assert.Equal(92, Shown(p).Col, 6);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T61, CZ-3.5.2: a jump back to the top (Home) unfolds the stage the same way
    public void A_jump_back_unfolds_the_stage_the_same_way() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        p.Rig.ScrollTo(100); // a step of 92: exactly followed? 100 - 0 targets 92, |92 - 0| > 72: eased; let it end
        p.Rig.Advance(Frame * 40);
        Assert.Equal(92, Shown(p).Col, 6);
        p.Rig.ScrollTo(0);
        var previous = 92.0;
        var first = 0.0;
        for (var frame = 1; frame <= 40; frame++)
        {
            p.Rig.Advance(Frame);
            var col = Shown(p).Col;
            Assert.True(col <= previous);
            if (frame == 1) first = previous - col;
            Assert.True(previous - col <= first + 1e-9, "no frame is bigger than the first");
            previous = col;
        }
        Assert.Equal(0, previous, 6);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T61, CZ-3.5.2: under reduce motion the stage is at its target at once, a jump too
    public void Reduce_motion_takes_the_compaction_at_once() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        p.Rig.ScrollTo(300);
        Assert.Equal(92, Shown(p).Col, 6);
        p.Rig.ScrollTo(0);
        Assert.Equal(0, Shown(p).Col, 6);
        p.Rig.ScrollTo(40);
        Assert.Equal(40, Shown(p).Col, 6);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T61, CZ-3.5.2: a wheel scroll during an eased jump goes on easing to the new target (the ease is never cut short)
    public void A_scroll_during_an_ease_goes_on_easing() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        p.Rig.ScrollTo(300);
        p.Rig.Advance(Frame);
        var a = Shown(p).Col;
        Assert.InRange(a, 20, 24);
        p.Rig.ScrollTo(305); // a wheel step in the middle of it
        Assert.Equal(a, Shown(p).Col, 6);
        p.Rig.Advance(Frame);
        Assert.True(Shown(p).Col > a);
        Assert.True(Shown(p).Col < 92);
    });

    [Theory]
    [InlineData(0, 212, 1, 0)]
    [InlineData(23, 189, 0.55, -3.5)]
    [InlineData(46, 166, 0.1, -7)]
    [InlineData(92, 120, 0, -14)]
    [Trait("Decision", "ED-251")] // CZ-T62, CZ-3.5.2: the stage's height, the text's opacity and the text's lift at k = 0, 0.25, 0.5 and 1; the composer keeps its 12 above the stage's bottom and the foot follows the stage
    public void The_stage_height_and_the_texts_opacity_and_lift_at_each_k(double offset, double height, double opacity, double lift) => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        p.Rig.ScrollTo(offset);
        var stage = p.Rig.Page.Stage!;
        var shown = Shown(p);
        Assert.Equal(height, shown.SurfaceHeight, 6);
        Assert.InRange(p.Rig.Get(() => stage.Surface.Bounds.Height), height - 0.6, height + 0.6);
        Assert.Equal(opacity, shown.Opacity, 6);
        Assert.Equal(lift, shown.Lift, 0); // whole device pixels
        // the composer's bottom is 12 above the stage's bottom (it never moves relative to it) and its size is its own
        var surfaceBottom = p.ViewportTop(stage.Surface) + p.Rig.Get(() => stage.Surface.Bounds.Height);
        var pillBottom = p.ViewportTop(stage.Preview.Pill) + p.Rig.Get(() => stage.Preview.Pill.Bounds.Height);
        Assert.Equal(12, surfaceBottom - pillBottom, 0);
        Assert.Equal(46, p.Rig.Get(() => stage.Preview.Pill.Bounds.Height), 0);
        Assert.Equal(472, p.Rig.Get(() => stage.Preview.Pill.Bounds.Width), 0);
        // the foot follows the stage's bottom, the block's bottom is 14 below the card, the slot keeps its rest height
        Assert.Equal(surfaceBottom, p.ViewportTop(stage.Foot), 0);
        Assert.Equal(372 - offset, shown.BlockBottom, 0);
        Assert.Equal(372, p.Rig.Get(() => stage.Bounds.Height));
    });
}
