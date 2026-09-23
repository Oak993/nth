using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-181 (orchestrator ruling on the UI.Chrome.Chrome stop #3): SwiftUI's RoundedRectangle(cornerRadius:style:) and
// Capsule(style:) as one shared helper. The corner style is a parameter; `.continuous` is drawn with circular arcs at
// the exact radius until the continuous curve lands (MARROW_DEFERRED ED-181, tracked incomplete work), so both styles
// draw the same arcs today and the style is kept on the shape for that later work.
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

    [Theory]
    [Trait("Decision", "ED-181")]
    [InlineData(RoundedCornerStyle.Continuous)]
    [InlineData(RoundedCornerStyle.Circular)]
    public void A_rounded_rectangle_geometry_has_circular_corners_at_the_exact_radius(RoundedCornerStyle style) => headless.OnUiThread(() =>
    {
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
    public void Until_the_continuous_curve_lands_both_styles_draw_the_same_arcs() => headless.OnUiThread(() =>
    {
        // MARROW_DEFERRED ED-181: `.continuous` is drawn with circular arcs at the exact radius.
        var rect = new Rect(0, 0, 120, 80);
        var continuous = SwiftShapes.RoundedRectangleGeometry(rect, 26, RoundedCornerStyle.Continuous);
        var circular = SwiftShapes.RoundedRectangleGeometry(rect, 26, RoundedCornerStyle.Circular);
        for (var x = 0.25; x < 30; x += 0.5)
            for (var y = 0.25; y < 30; y += 0.5)
                Assert.Equal(circular.FillContains(new Point(x, y)), continuous.FillContains(new Point(x, y)));
    });
}
