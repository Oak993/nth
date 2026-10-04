using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.UI.Port;
using SkiaSharp;
using Xunit;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The continuous corner on the pixels a mounted element draws (ED-275 (2)): the element itself is rendered through Avalonia's own
/// renderer at <see cref="Scale"/> times its size, so the 0.0138 r sliver between Apple's curve and the circle (0.1 pt at r 7) is
/// 2.3 px of the bitmap. <see cref="ContinuousCornerChecks"/> reads the path a shape hands the renderer; this reads what the renderer
/// then draws, through the fill, the ring's stroke and the clip together, and cannot pass if the corner it drew were circular.
/// </summary>
internal sealed class RenderedCorner : IDisposable
{
    /// <summary>Bitmap pixels per point: r 6 is 144 px, and the sliver at its widest (0.0138 r) 2.0 px.</summary>
    public const double Scale = 24;

    // ED-275 (1): the first cubic of one corner, in units of the radius with u along the edge from the vertex and v into the shape, from the
    // run-in's far end (1.528665, 0) to the arc's start (0.63149379, 0.07491139). Written out here, not read from the product, so a wrong curve fails.
    private static readonly (double X, double Y)[] RunIn = [(1.528665, 0), (1.08849296, 0), (0.86840694, 0), (0.63149379, 0.07491139)];

    // Where along the edge to look (fractions of r): the circle and Apple's curve are 0.0092 r, 0.0139 r, 0.0096 r apart at 0.7, 0.869 and 1.0.
    private static readonly double[] AlongEdge = [0.7, 0.8, 0.869, 0.95, 1.0];

    /// <summary>How far in from the edge Apple's curve is at <paramref name="u"/> (in units of r, along the edge from the vertex), 0 past its run-in.</summary>
    private static double AppleDepth(double u)
    {
        if (u >= RunIn[0].X) return 0;
        double lo = 0, hi = 1; // x falls from 1.528665 to 0.6315 as t rises: bisect t for x(t) = u
        for (var i = 0; i < 60; i++)
        {
            var mid = (lo + hi) / 2;
            if (Cubic(mid).X > u) lo = mid; else hi = mid;
        }
        return Cubic((lo + hi) / 2).Y;
    }

    private static (double X, double Y) Cubic(double t)
    {
        double a = Math.Pow(1 - t, 3), b = 3 * Math.Pow(1 - t, 2) * t, c = 3 * (1 - t) * t * t, d = t * t * t;
        return (a * RunIn[0].X + b * RunIn[1].X + c * RunIn[2].X + d * RunIn[3].X, a * RunIn[0].Y + b * RunIn[1].Y + c * RunIn[2].Y + d * RunIn[3].Y);
    }

    /// <summary>How far in from the edge the circle of radius r is at <paramref name="u"/> (units of r): 0 from its tangent point (u = 1) on.</summary>
    private static double CircleDepth(double u) => u >= 1 ? 0 : 1 - Math.Sqrt(1 - (1 - u) * (1 - u));

    private readonly SKBitmap bitmap;
    private readonly Size size;

    private RenderedCorner(SKBitmap bitmap, Size size) { this.bitmap = bitmap; this.size = size; }

    /// <summary>Renders <paramref name="element"/> (mounted and laid out) at <see cref="Scale"/> x.</summary>
    public static RenderedCorner Of(Control element)
    {
        var size = element.Bounds.Size;
        Assert.True(size.Width > 0 && size.Height > 0, "the element has no size");
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * Scale), (int)Math.Ceiling(size.Height * Scale));
        using var target = new RenderTargetBitmap(pixels, new Vector(96 * Scale, 96 * Scale));
        target.Render(element);
        using var stream = new MemoryStream();
        target.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return new RenderedCorner(SKBitmap.Decode(stream), size);
    }

    /// <summary>The drawn opacity (0 to 1) at a point of the element, in points from its top-left, at the pixel centre nearest it.</summary>
    public double Alpha(double x, double y) =>
        bitmap.GetPixel(Math.Clamp((int)Math.Floor(x * Scale), 0, bitmap.Width - 1), Math.Clamp((int)Math.Floor(y * Scale), 0, bitmap.Height - 1)).Alpha / 255.0;

    /// <summary>
    /// The points to read at one corner (0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left), each on a pixel centre: for each place along
    /// each of the corner's two edges, one in the circle's area and outside Apple's curve (at least 0.4 px from both, so the pixel is wholly
    /// one or the other) and one just inside both (0.6 px inside Apple's curve).
    /// </summary>
    public List<(Point Sliver, Point Solid)> Probes(int corner, double radius)
    {
        var list = new List<(Point, Point)>();
        var px = 1 / Scale; // one bitmap pixel in points
        foreach (var swap in new[] { false, true })
            foreach (var fraction in AlongEdge)
            {
                var u = (Math.Floor(fraction * radius * Scale) + 0.5) * px; // a pixel centre along the edge
                var apple = AppleDepth(u / radius) * radius; var circle = CircleDepth(u / radius) * radius;
                // the first pixel row whose centre is 0.4 px inside the circle's edge; it must also be 0.4 px outside Apple's curve
                var v = (Math.Ceiling(circle * Scale - 0.1) + 0.5) * px; // its centre is at least 0.4 px past the circle's edge
                if (v < circle + 0.4 * px || v > apple - 0.4 * px) continue;
                var solidV = (Math.Ceiling(apple * Scale + 0.1) + 0.5) * px; // its centre is at least 0.6 px past Apple's curve
                list.Add((swap ? At(corner, v, u) : At(corner, u, v), swap ? At(corner, solidV, u) : At(corner, u, solidV)));
            }
        return list;
    }

    /// <summary>
    /// Whether the rendered element cuts the circle's sliver off at every corner of <paramref name="radii"/> that <paramref name="expectApple"/>
    /// names: <c>true</c> asserts the sliver points are (almost) undrawn while the points just inside are drawn; <c>false</c> asserts the
    /// circular arc, where the sliver points are as drawn as the points beside them. <paramref name="full"/> is the opacity the shape's own fill or stroke draws at (its brush's alpha). Returns the opacity pairs it read.
    /// </summary>
    public (double Sliver, double Solid)[] AssertCorners(CornerRadius radii, bool expectApple, double full)
    {
        var seen = new List<(double, double)>();
        var rs = new[] { radii.TopLeft, radii.TopRight, radii.BottomRight, radii.BottomLeft };
        for (var corner = 0; corner < 4; corner++)
        {
            if (rs[corner] <= 0) continue;
            var probes = Probes(corner, rs[corner]);
            Assert.True(probes.Count >= 4, $"corner {corner} r {rs[corner]}: only {probes.Count} readable points between the curve and the circle");
            foreach (var (sliver, solid) in probes)
            {
                var a = Alpha(sliver.X, sliver.Y); var b = Alpha(solid.X, solid.Y);
                seen.Add((a, b));
                Assert.True(b >= 0.6 * full, $"corner {corner} r {rs[corner]}: the point just inside Apple's curve ({solid.X:F3}, {solid.Y:F3}) is drawn at {b:F3}, the shape's own opacity is {full:F3}");
                if (expectApple) Assert.True(a <= 0.35 * full, $"corner {corner} r {rs[corner]}: the circle's sliver at ({sliver.X:F3}, {sliver.Y:F3}) is drawn ({a:F3} against {b:F3} inside): a circular corner");
                else Assert.True(a >= 0.65 * full, $"corner {corner} r {rs[corner]}: the circular arc's point ({sliver.X:F3}, {sliver.Y:F3}) is cut away ({a:F3} against {b:F3}): not the circular regime");
            }
        }
        return seen.ToArray();
    }

    // The point (u, v) from the vertex of a corner, u along the horizontal edge.
    private Point At(int corner, double u, double v) => corner switch
    {
        0 => new Point(u, v),
        1 => new Point(size.Width - u, v),
        2 => new Point(size.Width - u, size.Height - v),
        _ => new Point(u, size.Height - v),
    };

    public void Dispose() => bitmap.Dispose();
}
