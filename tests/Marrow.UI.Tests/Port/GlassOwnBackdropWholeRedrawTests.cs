// ED-NEW-glass-c10-6: a piece that reads its own backdrop (GlassEffect.OwnBackdrop, the voice pill) is drawn whole whenever a frame redraws any of it.
// Skia antialiases a path that the frame's clip cuts differently from the same path drawn whole (1 to 3 levels on its edge pixels, far from the cut);
// the pill's rim draws its shade, fringe, rings and highlights as such paths over the glass, so a redraw beside the pill (the composer's text column)
// changed the pill's cap by 3 levels (ComposerPillClearsTextTests). Here antialiased capsule rings and an inset shade lie over an own-backdrop capsule
// placed as the pill is (unrounded, at a fractional x), and a block beside its left end changes: the region the block's change reaches ends in the
// capsule's right cap. The piece must be exactly what a whole drawing gives.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using Shape = Avalonia.Controls.Shapes.Path;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassOwnBackdropWholeRedrawTests(HeadlessSession headless)
{
    private const double Top = 40, Height = 40;

    private static Shape Ring(double width, double inset, Color color)
    {
        var outer = new Rect(0, 0, width, Height);
        var inner = outer.Deflate(inset);
        return new Shape
        {
            Fill = new SolidColorBrush(color),
            Data = new GeometryGroup
            {
                FillRule = FillRule.EvenOdd,
                Children = { new RectangleGeometry(outer, Height / 2, Height / 2), new RectangleGeometry(inner, inner.Height / 2, inner.Height / 2) },
            },
        };
    }

    // The voice pill's arrangement (VoicePill): the glass capsule and, over it, siblings that draw an inset shade and antialiased capsule rings.
    private static Panel Pill(double left, double width)
    {
        var glass = new Border { Background = Brushes.Transparent };
        var effect = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Circular)).WithoutRefraction().WithoutContactLine();
        SwiftGlass.SetEffect(glass, effect.WithOwnBackdrop().WithLook(GlassLook.Release));
        var shade = new Border
        {
            CornerRadius = new CornerRadius(Height / 2),
            BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = -2, Blur = 4, Color = Color.FromArgb(90, 0, 0, 0) }),
        };
        return new Panel
        {
            Width = width, Height = Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(left, Top, 0, 0), UseLayoutRounding = false,
            Children = { glass, shade, Ring(width, 1, Color.FromArgb(120, 255, 255, 255)), Ring(width, 0.5, Color.FromArgb(160, 230, 236, 255)) },
        };
    }

    private static int Largest(Func<int, int, Color> a, Func<int, int, Color> b, Rect box, out PixelPoint at)
    {
        var most = 0;
        at = default;
        for (var y = (int)box.Top - 2; y < (int)box.Bottom + 2; y++)
            for (var x = (int)box.Left - 2; x < (int)box.Right + 2; x++)
            {
                Color p = a(x, y), q = b(x, y);
                var d = Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B)));
                if (d > most) { most = d; at = new PixelPoint(x, y); }
            }
        return most;
    }

    // The cut falls in the right cap (100 pt, as the closing pill), on the straight body (300 pt; the left cap still changed) and just inside the right end.
    [Theory]
    [InlineData(49.757, 100.012)]
    [InlineData(60.25, 300)]
    [InlineData(40.5, 110)]
    [Trait("Decision", "ED-NEW-glassrest-6")] // traceability alias of ED-NEW-glass-c10-6
    public void A_redraw_beside_an_own_backdrop_piece_leaves_it_as_a_whole_drawing_gives(double left, double width) => headless.OnUiThread(() =>
    {
        var block = new Border
        {
            Width = 16, Height = Height, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(20, Top, 0, 0),
        };
        using var scene = new GlassScene(new GlassBackdropPattern { Flat = true }, new Panel { Children = { block, Pill(left, width) } }, 420, 120);
        _ = scene.Pixels();
        block.Background = Brushes.White;
        var partial = scene.Pixels();
        foreach (var visual in scene.Window.GetVisualDescendants()) visual.InvalidateVisual();
        var whole = scene.Pixels();
        var apart = Largest(partial, whole, new Rect(left, Top, width, Height), out var at);
        Assert.True(apart == 0, $"the own-backdrop piece is {apart} levels from a whole drawing at {at}");
    });
}
