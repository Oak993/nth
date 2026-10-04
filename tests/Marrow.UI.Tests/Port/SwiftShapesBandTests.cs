using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// OD-0102 (owner, 2026-10-04, "corner-band: A"): a `.continuous` shape whose height sits in ED-275 (2)'s band (Apple's corner does not fit at the
// stated radius, 2 * 1.528665 * r > min(w, h), yet r is under half the shorter side; for r 18 that is 36 < h < 55) is drawn as Apple's continuous
// corner at the largest radius that fits, ContinuousCorner.LargestRadius = min(w, h) / (2 * 1.528665), instead of a circular arc. Capsules (r at least
// half the shorter side) and shapes large enough for the full corner are unchanged. EXCEPTION, SOURCE FACT from the reference video (ED-275 (2)): the
// composer pill (r 22 on 46 pt) is measured as the circular arc (0.156 px rms; the clamped continuous corner 1.148 px rms), so it stays circular.
// The expected control points are NOT read back from the port: they are SPEC.md 1.2's test vector (the top-left corner of r = 20, typed in
// SwiftShapesContinuousTests) scaled by L / 20 and mirrored per corner, with L = min(w, h) / (2 * 1.528665) typed here from the rule.
// Swift: the sites are TimelineRows.swift:2385-2431 (notice), :2598-2701 (steps card), :2735-2827 (Edited-files card), :652-783 (delegation card),
// ComposerView.swift:1298 (composer); the shape rule is the owner's, not the Swift's (the Swift states `.continuous` and nothing more).
[Collection("Headless")]
public sealed class SwiftShapesBandTests(HeadlessSession headless)
{
    private const double E = 1.528665;

    // SPEC.md 1.2 test vector (r = 20, top-left corner of a 200 x 100 rect at the origin), as (x, y) from the vertex.
    private static readonly (double X, double Y)[][] TopLeftVector =
    [
        [(30.5733, 0), (21.7698592, 0), (17.3681388, 0), (12.6298758, 1.4982278)],
        [(12.6298758, 1.4982278), (7.4564766, 3.3811912), (3.3811912, 7.4564766), (1.4982278, 12.6298758)],
        [(1.4982278, 12.6298758), (0, 17.3681388), (0, 21.7698592), (0, 30.5732996)],
    ];

    /// <summary>The 12 cubics (start, control 1, control 2, end) of a w x h shape whose four corners are Apple's at radius <paramref name="radius"/>.</summary>
    private static List<Point[]> Expected(double w, double h, double radius)
    {
        var scale = radius / 20;
        var all = new List<Point[]>();
        foreach (var mirror in new Func<double, double, Point>[]
                 {
                     (x, y) => new Point(x, y),
                     (x, y) => new Point(w - x, y),
                     (x, y) => new Point(w - x, h - y),
                     (x, y) => new Point(x, h - y),
                 })
            foreach (var cubic in TopLeftVector) all.Add(cubic.Select(p => mirror(p.X * scale, p.Y * scale)).ToArray());
        return all;
    }

    private static List<Point[]> CubicsOf(ShapeOutline outline)
    {
        var cubics = new List<Point[]>();
        var current = new Point();
        foreach (var s in outline.Segments)
        {
            if (s.Verb == PathVerb.Cubic) cubics.Add([current, s.Control1, s.Control2, s.End]);
            current = s.End;
        }
        return cubics;
    }

    private static bool Close(Point a, Point b, double tolerance) => Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    /// <summary>Whether the cubic is the expected one or its reverse (the corners are drawn clockwise, the vector from the edge).</summary>
    private static bool Same(Point[] a, Point[] b, double tolerance) =>
        (Close(a[0], b[0], tolerance) && Close(a[1], b[1], tolerance) && Close(a[2], b[2], tolerance) && Close(a[3], b[3], tolerance))
        || (Close(a[0], b[3], tolerance) && Close(a[1], b[2], tolerance) && Close(a[2], b[1], tolerance) && Close(a[3], b[0], tolerance));

    private static void AssertSameCubics(List<Point[]> actual, List<Point[]> expected, double tolerance, string name)
    {
        Assert.Equal(expected.Count, actual.Count);
        var unmatched = new List<Point[]>(expected);
        foreach (var cubic in actual)
        {
            var index = unmatched.FindIndex(e => Same(cubic, e, tolerance));
            Assert.True(index >= 0, $"{name}: no expected cubic matches ({cubic[0]}) ({cubic[1]}) ({cubic[2]}) ({cubic[3]})");
            unmatched.RemoveAt(index);
        }
    }

    // ---- the band: the sizes of the four records and the corner-band page's own ---------------------------------------

    // 40 pt (the Edited-files card), 43 pt (a turn of one step), 48 pt (the corner-band page's size), 50 pt (a notice two lines tall, mounted at 380 pt wide: 16 + two callout lines of 17, ED-290 (1)) and 54 pt (a delegation card of one head), all r 18.
    // The corner-band page's JS port (cb-geometry.js optionA) draws the same: Outline(rect, min(r, LargestRadius), .continuous).
    [Theory]
    [Trait("Decision", "ED-181")]
    [InlineData(380, 40)]
    [InlineData(380, 43)]
    [InlineData(380, 48)]
    [InlineData(380, 50)] // a two-line notice
    [InlineData(380, 54)]
    public void A_shape_in_the_band_draws_Apples_corner_at_the_largest_radius_that_fits(double w, double h)
    {
        const double r = 18;
        Assert.True(2 * E * r > h && r < h / 2, $"{h} pt is not in the band for r {r}");
        var largest = h / (2 * E); // the largest radius whose two run-ins fit the shorter side
        var outline = SwiftShapes.Outline(new Rect(0, 0, w, h), new CornerRadius(r), RoundedCornerStyle.Continuous);
        Assert.All(outline.Corners, k => Assert.Equal(CornerKind.Continuous, k));
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(largest, outline.DrawnRadius(i), 9));
        Assert.Equal(largest, ContinuousCorner.LargestRadius(new Size(w, h)), 9);
        AssertSameCubics(CubicsOf(outline), Expected(w, h, largest), 1e-6, $"{w} x {h}, the corner at radius {largest:F4}");
        // The two run-ins of a short side meet at its middle: the corner starts E * largest = h / 2 from the vertex along each edge.
        Assert.True(Close(outline.Segments[0].End, new Point(h / 2, 0), 1e-6), "the top-left corner's run-in along the top edge ends at h / 2");
        Assert.Equal(PathVerb.Line, outline.Segments[1].Verb);
        Assert.DoesNotContain(outline.Segments, s => s.Verb == PathVerb.Arc); // no circular arc remains
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_band_runs_from_the_capsule_to_the_full_corner_and_each_boundary_is_exact()
    {
        const double r = 18;
        var full = 2 * E * r; // 55.0319: the full corner fits
        // At and above the full-corner height the stated radius is Apple's, unchanged.
        foreach (var h in new[] { full, full + 0.01, 80.0, 280.0 })
        {
            var outline = SwiftShapes.Outline(new Rect(0, 0, 380, h), new CornerRadius(r), RoundedCornerStyle.Continuous);
            Assert.All(outline.Corners, k => Assert.Equal(CornerKind.Continuous, k));
            Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(r, outline.DrawnRadius(i), 9));
        }
        // Just under it the radius shrinks continuously: the largest radius that fits is just under the stated one.
        var below = SwiftShapes.Outline(new Rect(0, 0, 380, full - 0.5), new CornerRadius(r), RoundedCornerStyle.Continuous);
        Assert.All(below.Corners, k => Assert.Equal(CornerKind.Continuous, k));
        Assert.Equal((full - 0.5) / (2 * E), below.DrawnRadius(0), 9);
        Assert.True(below.DrawnRadius(0) < r && r - below.DrawnRadius(0) < 0.25);
        // Just over twice the radius (a hair above a capsule) is in the band; exactly twice the radius, a capsule, is the circular arc.
        var hairOver = SwiftShapes.Outline(new Rect(0, 0, 380, 2 * r + 0.01), new CornerRadius(r), RoundedCornerStyle.Continuous);
        Assert.All(hairOver.Corners, k => Assert.Equal(CornerKind.Continuous, k));
        Assert.Equal((2 * r + 0.01) / (2 * E), hairOver.DrawnRadius(0), 9);
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_capsule_regime_is_unchanged_circular_ends_at_half_the_shorter_side_or_more()
    {
        // Every single-line pill (34 pt tall, r 18), a 28 pt glass capsule, a 60 x 22 pill at r 11, and a radius past half the height.
        foreach (var (w, h, r) in new[] { (300.0, 34.0, 18.0), (140.0, 28.0, 14.0), (60.0, 22.0, 11.0), (200.0, 36.0, 18.0), (200.0, 40.0, 30.0), (60.0, 22.0, 20.0) })
        {
            var rect = new Rect(0, 0, w, h);
            var continuous = SwiftShapes.Outline(rect, new CornerRadius(r), RoundedCornerStyle.Continuous);
            var circular = SwiftShapes.Outline(rect, new CornerRadius(r), RoundedCornerStyle.Circular);
            Assert.All(continuous.Corners, k => Assert.Equal(CornerKind.Arc, k));
            Assert.Equal(circular.Segments, continuous.Segments);
            Assert.DoesNotContain(continuous.Segments, s => s.Verb == PathVerb.Cubic);
            Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(Math.Min(r, h / 2), continuous.DrawnRadius(i), 9));
        }
        Assert.False(SwiftShapes.HasContinuousCorner(new Size(300, 34), new CornerRadius(18), RoundedCornerStyle.Continuous));
        // Capsule(style: .continuous) sizes its radius to exactly half the shorter side, whatever the style.
        var capsule = SwiftShapes.Outline(new Rect(0, 0, 90, 30), new CornerRadius(SwiftShapes.CapsuleCornerRadius(new Size(90, 30))), RoundedCornerStyle.Continuous);
        Assert.All(capsule.Corners, k => Assert.Equal(CornerKind.Arc, k));
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_full_corner_regime_is_unchanged_Apples_corner_at_the_stated_radius()
    {
        var outline = SwiftShapes.Outline(new Rect(0, 0, W, H), new CornerRadius(20), RoundedCornerStyle.Continuous);
        AssertSameCubics(CubicsOf(outline), Expected(W, H, 20), 1e-6, "200 x 100 at r 20");
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(20, outline.DrawnRadius(i), 9));
        // A `.circular` style is circular at every size, the band included.
        var circular = SwiftShapes.Outline(new Rect(0, 0, 380, 40), new CornerRadius(18), RoundedCornerStyle.Circular);
        Assert.All(circular.Corners, k => Assert.Equal(CornerKind.Arc, k));
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(18, circular.DrawnRadius(i), 9));
    }

    private const double W = 200, H = 100;

    [Fact]
    [Trait("Decision", "ED-181")]
    public void An_uneven_shape_takes_the_band_per_corner_and_keeps_its_square_and_fitting_corners()
    {
        // 200 x 40, radii 18 except a square bottom-left: the left side holds TL (18) and the square BL (0): E * 18 = 27.5 <= 40, so TL keeps 18; the right side
        // holds TR and BR, E * 36 = 55 > 40 and both radii are under half of 40: both are in the band, at the largest radius that fits, 40 / (2 E).
        var outline = SwiftShapes.Outline(new Rect(0, 0, 200, 40), new CornerRadius(18, 18, 18, 0), RoundedCornerStyle.Continuous);
        Assert.Equal([CornerKind.Continuous, CornerKind.Continuous, CornerKind.Continuous, CornerKind.Square], outline.Corners);
        var largest = 40 / (2 * E);
        Assert.Equal(18, outline.DrawnRadius(0), 9);
        Assert.Equal(largest, outline.DrawnRadius(1), 9);
        Assert.Equal(largest, outline.DrawnRadius(2), 9);
        Assert.Equal(0, outline.DrawnRadius(3), 9);
        // No two neighbouring corners overlap along a side they share.
        Assert.True(E * (outline.DrawnRadius(1) + outline.DrawnRadius(2)) <= 40 + 1e-9);
        Assert.True(E * (outline.DrawnRadius(0) + outline.DrawnRadius(3)) <= 40 + 1e-9);
        Assert.True(E * (outline.DrawnRadius(0) + outline.DrawnRadius(1)) <= 200 + 1e-9);
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_cheap_regime_check_agrees_with_the_outline_across_the_band()
    {
        foreach (var circularInBand in new[] { false, true })
            foreach (var h in new[] { 20.0, 36.0, 36.01, 40.0, 46.0, 54.0, 55.03, 56.0, 100.0 })
                foreach (var radii in new[] { new CornerRadius(18), new CornerRadius(22), new CornerRadius(18, 18, 0, 0), new CornerRadius(18, 18, 18, 0), new CornerRadius(40) })
                    Assert.Equal(SwiftShapes.Outline(new Rect(0, 0, 380, h), radii, RoundedCornerStyle.Continuous, circularInBand).HasContinuousCorner,
                        SwiftShapes.HasContinuousCorner(new Size(380, h), radii, RoundedCornerStyle.Continuous, circularInBand));
    }

    // ---- the exception: the composer pill, measured circular -----------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_composer_pill_r_22_on_46_pt_stays_the_circular_arc_the_reference_video_measures()
    {
        // ED-275 (2), SOURCE FACT: on the composer (r 22 on 46 pt, r / h = 0.478, in the band) the reference video fits the circle at 0.156 px rms, the iOS 7
        // shape_2 at 0.212 and PaintCode's clamped continuous corner (this rule's corner, at radius 46 / (2 E) = 15.05) at 1.148, so the clamped corner is
        // rejected there; OD-0102 keeps what the frame measures. The shape says so (CircularInBand); every other shape of that size takes the band's corner.
        var rect = new Rect(0, 0, 818, 46);
        var composer = SwiftShapes.Outline(rect, new CornerRadius(22), RoundedCornerStyle.Continuous, circularInBand: true);
        var circular = SwiftShapes.Outline(rect, new CornerRadius(22), RoundedCornerStyle.Circular);
        Assert.Equal(circular.Segments, composer.Segments);
        Assert.All(composer.Corners, k => Assert.Equal(CornerKind.Arc, k));
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(22, composer.DrawnRadius(i), 9));
        Assert.False(SwiftShapes.HasContinuousCorner(rect.Size, new CornerRadius(22), RoundedCornerStyle.Continuous, circularInBand: true));
        // The same size without the exception is the band's corner, at 46 / (2 E).
        var other = SwiftShapes.Outline(rect, new CornerRadius(22), RoundedCornerStyle.Continuous);
        Assert.All(other.Corners, k => Assert.Equal(CornerKind.Continuous, k));
        Assert.Equal(46 / (2 * E), other.DrawnRadius(0), 9);
        // The exception reaches only the band: a composer tall enough for Apple's corner (2 * E * 22 = 67.3) takes it, flag or not.
        var tall = SwiftShapes.Outline(new Rect(0, 0, 818, 80), new CornerRadius(22), RoundedCornerStyle.Continuous, circularInBand: true);
        Assert.All(tall.Corners, k => Assert.Equal(CornerKind.Continuous, k));
        Assert.Equal(22, tall.DrawnRadius(0), 9);
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void CircularInBand_is_recorded_on_the_shape_and_changes_what_a_Border_clips_to_and_a_shape_fills() => headless.OnUiThread(() =>
    {
        var plain = new Border { Width = 818, Height = 46, Background = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }.RoundedRectangle(22, RoundedCornerStyle.Continuous);
        var composer = new Border { Width = 818, Height = 46, Background = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }.RoundedRectangle(22, RoundedCornerStyle.Continuous).CircularInBand();
        var shape = new SwiftShapeBorder { Width = 818, Height = 46, Background = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }.RoundedRectangle(22, RoundedCornerStyle.Continuous).CircularInBand();
        Assert.False(SwiftShapes.GetCircularInBand(plain));
        Assert.True(SwiftShapes.GetCircularInBand(composer));
        Assert.True(shape.CircularInBand);
        var window = new Window { Width = 840, Height = 200, Content = new StackPanel { Children = { plain, composer, shape } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // The plain Border in the band is cut to Apple's corner at 46 / (2 E); the composer's is not cut at all (a Border draws its own circular corner).
        Assert.Same(SwiftShapes.ClipGeometry(new Rect(0, 0, 818, 46), new CornerRadius(22), exact: true), plain.Clip); // childless: the exact outline (ED-297 (9))
        Assert.Null(composer.Clip);
        // A shape the flag was set on after the first layout follows at once.
        plain.CircularInBand();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(plain.Clip);
        plain.CircularInBand(false);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(plain.Clip);
        window.Close();
    });

    // ---- the fill, the clip and the stroke are on the same curve ------------------------------------------------------

    private static (Window Window, RenderedPixels Pixels) Render(Control content, double width, double height)
    {
        var window = new Window { Width = width, Height = height, Background = Brushes.Black, Content = new Panel { Children = { content } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return (window, RenderedPixels.Capture(window));
    }

    private static double Level(RenderedPixels pixels, double x, double y)
    {
        var px = x - 0.5;
        var py = y - 0.5;
        var x0 = (int)Math.Floor(px);
        var y0 = (int)Math.Floor(py);
        var fx = px - x0;
        var fy = py - y0;
        double v(int xx, int yy) => pixels.Level(Math.Clamp(xx, 0, pixels.Width - 1), Math.Clamp(yy, 0, pixels.Height - 1));
        return v(x0, y0) * (1 - fx) * (1 - fy) + v(x0 + 1, y0) * fx * (1 - fy) + v(x0, y0 + 1) * (1 - fx) * fy + v(x0 + 1, y0 + 1) * fx * fy;
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_band_shapes_fill_hit_shape_and_clip_follow_the_curve_at_the_largest_radius() => headless.OnUiThread(() =>
    {
        // 200 x 40, r 18 (the Edited-files card's size class): L = 40 / (2 E) = 13.085. Apple's curve at that radius crosses the corner's diagonal at
        // (0.2915 L, 0.2915 L) = (3.81, 3.81) from the vertex; a circular arc of radius 18 crosses it at 18 * (1 - 1 / sqrt 2) = 5.27 per axis.
        var shape = new SwiftShapeBorder { Width = 200, Height = 40, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(18, RoundedCornerStyle.Continuous);
        var (window, _) = Render(shape, 220, 60);
        // The diagonal of the corner: Apple's curve at L passes (0.2915 L, 0.2915 L) = (3.815, 3.815). A point 0.4 pt inside it per axis is a hit; 0.4 outside is not.
        const double L = 40 / (2 * E);
        var d = 0.29150692 * L;
        Assert.Same(shape, window.InputHitTest(new Point(d + 0.4, d + 0.4)));
        Assert.NotSame(shape, window.InputHitTest(new Point(d - 0.4, d - 0.4)));
        // A circular r 18 would not hit at (d + 0.4, d + 0.4): its diagonal point is at 18 * (1 - 1 / sqrt 2) = 5.27.
        Assert.True(d + 0.4 < 18 * (1 - 1 / Math.Sqrt(2)));
        // The straight side between the run-ins is the shape: the middle of the top edge is a hit at 0.5 pt down.
        Assert.Same(shape, window.InputHitTest(new Point(100, 0.5)));
        window.Close();

        // A plain Border is cut to the same curve: the clip is the shared cached path and its probes agree.
        var border = new Border { Width = 200, Height = 40, Background = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(18, RoundedCornerStyle.Continuous);
        (window, var pixels) = Render(border, 220, 60);
        var clip = Assert.IsAssignableFrom<Geometry>(border.Clip);
        Assert.Same(clip, SwiftShapes.ClipGeometry(new Rect(0, 0, 200, 40), new CornerRadius(18), exact: true)); // childless: the exact outline (ED-297 (9))
        Assert.True(clip.FillContains(new Point(d + 0.4, d + 0.4)));
        Assert.False(clip.FillContains(new Point(d - 0.4, d - 0.4)));
        // Rendered: 1.5 pt in along the diagonal is red, 1.5 pt out (towards the vertex) is not.
        Assert.True(pixels.At((int)Math.Round(d + 1.5 / Math.Sqrt(2)), (int)Math.Round(d + 1.5 / Math.Sqrt(2))).Red >= 200);
        Assert.True(pixels.At((int)Math.Round(d - 1.5 / Math.Sqrt(2) - 0.5), (int)Math.Round(d - 1.5 / Math.Sqrt(2) - 0.5)).Red <= 40);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_band_shapes_stroke_has_its_outer_edge_on_the_curve_at_the_largest_radius() => headless.OnUiThread(() =>
    {
        // strokeBorder of a 200 x 40, r 18 shape, width 2: the outer edge is the shape, so it crosses the corner's diagonal at 0.2915 L * sqrt 2 from the vertex
        // (L = 13.085: 5.39 pt), where a circular arc of radius 18 would cross at 18 * (1 - 1 / sqrt 2) * sqrt 2 = 7.45 pt.
        var ring = new SwiftShapeBorder { Width = 200, Height = 40, BorderBrush = Brushes.White, BorderThickness = new Thickness(2), Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(18, RoundedCornerStyle.Continuous);
        var (window, pixels) = Render(ring, 220, 60);
        const double L = 40 / (2 * E);
        double Crossing()
        {
            for (var d = 2.5; d < 12; d += 0.02)
                if (Level(pixels, 10 + d / Math.Sqrt(2), 10 + d / Math.Sqrt(2)) >= 127.5) return d;
            return double.NaN;
        }
        var edge = Crossing();
        Assert.InRange(edge, 0.29150692 * L * Math.Sqrt(2) - 0.25, 0.29150692 * L * Math.Sqrt(2) + 0.25);
        Assert.True(edge < 18 * (1 - 1 / Math.Sqrt(2)) * Math.Sqrt(2) - 1.5, $"the ring's outer edge ({edge:F2}) is not clear of a circular r 18 arc's");
        // The straight sides: the stroke covers the outer 2 pt and not the third.
        Assert.True(Level(pixels, 10 + 100, 10.5) >= 250 && Level(pixels, 10 + 100, 11.5) >= 250);
        Assert.True(Level(pixels, 10 + 100, 12.5) <= 5);
        // The stroke follows the same curve inside: 0.75 pt inside the outer edge at the diagonal is stroked, 2.75 pt in is not.
        var n = 1 / Math.Sqrt(2);
        Assert.True(Level(pixels, 10 + edge * n + 0.75 * n, 10 + edge * n + 0.75 * n) >= 175);
        Assert.True(Level(pixels, 10 + edge * n + 2.75 * n, 10 + edge * n + 2.75 * n) <= 80);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_composer_shape_with_the_exception_draws_a_Borders_circular_pixels_where_a_band_shape_does_not() => headless.OnUiThread(() =>
    {
        // 400 x 46, r 22 with the exception: what a plain Border of CornerRadius 22 draws, pixel for pixel (the circular regime).
        Control Make(bool shape) => shape
            ? new SwiftShapeBorder { Width = 400, Height = 46, Background = Brushes.DarkSlateBlue, Margin = new Thickness(10.5, 7.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }.RoundedRectangle(22, RoundedCornerStyle.Continuous).CircularInBand()
            : new Border { Width = 400, Height = 46, Background = Brushes.DarkSlateBlue, CornerRadius = new CornerRadius(22), Margin = new Thickness(10.5, 7.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var (shapeWindow, composer) = Render(Make(true), 440, 76);
        var (borderWindow, border) = Render(Make(false), 440, 76);
        for (var y = 0; y < composer.Height; y++)
            for (var x = 0; x < composer.Width; x++) Assert.Equal(border.At(x, y), composer.At(x, y));
        shapeWindow.Close();
        borderWindow.Close();
        // Without the exception the same shape draws the band's corner at 46 / (2 E) = 15.05, which differs from the radius-22 circle.
        var band = new SwiftShapeBorder { Width = 400, Height = 46, Background = Brushes.DarkSlateBlue, Margin = new Thickness(10.5, 7.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }.RoundedRectangle(22, RoundedCornerStyle.Continuous);
        var (bandWindow, bandPixels) = Render(band, 440, 76);
        var differing = 0;
        for (var y = 0; y < bandPixels.Height; y++)
            for (var x = 0; x < bandPixels.Width; x++) if (bandPixels.At(x, y) != border.At(x, y)) differing++;
        Assert.True(differing > 40, $"only {differing} pixels differ between the band's corner and the circular r 22 corner");
        bandWindow.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void REVIEW_a_plain_Border_in_the_band_draws_Apples_corner_at_the_largest_radius_not_the_circle_of_the_stated_radius() => headless.OnUiThread(() =>
    {
        // REVIEW (adversarial, unit blitz-cornerband): every record of OD-0102 (UI-CHAT-034/036/037/054) is a PLAIN Border (ED-275 (3) (b)): it keeps
        // CornerRadius 18 (Avalonia draws the circle of 18) and is cut by a Clip. Apple's corner at L = 40 / (2 E) = 13.08 lies OUTSIDE the circle of 18
        // (2.06 pt at the diagonal), so the clip removes nothing the fill drew and the pixels are the circle of 18 (option B), not option A.
        // A plain Border must draw what the SwiftShapeBorder of the same shape draws (its fill is the path itself), and must differ from the circle.
        Control Make(int kind) => kind switch
        {
            0 => new Border { Width = 200, Height = 40, Background = Brushes.Red, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }.RoundedRectangle(18, RoundedCornerStyle.Continuous),
            1 => new SwiftShapeBorder { Width = 200, Height = 40, Background = Brushes.Red, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }.RoundedRectangle(18, RoundedCornerStyle.Continuous),
            _ => new Border { Width = 200, Height = 40, Background = Brushes.Red, CornerRadius = new CornerRadius(18), Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top },
        };
        var (w0, plain) = Render(Make(0), 220, 60);
        var (w1, path) = Render(Make(1), 220, 60);
        var (w2, circle) = Render(Make(2), 220, 60);
        int Diff(RenderedPixels a, RenderedPixels b)
        {
            var worst = 0;
            for (var y = 0; y < 60; y++)
                for (var x = 0; x < 220; x++) worst = Math.Max(worst, Math.Abs(a.At(x, y).Red - b.At(x, y).Red));
            return worst;
        }
        var plainVsCircle = Diff(plain, circle);
        var plainVsPath = Diff(plain, path);
        var pathVsCircle = Diff(path, circle);
        w0.Close(); w1.Close(); w2.Close();
        // The diagonal pixel (14, 14) is inside Apple's corner at 13.08 (its curve crosses the diagonal at 13.81) and outside the circle of 18 (15.27).
        Assert.True(pathVsCircle > 100, $"the SwiftShapeBorder's band corner differs from the circle by only {pathVsCircle}");
        Assert.True(plainVsPath < 40, $"the plain Border in the band differs from Apple's corner at L by {plainVsPath} levels of 255 (it differs from the circle of 18 by {plainVsCircle})");
    });

    // ---- what a plain Border in the band DRAWS (OD-0102; the blitz-cornerband review: a clip can only remove fill) ----------------------------------------
    // Avalonia fills the circle of the Border's CornerRadius, and Apple's corner at L = h / (2 E) lies OUTSIDE the circle of the stated radius near the diagonal, so a
    // Border that kept CornerRadius 18 drew the circle of 18 under a clip that removed nothing. ContinuousClip now keeps the stated radius (SwiftShapes.StatedCornerRadius)
    // and sets the Border's own CornerRadius to what draws the corner: 0 when the clip alone cuts a plain fill (the Border fills its pixel-aligned rectangle and the clip,
    // Apple's path, is the only anti-aliased edge), the circle of L when a border ring or a shadow needs a corner of the Border's own, or when the clip is not ours.
    // ENGINEERING DECISION (ED-297 (8)).

    private static Border BandBorder(IBrush? background, double width = 200, double height = 40) =>
        new Border { Width = width, Height = height, Background = background, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(18, RoundedCornerStyle.Continuous);

    private const double BandL = 40 / (2 * E);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // the pixels of a fill-only Border; the ring, shadow and clip-not-ours cases and the regimes are the tests below
    public void A_fill_only_Border_in_the_band_is_a_square_fill_under_Apples_clip_and_keeps_its_stated_radius() => headless.OnUiThread(() =>
    {
        var border = BandBorder(Brushes.Red);
        var shape = new SwiftShapeBorder { Width = 200, Height = 40, Background = Brushes.Red, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(18, RoundedCornerStyle.Continuous);
        var (window, plain) = Render(border, 220, 60);
        var (shapeWindow, path) = Render(shape, 220, 60);
        Assert.Equal(new CornerRadius(18), SwiftShapes.StatedCornerRadius(border));
        Assert.Equal(default, border.CornerRadius);
        Assert.Same(SwiftShapes.ClipGeometry(new Rect(0, 0, 200, 40), new CornerRadius(18), exact: true), border.Clip); // childless: the exact outline (ED-297 (9))
        // (14, 14): 4.5 pt from the vertex along each axis, inside Apple's corner at L (its diagonal is at 0.2915 L = 3.81) and outside the circle of 18 (5.27).
        Assert.True(plain.At(14, 14).Red >= 200, $"(14, 14) is {plain.At(14, 14).Red}: the corner is not Apple's");
        Assert.True(plain.At(12, 12).Red <= 40, $"(12, 12) is {plain.At(12, 12).Red}: outside Apple's corner");
        var worst = 0;
        for (var y = 10; y < 22; y++)
            for (var x = 10; x < 22; x++) worst = Math.Max(worst, Math.Abs(plain.At(x, y).Red - path.At(x, y).Red));
        // The exact outline is anti-aliased as the path fill is (within 1 level at 200 x 40); the circle of 18 differs by 255.
        Assert.True(worst < 12, $"the fill-only Border's corner differs from the path fill of the same shape by {worst} levels of 255");
        window.Close();
        shapeWindow.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_Border_with_a_ring_or_a_shadow_in_the_band_carries_the_circle_of_the_drawn_radius() => headless.OnUiThread(() =>
    {
        // A ring is a stroke along the Border's own corner, which a square corner would leave outside the curve; a shadow is cut by a clip, so there is none: both need the circle of L
        // (Apple's curve lies within 0.0138 L of it, ContinuousCorner).
        var ring = BandBorder(Brushes.Red);
        ring.BorderBrush = Brushes.White;
        ring.BorderThickness = new Thickness(2);
        var shadow = BandBorder(Brushes.Red);
        shadow.BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 2, Blur = 8, Color = Colors.Black });
        var window = new Window { Width = 240, Height = 200, Content = new StackPanel { Children = { ring, shadow } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(BandL), ring.CornerRadius);
        Assert.Equal(new CornerRadius(18), SwiftShapes.StatedCornerRadius(ring));
        Assert.NotNull(ring.Clip);
        Assert.Equal(new CornerRadius(BandL), shadow.CornerRadius);
        Assert.Equal(new CornerRadius(18), SwiftShapes.StatedCornerRadius(shadow));
        Assert.Null(shadow.Clip);
        // The ring appears and goes: the corner follows at once.
        ring.BorderBrush = null;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(default, ring.CornerRadius);
        ring.BorderBrush = Brushes.White;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(BandL), ring.CornerRadius);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_Border_whose_clip_is_the_sites_own_keeps_the_circle_of_the_drawn_radius_and_its_clip() => headless.OnUiThread(() =>
    {
        // The square corner is only safe while this tracker's clip is what cuts the Border; a Clip a site set stays and the Border draws its own circular corner at the drawn radius.
        var own = new RectangleGeometry(new Rect(-5, -5, 300, 80));
        var border = BandBorder(Brushes.Red);
        border.Clip = own;
        var window = new Window { Width = 240, Height = 100, Content = border };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(own, border.Clip);
        Assert.Equal(new CornerRadius(BandL), border.CornerRadius);
        Assert.Equal(new CornerRadius(18), SwiftShapes.StatedCornerRadius(border));
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_Borders_own_corner_follows_the_size_and_the_stated_radius_through_the_regimes() => headless.OnUiThread(() =>
    {
        var border = BandBorder(Brushes.Red, 200, 100); // 100 pt: Apple's corner at 18 fits (2 E 18 = 55): the stated radius is the Border's own, as it was
        var window = new Window { Width = 240, Height = 160, Content = new Panel { Children = { border } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(18), border.CornerRadius);
        Assert.NotNull(border.Clip);
        border.Height = 40; // into the band
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(default, border.CornerRadius);
        Assert.Equal(new CornerRadius(18), SwiftShapes.StatedCornerRadius(border));
        border.Height = 30; // a capsule (30 <= 2 * 18): circular, the stated radius is the Border's own again and there is no clip
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(18), border.CornerRadius);
        Assert.Null(border.Clip);
        border.Height = 40;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(default, border.CornerRadius);
        // The site states another radius: a radius that fits at 40 pt (2 E 10 = 30.6) is the Border's own; back to 18 is the band again.
        border.CornerRadius = new CornerRadius(10);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(10), SwiftShapes.StatedCornerRadius(border));
        Assert.Equal(new CornerRadius(10), border.CornerRadius);
        border.CornerRadius = new CornerRadius(18);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(18), SwiftShapes.StatedCornerRadius(border));
        Assert.Equal(default, border.CornerRadius);
        // The composer pill (measured circular in the band) keeps its circular corner and takes no clip.
        var composer = new Border { Width = 818, Height = 46, Background = Brushes.Red, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(22, RoundedCornerStyle.Continuous).CircularInBand();
        window.Content = composer;
        window.Width = 840;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(22), composer.CornerRadius);
        Assert.Null(composer.Clip);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void Drawn_corner_radius_is_zero_or_the_largest_radius_only_for_a_corner_that_is_Apples_in_the_band()
    {
        var band = new Size(380, 40);
        Assert.Equal(default, SwiftShapes.DrawnCornerRadius(band, new CornerRadius(18), false, clipOnly: true));
        Assert.Equal(new CornerRadius(40 / (2 * E)), SwiftShapes.DrawnCornerRadius(band, new CornerRadius(18), false, clipOnly: false));
        // It fits (the stated radius is Apple's), a capsule, the composer's exemption, a square corner: the stated radii are kept.
        Assert.Equal(new CornerRadius(18), SwiftShapes.DrawnCornerRadius(new Size(380, 100), new CornerRadius(18), false, clipOnly: true));
        Assert.Equal(new CornerRadius(18), SwiftShapes.DrawnCornerRadius(new Size(380, 30), new CornerRadius(18), false, clipOnly: true));
        Assert.Equal(new CornerRadius(22), SwiftShapes.DrawnCornerRadius(new Size(818, 46), new CornerRadius(22), true, clipOnly: true));
        Assert.Equal(new CornerRadius(0, 10, 0, 10), SwiftShapes.DrawnCornerRadius(new Size(380, 100), new CornerRadius(0, 10, 0, 10), false, clipOnly: true));
        Assert.Equal(new CornerRadius(18), SwiftShapes.DrawnCornerRadius(new Size(0, 0), new CornerRadius(18), false, clipOnly: true));
    }
    // ---- the clip's anti-aliasing at any bound size (the blitz-cornerband re-review: UI-CHAT-034's REVIEW measured 49 against 40) ------------------------------
    // MEASURED (scratch, 2026-10-04, SkiaSharp directly): the 49 is not the clip's size. Skia anti-aliases a CONVEX path with its exact walker and any other path with
    // a coarser one; the same outline with a tiny notch in a straight side, or with the straight sides pushed 0.001 pt out, gives the same 128 and 160 where the path
    // fill gives 177 and 192, and a slack of 4096 or of 0.001 makes no difference. So a Border that cannot draw beyond its sides is cut by the exact outline.

    private static (Window Window, RenderedPixels Pixels) RenderPlain(Control border, double width, double height) => Render(border, width + 20, height + 20);

    private static Border Plain(double width, double height, double radius = 18) =>
        new Border { Width = width, Height = height, Background = Brushes.Red, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(radius, RoundedCornerStyle.Continuous);

    private static SwiftShapeBorder PathFill(double width, double height, double radius = 18) =>
        new SwiftShapeBorder { Width = width, Height = height, Background = Brushes.Red, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .RoundedRectangle(radius, RoundedCornerStyle.Continuous);

    private static int Worst(RenderedPixels a, RenderedPixels b)
    {
        var worst = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++) worst = Math.Max(worst, Math.Abs(a.At(x, y).Red - b.At(x, y).Red));
        return worst;
    }

    [Theory]
    [InlineData(60, 40, 18)]
    [InlineData(200, 40, 18)]
    [InlineData(380, 46, 18)]
    [InlineData(380, 54, 18)]
    [InlineData(1500, 40, 18)]
    [InlineData(1500, 54, 18)]
    [InlineData(300, 30, 12)]
    [Trait("Decision", "ED-181")]
    public void A_childless_plain_Border_in_the_band_matches_the_path_fill_at_every_bound_size_within_the_clips_own_anti_aliasing(double width, double height, double radius) => headless.OnUiThread(() =>
    {
        var (w0, plain) = RenderPlain(Plain(width, height, radius), width, height);
        var (w1, path) = RenderPlain(PathFill(width, height, radius), width, height);
        var worst = Worst(plain, path);
        w0.Close(); w1.Close();
        Assert.True(worst < 12, $"{width} x {height}, r {radius}: the clipped Border differs from the path fill by {worst} levels of 255");
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_Border_that_cannot_draw_beyond_its_sides_is_cut_by_the_exact_outline_and_one_that_can_keeps_the_slack() => headless.OnUiThread(() =>
    {
        // No child: nothing but its own fill, which is inside its bounds. The exact outline (convex: Skia's exact anti-aliasing) is the clip.
        var bare = Plain(200, 40);
        var window = new Window { Width = 240, Height = 80, Content = new Panel { Children = { bare } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var exact = Assert.IsAssignableFrom<Geometry>(bare.Clip);
        Assert.Equal(200, exact.Bounds.Width, 6);
        Assert.Equal(40, exact.Bounds.Height, 6);
        Assert.False(exact.FillContains(new Point(100, 45)), "the exact outline reaches past the bottom side");
        Assert.Same(exact, SwiftShapes.ClipGeometry(new Rect(0, 0, 200, 40), new CornerRadius(18), exact: true));

        // A child that is not clipped to the bounds of a Border whose site marks its content as drawn beyond its sides (ED-275 (3), ED-NEW-rec-cornerclip): the slack clip,
        // which is not convex. An unmarked Border with content is cut by the exact outline (SwiftShapesContentClipTests).
        var child = new Border { Child = new Border { Width = 10, Height = 10 } };
        child.Child = new Border { Width = 10, Height = 10 };
        child.Width = 200; child.Height = 40;
        child.Background = Brushes.Red;
        child.RoundedRectangle(18, RoundedCornerStyle.Continuous);
        window.Content = child;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(exact, child.Clip);
        child.ContentBeyondSides();
        Dispatcher.UIThread.RunJobs();
        var slack = Assert.IsAssignableFrom<Geometry>(child.Clip);
        Assert.Equal(200 + 2 * SwiftShapes.ClipSlack, slack.Bounds.Width, 6);
        Assert.True(slack.FillContains(new Point(100, 45)));
        Assert.Same(slack, SwiftShapes.ClipGeometry(new Rect(0, 0, 200, 40), new CornerRadius(18)));

        // ... unless it is clipped to its bounds, where nothing beyond a side survives anyway.
        child.ClipToBounds = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(exact, child.Clip);

        // The tracker follows the child as it comes and goes.
        child.ClipToBounds = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(slack, child.Clip);
        child.Child = null;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(exact, child.Clip);
        child.Child = new Border { Width = 10, Height = 10 };
        Dispatcher.UIThread.RunJobs();
        Assert.Same(slack, child.Clip);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_corner_that_is_an_arc_keeps_the_slack_clip_so_the_Borders_own_arc_is_not_anti_aliased_twice() => headless.OnUiThread(() =>
    {
        // 100 x 100 with radii 10, 10, 10, 60: the top-right corner is Apple's, the other three do not fit beside the 60 and are circular arcs.
        var mixed = new Border { Width = 100, Height = 100, Background = Brushes.Red, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            .UnevenRoundedRectangle(new CornerRadius(10, 10, 10, 60), RoundedCornerStyle.Continuous);
        var window = new Window { Width = 140, Height = 140, Content = new Panel { Children = { mixed } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var outline = SwiftShapes.Outline(new Rect(0, 0, 100, 100), new CornerRadius(10, 10, 10, 60), RoundedCornerStyle.Continuous);
        Assert.Contains(CornerKind.Arc, outline.Corners);
        Assert.Contains(CornerKind.Continuous, outline.Corners);
        var clip = Assert.IsAssignableFrom<Geometry>(mixed.Clip);
        Assert.Equal(100 + 2 * SwiftShapes.ClipSlack, clip.Bounds.Width, 6);
        window.Close();
    });
}
