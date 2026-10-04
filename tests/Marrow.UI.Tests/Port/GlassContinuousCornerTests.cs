using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;
using Pattern = Marrow.UI.Tests.Fixtures.GlassBackdropPattern;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

// ED-181, the glass: the shader's signed distance for a `.continuous` rounded rectangle is the circular corner's plus the
// offset of Apple's curve from the circle (ContinuousCorner.ShaderSource), where the fourth-power corner of the P1 shader
// (0.187 r from Apple's curve, 4.1 px at r 22, SPEC.md 1.2) used to be. The expected shape is the path SwiftShapes draws
// (SwiftShapesContinuousTests holds its control points); the shader text is run on the CPU by Skia and compared with its C#
// mirror, ContinuousCorner.Distance, so a drift between the two (or a shader that does not compile) fails here.
// Swift sources: Chrome.swift:153-184 `.glassEffect(_:in:)`, the shapes of the sites (HydraPanel.swift:105 RoundedRectangle(
// cornerRadius: 22, style: .continuous); Chrome.swift:164-165 Capsule(style: .continuous)).
[Collection("Headless")]
public sealed class GlassContinuousCornerTests(HeadlessSession headless)
{
    // The path of a shape of w x h with radius r as dense points (cubics by their parameter), relative to the shape's centre.
    private static List<Point> PathPoints(double w, double h, double r, RoundedCornerStyle style)
    {
        var outline = SwiftShapes.Outline(new Rect(0, 0, w, h), new CornerRadius(r), style);
        var points = new List<Point>();
        var current = new Point();
        foreach (var s in outline.Segments)
        {
            switch (s.Verb)
            {
                case PathVerb.Line:
                    for (var i = 1; i <= 50; i++) points.Add(current + (s.End - current) * (i / 50.0));
                    break;
                case PathVerb.Cubic:
                    for (var i = 1; i <= 2000; i++)
                    {
                        var t = i / 2000.0;
                        var m = 1 - t;
                        points.Add(new Point(
                            m * m * m * current.X + 3 * m * m * t * s.Control1.X + 3 * m * t * t * s.Control2.X + t * t * t * s.End.X,
                            m * m * m * current.Y + 3 * m * m * t * s.Control1.Y + 3 * m * t * t * s.Control2.Y + t * t * t * s.End.Y));
                    }
                    break;
                case PathVerb.Arc:
                {
                    var d = s.End - current;
                    var chord = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                    var height = Math.Sqrt(Math.Max(0, s.ArcRadius * s.ArcRadius - chord * chord / 4));
                    var centre = new Point((current.X + s.End.X) / 2 - height * d.Y / chord, (current.Y + s.End.Y) / 2 + height * d.X / chord);
                    var a0 = Math.Atan2(current.Y - centre.Y, current.X - centre.X);
                    var a1 = Math.Atan2(s.End.Y - centre.Y, s.End.X - centre.X);
                    if (a1 < a0) a1 += 2 * Math.PI;
                    for (var i = 1; i <= 2000; i++)
                    {
                        var a = a0 + (a1 - a0) * i / 2000.0;
                        points.Add(new Point(centre.X + s.ArcRadius * Math.Cos(a), centre.Y + s.ArcRadius * Math.Sin(a)));
                    }
                    break;
                }
            }
            current = s.End;
        }
        return points.Select(p => new Point(p.X - w / 2, p.Y - h / 2)).ToList();
    }

    // ---- the mirror's zero level is the path -------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-181")]
    [InlineData(780, 580, 26)] // the Settings window
    [InlineData(400, 280, 22)] // Hydra's card
    [InlineData(300, 90, 12)] // a row card
    [InlineData(380, 40, 18)] // OD-0102's band: r 18 on 40 pt is Apple's corner at 40 / (2 E) = 13.08 (the Edited-files card)
    [InlineData(380, 48, 18)] // a two-line notice
    [InlineData(380, 54, 18)] // a one-head delegation card
    [InlineData(140, 28, 14)] // Chrome.swift:164 a 28 pt capsule: the circular regime (radius half the height)
    public void The_distance_field_is_zero_on_the_path_to_a_twentieth_of_a_pixel_and_a_pixel_away_is_a_pixel(double w, double h, double r)
    {
        var half = new Size(w / 2, h / 2);
        var worst = 0.0;
        foreach (var p in PathPoints(w, h, r, RoundedCornerStyle.Continuous))
            worst = Math.Max(worst, Math.Abs(ContinuousCorner.Distance(p, half, r, continuous: true)));
        Assert.True(worst <= 0.05, $"the field is {worst:F4} px from zero on the path at r {r}");

        // Away from the path the field keeps its scale: a point one pixel inside the straight top edge is -1, one outside +1.
        Assert.Equal(-1, ContinuousCorner.Distance(new Point(0, -h / 2 + 1), half, r, true), 9);
        Assert.Equal(1, ContinuousCorner.Distance(new Point(0, -h / 2 - 1), half, r, true), 9);
        // And it is a distance near the path: the gradient has length 1 to within the offset's own slope (a tenth) along the whole path.
        foreach (var p in PathPoints(w, h, r, RoundedCornerStyle.Continuous).Where((_, i) => i % 97 == 0))
        {
            double D(double dx, double dy) => ContinuousCorner.Distance(new Point(p.X + dx, p.Y + dy), half, r, true);
            var gx = (D(0.25, 0) - D(-0.25, 0)) / 0.5;
            var gy = (D(0, 0.25) - D(0, -0.25)) / 0.5;
            Assert.InRange(Math.Sqrt(gx * gx + gy * gy), 0.9, 1.1);
        }
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void The_fourth_power_corner_of_the_P1_shader_was_far_from_the_path_where_this_field_is_on_it()
    {
        // The deferred field: ((qx^4 + qy^4)^0.25 + ... - r) put the corner's diagonal point 0.187 r from Apple's curve (SPEC.md 1.2).
        const double w = 400, h = 300, r = 26;
        var half = new Size(w / 2, h / 2);
        double Fourth(Point p)
        {
            var qx = Math.Abs(p.X) - half.Width + r;
            var qy = Math.Abs(p.Y) - half.Height + r;
            var outside = Math.Pow(Math.Pow(Math.Max(qx, 0), 4) + Math.Pow(Math.Max(qy, 0), 4), 0.25);
            return outside + Math.Min(Math.Max(qx, qy), 0) - r;
        }
        var onPath = PathPoints(w, h, r, RoundedCornerStyle.Continuous);
        Assert.True(onPath.Max(p => Math.Abs(Fourth(p))) > 3.0, "the fourth-power field was within 3 px of the path");
        Assert.True(onPath.Max(p => Math.Abs(ContinuousCorner.Distance(p, half, r, true))) <= 0.05);
    }

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_circular_style_and_a_radius_past_the_limit_keep_the_plain_circle_distance()
    {
        // `.circular`, a capsule and the composer's 22 pt corner on a 46 pt bar are what the video shows: circular ends. Under OD-0102 the composer is the one band shape
        // that says so (circularInBand); any other shape between the capsule and the full corner takes Apple's corner (the theory above runs those).
        foreach (var (w, h, r, continuous, inBand) in new (double, double, double, bool, bool)[] { (400, 300, 26, false, false), (818, 46, 22, true, true), (60, 22, 11, true, false), (200, 28, 14, true, false) })
        {
            var half = new Size(w / 2, h / 2);
            for (var x = -w / 2 - 3; x <= w / 2 + 3; x += 3.7)
                for (var y = -h / 2 - 3; y <= h / 2 + 3; y += 2.3)
                    Assert.Equal(ContinuousCorner.Distance(new Point(x, y), half, r, false), ContinuousCorner.Distance(new Point(x, y), half, r, continuous, inBand), 12);
        }
    }

    // ---- the shader is the mirror ------------------------------------------------------------------------------------

    /// <summary>Runs the glass shader's own <c>shapeDistance</c> on Skia's CPU rasteriser over a grid and returns d per pixel centre.</summary>
    private static float[,] RunShader(int width, int height, float[] size, float[] corners, float kind, float continuous)
    {
        var source = GlassMaterial.ShaderHeader + """

            uniform float2 size;
            uniform float4 corners;
            uniform float kind;
            uniform float continuous;
            half4 main(float2 xy) {
                float d = shapeDistance(xy, size, corners, kind, continuous);
                return half4(half(d * 0.005 + 0.5), 0.0, 0.0, 1.0);
            }
            """;
        using var effect = SKRuntimeEffect.CreateShader(source, out var error);
        Assert.True(effect is not null, "the glass shader does not compile: " + error);
        var uniforms = new SKRuntimeEffectUniforms(effect!) { ["size"] = size, ["corners"] = corners, ["kind"] = kind, ["continuous"] = continuous };
        using var black = SKShader.CreateColor(SKColors.Black);
        var children = new SKRuntimeEffectChildren(effect!) { ["backdrop"] = black };
        using var shader = effect!.ToShader(uniforms, children);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.RgbaF32, SKAlphaType.Premul));
        using var paint = new SKPaint { Shader = shader };
        surface.Canvas.DrawRect(new SKRect(0, 0, width, height), paint);
        using var pixmap = surface.PeekPixels();
        var values = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(pixmap.GetPixelSpan());
        var result = new float[height, width];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                result[y, x] = (values[(y * width + x) * 4] - 0.5f) / 0.005f;
        return result;
    }

    [Theory]
    [Trait("Decision", "ED-181")]
    [InlineData(400, 300, 26, 26, 26, 26, 0, 1)] // the window: continuous
    [InlineData(400, 300, 26, 26, 26, 26, 0, 0)] // `.circular`
    [InlineData(120, 80, 26, 26, 26, 26, 0, 1)] // 2 * 1.528665 * 26 = 79.5 <= 80: continuous
    [InlineData(120, 70, 26, 26, 26, 26, 0, 1)] // 79.5 > 70, 26 < 35: OD-0102's band, Apple's corner at 70 / (2 E) = 22.9
    [InlineData(380, 40, 18, 18, 18, 18, 0, 1)] // the Edited-files card's band size (r 18 on 40 pt): Apple's corner at 13.08
    [InlineData(380, 54, 18, 18, 18, 18, 0, 1)] // a one-head delegation card (r 18 on 54 pt): Apple's corner at 17.66
    [InlineData(818, 46, 22, 22, 22, 22, 0, 2)] // the composer pill (continuous 2: measured circular in the band, ED-275 (2))
    [InlineData(380, 34, 18, 18, 18, 18, 0, 1)] // a single-line pill (r 18 on 34 pt): a capsule, circular
    [InlineData(200, 80, 22, 22, 22, 22, 0, 1)] // Hydra's 22
    [InlineData(300, 90, 12, 12, 0, 0, 0, 1)] // an uneven shape (ThreadChangesTab.swift:15,38: 12 on top): per-corner radii
    [InlineData(200, 28, 14, 14, 14, 14, 1, 1)] // a capsule (kind 1): circular
    [InlineData(100, 100, 0, 0, 0, 0, 2, 1)] // a circle (kind 2)
    public void The_shaders_distance_is_the_mirrors_to_a_thousandth_of_a_pixel_on_Skia(int w, int h, float tl, float tr, float br, float bl, float kind, float continuous)
    {
        var field = RunShader(w, h, [w, h], [tl, tr, br, bl], kind, continuous);
        double worst = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var p = new Point(x + 0.5 - w / 2.0, y + 0.5 - h / 2.0);
                double expected;
                if (kind > 1.5) expected = Math.Sqrt(p.X * p.X + p.Y * p.Y) - Math.Min(w, h) / 2.0;
                else
                {
                    var radius = kind > 0.5 ? Math.Min(w, h) / 2.0 : p.Y < 0 ? (p.X < 0 ? tl : tr) : (p.X < 0 ? bl : br);
                    expected = ContinuousCorner.Distance(p, new Size(w / 2.0, h / 2.0), radius, continuous > 0.5, circularInBand: continuous > 1.5);
                }
                worst = Math.Max(worst, Math.Abs(field[y, x] - expected));
            }
        Assert.True(worst <= 1e-3, $"the shader differs from the mirror by {worst:F5} px");
    }

    // ---- a rendered glass region -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-181")]
    public void A_rendered_continuous_glass_differs_from_the_circular_one_at_the_corners_and_nowhere_else() => headless.OnUiThread(() =>
    {
        // 120 x 80 at r 26 is the largest corner a 80 pt side holds as a continuous one (2 * 1.528665 * 26 = 79.5), so its corners differ
        // from a circular r 26 by up to 0.36 px, in a 40 x 40 box at each corner and nowhere else.
        Color[,] Render(RoundedCornerStyle style)
        {
            var glass = new Border { Background = Brushes.Transparent, Width = 120, Height = 80, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.RoundedRectangle(26, style)));
            glass.CornerRadius = new CornerRadius(26);
            using var scene = new Scene(new Pattern { Flat = true }, glass);
            var pixels = scene.Pixels();
            var image = new Color[160, 256];
            for (var y = 0; y < 160; y++)
                for (var x = 0; x < 256; x++) image[y, x] = pixels(x, y);
            return image;
        }
        var circular = Render(RoundedCornerStyle.Circular);
        var continuous = Render(RoundedCornerStyle.Continuous);
        // The glass sits at x 68..188, y 40..120 of the 256 x 160 scene. A pixel differs if any channel does.
        var corner = 0;
        var elsewhere = 0;
        for (var y = 0; y < 160; y++)
            for (var x = 0; x < 256; x++)
            {
                if (circular[y, x] == continuous[y, x]) continue;
                var dx = Math.Min(Math.Abs(x - 68), Math.Abs(x - 188));
                var dy = Math.Min(Math.Abs(y - 40), Math.Abs(y - 120));
                if (dx <= 42 && dy <= 42) corner++; else elsewhere++;
            }
        Assert.True(corner >= 10, $"only {corner} pixels differ at the corners");
        Assert.Equal(0, elsewhere);
    });
}
