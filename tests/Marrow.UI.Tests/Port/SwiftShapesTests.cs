using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-181 (orchestrator ruling on the UI.Chrome.Chrome stop #3): SwiftUI's RoundedRectangle(cornerRadius:style:) and
// Capsule(style:) as one shared helper. The corner style is a parameter; `.continuous` is Apple's continuous-curvature
// corner (SwiftShapesContinuousTests.cs holds the curve; the former deferral, circular arcs for both styles, is closed).
// This file keeps the helper's surface: the Border takes the radius and keeps its style, capsules are half the shorter
// side, and `.circular` is the circular arc at the exact radius at every size.
// PORT-ONLY surface these tests fix, namespace Marrow.UI.Port, src/Marrow.UI/Port/SwiftShapes.cs:
//   enum RoundedCornerStyle { Circular, Continuous }   (SwiftUI RoundedCornerStyle .circular, .continuous)
//   static class SwiftShapes {
//       AttachedProperty<RoundedCornerStyle?> CornerStyleProperty; RoundedCornerStyle? GetCornerStyle(Control control);
//       T RoundedRectangle<T>(this T border, double cornerRadius, RoundedCornerStyle style) where T : Border;
//       T Capsule<T>(this T border, RoundedCornerStyle style) where T : Border;   (radius: half the shorter side, kept on resize)
//       double CapsuleCornerRadius(Size size);
//       Geometry RoundedRectangleGeometry(Rect rect, double cornerRadius, RoundedCornerStyle style);
//       Geometry CapsuleGeometry(Rect rect, RoundedCornerStyle style); }
// Expected numbers: a capsule's corner radius is half its shorter side (SwiftUI Capsule; the chrome capsules at
// Chrome.swift:165 are 32 points tall, radius 16); the window shape is Chrome.swift:218 RoundedRectangle(cornerRadius: 26,
// style: .continuous); the divider Chrome.swift:326 RoundedRectangle(cornerRadius: 0.5, style: .continuous).
[Collection("Headless")]
public sealed class SwiftShapesTests(HeadlessSession headless)
{
    // A point on the corner arc's diagonal, `inset` along the diagonal from the arc toward the corner's centre (positive
    // is inside the shape, negative outside), for the top-left corner of a shape at the origin with radius r.
    private static Point OnTopLeftDiagonal(double r, double inset)
    {
        var d = (r - inset) / Math.Sqrt(2);
        return new Point(r - d, r - d);
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_rounded_rectangle_border_takes_the_exact_radius_and_keeps_its_corner_style() => headless.OnUiThread(() =>
    {
        var window = new Border().RoundedRectangle(26, RoundedCornerStyle.Continuous);
        Assert.Equal(new CornerRadius(26), window.CornerRadius);
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(window));

        var divider = new Border().RoundedRectangle(0.5, RoundedCornerStyle.Continuous);
        Assert.Equal(new CornerRadius(0.5), divider.CornerRadius);

        var circular = new Border().RoundedRectangle(8, RoundedCornerStyle.Circular);
        Assert.Equal(RoundedCornerStyle.Circular, SwiftShapes.GetCornerStyle(circular));
        Assert.Null(SwiftShapes.GetCornerStyle(new Border()));
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_capsule_border_is_rounded_by_half_its_shorter_side_and_follows_its_size() => headless.OnUiThread(() =>
    {
        var capsule = new Border { Width = 62, Height = 32 }.Capsule(RoundedCornerStyle.Continuous);
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { capsule } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(16), capsule.CornerRadius);
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(capsule));

        capsule.Width = 20;
        capsule.Height = 60;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(10), capsule.CornerRadius);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_capsule_radius_is_half_the_shorter_side() => headless.OnUiThread(() =>
    {
        Assert.Equal(16, SwiftShapes.CapsuleCornerRadius(new Size(200, 32)));
        Assert.Equal(15, SwiftShapes.CapsuleCornerRadius(new Size(30, 90)));
        Assert.Equal(14, SwiftShapes.CapsuleCornerRadius(new Size(28, 28)));
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_circular_rounded_rectangle_geometry_has_circular_corners_at_the_exact_radius() => headless.OnUiThread(() =>
    {
        const RoundedCornerStyle style = RoundedCornerStyle.Circular;
        var rect = new Rect(0, 0, 400, 300);
        var shape = SwiftShapes.RoundedRectangleGeometry(rect, 26, style);
        Assert.Equal(rect, shape.Bounds);
        Assert.False(shape.FillContains(new Point(1, 1))); // the corner itself is cut away
        Assert.True(shape.FillContains(OnTopLeftDiagonal(26, 0.25))); // just inside the arc of radius 26
        Assert.False(shape.FillContains(OnTopLeftDiagonal(26, -0.25))); // just outside it
        Assert.True(shape.FillContains(new Point(200, 0.5))); // straight edges run to the corners' tangents
        Assert.True(shape.FillContains(new Point(0.5, 150)));
        Assert.True(shape.FillContains(new Point(399.5, 299.5 - 26)));
        Assert.False(shape.FillContains(new Point(399, 299)));
    });

    [Theory]
    [Trait("Decision", "ED-181")]
    [InlineData(RoundedCornerStyle.Continuous)]
    [InlineData(RoundedCornerStyle.Circular)]
    public void A_capsule_geometry_is_rounded_by_half_its_shorter_side(RoundedCornerStyle style) => headless.OnUiThread(() =>
    {
        var rect = new Rect(0, 0, 200, 32);
        var shape = SwiftShapes.CapsuleGeometry(rect, style);
        Assert.Equal(rect, shape.Bounds);
        Assert.True(shape.FillContains(OnTopLeftDiagonal(16, 0.25)));
        Assert.False(shape.FillContains(OnTopLeftDiagonal(16, -0.25)));
        Assert.True(shape.FillContains(new Point(0.5, 16))); // the end is a half circle: its leftmost point is at mid-height
        Assert.False(shape.FillContains(new Point(0.5, 4)));
        Assert.True(shape.FillContains(new Point(100, 0.5)));
    });

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_continuous_rounded_rectangle_geometry_is_not_the_circular_one_and_has_the_continuous_corners_extent() => headless.OnUiThread(() =>
    {
        // The deferral is closed: `.continuous` is no longer drawn with the `.circular` arcs. A shape a corner of the
        // same radius on a 120 x 80 rect holds (2 * 1.528665 * 26 = 79.5 <= 80) differs from the circle by up to 0.0138 r.
        var rect = new Rect(0, 0, 120, 80);
        var continuous = SwiftShapes.RoundedRectangleGeometry(rect, 26, RoundedCornerStyle.Continuous);
        var circular = SwiftShapes.RoundedRectangleGeometry(rect, 26, RoundedCornerStyle.Circular);
        var differing = 0;
        for (var x = 0.05; x < 40; x += 0.1)
            for (var y = 0.05; y < 40; y += 0.1)
                if (circular.FillContains(new Point(x, y)) != continuous.FillContains(new Point(x, y))) differing++;
        Assert.True(differing > 50, $"{differing} probes differ"); // the sliver between the curves is about 4 pt^2 over the corner's two halves: hundreds of 0.1 pt probes
        // Past the corner's run-in (1.528665 * 26 = 39.7 pt along an edge) the two shapes are the same.
        for (var x = 40.0; x < 80; x += 0.5)
            for (var y = 0.05; y < 1; y += 0.1)
                Assert.Equal(circular.FillContains(new Point(x, y)), continuous.FillContains(new Point(x, y)));
    });
}
