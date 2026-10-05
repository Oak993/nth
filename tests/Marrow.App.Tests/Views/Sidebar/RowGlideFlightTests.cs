using Avalonia;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Testing;
using Marrow.UI.Port;
using Avalonia.Automation;

namespace Marrow.App.Tests.Views.Sidebar;

// DroppyCode/App/Views/Sidebar/RowGlide.swift (B3 52bc792), the glide layer and one flight of a ghost (:139-243), drawn by the
// headless renderer. Time moves only through the manual clock and the manual frame source. The progress of the single spring
// keyframe (response 0.6, damping ratio 0.86, :224-226) is read from the SwiftUI spring curve; every position, opacity and
// colour below is computed from the formulas of :174-224 with the literals of the Swift file, not from the port.
[Collection("Headless")]
public sealed class RowGlideFlightTests(HeadlessSession headless)
{
    private static readonly Rect From = new(10, 40, 240, 28);
    private static readonly Rect To = new(60, 150, 180, 36);
    private static readonly Rect Everywhere = new(0, 0, 400, 300);

    private static double Progress(double seconds) => Animation.Spring(new Spring(0.6, 0.86)).Progress(seconds);

    private static double Smoothstep(double edge0, double edge1, double x)
    {
        var t = Math.Min(Math.Max((x - edge0) / (edge1 - edge0), 0), 1);
        return t * t * (3 - 2 * t);
    }

    private static double Lerp(double a, double b, double p) => a + (b - a) * p;

    private static Rect Frame(double p) => new(Lerp(From.X, To.X, p), Lerp(From.Y, To.Y, p), Lerp(From.Width, To.Width, p), Lerp(From.Height, To.Height, p));

    private static double Lift(double p) => Math.Min(Smoothstep(0, 0.12, p), 1 - Smoothstep(0.7, 1, p));

    // ED-NEW-glass-g3: the lift's shadow (:208, black 0.28 x lift, radius 10, y 4) is the filled shape's own coverage cast at the fill's alpha (the
    // label colour's 0.85 x fill) and drawn under the fill, through it: its darkness along the ghost's middle column (the ghost's width covers the
    // blur) at row y, for a ghost of fill `fill` in `frame`.
    private static double Cast(double p, double fill, Rect frame, double y)
    {
        var alpha = SwiftShadow.Cast(Color.FromArgb((byte)Math.Round(0.28 * Lift(p) * 255), 0, 0, 0), Math.Round(0.85 * fill * 255) / 255).A / 255.0;
        return alpha * GaussianEdge.Coverage(y + 0.5, frame.Y + 4, frame.Bottom + 4, 10, 1);
    }

    private static bool IsGreen((int R, int G, int B) c) => c.G > c.R + 20 && c.G > c.B + 20;

    private static void AssertNear(double expected, double actual, double tolerance) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    private static void AssertColour((double R, double G, double B) expected, (int R, int G, int B) actual, double tolerance)
    {
        AssertNear(expected.R, actual.R, tolerance);
        AssertNear(expected.G, actual.G, tolerance);
        AssertNear(expected.B, actual.B, tolerance);
    }

    private static WriteableBitmap Faces(byte red, byte green, byte blue, byte alpha, int width, int height) => GlidePixels.Solid(width, height, red, green, blue, alpha);

    // ---- PR-SIDEBAR-142: the window-wide overlay that never takes clicks ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-142")] // RowGlide.swift:149-153 the layer takes no clicks and is hidden from accessibility
    public void The_glide_layer_never_takes_clicks_and_is_hidden_from_accessibility() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, Faces(255, 0, 0, 255, 240, 28), Faces(0, 0, 255, 255, 180, 36), 0.1, 0.3);

        Assert.False(rig.Layer.IsHitTestVisible);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(rig.Layer));
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-142")] // RowGlide.swift:149-153 a click during a flight lands on whatever is underneath
    public void A_click_on_the_flying_ghost_lands_on_the_pane_underneath() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, Faces(255, 0, 0, 255, 240, 28), Faces(0, 0, 255, 255, 180, 36), 0.1, 0.3);
        rig.Step(0.1);
        var frame = Frame(Progress(0.1));
        var middle = new Point(frame.X + 120, frame.Y + 10);
        var drawn = rig.Snapshot().At((int)middle.X, (int)middle.Y);
        Assert.True(drawn.R > drawn.G + 40, "the ghost is drawn where the click lands"); // reddish: mostly the departing face

        var hit = rig.Window.InputHitTest(middle);

        Assert.Same(rig.Under, hit);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-142")] // RowGlide.swift:139-148 every in-flight ghost is drawn top-left aligned in a layer the size of the window
    public void Every_in_flight_ghost_is_drawn_in_a_layer_the_size_of_the_window_aligned_top_left() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, Faces(255, 0, 0, 255, 240, 28), Faces(0, 0, 255, 255, 180, 36), 0.1, 0.3);
        var other = Guid.Parse("00000000-0000-0000-0000-0000000000B8");
        runtime.OnMain(() => rig.Animator.Launch(other, RowGlideAnimator.Glide.Direction.Reopen, new Rect(20, 80, 200, 28), new Rect(20, 300, 200, 28), Everywhere,
            Faces(255, 0, 0, 255, 200, 28), Faces(0, 0, 255, 255, 200, 28), 0, 0));
        rig.Window.UpdateLayout();

        var ghosts = rig.Layer.Children.OfType<RowGlideView>().ToArray();

        Assert.Equal(2, ghosts.Length);
        Assert.Equal(new Rect(0, 0, 400, 300), rig.Layer.Bounds);
        Assert.All(ghosts, ghost => Assert.Equal(new Rect(0, 0, 400, 300), ghost.Bounds));
        Assert.Equal(rig.Window.ClientSize, rig.Layer.Bounds.Size);
    });

    // ---- UI-SIDEBAR-050: the gliding ghost ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-050")] // RowGlide.swift:176-181, :191-196, :203 the ghost starts on the departing frame in the departing face, clipped to a 7 pt rounded rectangle
    public void The_ghost_starts_on_the_departing_rows_frame_showing_the_departing_face_with_rounded_corners() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, Faces(255, 0, 0, 255, 240, 28), Faces(0, 0, 255, 255, 180, 36), 0.1, 0.3);

        var shot = rig.Snapshot();

        AssertColour((255, 0, 0), shot.At(130, 41), 2); // inside, on the top edge
        AssertColour((255, 0, 0), shot.At(13, 43), 2); // inside the rounded corner (about 5.7 pt from its centre)
        AssertColour((255, 255, 255), shot.At(10, 40), 2); // the corner pixel is cut away by the 7 pt radius
        AssertColour((255, 255, 255), shot.At(249, 67), 2);
        AssertColour((255, 255, 255), shot.At(130, 38), 2); // above the row
        AssertColour((255, 255, 255), shot.At(130, 71), 2); // and below it: no lift yet, so no shadow
        AssertColour((255, 255, 255), shot.At(255, 54), 2);
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-050")] // RowGlide.swift:176-181, :191-200 the ghost ends on the destination frame in the arriving face
    public void The_ghost_ends_on_the_destination_frame_showing_the_arriving_face() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, Faces(255, 0, 0, 255, 240, 28), Faces(0, 0, 255, 255, 180, 36), 0.1, 0.3);
        rig.Step(0.6); // the spring has all but settled; the ghost leaves at 0.77 s

        var shot = rig.Snapshot();

        AssertColour((0, 0, 255), shot.At(150, 168), 2);
        AssertColour((0, 0, 255), shot.At(65, 155), 2); // inside the destination's top-left corner
        AssertColour((255, 255, 255), shot.At(150, 148), 2); // just above it
        AssertColour((255, 255, 255), shot.At(150, 190), 2); // just below it: the lift is gone by the landing
        AssertColour((255, 255, 255), shot.At(20, 50), 2); // nothing is left on the departing frame
        AssertColour((255, 255, 255), shot.At(245, 168), 2);
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-050")] // RowGlide.swift:176-181 the rectangle is interpolated linearly between the two frames, size included
    public void The_ghosts_rectangle_is_the_linear_interpolation_of_the_two_frames_at_the_springs_progress() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var clear = Faces(0, 0, 0, 0, 240, 28);
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, clear, clear, 0.3, 0.3);
        var time = 0.0;
        foreach (var step in new[] { 0.05, 0.05, 0.05, 0.1 })
        {
            rig.Step(step);
            time += step;
            var expected = Frame(Progress(time));
            // The fill under the row (0.3 of the label colour) is the one thing that is darker than the lift's shadow.
            var drawn = rig.Snapshot().BoundsOf(c => c.R < 205 && Math.Abs(c.R - c.G) < 4 && Math.Abs(c.R - c.B) < 4)!.Value;
            AssertNear(expected.X, drawn.X, 1.5);
            AssertNear(expected.Y, drawn.Y, 1.5);
            AssertNear(expected.Width, drawn.Width, 2);
            AssertNear(expected.Height, drawn.Height, 2);
        }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-050")] // RowGlide.swift:189-200 both faces sit at the ghost's top-left corner, each at the size it was drawn at
    public void Both_faces_are_anchored_at_the_ghosts_top_left_at_the_size_each_was_drawn_at() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, Faces(255, 0, 0, 255, 240, 28), Faces(0, 0, 255, 255, 180, 36), 0.1, 0.3);
        rig.Step(0.1); // the departing face is still the one showing
        var p = Progress(0.1);
        var frame = Frame(p);

        var drawn = rig.Snapshot().BoundsOf(c => c.R - c.B > 60)!.Value; // the reddish pixels: the departing face

        AssertNear(frame.X, drawn.X, 1.5);
        AssertNear(frame.Y, drawn.Y, 1.5);
        AssertNear(frame.Width, drawn.Width, 2); // clipped to the narrower frame
        AssertNear(28, drawn.Height, 1.5); // 28 pt, the face's own height, not the frame's 30.4 pt: the face is not stretched to the frame
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-050")] // RowGlide.swift:184, :189-200 the departing face fades out between 20 and 65 percent, the arriving one in between 35 and 80
    public void The_faces_crossfade_over_the_fill_with_the_published_windows() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, Faces(255, 0, 0, 255, 240, 28), Faces(0, 0, 255, 255, 180, 36), 0.1, 0.3);
        var time = 0.0;
        foreach (var step in new[] { 0.05, 0.05, 0.05, 0.05, 0.1, 0.1 })
        {
            rig.Step(step);
            time += step;
            var p = Progress(time);
            var frame = Frame(p);
            var fill = Math.Max(Lerp(0.1, 0.3, p), 0.12 * Lift(p));
            var under = 255 * (1 - 0.85 * fill); // the fill is the label colour (black at 0.85, light appearance) at `fill`, over the white pane
            var departing = 1 - Smoothstep(0.2, 0.65, p);
            var arriving = Smoothstep(0.35, 0.8, p);
            (double R, double G, double B) pixel = (under, under, under);
            pixel = (pixel.R * (1 - departing) + 255 * departing, pixel.G * (1 - departing), pixel.B * (1 - departing));
            pixel = (pixel.R * (1 - arriving), pixel.G * (1 - arriving), pixel.B * (1 - arriving) + 255 * arriving);

            var shot = rig.Snapshot();

            AssertColour(pixel, shot.At((int)(frame.X + 30), (int)(frame.Y + 10)), 5);
        }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-050")] // RowGlide.swift:183-184, :206-208 the fill is overlay(interpolated fill), floored at 0.12 times the lift
    public void The_fill_interpolates_from_the_departing_to_the_arriving_fill_and_is_floored_by_the_lift() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var clear = Faces(0, 0, 0, 0, 240, 28);
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, clear, clear, 0.1, 0.3);
        var shot = rig.Snapshot();
        AssertNear(255 * (1 - 0.85 * 0.1), shot.At(130, 54).R, 3); // at the start: the hover fill the row had

        rig.Step(0.45); // landed: p is 0.993, the lift is gone
        var landed = rig.Snapshot().At(150, 168).R;
        AssertNear(255 * (1 - 0.85 * Lerp(0.1, 0.3, Progress(0.45))), landed, 3);
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-050")] // RowGlide.swift:183-184 with no fill at either end, the lift alone gives the row 0.12 of fill
    public void With_no_fill_at_either_end_the_lift_gives_the_ghost_a_fill_of_012_times_the_lift() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var clear = Faces(0, 0, 0, 0, 240, 28);
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, clear, clear, 0, 0);
        AssertNear(255, rig.Snapshot().At(130, 54).R, 2); // no lift, no fill

        var time = 0.0;
        foreach (var step in new[] { 0.05, 0.1, 0.1 })
        {
            rig.Step(step);
            time += step;
            var p = Progress(time);
            var frame = Frame(p);
            // the fill over its own shadow (ED-NEW-glass-g3: the shadow is drawn under the fill, so it shows through it)
            var y = (int)(frame.Y + 14);
            AssertNear(255 * (1 - Cast(p, 0.12 * Lift(p), frame, y)) * (1 - 0.85 * 0.12 * Lift(p)), rig.Snapshot().At((int)(frame.X + 120), y).R, 3);
        }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-050")] // RowGlide.swift:183, :206-208 the lift's shadow, black at 0.28 times the lift, drops 4 pt, and is gone at the start and the end
    public void The_lift_casts_a_shadow_that_falls_below_the_row_and_is_gone_at_both_ends() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var clear = Faces(0, 0, 0, 0, 240, 28);
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, clear, clear, 0, 0);
        var start = rig.Snapshot();
        AssertNear(255, start.At(130, 71).R, 1); // below the row at the start
        AssertNear(255, start.At(130, 36).R, 1);

        rig.Step(0.1);
        var frame = Frame(Progress(0.1));
        var lifted = rig.Snapshot();
        var below = lifted.At((int)(frame.X + 120), (int)(frame.Bottom + 3)).R;
        var above = lifted.At((int)(frame.X + 120), (int)(frame.Y - 4)).R;
        // ED-NEW-glass-g3: cast at the fill's alpha (0.28 x lift x 0.85 x 0.12 x lift; the colour's full 0.28 x lift drew 20 levels and more below
        // the row): a few levels, more below the row than above it (y offset 4).
        var p1 = Progress(0.1);
        var (belowDesign, aboveDesign) = (255 * (1 - Cast(p1, 0.12 * Lift(p1), frame, (int)(frame.Bottom + 3))), 255 * (1 - Cast(p1, 0.12 * Lift(p1), frame, (int)(frame.Y - 4))));
        Assert.True(belowDesign < 253.5, $"the instrument: the design casts {255 - belowDesign:F1} levels below the row");
        AssertNear(belowDesign, below, 1.5);
        AssertNear(aboveDesign, above, 1.5);
        Assert.True(below < above, "the shadow falls more below the row than above it (y offset 4)");
        AssertNear(255, lifted.At((int)(frame.X + 120), (int)(frame.Bottom + 40)).R, 1);

        rig.Step(0.35); // landed
        var landed = Frame(Progress(0.45));
        AssertNear(255, rig.Snapshot().At((int)(landed.X + 120), (int)(landed.Bottom + 3)).R, 2);
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-050")] // RowGlide.swift:222-223 the flight is clipped to the list's own rectangle
    public void The_flight_is_clipped_to_the_lists_own_rectangle() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var clear = Faces(0, 0, 0, 0, 240, 28);
        var list = new Rect(0, 0, 200, 150);
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, new Rect(10, 100, 240, 28), new Rect(10, 200, 240, 28), list, clear, clear, 0.3, 0.3);
        rig.Step(0.1); // p is 0.30: the ghost spans y 130 to 158, x 10 to 250
        var shot = rig.Snapshot();

        // inside the list: drawn, the fill over its own shadow (ED-NEW-glass-g3: drawn under the fill, through it)
        var under = 255 * (1 - Cast(0.3, 0.3, new Rect(10, 130, 240, 28), 140)) * (1 - 0.85 * 0.3);
        AssertNear(under, shot.At(100, 140).R, 3);
        AssertNear(under, shot.At(130, 140).R, 3);
        AssertColour((255, 255, 255), shot.At(100, 153), 1); // inside the ghost but below the list's bottom edge: clipped away
        AssertColour((255, 255, 255), shot.At(100, 156), 1); // and so is its shadow
        AssertColour((255, 255, 255), shot.At(210, 140), 1); // right of the list's edge
    });

    // ---- UI-SIDEBAR-051: the travelling check ----

    private static Rect CheckArea => new(0, 0, 400, 300);

    [Fact]
    [Trait("Req", "UI-SIDEBAR-051")] // RowGlide.swift:186-188, :211-220 settling: the filled check travels from 36 pt inside the trailing edge to 16 pt inside the leading edge at the ghost's vertical centre
    public void The_settling_check_crosses_from_the_trailing_to_the_leading_end_at_the_ghosts_vertical_centre() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var clear = Faces(0, 0, 0, 0, 240, 28);
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, clear, clear, 0, 0);
        var time = 0.0;
        foreach (var step in new[] { 0.0, 0.05, 0.05, 0.05, 0.1, 0.1, 0.1 })
        {
            rig.Step(step);
            time += step;
            var p = Progress(time);
            var frame = Frame(p);
            var start = frame.Right - 36;
            var end = frame.Left + 16;
            var expectedX = start + (end - start) * Smoothstep(0.05, 0.95, p);

            var centre = rig.Snapshot().CentreOf(CheckArea, IsGreen)!.Value;

            AssertNear(expectedX, centre.X, 1.5);
            AssertNear(frame.Center.Y, centre.Y, 1.5);
        }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-051")] // RowGlide.swift:218-219 settling: the filled mark eases from 1.35 to 1.0 scale over the first half and fades to 0.8 opacity by the end
    public void The_settling_check_is_the_filled_mark_that_shrinks_over_the_first_half_and_fades_to_08() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var clear = Faces(0, 0, 0, 0, 240, 28);
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Settle, From, To, Everywhere, clear, clear, 0, 0);
        var success = Marrow.UI.Chrome.Chrome.Success.Resolve(ThemeVariant.Light);

        var first = rig.Snapshot();
        var firstBounds = first.BoundsOf(IsGreen)!.Value;
        var fullChroma = MaxChroma(first);
        AssertNear(success.G - success.R, fullChroma, 3); // the success colour at full opacity
        var filledPixels = CountGreen(first);

        var time = 0.0;
        foreach (var step in new[] { 0.15, 0.1, 0.2 })
        {
            rig.Step(step);
            time += step;
            var p = Progress(time);
            var shot = rig.Snapshot();
            AssertNear((1 - 0.2 * p) * fullChroma, MaxChroma(shot), 0.04 * fullChroma); // opacity 1 - 0.2 p
            var scaleRatio = (1.35 - 0.35 * Smoothstep(0, 0.5, p)) / 1.35;
            AssertNear(firstBounds.Width * scaleRatio, shot.BoundsOf(IsGreen)!.Value.Width, 1.6);
        }
        // The outline mark of the reopening direction covers far fewer pixels than this filled one.
        using var reopening = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Reopen, From, To, Everywhere, clear, clear, 0, 0);
        Assert.True(filledPixels > 2 * CountGreen(reopening.Snapshot()));
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-051")] // RowGlide.swift:186-188, :218-220 reopening: the outline mark travels from the leading to the trailing end at constant scale and fades out between 55 and 90 percent
    public void The_reopening_check_is_the_outline_mark_that_crosses_the_other_way_and_fades_out_late() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var clear = Faces(0, 0, 0, 0, 240, 28);
        using var rig = new RowGlideFlightRig(runtime, RowGlideAnimator.Glide.Direction.Reopen, From, To, Everywhere, clear, clear, 0, 0);
        var first = rig.Snapshot();
        var fullChroma = MaxChroma(first);
        var width = first.BoundsOf(IsGreen)!.Value.Width;
        var time = 0.0;
        foreach (var step in new[] { 0.0, 0.05, 0.05, 0.05, 0.1, 0.1 })
        {
            rig.Step(step);
            time += step;
            var p = Progress(time);
            var frame = Frame(p);
            var start = frame.Left + 16; // reopening goes the other way: from the front to beside the ellipsis
            var end = frame.Right - 36;
            var shot = rig.Snapshot();

            AssertNear((1 - Smoothstep(0.55, 0.9, p)) * fullChroma, MaxChroma(shot), 0.05 * fullChroma);
            if (p > 0.55) continue; // fading: the mark is partly there, its position no longer measured
            var centre = shot.CentreOf(CheckArea, IsGreen)!.Value;
            AssertNear(start + (end - start) * Smoothstep(0.05, 0.95, p), centre.X, 1.5);
            AssertNear(frame.Center.Y, centre.Y, 1.5);
            AssertNear(width, shot.BoundsOf(IsGreen)!.Value.Width, 1.2); // constant scale
        }
        rig.Step(0.15);
        Assert.Null(rig.Snapshot().BoundsOf(IsGreen)); // gone once the row is open
    });

    private static double MaxChroma(RowGlideSnapshot shot)
    {
        var best = 0;
        for (var y = 0; y < shot.Height; y++)
        {
            for (var x = 0; x < shot.Width; x++)
            {
                var c = shot.At(x, y);
                best = Math.Max(best, c.G - c.R);
            }
        }
        return best;
    }

    private static int CountGreen(RowGlideSnapshot shot)
    {
        var count = 0;
        for (var y = 0; y < shot.Height; y++)
        {
            for (var x = 0; x < shot.Width; x++)
            {
                if (IsGreen(shot.At(x, y))) count++;
            }
        }
        return count;
    }
}
