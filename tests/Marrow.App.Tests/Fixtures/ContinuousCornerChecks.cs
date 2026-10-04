using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// What a `.continuous` shape of this port draws, asserted from the mounted control (ED-181 closed; the curve itself is
/// SwiftShapesContinuousTests and SwiftShapesOracleTests in Marrow.UI.Tests): its style, radius and corner regime
/// (Apple's corner while 2 * 1.528665 * r fits the shorter side, else the circular arc the video shows for capsules and large
/// radii) and, where the corner is Apple's, the shape's own path: the clip of a plain Border, the fill of a SwiftShapeBorder.
/// </summary>
internal static class ContinuousCornerChecks
{
    // The sliver between Apple's curve and the circle (SPEC.md 1.2) at 0.869 r along an edge: the circle is 0.0086 r from the edge
    // there, Apple's curve 0.0225 r. A point 0.0155 r in is inside the circle and outside Apple's curve; 0.030 r in is inside both.
    private const double Along = 0.869, Between = 0.0155, Inside = 0.030;

    /// <summary>
    /// Asserts <paramref name="shape"/> was drawn with `.continuous` corners and returns whether any corner is Apple's (its size allows
    /// it) rather than the circular arc. <paramref name="expectApple"/> pins the regime the test's own size must be in.
    /// </summary>
    public static bool Drawn(Control shape, bool expectApple)
    {
        var (radii, style, size, clip, fill) = shape switch
        {
            SwiftShapeBorder s => (s.CornerRadius, SwiftShapes.GetCornerStyle(s), s.Bounds.Size, s.Clip, true),
            Border b => (SwiftShapes.StatedCornerRadius(b), SwiftShapes.GetCornerStyle(b), b.Bounds.Size, b.Clip, false),
            _ => throw new Xunit.Sdk.XunitException($"{shape.GetType().Name} is not a rounded shape"),
        };
        Assert.Equal(RoundedCornerStyle.Continuous, style);
        Assert.True(size.Width > 0 && size.Height > 0, "the shape has no size");
        // OD-0102: the corner Apple draws in ED-275 (2)'s band is at the largest radius that fits, which ShapeOutline.DrawnRadius reports (the composer pill, marked
        // CircularInBand because the reference video measures it circular, is the one shape that keeps the arc there).
        var circularInBand = SwiftShapes.GetCircularInBand(shape);
        var outline = SwiftShapes.Outline(new Rect(size), radii, RoundedCornerStyle.Continuous, circularInBand);
        Assert.Equal(expectApple, outline.HasContinuousCorner);
        if (!outline.HasContinuousCorner)
        {
            // The circular regime (ED-275 (2)): every rounded corner is an arc, and a plain Border takes no clip of its own.
            Assert.DoesNotContain(CornerKind.Continuous, outline.Corners);
            if (shape is Border) Assert.Null(clip);
            return false;
        }

        // Apple's corner: the Border is cut to the clip variant of the path, a SwiftShapeBorder fills the path itself.
        Geometry path;
        if (shape is Border)
        {
            path = Assert.IsAssignableFrom<Geometry>(clip);
            Assert.Same(path, ExpectedClip((Border)shape, size, radii, circularInBand));
        }
        else path = SwiftShapes.UnevenRoundedRectangleGeometry(new Rect(size), radii, RoundedCornerStyle.Continuous, circularInBand);
        _ = fill;
        foreach (var index in new[] { 0, 1, 2, 3 })
        {
            if (outline.Corner(index) != CornerKind.Continuous) continue;
            var r = outline.DrawnRadius(index);
            foreach (var (u, v) in new[] { (Along * r, Between * r), (Between * r, Along * r) })
                Assert.False(path.FillContains(At(size, index, u, v)), $"corner {index}, r {r}: ({u:F2}, {v:F2}) from the vertex is inside the circle but outside Apple's curve");
            foreach (var (u, v) in new[] { (Along * r, Inside * r), (Inside * r, Along * r) })
                Assert.True(path.FillContains(At(size, index, u, v)), $"corner {index}, r {r}: ({u:F2}, {v:F2}) from the vertex is inside Apple's curve");
            Assert.False(path.FillContains(At(size, index, 0.1 * r, 0.1 * r)), $"corner {index}: the vertex side is cut away");
        }
        return true;
    }

    /// <summary>
    /// The clip a plain Border carries (ED-297 (9), ED-NEW-rec-cornerclip): the shape's own outline, which Skia anti-aliases exactly, when every rounded corner is Apple's,
    /// unless the site marks the Border's content as drawn beyond its sides (ContentBeyondSides) and it has a child not clipped to its bounds; that one keeps the slack
    /// clip (ED-275 (3)) that leaves what is drawn beyond a side alone.
    /// </summary>
    public static Geometry ExpectedClip(Border border, Size size, CornerRadius stated, bool circularInBand) =>
        SwiftShapes.ClipGeometry(new Rect(size), stated, circularInBand,
            exact: (border.Child is null || border.ClipToBounds || !SwiftShapes.GetContentBeyondSides(border)) && SwiftShapes.ExactClipFits(size, stated, circularInBand));

    /// <summary>
    /// PopoverItem's hover surface (Popovers.swift:318-326: <c>.padding(.horizontal, 8).frame(height: 26).background { RoundedRectangle(cornerRadius: 6,
    /// style: .continuous) }</c>): the radius, the row height and the horizontal inset the Swift states, then the corner it draws. A 26 pt row
    /// holds the 18 pt run-ins of a 6 pt corner (2 * 1.528665 * 6 = 18.3), so every corner is Apple's.
    /// </summary>
    public static void MenuItemSurface(Border surface)
    {
        Assert.Equal(new CornerRadius(6), surface.CornerRadius);
        Assert.Equal(26, surface.Bounds.Height, 0.01);
        var row = Assert.IsAssignableFrom<Control>(surface.Child);
        Assert.Equal(new Thickness(8, 0), row.Margin);
        Assert.Equal(26, row.Height);
        Assert.True(Drawn(surface, expectApple: true));
    }

    // The point (u, v) from the vertex of a corner (0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left), u along the horizontal edge.
    private static Point At(Size size, int corner, double u, double v) => corner switch
    {
        0 => new Point(u, v),
        1 => new Point(size.Width - u, v),
        2 => new Point(size.Width - u, size.Height - v),
        _ => new Point(u, size.Height - v),
    };
}
