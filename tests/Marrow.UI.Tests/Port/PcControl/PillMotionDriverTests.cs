// PORT-ONLY: the Tactus pill's motion driver (OD-0145, ED-NEW-pcctl-22 item 5, ED-NEW-pcctl-24; DESIGN.md 10.6, 10.11
// "MotionDriver"): one layout at the final pose, then key frames that carry each part from where it is drawn now to that
// layout (a FLIP: every track ends at zero offset), the surfaces handed the motion, a held pose set at once, and the switch
// that picks who draws the glass while it moves.
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-24")]
public sealed class PillMotionDriverTests(HeadlessSession headless)
{
    private static readonly PillParts Parts = new(28, 170, 90, true);

    private static PillMotion Still(double x = 400, double width = 300) =>
        PillMotion.Still(new PillPose(new PillVector(x, 8), 0.5, width, 48, 0, 1, 1, 1, 1), 0);

    [Fact]
    public void A_motion_lays_out_once_at_its_end_and_every_track_ends_at_that_layout() => headless.OnUiThread(() =>
    {
        var targets = new FakePillTargets();
        var animator = new RecordingPillAnimator();
        var driver = new PillMotionDriver(targets, animator);
        var motion = Still().TravelTo(0, new PillVector(900, 8), false).Widen(0, 380, false);

        driver.Run(motion, Parts, 0);

        var layout = Assert.Single(targets.Layouts);
        Assert.Equal(motion.Final, layout.Pose);
        Assert.Equal(PillPlaces.Of(motion.Final, Parts), layout.Places);
        var stage = animator.Last(targets.Stage, "Translate")!;
        // the stage starts where the pill is drawn now (500 DIP back) and ends at the layout
        Assert.Equal(400 - 900, stage.X!.Values[0], 6);
        Assert.Equal(0, stage.X.Last, 9);
        Assert.Equal(0, stage.Y!.Last, 9);
        Assert.Equal(motion.DoneAt(), stage.X.Start + stage.X.Duration, 6);
        // the group, Stop and the row move from their places at the start width to their final places
        var start = PillPlaces.Of(motion.At(0), Parts);
        var end = PillPlaces.Of(motion.Final, Parts);
        Assert.Equal(start.GroupLeft - end.GroupLeft, animator.Last(targets.Group, "Translate")!.X!.Values[0], 6);
        Assert.Equal(0, animator.Last(targets.Group, "Translate")!.X!.Last, 9);
        Assert.Equal(start.StopLeft - end.StopLeft, animator.Last(targets.StopSlot, "Translate")!.X!.Values[0], 6);
        Assert.Equal(start.RowLeft - end.RowLeft, animator.Last(targets.Row, "Translate")!.X!.Values[0], 6);
        var text = animator.Last(targets.TextClip, "Resize")!;
        Assert.Equal(start.TextClip, text.X!.Values[0], 6);
        Assert.Equal(end.TextClip, text.X.Last, 6);
        Assert.Equal(PillMotionDriver.TextClipHeight, text.Y!.Last, 9);
        var stop = animator.Last(targets.StopClip, "Resize")!;
        Assert.Equal(end.StopVisible - PillMotionDriver.StopClipInset, stop.X!.Last, 6);
        // the scale does not change on a travel; the opacity is set, not faded
        Assert.Equal(1, animator.Last(targets.Stage, "SetScale")!.A, 9);
        Assert.Equal(1, animator.Last(targets.Content, "SetOpacity")!.A, 9);
        Assert.Null(animator.Last(targets.Content, "Fade"));
        // both surfaces got the motion (the render-thread driver evaluates it)
        Assert.Same(motion, targets.Body.Last!.Motion);
        Assert.Same(motion, targets.StopSurface.Last!.Motion);
        Assert.Same(motion, driver.Current);
    });

    [Fact]
    public void The_scale_carries_its_offset_in_the_stage_s_travel() => headless.OnUiThread(() =>
    {
        var targets = new FakePillTargets();
        var animator = new RecordingPillAnimator();
        var driver = new PillMotionDriver(targets, animator);
        var hidden = PillMotion.Hidden(new PillVector(400, 8), 0.5, 300, 48, 0);
        var arrive = hidden.Arrive(0, reduceMotion: false);

        driver.Run(arrive, Parts, 0);

        var scale = animator.Last(targets.Stage, "Scale")!;
        Assert.Equal(0, scale.X!.Values[0], 9);
        Assert.Equal(1, scale.X.Last, 9);
        var stage = animator.Last(targets.Stage, "Translate")!;
        // it grows about its anchor: outside a lift the scale needs no offset, so the stage does not travel
        Assert.Equal(0, arrive.At(0).ScaleOffset.Y, 9);
        Assert.Equal(0, stage.Y!.Values[0], 9);
        Assert.Equal(0, stage.Y.Last, 9);
        Assert.True(stage.Y.Constant);
    });

    [Fact]
    public void A_held_pose_and_a_motion_at_rest_are_set_at_once() => headless.OnUiThread(() =>
    {
        var targets = new FakePillTargets();
        var animator = new RecordingPillAnimator();
        var driver = new PillMotionDriver(targets, animator);
        var pose = new PillPose(new PillVector(500, 8), 0.5, 320, 48, 1, 1, 1, 1, 1);

        driver.Hold(pose, Parts, 3);

        Assert.Equal(pose, Assert.Single(targets.Layouts).Pose);
        Assert.DoesNotContain(animator.Calls, c => c.Kind is "Translate" or "Resize" or "Scale" or "Fade");
        var places = PillPlaces.Of(pose, Parts);
        Assert.Equal(places.TextClip, animator.Last(targets.TextClip, "SetSize")!.A, 9);
        Assert.Equal(places.StopVisible - PillMotionDriver.StopClipInset, animator.Last(targets.StopClip, "SetSize")!.A, 9);
        Assert.Equal(1, animator.Last(targets.Stage, "SetScale")!.A, 9);
        Assert.Null(targets.Body.Last!.Motion);
        Assert.Equal(pose, targets.Body.Last.Pose);
        Assert.True(driver.Current!.AtRest);

        animator.Clear();
        driver.Run(Still(), Parts, 4);
        Assert.DoesNotContain(animator.Calls, c => c.Kind is "Translate" or "Resize" or "Scale" or "Fade");
        Assert.Equal(0, animator.Last(targets.Row, "SetTranslation")!.A, 9);
    });

    [Fact]
    public void The_key_frame_driver_fills_its_animated_box() => headless.OnUiThread(() =>
    {
        var targets = new FakePillTargets(PillDriverKind.KeyFrames);
        var driver = new PillMotionDriver(targets, new RecordingPillAnimator());
        var motion = Still().Widen(0, 400, false);

        driver.Run(motion, Parts, 0);

        Assert.True(targets.Body.Last!.Fill);
        Assert.Null(targets.Body.Last.Motion);
        Assert.Equal(PillDriverKind.KeyFrames, targets.Body.Driver);
    });

    [Theory]
    [InlineData(null, PillDriverKind.RenderThread)]
    [InlineData("", PillDriverKind.RenderThread)]
    [InlineData("keyframes", PillDriverKind.KeyFrames)]
    [InlineData(" KeyFrames ", PillDriverKind.KeyFrames)]
    [InlineData("fallback", PillDriverKind.KeyFrames)]
    [InlineData("render-thread", PillDriverKind.RenderThread)]
    [InlineData("something else", PillDriverKind.RenderThread)]
    public void The_switch_picks_the_driver(string? value, PillDriverKind expected) =>
        Assert.Equal(expected, PillDriverSwitch.Parse(value));

    [Fact]
    public void The_render_thread_surfaces_cover_any_pill_round_the_anchor()
    {
        // the body's fixed area holds the widest card (1,400 DIP across the anchor) and its deepest shadow
        Assert.True(PillSurface.BodyArea.X <= -PillLayout.MaxWidth - PillSurfacePainter.Pad);
        Assert.Equal(-PillSurfacePainter.Pad, PillSurface.BodyArea.Y, 9);
        Assert.True(PillSurface.BodyArea.Right >= PillLayout.MaxWidth + PillSurfacePainter.Pad);
        // Stop's area starts at its slot and holds its ring
        Assert.Equal(-PillSurfacePainter.StopPad, PillSurface.StopArea.X, 9);
        Assert.Equal(PillSurfacePainter.StopHeight + (2 * PillSurfacePainter.StopPad), PillSurface.StopArea.Height, 9);
        Assert.Equal(PillSurface.StopArea, PillSurface.AreaOf(PillSurfaceKind.Stop));
        // Stop's shape is its visible capsule from the slot's left edge
        var pose = new PillPose(new PillVector(400, 8), 0.5, 300, 48, 1, 1, 1, 1, 1);
        Assert.Equal(new Avalonia.Rect(0, 0, 80, PillSurfacePainter.StopHeight), PillSurfacePainter.ShapeOf(PillSurfaceKind.Stop, pose, Parts));
        Assert.Equal(new Avalonia.Rect(-150, 0, 300, 48), PillSurfacePainter.ShapeOf(PillSurfaceKind.Body, pose, Parts));
    }
}
