using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// F.SymbolImage (ED-179 (1)): the small SVG converter that turns the elements Lucide uses (path d, circle, ellipse,
// line, polyline, polygon, rect with rx and ry) into Avalonia geometry in the file's view box, with the paint Lucide
// declares (stroke currentColor, width 2, round caps and joins, fill none unless an element says otherwise).
// PORT-ONLY surface these tests fix, in src/Marrow.UI/Port/SvgIcon.cs, namespace Marrow.UI.Port:
//   enum SvgPaintKind { None, CurrentColor, Color }
//   readonly record struct SvgPaint(SvgPaintKind Kind, Color Color)
//   sealed record SvgShape(string Element, Geometry Geometry, SvgPaint Fill, SvgPaint Stroke, double StrokeWidth,
//       PenLineCap LineCap, PenLineJoin LineJoin, FillRule FillRule)
//   sealed class SvgIcon { Rect ViewBox; IReadOnlyList<SvgShape> Shapes;
//       static SvgIcon Parse(string svgText); static Geometry ParsePathData(string d); }
// Hit tests run on the headless UI thread because geometry hit testing goes through the Skia render interface.
[Collection("Headless")]
public sealed class SvgIconTests(HeadlessSession headless)
{
    private static readonly Pen Hairline = new(Brushes.Black, 0.2);

    private static string Lucide(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Marrow.UI/Assets/Icons/lucide/{name}.svg"));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AssertRect(Rect expected, Rect actual, double tolerance = 0.01)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Width, expected.Width - tolerance, expected.Width + tolerance);
        Assert.InRange(actual.Height, expected.Height - tolerance, expected.Height + tolerance);
    }

    private static bool OnStroke(Geometry geometry, double x, double y) => geometry.StrokeContains(Hairline, new Point(x, y));

    [Fact]
    [Trait("Decision", "ED-179")]
    public void The_root_gives_the_24_unit_view_box_and_the_Lucide_stroke() => headless.OnUiThread(() =>
    {
        var icon = SvgIcon.Parse(Lucide("x"));
        Assert.Equal(new Rect(0, 0, 24, 24), icon.ViewBox);
        Assert.Equal(2, icon.Shapes.Count);
        foreach (var shape in icon.Shapes)
        {
            Assert.Equal("path", shape.Element);
            Assert.Equal(SvgPaintKind.None, shape.Fill.Kind);
            Assert.Equal(SvgPaintKind.CurrentColor, shape.Stroke.Kind);
            Assert.Equal(2, shape.StrokeWidth);
            Assert.Equal(PenLineCap.Round, shape.LineCap);
            Assert.Equal(PenLineJoin.Round, shape.LineJoin);
        }
        // x.svg: "M18 6 6 18" and "m6 6 12 12", the two diagonals of the 6..18 square.
        AssertRect(new Rect(6, 6, 12, 12), icon.Shapes[0].Geometry.Bounds);
        AssertRect(new Rect(6, 6, 12, 12), icon.Shapes[1].Geometry.Bounds);
        Assert.True(OnStroke(icon.Shapes[0].Geometry, 12, 12));
        Assert.True(OnStroke(icon.Shapes[1].Geometry, 9, 9));
        Assert.False(OnStroke(icon.Shapes[1].Geometry, 9, 15));
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void A_circle_is_centred_with_its_radius() => headless.OnUiThread(() =>
    {
        // circle-check.svg: <circle cx="12" cy="12" r="10" /> then <path d="m16 9-5.5 5.5L8 12" />.
        var icon = SvgIcon.Parse(Lucide("circle-check"));
        Assert.Equal(["circle", "path"], icon.Shapes.Select(s => s.Element));
        var circle = icon.Shapes[0].Geometry;
        AssertRect(new Rect(2, 2, 20, 20), circle.Bounds);
        Assert.True(circle.FillContains(new Point(12, 12)));
        Assert.True(OnStroke(circle, 2, 12));
        Assert.True(OnStroke(circle, 12 + 10 * Math.Cos(Math.PI / 4), 12 + 10 * Math.Sin(Math.PI / 4)));
        Assert.False(OnStroke(circle, 12, 12));
        Assert.False(circle.FillContains(new Point(2.5, 2.5)));
        var check = icon.Shapes[1].Geometry;
        AssertRect(new Rect(8, 9, 8, 5.5), check.Bounds);
        Assert.True(OnStroke(check, 10.5, 14.5));
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void An_element_fill_of_currentColor_is_kept() => headless.OnUiThread(() =>
    {
        // palette.svg: four dots carry fill="currentColor"; its outline path does not.
        var icon = SvgIcon.Parse(Lucide("palette"));
        var dots = icon.Shapes.Where(s => s.Element == "circle").ToList();
        Assert.NotEmpty(dots);
        Assert.All(dots, s => Assert.Equal(SvgPaintKind.CurrentColor, s.Fill.Kind));
        Assert.All(icon.Shapes.Where(s => s.Element == "path"), s => Assert.Equal(SvgPaintKind.None, s.Fill.Kind));
        var half = dots.Single(s => s.Geometry.Bounds.Width < 1.5 && Math.Abs(s.Geometry.Bounds.Center.X - 6.5) < 0.01);
        AssertRect(new Rect(6, 12, 1, 1), half.Geometry.Bounds);
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void An_ellipse_has_both_radii() => headless.OnUiThread(() =>
    {
        // cylinder.svg: <ellipse cx="12" cy="5" rx="9" ry="3" />.
        var icon = SvgIcon.Parse(Lucide("cylinder"));
        var ellipse = icon.Shapes.First(s => s.Element == "ellipse").Geometry;
        AssertRect(new Rect(3, 2, 18, 6), ellipse.Bounds);
        Assert.True(ellipse.FillContains(new Point(20, 5)));
        Assert.False(ellipse.FillContains(new Point(12, 8.5)));
        Assert.True(OnStroke(ellipse, 12, 8));
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void A_line_runs_from_its_first_point_to_its_second() => headless.OnUiThread(() =>
    {
        // badge-x.svg: <line x1="15" x2="9" y1="9" y2="15" /> and <line x1="9" x2="15" y1="9" y2="15" />.
        var lines = SvgIcon.Parse(Lucide("badge-x")).Shapes.Where(s => s.Element == "line").ToList();
        Assert.Equal(2, lines.Count);
        AssertRect(new Rect(9, 9, 6, 6), lines[0].Geometry.Bounds);
        Assert.True(OnStroke(lines[0].Geometry, 12, 12));
        Assert.True(OnStroke(lines[0].Geometry, 14, 10));
        Assert.False(OnStroke(lines[0].Geometry, 10, 10));
        Assert.True(OnStroke(lines[1].Geometry, 10, 10));
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void A_polyline_is_open_and_a_polygon_is_closed() => headless.OnUiThread(() =>
    {
        // inbox.svg: <polyline points="22 12 16 12 14 15 10 15 8 12 2 12" />.
        var polyline = SvgIcon.Parse(Lucide("inbox")).Shapes.Single(s => s.Element == "polyline").Geometry;
        AssertRect(new Rect(2, 12, 20, 3), polyline.Bounds);
        Assert.True(OnStroke(polyline, 12, 15));
        Assert.True(OnStroke(polyline, 15, 13.5));
        Assert.False(OnStroke(polyline, 12, 12)); // open: nothing joins 2,12 back to 22,12

        // navigation.svg: <polygon points="3 11 22 2 13 21 11 13 3 11" />.
        var polygon = SvgIcon.Parse(Lucide("navigation")).Shapes.Single(s => s.Element == "polygon").Geometry;
        AssertRect(new Rect(3, 2, 19, 19), polygon.Bounds);
        Assert.True(polygon.FillContains(new Point(14, 10)));
        Assert.False(polygon.FillContains(new Point(4, 20)));
        Assert.True(OnStroke(polygon, 7, 12)); // on the closing edge 11,13 -> 3,11
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void A_rect_takes_rx_and_ry_with_the_SVG_defaults() => headless.OnUiThread(() =>
    {
        // square.svg: <rect width="18" height="18" x="3" y="3" rx="2" />; ry is absent, so it equals rx.
        var square = SvgIcon.Parse(Lucide("square")).Shapes.Single().Geometry;
        AssertRect(new Rect(3, 3, 18, 18), square.Bounds);
        Assert.True(square.FillContains(new Point(4, 4)));
        Assert.False(square.FillContains(new Point(3.3, 3.3)));
        Assert.False(square.FillContains(new Point(20.7, 20.7)));
        Assert.True(OnStroke(square, 12, 3));

        var sharp = SvgIcon.Parse("<svg viewBox=\"0 0 24 24\"><rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" /></svg>").Shapes.Single().Geometry;
        Assert.True(sharp.FillContains(new Point(3.3, 3.3)));

        // ry alone sets rx; rx larger than half the width is clamped to half the width.
        var onlyRy = SvgIcon.Parse("<svg viewBox=\"0 0 24 24\"><rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" ry=\"2\" /></svg>").Shapes.Single().Geometry;
        Assert.False(onlyRy.FillContains(new Point(3.3, 3.3)));
        Assert.True(onlyRy.FillContains(new Point(4, 4)));
        var pill = SvgIcon.Parse("<svg viewBox=\"0 0 24 24\"><rect x=\"2\" y=\"8\" width=\"20\" height=\"8\" rx=\"40\" ry=\"4\" /></svg>").Shapes.Single().Geometry;
        Assert.True(pill.FillContains(new Point(12, 8.2)));
        Assert.False(pill.FillContains(new Point(2.5, 8.5)));
        Assert.True(pill.FillContains(new Point(2.2, 12)));
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void Absolute_path_commands_draw_where_SVG_says() => headless.OnUiThread(() =>
    {
        var lines = SvgIcon.ParsePathData("M2 2 L10 2 H20 V10 Z");
        AssertRect(new Rect(2, 2, 18, 8), lines.Bounds);
        Assert.True(OnStroke(lines, 15, 2));
        Assert.True(OnStroke(lines, 20, 6));
        Assert.True(OnStroke(lines, 11, 6)); // Z closes 20,10 -> 2,2

        var cubic = SvgIcon.ParsePathData("M2 12 C2 2 22 2 22 12");
        Assert.True(OnStroke(cubic, 12, 4.5)); // the midpoint of the curve: y = 12 - 0.75 * 10
        Assert.False(OnStroke(cubic, 12, 2));

        var smooth = SvgIcon.ParsePathData("M2 12 C2 6 10 6 10 12 S18 18 18 12"); // S reflects 10,6 into 10,18
        Assert.True(OnStroke(smooth, 14, 16.5));
        Assert.True(OnStroke(smooth, 6, 7.5));

        var quad = SvgIcon.ParsePathData("M2 12 Q12 2 22 12");
        Assert.True(OnStroke(quad, 12, 7)); // the midpoint: y = 12 - 0.5 * 10
        var smoothQuad = SvgIcon.ParsePathData("M2 12 Q7 2 12 12 T22 12"); // T reflects 7,2 into 17,22
        Assert.True(OnStroke(smoothQuad, 17, 17));

        var arc = SvgIcon.ParsePathData("M2 12 A10 10 0 0 1 22 12"); // sweep 1 goes clockwise: over the top
        Assert.True(OnStroke(arc, 12, 2));
        Assert.False(OnStroke(arc, 12, 22));
        var largeArc = SvgIcon.ParsePathData("M12 2 A10 10 0 1 0 22 12"); // the long way round, counterclockwise
        Assert.True(OnStroke(largeArc, 2, 12));
        Assert.True(OnStroke(largeArc, 12, 22));
        Assert.False(OnStroke(largeArc, 12 + 10 * Math.Cos(-Math.PI / 4), 12 + 10 * Math.Sin(-Math.PI / 4)));
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void Relative_path_commands_draw_from_the_current_point() => headless.OnUiThread(() =>
    {
        var lines = SvgIcon.ParsePathData("m2 2 l8 0 h10 v8 z m0 10 l2 2");
        Assert.True(OnStroke(lines, 15, 2));
        Assert.True(OnStroke(lines, 20, 6));
        Assert.True(OnStroke(lines, 11, 6));
        // after z the current point is the subpath start 2,2, so m0 10 moves to 2,12
        Assert.True(OnStroke(lines, 3, 13));

        var cubic = SvgIcon.ParsePathData("m2 12 c0-10 20-10 20 0");
        Assert.True(OnStroke(cubic, 12, 4.5));
        var smooth = SvgIcon.ParsePathData("M2 12 c0-6 8-6 8 0 s8 6 8 0");
        Assert.True(OnStroke(smooth, 14, 16.5));
        var quad = SvgIcon.ParsePathData("m2 12 q10-10 20 0");
        Assert.True(OnStroke(quad, 12, 7));
        var smoothQuad = SvgIcon.ParsePathData("M2 12 q5-10 10 0 t10 0");
        Assert.True(OnStroke(smoothQuad, 17, 17));
        var arc = SvgIcon.ParsePathData("m2 12 a10 10 0 0 1 20 0");
        Assert.True(OnStroke(arc, 12, 2));
        Assert.False(OnStroke(arc, 12, 22));
        // implicit repeats: after m the extra pairs are lineto; after l they stay lineto
        var repeats = SvgIcon.ParsePathData("m2 2 10 0 0 10");
        Assert.True(OnStroke(repeats, 7, 2));
        Assert.True(OnStroke(repeats, 12, 7));
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void Compact_numbers_and_packed_arc_flags_parse_as_SVG_defines_them() => headless.OnUiThread(() =>
    {
        // Lucide writes "c0-1.1.9-2" (0, -1.1, 0.9, -2 ...) and exponent-free decimals like ".5".
        var compact = SvgIcon.ParsePathData("M14 8V7c0-1.1.9-2 2-2h2");
        AssertRect(new Rect(14, 5, 4, 3), compact.Bounds, 0.05);
        Assert.True(OnStroke(compact, 17, 5));
        var packed = SvgIcon.ParsePathData("M2 12a10 10 0 0120 0");
        Assert.True(OnStroke(packed, 12, 2));
        var exponent = SvgIcon.ParsePathData("M2e0 2 L2e1 2");
        AssertRect(new Rect(2, 2, 18, 0), exponent.Bounds);
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void Every_embedded_Lucide_svg_parses_into_at_least_one_shape_inside_its_view_box() => headless.OnUiThread(() =>
    {
        var assets = AssetLoader.GetAssets(new Uri("avares://Marrow.UI/Assets/Icons/lucide/"), null)
            .Where(u => u.AbsolutePath.EndsWith(".svg", StringComparison.Ordinal)).ToList();
        // porting/icons/README.md: "the 713 chosen Lucide SVGs" (ED-179 counts 714 files, which includes LICENSE).
        Assert.Equal(713, assets.Count);
        foreach (var uri in assets)
        {
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var icon = SvgIcon.Parse(reader.ReadToEnd());
            Assert.Equal(new Rect(0, 0, 24, 24), icon.ViewBox);
            Assert.NotEmpty(icon.Shapes);
            foreach (var shape in icon.Shapes)
            {
                var b = shape.Geometry.Bounds;
                Assert.True(b.X >= -0.5 && b.Y >= -0.5 && b.Right <= 24.5 && b.Bottom <= 24.5, $"{uri}: {shape.Element} bounds {b}");
            }
        }
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void Malformed_path_data_is_an_error_not_a_silent_shape()
    {
        Assert.ThrowsAny<FormatException>(() => SvgIcon.ParsePathData("M2 2 X4 4"));
        Assert.ThrowsAny<FormatException>(() => SvgIcon.ParsePathData("L2 2"));
    }
}
