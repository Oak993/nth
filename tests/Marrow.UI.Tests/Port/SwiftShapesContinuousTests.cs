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

// ED-181 closed: SwiftUI's `.continuous` corner (Apple's continuous-curvature corner) as three cubic Beziers per corner.
// The expected numbers are NOT read back from the port: the control points are the ones the build spec quotes from a
// third-party measurement of Apple's runtime (SPEC.md 1.2, "Test vector": the top-left corner of a 200 x 100 rect at the
// origin, r = 20; the extent 1.528665 r), mirrored here per corner by the test. The regime rule (SPEC.md 1.3, 4.2) is the
// build spec's: the continuous corner while 2 * 1.528665 * r <= min(w, h), the circular arc after (the reference video
// shows circular ends for capsules and r >= h / 2, SPEC.md 2.3).
// Source: build spec E:\Droppy\work\design\build-specs\continuous-corners\SPEC.md; Swift sites are the RoundedRectangle /
// Capsule calls named in the tests of the sites themselves.
[Collection("Headless")]
public sealed class SwiftShapesContinuousTests(HeadlessSession headless)
{
    // SPEC.md 1.2, test vector: top-left corner of a 200 x 100 rect at the origin, r = 20, from the edge toward the diagonal.
    private static readonly (double X, double Y)[][] TopLeftVector =
    [
        [(30.5733, 0), (21.7698592, 0), (17.3681388, 0), (12.6298758, 1.4982278)],
        [(12.6298758, 1.4982278), (7.4564766, 3.3811912), (3.3811912, 7.4564766), (1.4982278, 12.6298758)],
        [(1.4982278, 12.6298758), (0, 17.3681388), (0, 21.7698592), (0, 30.5732996)],
    ];

    private const double W = 200, H = 100;

    /// <summary>The four corners' cubics a 200 x 100 rect of radius 20 must hold: the top-left vector mirrored per corner.</summary>
    private static List<Point[]> ExpectedCubics()
    {
        var all = new List<Point[]>();
        foreach (var mirror in new Func<(double X, double Y), Point>[]
        {
            p => new Point(p.X, p.Y), // top-left
            p => new Point(W - p.X, p.Y), // top-right
            p => new Point(W - p.X, H - p.Y), // bottom-right
            p => new Point(p.X, H - p.Y), // bottom-left
        })
            foreach (var cubic in TopLeftVector) all.Add(cubic.Select(mirror).ToArray());
        return all;
    }

    /// <summary>The cubics of an outline as start, control 1, control 2, end.</summary>
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

    private static void AssertSameCubics(List<Point[]> actual, List<Point[]> expected, double tolerance)
    {
        Assert.Equal(expected.Count, actual.Count);
        var unmatched = new List<Point[]>(expected);
        foreach (var cubic in actual)
        {
            var index = unmatched.FindIndex(e => Same(cubic, e, tolerance));
            Assert.True(index >= 0, $"no expected cubic matches ({cubic[0]}) ({cubic[1]}) ({cubic[2]}) ({cubic[3]})");
            unmatched.RemoveAt(index);
        }
    }

    // ---- 5.1-1 the exact control points ------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_continuous_corner_is_three_cubics_at_the_measured_control_points_in_all_four_corners()
    {
        var outline = SwiftShapes.Outline(new Rect(0, 0, W, H), new CornerRadius(20), RoundedCornerStyle.Continuous);
        Assert.All(outline.Corners, k => Assert.Equal(CornerKind.Continuous, k));
        AssertSameCubics(CubicsOf(outline), ExpectedCubics(), 1e-6);
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_continuous_rectangle_runs_straight_between_the_corners_run_ins_and_is_one_closed_clockwise_figure()
    {
        var outline = SwiftShapes.Outline(new Rect(0, 0, W, H), new CornerRadius(20), RoundedCornerStyle.Continuous);
        var verbs = outline.Segments.Select(s => s.Verb).ToArray();
        Assert.Equal(PathVerb.Move, verbs[0]);
        Assert.Equal(12, verbs.Count(v => v == PathVerb.Cubic));
        Assert.Equal(0, verbs.Count(v => v == PathVerb.Arc));
        Assert.Equal(1, verbs.Count(v => v == PathVerb.Move));
        // Figure starts where the top-left corner ends on the top edge (1.528665 r along it) and visits the four run-in ends.
        Assert.True(Close(outline.Segments[0].End, new Point(30.5733, 0), 1e-9));
        var lines = outline.Segments.Where(s => s.Verb == PathVerb.Line).Select(s => s.End).ToArray();
        Assert.Equal(4, lines.Length);
        Assert.True(Close(lines[0], new Point(W - 30.5733, 0), 1e-9)); // along the top to the top-right corner's run-in
        Assert.True(Close(lines[1], new Point(W, H - 30.5733), 1e-9)); // down the right side
        Assert.True(Close(lines[2], new Point(30.5733, H), 1e-9)); // back along the bottom
        Assert.True(Close(lines[3], new Point(0, 30.5733), 1e-9)); // and up the left side
        Assert.True(Close(outline.Segments[^1].End, outline.Segments[0].End, 1e-9)); // the last cubic ends where the figure began
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_continuous_outline_sits_in_its_rect_at_any_origin_and_size()
    {
        var rect = new Rect(13.5, 7.25, 333.5, 211.75);
        var outline = SwiftShapes.Outline(rect, new CornerRadius(26), RoundedCornerStyle.Continuous);
        Assert.All(outline.Segments, s => Assert.True(s.End.X >= rect.Left - 1e-9 && s.End.X <= rect.Right + 1e-9 && s.End.Y >= rect.Top - 1e-9 && s.End.Y <= rect.Bottom + 1e-9));
        // The top-left corner's first run-in end is E * 26 from the vertex along the top edge.
        Assert.True(Close(outline.Segments[0].End, new Point(rect.Left + 1.528665 * 26, rect.Top), 1e-9));
        var scaled = SwiftShapes.Outline(new Rect(0, 0, 2 * W, 2 * H), new CornerRadius(40), RoundedCornerStyle.Continuous);
        var corners = CubicsOf(scaled);
        Assert.Equal(12, corners.Count);
        Assert.All(corners.Take(3), c => Assert.True(c.All(p => p.X >= -1e-9 && p.Y >= -1e-9)));
    }

    // ---- 5.1-2 the regime boundary -----------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_continuous_corner_holds_up_to_the_radius_two_extents_fit_and_circular_arcs_take_over_after()
    {
        var rect = new Rect(0, 0, W, H);
        var limit = H / (2 * 1.528665); // 32.7088: both run-ins fit along the 100 pt side
        var below = SwiftShapes.Outline(rect, new CornerRadius(limit - 0.01), RoundedCornerStyle.Continuous);
        Assert.All(below.Corners, k => Assert.Equal(CornerKind.Continuous, k));
        Assert.Equal(12, CubicsOf(below).Count);

        var above = SwiftShapes.Outline(rect, new CornerRadius(limit + 0.01), RoundedCornerStyle.Continuous);
        Assert.All(above.Corners, k => Assert.Equal(CornerKind.Arc, k));
        Assert.Empty(CubicsOf(above));
        var arcs = above.Segments.Where(s => s.Verb == PathVerb.Arc).ToArray();
        Assert.Equal(4, arcs.Length);
        Assert.All(arcs, a => Assert.Equal(limit + 0.01, a.ArcRadius, 9)); // the arc keeps the requested radius, no clamp to the continuous limit

        var wide = SwiftShapes.Outline(rect, new CornerRadius(60), RoundedCornerStyle.Continuous); // r >= h / 2: held to half the shorter side
        Assert.All(wide.Segments.Where(s => s.Verb == PathVerb.Arc), a => Assert.Equal(50, a.ArcRadius, 9));
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_capsule_and_a_circular_style_draw_the_same_circular_arcs_a_continuous_capsule_is_not_clamped_continuous()
    {
        var capsule = new Rect(0, 0, 60, 22);
        var continuous = SwiftShapes.Outline(capsule, new CornerRadius(SwiftShapes.CapsuleCornerRadius(capsule.Size)), RoundedCornerStyle.Continuous);
        var circular = SwiftShapes.Outline(capsule, new CornerRadius(11), RoundedCornerStyle.Circular);
        Assert.Equal(circular.Segments, continuous.Segments);
        Assert.Empty(CubicsOf(continuous));
        // The composer (22 pt on a 46 pt bar) is past the limit too (SPEC.md 2.3: circular ends in the video).
        var composer = SwiftShapes.Outline(new Rect(0, 0, 818, 46), new CornerRadius(22), RoundedCornerStyle.Continuous);
        Assert.Equal(SwiftShapes.Outline(new Rect(0, 0, 818, 46), new CornerRadius(22), RoundedCornerStyle.Circular).Segments, composer.Segments);
        // ... and the same 22 pt corner on a tall shape (Hydra's card, 400 x 280) is continuous.
        Assert.All(SwiftShapes.Outline(new Rect(0, 0, 400, 280), new CornerRadius(22), RoundedCornerStyle.Continuous).Corners, k => Assert.Equal(CornerKind.Continuous, k));
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_circular_style_is_circular_at_every_size_and_a_zero_radius_is_square()
    {
        var rect = new Rect(0, 0, 400, 300);
        var circular = SwiftShapes.Outline(rect, new CornerRadius(26), RoundedCornerStyle.Circular);
        Assert.All(circular.Corners, k => Assert.Equal(CornerKind.Arc, k));
        Assert.Equal(4, circular.Segments.Count(s => s.Verb == PathVerb.Arc));
        var square = SwiftShapes.Outline(rect, new CornerRadius(0), RoundedCornerStyle.Continuous);
        Assert.All(square.Corners, k => Assert.Equal(CornerKind.Square, k));
        Assert.DoesNotContain(square.Segments, s => s.Verb is PathVerb.Arc or PathVerb.Cubic);
        Assert.Equal([PathVerb.Move, PathVerb.Line, PathVerb.Line, PathVerb.Line, PathVerb.Line], square.Segments.Select(x => x.Verb)); // a plain rectangle, four sides
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_cheap_regime_check_agrees_with_the_outline_for_every_size_radius_and_style()
    {
        foreach (var style in new[] { RoundedCornerStyle.Continuous, RoundedCornerStyle.Circular })
            foreach (var (w, h) in new[] { (400.0, 300.0), (120.0, 80.0), (120.0, 70.0), (60.0, 22.0), (818.0, 46.0), (30.0, 40.0), (0.0, 10.0) })
                foreach (var radii in new[] { new CornerRadius(0), new CornerRadius(6), new CornerRadius(26), new CornerRadius(12, 12, 0, 0), new CornerRadius(0, 0, 18, 18), new CornerRadius(16, 0, 0, 0), new CornerRadius(80) })
                    Assert.Equal(SwiftShapes.Outline(new Rect(0, 0, w, h), radii, style).HasContinuousCorner, SwiftShapes.HasContinuousCorner(new Size(w, h), radii, style));
    }

    // ---- 5.1-3 containment probes (r 26, top-left corner of 400 x 300) -----------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void Where_the_curve_leaves_the_circle_a_probe_between_them_tells_the_continuous_corner_from_the_circular_one() => headless.OnUiThread(() =>
    {
        var rect = new Rect(0, 0, 400, 300);
        var continuous = SwiftShapes.RoundedRectangleGeometry(rect, 26, RoundedCornerStyle.Continuous);
        var circular = SwiftShapes.RoundedRectangleGeometry(rect, 26, RoundedCornerStyle.Circular);
        // At 22.6 pt along the top the circle is 0.22 pt below the edge, Apple's curve 0.585 pt (0.0225 r, SPEC.md 1.2).
        Assert.False(continuous.FillContains(new Point(22.6, 0.40)));
        Assert.True(circular.FillContains(new Point(22.6, 0.40)));
        Assert.True(continuous.FillContains(new Point(22.6, 0.75)));
        Assert.True(circular.FillContains(new Point(22.6, 0.75)));
        // The same on the left side (the corner is symmetric about the diagonal) and in each of the other three corners.
        Assert.False(continuous.FillContains(new Point(0.40, 22.6)));
        Assert.False(continuous.FillContains(new Point(399.6, 22.6)));
        Assert.False(continuous.FillContains(new Point(377.4, 0.40)));
        Assert.False(continuous.FillContains(new Point(22.6, 299.6)));
        Assert.False(continuous.FillContains(new Point(0.4, 277.4)));
        Assert.False(continuous.FillContains(new Point(399.6, 277.4)));
        Assert.False(continuous.FillContains(new Point(377.4, 299.6)));
        Assert.True(continuous.FillContains(new Point(377.4, 299.25)));
        // The run-in (38 pt from the vertex, 1.46 r) is on the edge for both, the corner itself is cut away for both.
        Assert.True(continuous.FillContains(new Point(38.0, 0.20)));
        Assert.True(circular.FillContains(new Point(38.0, 0.20)));
        Assert.False(continuous.FillContains(new Point(1, 1)));
        Assert.True(Math.Abs(continuous.Bounds.Left) < 1e-3 && Math.Abs(continuous.Bounds.Top) < 1e-3
            && Math.Abs(continuous.Bounds.Right - 400) < 1e-3 && Math.Abs(continuous.Bounds.Bottom - 300) < 1e-3);
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_continuous_corner_on_the_diagonal_reaches_a_little_nearer_the_vertex_than_the_circle() => headless.OnUiThread(() =>
    {
        // Diagonal point (0.29151, 0.29151) r against the circle's (0.29289, 0.29289) r (SPEC.md 1.2): 0.0020 r nearer the
        // vertex, at r 80 (the largest radius a 300 pt side holds: 2 * 1.528665 * 80 = 244.6) 23.32 against 23.43.
        var shape = SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, 400, 300), 80, RoundedCornerStyle.Continuous);
        Assert.True(shape.FillContains(new Point(23.38, 23.38)));
        Assert.False(shape.FillContains(new Point(23.28, 23.28)));
        var circle = SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, 400, 300), 80, RoundedCornerStyle.Circular);
        Assert.False(circle.FillContains(new Point(23.38, 23.38)));
        Assert.True(circle.FillContains(new Point(23.48, 23.48)));
    });

    // ---- 5.1-4 symmetry ----------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void Each_corner_is_symmetric_about_its_diagonal_and_the_four_corners_are_mirror_images()
    {
        var outline = SwiftShapes.Outline(new Rect(0, 0, 360, 360), new CornerRadius(30), RoundedCornerStyle.Continuous);
        var cubics = CubicsOf(outline);
        // About the diagonal: reflecting a corner's (x, y) -> (y, x) maps its set of cubics onto itself (TL: corner at the origin).
        var topLeft = cubics.Where(c => c.All(p => p.X < 100 && p.Y < 100)).ToList();
        Assert.Equal(3, topLeft.Count);
        var reflected = topLeft.Select(c => c.Select(p => new Point(p.Y, p.X)).ToArray()).ToList();
        AssertSameCubics(reflected, topLeft, 1e-9);
        // Between corners: left-right and top-bottom mirrors of the whole figure map the cubic set onto itself.
        AssertSameCubics(cubics.Select(c => c.Select(p => new Point(360 - p.X, p.Y)).ToArray()).ToList(), cubics, 1e-9);
        AssertSameCubics(cubics.Select(c => c.Select(p => new Point(p.X, 360 - p.Y)).ToArray()).ToList(), cubics, 1e-9);
        // Every cubic's control points stay inside the 1.528665 r run-in box of its corner (C1 and C3 on the edge lines).
        Assert.All(topLeft, c => Assert.All(c, p => Assert.True(p.X <= 1.528665 * 30 + 1e-9 && p.Y <= 1.528665 * 30 + 1e-9)));
    }

    // ---- 5.1-6 the cache ---------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void Equal_shapes_share_one_geometry_a_new_size_builds_one_more_and_a_colour_change_builds_none() => headless.OnUiThread(() =>
    {
        var rect = new Rect(0, 0, 431.5, 297.25); // sizes no other test uses, so the cache has not seen them
        var first = SwiftShapes.RoundedRectangleGeometry(rect, 22, RoundedCornerStyle.Continuous);
        var builds = SwiftShapes.GeometryBuildCount;
        Assert.Same(first, SwiftShapes.RoundedRectangleGeometry(rect, 22, RoundedCornerStyle.Continuous));
        Assert.Equal(builds, SwiftShapes.GeometryBuildCount);
        var larger = SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, 432.5, 297.25), 22, RoundedCornerStyle.Continuous);
        Assert.NotSame(first, larger);
        Assert.Equal(builds + 1, SwiftShapes.GeometryBuildCount);
        Assert.NotSame(first, SwiftShapes.RoundedRectangleGeometry(rect, 22, RoundedCornerStyle.Circular)); // style is part of the key
        Assert.NotSame(first, SwiftShapes.RoundedRectangleGeometry(rect, 21, RoundedCornerStyle.Continuous)); // and so is the radius
    });

    // ---- 5.1-7 uneven corners ----------------------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-181")]
    // The six UnevenRoundedRectangle sources (SPEC.md 3, 6.4): a block's top and bottom corners (DiffInspector, radius 12),
    // the follow-up queue and changes tabs (12 on top), the terminal panel (16 on top, ChatView.swift:1681-1691).
    [InlineData(400, 40, 12, 12, 0, 0, "CCSS")] // both 12 pt corners fit: 2 * 1.528665 * 12 = 36.7 <= 400, and 18.3 <= 40
    [InlineData(30, 40, 12, 12, 0, 0, "AASS")] // 36.7 > 30 along the top: both fall back to arcs (of 12, half of 30 is 15)
    [InlineData(600, 24, 16, 16, 0, 0, "AASS")] // 1.528665 * 16 = 24.46 > 24 down the left side: arcs of radius min(16, 12)
    [InlineData(600, 25, 16, 16, 0, 0, "CCSS")] // 24.46 <= 25: continuous
    [InlineData(400, 80, 12, 12, 12, 12, "CCCC")]
    [InlineData(400, 80, 0, 0, 12, 12, "SSCC")]
    public void An_uneven_shape_takes_the_continuous_corner_per_corner_where_its_run_ins_fit(double w, double h, double tl, double tr, double br, double bl, string expected)
    {
        var outline = SwiftShapes.Outline(new Rect(0, 0, w, h), new CornerRadius(tl, tr, br, bl), RoundedCornerStyle.Continuous);
        var letters = string.Concat(outline.Corners.Select(k => k switch { CornerKind.Continuous => 'C', CornerKind.Arc => 'A', _ => 'S' }));
        Assert.Equal(expected, letters);
        if (expected.Contains('A', StringComparison.Ordinal))
            Assert.All(outline.Segments.Where(s => s.Verb == PathVerb.Arc), a => Assert.Equal(Math.Min(tl, Math.Min(w, h) / 2), a.ArcRadius, 9));
    }

    // ---- 5.3 a Border takes the curve by its clip, and hit testing follows it -----------------------------------------

    private static (Window Window, Border Border) Mount(Border border, double width = 500, double height = 400)
    {
        border.HorizontalAlignment = HorizontalAlignment.Left;
        border.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width, Height = height, Content = new Panel { Children = { border } } };
        window.Show();
        Settle();
        return (window, border);
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_continuous_rounded_rectangle_border_is_cut_to_the_continuous_path_and_keeps_its_border_properties() => headless.OnUiThread(() =>
    {
        var (window, border) = Mount(new Border { Width = 400, Height = 300, Background = Brushes.Red }.RoundedRectangle(26, RoundedCornerStyle.Continuous));
        Assert.Equal(new CornerRadius(26), border.CornerRadius); // what the sites and tests read
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(border));
        var clip = Assert.IsAssignableFrom<Geometry>(border.Clip);
        Assert.False(clip.FillContains(new Point(22.6, 0.40)));
        Assert.True(clip.FillContains(new Point(22.6, 0.75)));
        Assert.True(clip.FillContains(new Point(200, -1.5))); // the straight sides are left to the Border's own fill: the clip reaches 2 pt past them
        Assert.False(clip.FillContains(new Point(1, 1)));
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_circular_a_capsule_and_a_shape_past_the_limit_take_no_clip() => headless.OnUiThread(() =>
    {
        foreach (var border in new[]
        {
            new Border { Width = 400, Height = 300, Background = Brushes.Red }.RoundedRectangle(26, RoundedCornerStyle.Circular),
            new Border { Width = 200, Height = 28, Background = Brushes.Red }.Capsule(RoundedCornerStyle.Continuous),
            new Border { Width = 818, Height = 46, Background = Brushes.Red }.RoundedRectangle(22, RoundedCornerStyle.Continuous),
            new Border { Width = 400, Height = 300, Background = Brushes.Red }.RoundedRectangle(0, RoundedCornerStyle.Continuous),
        })
        {
            var (window, _) = Mount(border, 900);
            Assert.Null(border.Clip);
            window.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_continuous_border_is_clipped_only_while_it_draws_something_the_clip_can_cut() => headless.OnUiThread(() =>
    {
        var border = new Border { Width = 400, Height = 300 }.RoundedRectangle(26, RoundedCornerStyle.Continuous);
        var (window, _) = Mount(border);
        Assert.Null(border.Clip); // nothing drawn, no children to clip: SwiftUI's `.background` of nothing
        border.Background = Brushes.Red;
        Settle();
        Assert.NotNull(border.Clip);
        border.BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, OffsetY = 4, Color = Colors.Black }); // a clip would cut the shadow
        Settle();
        Assert.Null(border.Clip);
        border.BoxShadow = default;
        Settle();
        Assert.NotNull(border.Clip);
        border.Background = null;
        border.ClipToBounds = true; // `.clipShape`: the children are clipped
        Settle();
        Assert.NotNull(border.Clip);
        border.ClipToBounds = false;
        border.BorderBrush = Brushes.Blue;
        border.BorderThickness = new Thickness(1);
        Settle();
        Assert.NotNull(border.Clip); // `.strokeBorder`: the border's outer edge follows the curve
        border.BorderBrush = null;
        Settle();
        Assert.Null(border.Clip);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_clip_a_site_set_itself_is_never_replaced_and_the_same_geometry_is_the_same_clip() => headless.OnUiThread(() =>
    {
        var own = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var border = new Border { Width = 400, Height = 300, Background = Brushes.Red, Clip = own }.RoundedRectangle(26, RoundedCornerStyle.Continuous);
        var (window, _) = Mount(border);
        Assert.Same(own, border.Clip);
        window.Close();

        // A site that sets the shape's own geometry (RootView's window clip, the composer's working-dots slot) gets the same cached instance.
        var site = new Border { Width = 400, Height = 300, Background = Brushes.Red }.RoundedRectangle(26, RoundedCornerStyle.Continuous);
        var (second, _) = Mount(site);
        Assert.NotNull(site.Clip);
        Assert.Same(site.Clip, SwiftShapes.ClipGeometry(new Rect(0, 0, 400, 300), new CornerRadius(26)));
        second.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_clip_follows_a_resize_and_equal_borders_share_it_and_a_colour_change_builds_no_geometry() => headless.OnUiThread(() =>
    {
        var one = new Border { Width = 411, Height = 301, Background = Brushes.Red }.RoundedRectangle(26, RoundedCornerStyle.Continuous);
        var two = new Border { Width = 411, Height = 301, Background = Brushes.Blue }.RoundedRectangle(26, RoundedCornerStyle.Continuous);
        var window = new Window { Width = 900, Height = 400, Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { one, two } } };
        window.Show();
        Settle();
        Assert.NotNull(one.Clip);
        Assert.Same(one.Clip, two.Clip); // the compositor reuses one geometry for identical rows

        var builds = SwiftShapes.GeometryBuildCount;
        one.Background = Brushes.Green;
        Settle();
        Assert.Equal(builds, SwiftShapes.GeometryBuildCount); // nothing rebuilt for a colour

        var before = one.Clip!;
        one.Width = 333;
        Settle();
        Assert.NotSame(before, one.Clip);
        Assert.Equal(1, SwiftShapes.GeometryBuildCount - builds); // exactly one new geometry for the new size
        Assert.Equal(333 + 2 * SwiftShapes.ClipSlack, one.Clip!.Bounds.Width, 6); // and it is the new size's (the clip reaches the slack past the sides)
        Assert.Equal(411 + 2 * SwiftShapes.ClipSlack, two.Clip!.Bounds.Width, 6);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void Hit_testing_a_continuous_shape_follows_the_curve_not_the_rectangle_nor_the_circle() => headless.OnUiThread(() =>
    {
        var (window, border) = Mount(new Border { Width = 400, Height = 300, Background = Brushes.Transparent }.RoundedRectangle(26, RoundedCornerStyle.Continuous));
        // Inside the circle but outside Apple's curve (22.6, 0.40): a `.contentShape(RoundedRectangle(cornerRadius: 26, style: .continuous))` does not take it.
        Assert.NotSame(border, window.InputHitTest(new Point(22.6, 0.40)));
        Assert.Same(border, window.InputHitTest(new Point(22.6, 0.75)));
        Assert.Same(border, window.InputHitTest(new Point(200, 150)));
        Assert.Same(border, window.InputHitTest(new Point(200, 0.5)));
        Assert.NotSame(border, window.InputHitTest(new Point(1, 1)));

        border.Width = 360; // after a resize the hit shape is the new one
        Settle();
        Assert.NotSame(border, window.InputHitTest(new Point(360 - 22.6, 0.40)));
        Assert.Same(border, window.InputHitTest(new Point(360 - 22.6, 0.75)));
        window.Close();
    });
}
