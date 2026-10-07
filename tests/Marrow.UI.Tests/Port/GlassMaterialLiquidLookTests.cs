// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184 (.glassEffect(.regular)); App/Views/Composer/ComposerView.swift:1304.
// OD-0107 and its amendment: the app's glass is the liquid glass of the Mac videos, built with the two MIT repos' techniques
// (rizzytoday/liquid-glass's displacement map and chromatic aberration, dashersw/liquid-glass-js's rim ring and tint gradient).
// ED-356: the material is translucent (it keeps the alpha of what lies under it), lifts the colour under it, and has an edge:
// a specular rim on every side (the top brightest), a light inner edge, a contact line, a refraction bend and a colour fringe sized by the
// piece. The numbers below are the design's own (GlassMaterial.Optics, GlassMaterial.Film), measured on the headless renderer.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialLiquidLookTests(HeadlessSession headless)
{
    private static Border Piece(GlassShape shape, double width, double height, double left = double.NaN)
    {
        var piece = new Border
        {
            Background = Brushes.Transparent, Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = double.IsNaN(left) ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Margin = double.IsNaN(left) ? default : new Thickness(left, 0, 0, 0),
        };
        if (shape.Kind == GlassShapeKind.RoundedRectangle) piece.CornerRadius = new CornerRadius(shape.CornerRadius ?? 0);
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(shape));
        return piece;
    }

    private static Func<int, int, Color> Render(Control? backdrop, Control glass, IBrush? windowBackground, double width = 256, double height = 160, bool light = false)
    {
        var panel = new Panel();
        if (backdrop is not null) panel.Children.Add(backdrop);
        panel.Children.Add(glass);
        var window = new Window
        {
            Width = width, Height = height, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark, Background = windowBackground,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Content = panel,
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

    // The captured frame is premultiplied; the colour the pixel carries is its channels over its alpha.
    private static double Straight(byte channel, byte alpha) => alpha == 0 ? 0 : channel * 255.0 / alpha;
    private static double Luma(Color c) => 0.2126 * Straight(c.R, c.A) + 0.7152 * Straight(c.G, c.A) + 0.0722 * Straight(c.B, c.A);

    private static GlassShape Panel => GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous);

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0107: over the Acrylic window's transparent pixels the glass is a light translucent film, not an opaque veil
    public void Over_a_transparent_backdrop_the_glass_is_a_light_translucent_film_and_never_black() => headless.OnUiThread(() =>
    {
        var pixels = Render(null, Piece(Panel, 160, 120), Brushes.Transparent);
        var outside = pixels(10, 10);
        Assert.True(outside.A == 0, $"the instrument: the window around the glass is transparent ({outside})");
        var centre = pixels(128, 80);
        // dashersw's tint gradient at 0.17 alone: alpha 43, a light grey (0.94 white at the middle row).
        Assert.InRange(centre.A, (byte)35, (byte)90);
        Assert.True(Luma(centre) >= 170, $"the film is light, not black: {centre} (straight luma {Luma(centre):F0})");
        // No pixel of the shape is black or opaque: the midline and the midcolumn, rim and edge included.
        for (var x = 49; x <= 206; x++)
        {
            var p = pixels(x, 80);
            Assert.True(p.A > 20 && p.A < 200 && Luma(p) >= 150, $"x {x}: {p} (straight luma {Luma(p):F0})");
        }
        for (var y = 21; y <= 138; y++)
        {
            var p = pixels(128, y);
            Assert.True(p.A > 20 && p.A < 200 && Luma(p) >= 150, $"y {y}: {p} (straight luma {Luma(p):F0})");
        }
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0107: the scrim and the Acrylic under it show through; the sharp content under the glass does not leak through it
    public void Over_a_translucent_scrim_the_glass_keeps_its_translucency_and_the_sharp_content_under_it_does_not_leak() => headless.OnUiThread(() =>
    {
        var scrim = new Panel { Background = new SolidColorBrush(Color.FromArgb(66, 0, 0, 0)) };
        scrim.Children.Add(new Border { Width = 4, Height = 160, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        var pixels = Render(scrim, Piece(Panel, 160, 120), Brushes.Transparent);
        Assert.Equal((byte)66, pixels(10, 10).A); // the instrument: the scrim alone
        Assert.Equal((byte)255, pixels(128, 5).A); // and the sharp bar above the glass is opaque
        // Away from the bar: the scrim's alpha with the film over it, 66 + 0.17 * (255 - 66) = 98, not opaque.
        var away = pixels(70, 80);
        Assert.InRange(away.A, (byte)90, (byte)125);
        // Over the bar: the bar is blurred (the 4 pt bar under a 12 pt blur adds about a tenth), never the opaque bar through the glass.
        var over = pixels(128, 80);
        Assert.InRange(over.A, (byte)90, (byte)150);
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0107: a known backdrop shows through: the glass passes at least 75 % of a change under it
    public void A_known_backdrop_shows_through_the_glass_by_at_least_three_quarters() => headless.OnUiThread(() =>
    {
        var dark = Render(new Panel { Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)) }, Piece(Panel, 160, 120), Brushes.Black)(128, 80);
        var light = Render(new Panel { Background = new SolidColorBrush(Color.FromRgb(180, 180, 180)) }, Piece(Panel, 160, 120), Brushes.Black)(128, 80);
        var passed = (light.G - dark.G) / 120.0;
        Assert.True(passed >= 0.75, $"the glass passes {passed:P0} of the backdrop (60 gives {dark}, 180 gives {light})");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0107: the Mac glass lifts what is under it: brighter and more saturated, never darker or flat grey
    public void The_glass_lifts_the_brightness_and_the_saturation_of_what_is_under_it() => headless.OnUiThread(() =>
    {
        var grey = Render(new Panel { Background = new SolidColorBrush(Color.FromRgb(128, 128, 128)) }, Piece(Panel, 160, 120), Brushes.Black)(128, 80);
        // glass-g3's law: the gain c + 0.95 c (1 - c)^2, 128 to 158 (it was 110 under the 18 % dark veil, then 147 under the 0.17 film).
        Assert.InRange(grey.G, (byte)150, (byte)166);
        var under = Color.FromRgb(150, 110, 90);
        var coloured = Render(new Panel { Background = new SolidColorBrush(under) }, Piece(Panel, 160, 120), Brushes.Black)(128, 80);
        var chroma = Math.Max(coloured.R, Math.Max(coloured.G, coloured.B)) - Math.Min(coloured.R, Math.Min(coloured.G, coloured.B));
        // The saturation lift (1.4) outweighs the film's wash (0.83): 60 * 1.4 * 0.83 = 70.
        Assert.True(chroma >= 62, $"the colour under the glass keeps and gains its saturation: {under} gives {coloured} (chroma {chroma}, under it 60)");
        Assert.True(Luma(coloured) >= 0.2126 * 150 + 0.7152 * 110 + 0.0722 * 90 + 12, $"and it is lighter: {coloured}");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0107: the specular rim on every side, brightest at the top (the Mac composer pill and title capsules)
    public void A_flat_backdrop_shows_a_specular_rim_on_every_side_the_top_brightest_and_a_contact_line_at_the_sides() => headless.OnUiThread(() =>
    {
        // The panel lies at x 48 to 208, y 20 to 140. Each edge's first pixel against the pixel 6 pt further in on the same line.
        var pixels = Render(new Panel { Background = new SolidColorBrush(Color.FromRgb(128, 128, 128)) }, Piece(Panel, 160, 120), Brushes.Black);
        var top = pixels(128, 20).G - pixels(128, 26).G;
        var bottom = pixels(128, 139).G - pixels(128, 133).G;
        var left = pixels(48, 80).G - pixels(54, 80).G;
        var right = pixels(207, 80).G - pixels(201, 80).G;
        Assert.True(top >= 18, $"top rim +{top}");
        Assert.True(bottom >= 18, $"bottom rim +{bottom}");
        Assert.True(left >= 2 && right >= 2, $"side rims +{left} and +{right}");
        // The top rim is the brightest line (the bottom one stands out as far from the darker body under it, as on the Mac pill: +36 and +46).
        Assert.True(pixels(128, 20).G >= pixels(128, 139).G && pixels(128, 139).G > pixels(48, 80).G, $"the top is brightest, the sides least: {pixels(128, 20)} {pixels(128, 139)} {pixels(48, 80)}");
        // The light inner edge under the top rim: 1.5 pt in is lighter than 9.5 pt in (+5 here, +8 on the Mac pill's darker glass).
        Assert.True(pixels(128, 21).G >= pixels(128, 29).G + 3, $"inner edge {pixels(128, 21)} against {pixels(128, 29)}");
        // The contact line just outside, dark at the sides (the Mac frames' 1 px line at a capsule's ends), none at the top.
        Assert.True(pixels(47, 80).G <= 128 - 50, $"side contact line {pixels(47, 80)}");
        Assert.InRange(pixels(128, 19).G, (byte)124, (byte)132);
        Assert.Equal(pixels(10, 10), pixels(45, 80)); // and nothing past it
    });

    [Fact]
    [Trait("Decision", "ED-570")] // the voice pill's glass alone draws no contact line outside its edge; every other glass keeps it
    public void A_glass_that_asks_for_no_contact_line_draws_none_outside_its_edge_and_keeps_its_rim_and_the_rest_keep_the_line() => headless.OnUiThread(() =>
    {
        var flat = () => new Panel { Background = new SolidColorBrush(Color.FromRgb(128, 128, 128)) };
        var other = Render(flat(), Piece(Panel, 160, 120), Brushes.Black);
        var quiet = Piece(Panel, 160, 120);
        SwiftGlass.SetEffect(quiet, GlassEffect.Regular(Panel).WithoutContactLine());
        var plain = Render(flat(), quiet, Brushes.Black);
        // The instrument: the same glass without the flag still has its dark line at the side (the test above), and the pixel beside it is the backdrop alone.
        Assert.True(other(47, 80).G <= 128 - 50, $"other glass keeps the contact line {other(47, 80)}");
        // The flagged glass leaves the pixels just outside its edge as the backdrop is: the side, the top, the bottom, and the whole 1.25 pt skin beyond.
        foreach (var (x, y) in new[] { (47, 80), (46, 80), (128, 19), (128, 140), (209, 80), (208, 80) })
            Assert.True(plain(x, y) == other(10, 10), $"no contact line at ({x}, {y}): {plain(x, y)} against the backdrop {other(10, 10)}");
        // Everything inside the edge is the glass as it was: the rim light, the inner edge and the body.
        foreach (var (x, y) in new[] { (128, 20), (128, 21), (128, 139), (48, 80), (54, 80), (128, 80), (207, 80) })
            Assert.True(plain(x, y) == other(x, y), $"inside the edge ({x}, {y}): {plain(x, y)} against {other(x, y)}");
    });

    // Where along the ramp the glass read each channel at (x, y), in levels (= pixels of the ramp), from the ramp and its reverse:
    // a = (k s + f)(1 - h) + h and b = (k (1 - s) + f)(1 - h) + h with the same film f, light h and pass-through k, so the
    // light the rim and the inner edge add falls out. k is measured on two interior points, k + 2f on the deep point.
    private static (double R, double G, double B) Read(Func<int, int, Color> a, Func<int, int, Color> b, int x, int y, int deepX, int deepX2)
    {
        double Channel(Func<Color, byte> pick)
        {
            double A(int px) => pick(a(px, y)) / 255.0;
            double B(int px) => pick(b(px, y)) / 255.0;
            var k = (A(deepX2) - A(deepX)) / ((deepX2 - deepX) / 255.0);
            var sum = A(deepX) + B(deepX);
            var pass = (2 - (A(x) + B(x))) / (2 - sum);
            var s = ((A(x) - B(x)) / (k * pass) + 1) / 2;
            return s * 255;
        }
        return (Channel(c => c.R), Channel(c => c.G), Channel(c => c.B));
    }

    [Theory]
    // A panel (160 x 120): the bend 40 at the edge, 33.8 + 1.3 (the ring) 2.5 pt in, green 0.944 of it; red to blue 11 % of it.
    [InlineData(160, 120, 16, 2, 28, 40, 3.0, 6.0, 40)]
    // A title capsule (90 x 28): a fifth of the panel share of its size (3.1, ring 2.2), 2.5 + 0.7 at 1.5 pt in; its fringe under a pixel.
    [InlineData(90, 28, 14, 1, 2.0, 4.5, -0.5, 1.0, 30)]
    // The composer pill (300 x 46, the bar is 818 wide): 9.0 at the edge, ring 3.7, 8.0 + 1.1 at 1.5 pt in; red to blue 1.0 (the reading is good
    // to about 0.7 per channel).
    [InlineData(300, 46, 23, 1, 6.5, 11, 0.3, 2.0, 40)]
    [Trait("Req", "PR-WIN-180")] // OD-0107: rizzytoday's bend and chromatic aberration, dashersw's rim ring, sized by the piece
    public void The_edge_bends_and_fringes_by_the_size_of_the_piece(double width, double height, double radius, int inset, double bendLow, double bendHigh, double fringeLow, double fringeHigh, int span) =>
        headless.OnUiThread(() =>
        {
            const int left = 100, offset = 40; // the ramp starts 40 px in, so every read lies on it, clear of its clamped ends
            var shape = GlassShape.RoundedRectangle(radius, RoundedCornerStyle.Continuous);
            // Read in the light appearance, where the body is affine in what lies under it (the dark one's gain is not): the bend is the same in both.
            var a = Render(new GlassRamp(false, offset), Piece(shape, width, height, left), Brushes.Black, 420, 160, light: true);
            var b = Render(new GlassRamp(true, offset), Piece(shape, width, height, left), Brushes.Black, 420, 160, light: true);
            var x = left + inset; // the pixel `inset` + 0.5 in from the left edge, on the middle row
            var middle = (int)(left + width / 2);
            // k from two points `span` either side of the middle, where nothing bends sideways (on the ramp, short of its clamped end at 295).
            var read = Read(a, b, x, 80, middle - span, middle + span);
            var at = x - offset;
            var bend = at - read.G;
            var fringe = read.B - read.R;
            Assert.True(bend >= bendLow && bend <= bendHigh, $"{width} x {height}: bend {bend:F1} pt at {inset + 0.5} pt in (read R {read.R:F1} G {read.G:F1} B {read.B:F1}, the pixel is at {at})");
            Assert.True(fringe >= fringeLow && fringe <= fringeHigh, $"{width} x {height}: fringe {fringe:F2} pt from red to blue (bend {bend:F1}; read R {read.R:F1} G {read.G:F1} B {read.B:F1}, the pixel is at {at})");
            // Outward: the edge reads the backdrop beyond it, and deep inside nothing bends.
            Assert.True(read.R < at && read.G < at && read.B < at);
        });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // OD-0108: the warm-up builds every program the material draws with, by the material's own builder
    public void The_warm_up_programs_cover_every_shape_count_with_both_stages_and_the_inside() => headless.OnUiThread(() =>
    {
        var programs = GlassMaterial.WarmupPrograms();
        Assert.Equal(GlassMaterial.WarmShapes, programs.Count);
        for (var count = 1; count <= GlassMaterial.WarmShapes; count++)
        {
            var program = programs[count - 1];
            Assert.Same(GlassMaterial.WarmupProgram(count), program); // the cached program a draw of that many pieces uses
            Assert.Contains("stage", program.Uniforms); // DstOut and Plus are one program
            Assert.Contains("interior", program.Uniforms); // and so is a lone piece's inside
            Assert.Contains($"optics{count - 1}", program.Uniforms);
        }
    });
}
