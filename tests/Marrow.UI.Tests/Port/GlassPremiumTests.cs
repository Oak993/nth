// ED-NEW-glasslook-3 (the owner, 2026-10-11, relayed by the lead: "The Glass UI side by side comparison still shows the new glass ui to be plasticky? and
// not premium?"): the material's finish knobs and the premium candidate look (GlassLook.Premium) hold the lead's principles. Glass is what it does to
// the backdrop (a slight lift, a little frost, no slab); the rim is a hairline lit from the top left with a faint counter-highlight and no inner bevel;
// the bezel is a convex squircle lens whose centre is undistorted; the fringe is at most about 1 pt; the shadow is soft, wide and faint, with no halo.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassPremiumTests(HeadlessSession headless)
{
    private static Border Piece(GlassShape shape, double left, double top, double width, double height, GlassLook look)
    {
        var piece = new Border
        {
            Background = Brushes.Transparent, Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(left, top, 0, 0),
        };
        if (shape.Kind == GlassShapeKind.RoundedRectangle) piece.CornerRadius = new CornerRadius(shape.CornerRadius ?? 0);
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(shape).WithLook(look));
        return piece;
    }

    private static Border Bar(double left, double top, double width, double height, Color colour) => new()
    {
        Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(left, top, 0, 0), Background = new SolidColorBrush(colour),
    };

    private static Panel Backdrop(Color colour, params Border[] bars)
    {
        var panel = new Panel { Background = new SolidColorBrush(colour) };
        foreach (var bar in bars) panel.Children.Add(bar);
        return panel;
    }

    private static Func<int, int, Color> Render(Control backdrop, Control? glass, double width, double height, bool light = false)
    {
        var content = new Panel { Children = { backdrop } };
        if (glass is not null) content.Children.Add(glass);
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark, Background = Brushes.Black, Content = content };
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

    private static readonly Color Grey = Color.FromRgb(128, 128, 128);

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-3")] // principle 6: the fringe is at most about 1 pt, never a rainbow; the release's own is wider
    public void The_premium_fringe_parts_red_and_blue_by_at_most_a_point() => headless.OnUiThread(() =>
    {
        const int left = 60, top = 20;
        var shape = GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous);
        int Split(GlassLook look)
        {
            // a 6 pt line 21 pt out (the squircle's edge minifies what it bends: a thinner line falls between its pixels)
            var lined = Render(Backdrop(Grey, Bar(left - 24, 0, 6, 400, Colors.White)), Piece(shape, left, top, 280, 190, look), 400, 230);
            var plain = Render(Backdrop(Grey), Piece(shape, left, top, 280, 190, look), 400, 230);
            const int y = top + 95;
            var red = Enumerable.Range(left + 1, 30).MaxBy(x => lined(x, y).R - plain(x, y).R);
            var blue = Enumerable.Range(left + 1, 30).MaxBy(x => lined(x, y).B - plain(x, y).B);
            Assert.True(lined(red, y).R - plain(red, y).R >= 20, $"the line shows bent into the rim ({lined(red, y)} over {plain(red, y)})");
            return Math.Abs(red - blue);
        }
        var premium = Split(GlassLook.Premium);
        var wide = Split(GlassLook.Premium with { Profile = 0, FringeCap = 0, Fringe = 3 });
        Assert.True(premium <= 1, $"the premium fringe parts red and blue by {premium} pt");
        Assert.True(wide >= 3, $"the instrument: an uncapped wide fringe parts them by {wide} pt");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-3")] // principle 3: the squircle bezel bends at the edge and the centre is undistorted (a line well inside the bezel stays where it is)
    public void The_squircle_lens_bends_at_the_edge_and_leaves_the_centre_undistorted() => headless.OnUiThread(() =>
    {
        const int left = 60, top = 20;
        var shape = GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous);
        var look = GlassLook.Premium with { Blur = 0.3 };
        // a line 30 pt in from the left edge (past the bezel's steep part) and one 21 pt outside it
        var inner = Render(Backdrop(Grey, Bar(left + 29, 0, 2, 400, Colors.White)), Piece(shape, left, top, 280, 190, look), 400, 230);
        var outer = Render(Backdrop(Grey, Bar(left - 24, 0, 6, 400, Colors.White)), Piece(shape, left, top, 280, 190, look), 400, 230);
        var plain = Render(Backdrop(Grey), Piece(shape, left, top, 280, 190, look), 400, 230);
        const int y = top + 95;
        var innerAt = Enumerable.Range(left + 10, 50).MaxBy(x => inner(x, y).G - plain(x, y).G);
        Assert.True(Math.Abs(innerAt - (left + 30)) <= 1, $"the line 30 pt in shows at {innerAt - left} pt in");
        var outerAt = Enumerable.Range(left + 1, 30).MaxBy(x => outer(x, y).G - plain(x, y).G);
        Assert.True(outer(outerAt, y).G - plain(outerAt, y).G >= 20 && outerAt - left <= 8, $"the line 21 pt out is bent into the bezel's edge: {outerAt - left} pt in, +{outer(outerAt, y).G - plain(outerAt, y).G}");
    });

    [Theory]
    [Trait("Decision", "ED-NEW-glasslook-3")] // principle 2: a hairline specular lit from the top left, a faint counter-highlight at the bottom, no inner bevel under it
    [InlineData(false)]
    [InlineData(true)]
    public void The_premium_rim_is_a_hairline_lit_from_the_top_left_with_a_faint_counter_highlight(bool light) => headless.OnUiThread(() =>
    {
        const int left = 40, top = 30;
        var pixels = Render(Backdrop(Grey), Piece(GlassShape.Capsule(RoundedCornerStyle.Continuous), left, top, 400, 80, GlassLook.Premium), 480, 140, light);
        const int x = left + 200;
        double Lift(int y) => Luma(pixels(x, y)) - Luma(pixels(x, top + 40));
        var topLine = Lift(top);
        var bottomLine = Lift(top + 79);
        Assert.True(topLine >= 20, $"light {light}: the lit top hairline +{topLine:F1}");
        Assert.True(bottomLine >= 3 && bottomLine <= topLine * 0.6, $"light {light}: a fainter counter-highlight at the bottom +{bottomLine:F1} (top +{topLine:F1})");
        // a hairline: 2 pt in from the edge the rim is gone, and there is no inner bevel's glow down to 12 pt in
        for (var y = top + 2; y <= top + 12; y++) Assert.True(Math.Abs(Lift(y)) <= 3, $"light {light}: {y - top} pt under the top edge +{Lift(y):F1} (no bevel)");
        // and it fades around the shape: the right end (facing away from the light) is dimmer than the top
        var rightEnd = Luma(pixels(left + 399, top + 40)) - Luma(pixels(left + 380, top + 40));
        Assert.True(rightEnd < topLine * 0.6, $"light {light}: the right end +{rightEnd:F1}");
    });

    [Theory]
    [Trait("Decision", "ED-NEW-glasslook-3")] // principle 1: the body is what lies behind it, lifted a little and frosted: never a dark or milky slab
    [InlineData(false, (byte)30)]
    [InlineData(false, (byte)128)]
    [InlineData(true, (byte)245)]
    [InlineData(true, (byte)128)]
    public void The_premium_body_is_the_backdrop_lifted_a_little_not_a_slab(bool light, byte level) => headless.OnUiThread(() =>
    {
        const int left = 40, top = 30;
        var backdrop = Color.FromRgb(level, level, level);
        var pixels = Render(Backdrop(backdrop), Piece(GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous), left, top, 280, 190, GlassLook.Premium), 360, 250, light);
        var body = Luma(pixels(left + 140, top + 95));
        Assert.True(body >= level - 6, $"light {light} over {level}: the body {body:F1} is not darkened into a slab");
        // the lift over the Mac's own vibrancy (dark glass: c + 0.95 c (1 - c)^2, ED-358) is small: at most 12 % of the way to white
        var c0 = level / 255.0;
        var vibrant = light ? c0 : c0 + 0.95 * c0 * (1 - c0) * (1 - c0);
        Assert.True(body <= 255 * (vibrant + 0.12 * (1 - vibrant)) + 6, $"light {light} over {level}: the body {body:F1} is lifted a little, not milky");
        // colour shows through: an orange backdrop stays orange
        var orange = Render(Backdrop(Color.FromRgb(230, 120, 60)), Piece(GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous), left, top, 280, 190, GlassLook.Premium), 360, 250, light);
        var tone = orange(left + 140, top + 95);
        Assert.True(tone.R - tone.B >= 120, $"light {light}: the orange shows through as {tone}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-3")] // principle 5: a soft, wide, faint shadow under the glass and the tight contact line; no halo (nothing outside is lightened)
    public void The_premium_shadow_is_soft_wide_and_faint_with_no_halo() => headless.OnUiThread(() =>
    {
        const int left = 60, top = 30;
        var shape = GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous);
        var bare = Render(Backdrop(Grey), null, 400, 280);
        var glass = Render(Backdrop(Grey), Piece(shape, left, top, 280, 190, GlassLook.Premium), 400, 280);
        const int x = left + 140;
        var below = Enumerable.Range(1, 30).Select(dy => Luma(bare(x, top + 190 + dy)) - Luma(glass(x, top + 190 + dy))).ToArray();
        Assert.True(below[1] >= 4, $"just under the glass the shadow darkens by {below[1]:F1}");
        Assert.True(below[1] <= 40, $"and faintly: {below[1]:F1}");
        Assert.True(below[8] >= 1, $"it is wide: {below[8]:F1} at 9 pt");
        Assert.True(below[29] <= 1, $"and soft, gone by 30 pt: {below[29]:F1}");
        for (var dy = 1; dy < 29; dy++) Assert.True(below[dy] >= below[dy + 1] - 1, $"it fades steadily: {below[dy]:F1} then {below[dy + 1]:F1} at {dy + 1} pt");
        // no halo: no pixel around the glass is lighter than the backdrop
        for (var y = 0; y < 280; y += 3)
            for (var px = 0; px < 400; px += 3)
            {
                var inside = px >= left - 1 && px <= left + 281 && y >= top - 1 && y <= top + 191;
                if (!inside) Assert.True(Luma(glass(px, y)) <= Luma(bare(px, y)) + 1, $"({px}, {y}) {glass(px, y)} over {bare(px, y)}");
            }
    });
}
