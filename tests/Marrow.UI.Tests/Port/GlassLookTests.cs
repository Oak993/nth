// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184 (.glassEffect(.regular)); App/Views/Composer/ComposerView.swift:1304.
// ED-NEW-glasslook-1 (OD-0107 and its amendment, owner 2026-10-11: "What about the lacking glass ui look?"): the lens rim is clear. Measured against
// the two MIT repos rendered by their own code over the same backdrops (E:\Droppy\work\scratch\glasslook-cl-1011, measure.py): rizzytoday/liquid-glass
// bends the backdrop itself, unblurred, 40 to 50 px at the edge with red and blue 4 to 5 px apart; the port bent its 12 pt blur, so its 30 to 50 pt
// bend was a smear nobody could see. Now the rim band reads the unblurred pixels under the glass, bent and fringed, fading into the frosted body.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassLookTests(HeadlessSession headless)
{
    // The clear lens rim (GlassLook's Clarity) over the release's calibrated bend: what the tests below measure the engine with.
    private static readonly GlassLook Clear = new(Clarity: 0.9);

    private static Border Piece(GlassShape shape, double left, double top, double width, double height, GlassLook? look = null)
    {
        var piece = new Border
        {
            Background = Brushes.Transparent, Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(left, top, 0, 0),
        };
        if (shape.Kind == GlassShapeKind.RoundedRectangle) piece.CornerRadius = new CornerRadius(shape.CornerRadius ?? 0);
        SwiftGlass.SetEffect(piece, look is null ? GlassEffect.Regular(shape) : GlassEffect.Regular(shape).WithLook(look));
        return piece;
    }

    private static Border Bar(double left, double top, double width, double height, byte level) => new()
    {
        Width = width, Height = height, Margin = new Thickness(left, top, 0, 0), Background = new SolidColorBrush(Color.FromRgb(level, level, level)),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
    };

    // A flat grey, with bars over it (a line, stripes, text-like rows).
    private static Panel Backdrop(byte level, params Border[] bars)
    {
        var panel = new Panel { Background = new SolidColorBrush(Color.FromRgb(level, level, level)) };
        foreach (var bar in bars) panel.Children.Add(bar);
        return panel;
    }

    private static Func<int, int, Color> Render(Control backdrop, Control glass, double width, double height, bool light = false)
    {
        var window = new Window
        {
            Width = width, Height = height, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark, Background = Brushes.Black,
            Content = new Panel { Children = { backdrop, glass } },
        };
        window.Show();
        try
        {
            for (var i = 0; i < 4; i++) GlassScene.Drain();
            using var bitmap = window.CaptureRenderedFrame()!;
            var size = bitmap.PixelSize;
            var bytes = new byte[size.Width * size.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
            finally { handle.Free(); }
            var rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
            return (x, y) =>
            {
                var p = (y * size.Width + x) * 4;
                return Color.FromArgb(bytes[p + 3], bytes[p + (rgba ? 0 : 2)], bytes[p + 1], bytes[p + (rgba ? 2 : 0)]);
            };
        }
        finally { window.Close(); }
    }

    private static double Luma(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    // The material's read displacement (pt) `inside` pt in from a straight edge of a lit piece, the shader's formula (GlassMaterial's band branch).
    private static double Displacement(Size size, double inside)
    {
        var o = GlassMaterial.Optics.For(size, Clear);
        var dd = Math.Max(inside, 0);
        var a = GlassMaterial.Optics.Logistic;
        var lens = (1 + Math.Exp(-a * o.Border / o.Sigma)) / (1 + Math.Exp(a * (dd - o.Border) / o.Sigma));
        lens = Math.Max(lens - GlassMaterial.Optics.BendFloor, 0) / (1 - GlassMaterial.Optics.BendFloor);
        return o.Bend * lens + o.Ring * Math.Max(Math.Exp(-0.8 * dd) - GlassMaterial.Optics.RingFloor, 0) / (1 - GlassMaterial.Optics.RingFloor);
    }

    // Where (the pixel column, from the left edge `left`) a channel's read lands on the line's middle `lineMiddle`: x + 0.5 - scale * bend(x + 0.5 - left).
    private static double Lands(Size size, int left, double lineMiddle, double scale)
    {
        for (var x = left * 100.0; x < (left + 40) * 100.0; x++)
        {
            var at = x / 100;
            if (at - scale * Displacement(size, at - left) >= lineMiddle) return at - 0.5;
        }
        return double.NaN;
    }

    [Theory]
    // A panel (280 x 190, bend 40 + ring 10): a 2 pt white line 20 pt and 30 pt to the left of its edge, on a mid grey (the rim lit).
    [InlineData(280, 190, 24, 20)]
    [InlineData(280, 190, 24, 30)]
    // A pill (400 x 80, bend 33.5 + ring 6.4), the line 16 pt past its end.
    [InlineData(400, 80, 40, 16)]
    [Trait("Req", "PR-WIN-180")] // OD-0107: rizzytoday's displacement map bends the backdrop itself: what lies past the edge is seen bent into the rim, sharp
    public void The_rim_shows_a_line_from_past_the_edge_sharp_and_where_the_bend_puts_it(double width, double height, double radius, int outside) =>
        headless.OnUiThread(() =>
        {
            const int left = 60, top = 20;
            var size = new Size(width, height);
            var shape = radius * 2 >= height ? GlassShape.Capsule(RoundedCornerStyle.Continuous) : GlassShape.RoundedRectangle(radius, RoundedCornerStyle.Continuous);
            var lineLeft = left - outside - 1; // a 2 pt line whose middle lies `outside` pt past the edge
            foreach (var light in new[] { false, true })
            {
                var lined = Render(Backdrop(128, Bar(lineLeft, 0, 2, 400, 255)), Piece(shape, left, top, width, height, Clear), left + width + 60, height + 2 * top, light);
                var plain = Render(Backdrop(128), Piece(shape, left, top, width, height, Clear), left + width + 60, height + 2 * top, light);
                var y = top + (int)(height / 2);
                // The line's image in the rim band: the column where it lifts the green channel most over the glass without it.
                var peak = Enumerable.Range(left + 1, 30).MaxBy(x => lined(x, y).G - plain(x, y).G);
                var lift = lined(peak, y).G - plain(peak, y).G;
                var expected = Lands(size, left, lineLeft + 1, GlassMaterial.Optics.Green);
                Assert.True(Math.Abs(peak - expected) <= 1.5, $"{width} x {height}, light {light}: the line {outside} pt out shows at {peak - left} pt in, the bend puts it at {expected - left:F1}");
                // Sharp, not a smear: through the 12 pt blur the 2 pt line lifted the glass by 8 levels at most (2 x 127 / (2.5 x 12)), spread over 30 pt.
                Assert.True(lift >= 30, $"{width} x {height}, light {light}: the line lifts its image by {lift} levels at {peak - left} pt in");
                Assert.True(lined(peak + 4, y).G - plain(peak + 4, y).G <= lift / 2 && lined(peak - 4, y).G - plain(peak - 4, y).G <= lift / 2,
                    $"{width} x {height}, light {light}: and is narrow ({lined(peak - 4, y).G - plain(peak - 4, y).G}, {lift}, {lined(peak + 4, y).G - plain(peak + 4, y).G})");
                // Deep inside the line is frosted away (the body is the 12 pt blur): no sharp copy of it there.
                var middle = left + (int)(width / 2);
                Assert.True(Math.Abs(lined(middle, y).G - plain(middle, y).G) <= 2, $"{width} x {height}, light {light}: the middle {lined(middle, y)} against {plain(middle, y)}");
            }
        });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0107: rizzytoday's chromatic aberration: at the rim red, green and blue are read apart (red the furthest out)
    public void The_rim_parts_red_green_and_blue_of_what_it_bends_and_a_grey_stays_grey() => headless.OnUiThread(() =>
    {
        const int left = 60, top = 20;
        var shape = GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous);
        var lined = Render(Backdrop(128, Bar(left - 21, 0, 2, 400, 255)), Piece(shape, left, top, 280, 190, Clear), 400, 230);
        var plain = Render(Backdrop(128), Piece(shape, left, top, 280, 190, Clear), 400, 230);
        const int y = top + 95;
        // Red bends 1 / 0.889 of blue's bend: the line's red image lies deeper in than its blue one, so (R - B) is positive on its far side and
        // negative on its near side.
        var split = Enumerable.Range(left + 1, 20).Select(x => (X: x, D: lined(x, y).R - lined(x, y).B)).ToArray();
        var most = split.MaxBy(s => s.D);
        var least = split.MinBy(s => s.D);
        Assert.True(most.D >= 10 && least.D <= -10, $"the fringe: R - B from {least.D} at {least.X - left} pt to {most.D} at {most.X - left} pt in");
        Assert.True(most.X > least.X, $"red lies deeper in than blue: {most.X - left} against {least.X - left} pt");
        var redAt = Lands(new Size(280, 190), left, left - 20, 1);
        var blueAt = Lands(new Size(280, 190), left, left - 20, GlassMaterial.Optics.Blue);
        Assert.True(redAt - blueAt >= 0.5, $"the instrument: the bend parts red and blue by {redAt - blueAt:F2} pt there");
        // A grey backdrop stays grey through the whole rim (the fringe is the bend's, not a colour cast).
        for (var x = left; x < left + 30; x++) Assert.True(Math.Abs(plain(x, y).R - plain(x, y).B) <= 1, $"grey at {x - left} pt in: {plain(x, y)}");
    });

    [Theory]
    [Trait("Req", "PR-WIN-180")] // OD-0107: the specular rim: a bright line on the top and the bottom edge in both appearances, over a mid grey and over black
    [InlineData(false, (byte)128)]
    [InlineData(true, (byte)128)]
    [InlineData(false, (byte)0)]
    [InlineData(true, (byte)0)]
    public void The_specular_rim_lights_the_top_and_the_bottom_edge(bool light, byte level) => headless.OnUiThread(() =>
    {
        const int left = 40, top = 30;
        // OD-0147: the release material's rim (GlassLook.Release, which a piece that names it still draws); the canvas's light is angled from the
        // upper left (GlassTuningTests.The_rim_light_is_angled_from_the_upper_left)
        var pixels = Render(Backdrop(level), Piece(GlassShape.Capsule(RoundedCornerStyle.Continuous), left, top, 400, 80, GlassLook.Release), 480, 140, light);
        const int x = left + 200;
        var topRim = Luma(pixels(x, top)) - Luma(pixels(x, top + 10));
        var bottomRim = Luma(pixels(x, top + 79)) - Luma(pixels(x, top + 69));
        Assert.True(topRim >= 15, $"light {light} over {level}: top rim +{topRim:F1} ({pixels(x, top)} over {pixels(x, top + 10)})");
        Assert.True(bottomRim >= 15, $"light {light} over {level}: bottom rim +{bottomRim:F1} ({pixels(x, top + 79)} over {pixels(x, top + 69)})");
    });

    // WCAG relative luminance and contrast ratio (PopoverGlassCardTests' rule, glass-g2).
    private static double Linear(double v)
    {
        var c = v / 255;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    private static Color Over(Color text, Color glass)
    {
        var a = text.A / 255.0;
        byte Mix(byte t, byte g) => (byte)Math.Round(t * a + g * (1 - a));
        return Color.FromRgb(Mix(text.R, glass.R), Mix(text.G, glass.G), Mix(text.B, glass.B));
    }

    [Theory]
    [Trait("Req", "PR-WIN-180")] // the glass-g2 rule: body text on glass at 4.5:1 or more, secondary text at 3:1, over the app's own backdrops; the clear rim stays out of the text
    [InlineData(false, (byte)30, (byte)0)]    // the dark window base
    [InlineData(false, (byte)30, (byte)200)]  // dark content: light text-like rows on it
    [InlineData(false, (byte)58, (byte)0)]    // the Mac frame's level under the effort popover
    [InlineData(true, (byte)245, (byte)0)]    // the light window base
    [InlineData(true, (byte)245, (byte)40)]   // light content: dark text-like rows on it
    [InlineData(true, (byte)216, (byte)0)]    // the Mac light frame's level
    public void Text_on_the_glass_keeps_its_contrast(bool light, byte level, byte rows) => headless.OnUiThread(() =>
    {
        var appearance = light ? ThemeVariant.Light : ThemeVariant.Dark;
        var label = SwiftColor.LabelColor.Resolve(appearance);
        var secondary = Marrow.UI.Chrome.Chrome.SecondaryText.Resolve(appearance);
        // A pill (400 x 80), a panel (280 x 190) and a capsule (180 x 40), each with its text box: 24 pt in from a pill's ends and a line high,
        // 16 pt in on a panel.
        foreach (var (width, height, shape, inX, inY) in new[]
        {
            (400.0, 80.0, GlassShape.Capsule(RoundedCornerStyle.Continuous), 24, 31),
            (280.0, 190.0, GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous), 16, 16),
            (180.0, 40.0, GlassShape.Capsule(RoundedCornerStyle.Continuous), 16, 11),
        })
        {
            const int left = 50, top = 40;
            var bars = rows == 0 ? [] : Enumerable.Range(0, 60).Select(i => Bar(0, i * 6, 600, 1, rows)).ToArray();
            var pixels = Render(Backdrop(level, bars), Piece(shape, left, top, width, height), width + 100, height + 80, light);
            var worst = double.MaxValue;
            var worstSecondary = double.MaxValue;
            var at = "";
            for (var y = top + inY; y < top + height - inY; y++)
                for (var x = left + inX; x < left + width - inX; x++)
                {
                    var glass = pixels(x, y);
                    var g = Luminance(glass);
                    var c = Contrast(Luminance(Over(label, glass)), g);
                    if (c < worst) (worst, at) = (c, $"({x - left}, {y - top}) {glass}");
                    worstSecondary = Math.Min(worstSecondary, Contrast(Luminance(Over(secondary, glass)), g));
                }
            Assert.True(worst >= 4.5, $"{appearance} over {level}/{rows}, {width} x {height}: the label at {worst:F2}:1 at {at}");
            Assert.True(worstSecondary >= 3, $"{appearance} over {level}/{rows}, {width} x {height}: the secondary text at {worstSecondary:F2}:1");
        }
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0107 (the Mac frames, ED-356, ED-358): a small piece and an unlit panel keep a frosted rim; so does a piece without refraction
    public void A_small_piece_an_unlit_panel_and_a_plain_piece_keep_a_frosted_rim() => headless.OnUiThread(() =>
    {
        const int left = 60, top = 20;
        // A title capsule (90 x 28, a bend of 3.1: under Optics.ClearFrom) over a mid grey with a line 2 pt past its end.
        var capsule = GlassShape.Capsule(RoundedCornerStyle.Continuous);
        var small = Render(Backdrop(128, Bar(left - 4, 0, 2, 200, 255)), Piece(capsule, left, top, 90, 28, Clear), 220, 68);
        var smallPlain = Render(Backdrop(128), Piece(capsule, left, top, 90, 28, Clear), 220, 68);
        // The large panel (280 x 190) over a dark backdrop (20, under Optics' LitFrom): unlit, as the Mac head card.
        var panel = GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous);
        var dark = Render(Backdrop(20, Bar(left - 21, 0, 2, 400, 255)), Piece(panel, left, top, 280, 190, Clear), 400, 230);
        var darkPlain = Render(Backdrop(20), Piece(panel, left, top, 280, 190, Clear), 400, 230);
        // And a lit panel whose effect asks for no refraction (the voice pill's).
        var flat = Piece(panel, left, top, 280, 190);
        SwiftGlass.SetEffect(flat, GlassEffect.Regular(panel).WithoutRefraction().WithLook(Clear));
        var plainPiece = Render(Backdrop(128, Bar(left - 21, 0, 2, 400, 255)), flat, 400, 230);
        var flat2 = Piece(panel, left, top, 280, 190);
        SwiftGlass.SetEffect(flat2, GlassEffect.Regular(panel).WithoutRefraction().WithLook(Clear));
        var plainPieceBare = Render(Backdrop(128), flat2, 400, 230);
        int Lift(Func<int, int, Color> a, Func<int, int, Color> b, int y, int span) => Enumerable.Range(left + 1, span).Max(x => a(x, y).G - b(x, y).G);
        Assert.True(Lift(small, smallPlain, top + 14, 12) <= 20, $"the title capsule's rim: {Lift(small, smallPlain, top + 14, 12)}");
        Assert.True(Lift(dark, darkPlain, top + 95, 30) <= 20, $"the unlit panel's rim: {Lift(dark, darkPlain, top + 95, 30)}");
        Assert.True(Lift(plainPiece, plainPieceBare, top + 95, 30) <= 20, $"the plain piece's rim: {Lift(plainPiece, plainPieceBare, top + 95, 30)}");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // REVIEW_GlassWindowEdgeTests' window: where the bend reads past the window's surface, the clamped read stays frosted (no streaks)
    public void Where_the_window_clamps_the_bend_the_rim_stays_frosted() => headless.OnUiThread(() =>
    {
        // A window-sized glass over horizontal stripes 3 pt apart: at its left edge every outward read is clamped to the window's first column,
        // which would copy that column's stripes across the band, sharp.
        var stripes = Enumerable.Range(0, 60).Select(i => Bar(0, i * 6, 400, 3, 230)).ToArray();
        var pixels = Render(Backdrop(40, stripes), Piece(GlassShape.RoundedRectangle(0, RoundedCornerStyle.Continuous), 0, 0, 300, 200, Clear), 300, 200);
        foreach (var x in new[] { 2, 4, 8 })
        {
            var column = Enumerable.Range(60, 60).Select(y => Luma(pixels(x, y))).ToArray();
            var swing = column.Max() - column.Min();
            Assert.True(swing <= 25, $"{x} pt in from the window's edge the stripes swing {swing:F0} levels (frosted, not copied sharp)");
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-1")] // the release look (OD-0147 made the canvas's material every surface's; the release's is a piece's that names it) keeps its frosted rim
    public void A_piece_on_the_release_look_keeps_the_frosted_rim() => headless.OnUiThread(() =>
    {
        const int left = 60, top = 20;
        var shape = GlassShape.Capsule(RoundedCornerStyle.Continuous);
        var lined = Render(Backdrop(128, Bar(left - 17, 0, 2, 400, 255)), Piece(shape, left, top, 400, 80, GlassLook.Release), 520, 120);
        var plain = Render(Backdrop(128), Piece(shape, left, top, 400, 80, GlassLook.Release), 520, 120);
        var clear = Render(Backdrop(128, Bar(left - 17, 0, 2, 400, 255)), Piece(shape, left, top, 400, 80, Clear), 520, 120);
        const int y = top + 40;
        var frosted = Enumerable.Range(left + 1, 30).Max(x => lined(x, y).G - plain(x, y).G);
        var sharp = Enumerable.Range(left + 1, 30).Max(x => clear(x, y).G - plain(x, y).G);
        Assert.True(frosted <= 20, $"the release rim lifts the bent line by {frosted} levels (frosted)");
        Assert.True(sharp >= 30, $"and a clear look's by {sharp} (the instrument sees a sharp line)");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0108: the warm-up's programs are the draw's, with both backdrops (the blur and the clear rim's) bound
    public void The_warm_up_programs_take_the_blur_and_the_clear_rim() => headless.OnUiThread(() =>
    {
        var programs = GlassMaterial.WarmupPrograms();
        Assert.Equal(GlassMaterial.WarmShapes, programs.Count);
        for (var count = 1; count <= GlassMaterial.WarmShapes; count++)
        {
            Assert.Same(GlassMaterial.WarmupProgram(count), programs[count - 1]);
            Assert.Equal(["backdrop", "sharp"], programs[count - 1].Children.OrderBy(c => c, StringComparer.Ordinal));
        }
    });
}
