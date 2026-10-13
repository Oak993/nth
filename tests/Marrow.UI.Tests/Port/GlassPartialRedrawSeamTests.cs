// ED-NEW-glass-seams-1: a partial redraw leaves every antialiased drawing on a glass piece exactly as a whole redraw draws it, so no seam can show where
// the frame's region ends inside the piece. Skia's CPU rasterizer clips the edges of an antialiased path to the frame's clip when the path's bounds are
// not inside it, and then rasterizes it differently (up to 30 levels beside the cut, up to 22 far from it, measured): a piece's rims, its inset shade
// and its icons, cut by the region a change under the glass reaches, drew a faint seam that came and went as regions redrew. Here a regular glass
// piece (not one that reads its own backdrop) carries two capsule rings, an inset shade, a line of text and three stroked icons, at a fractional x;
// a small block under it changes at several places, so the region the change reaches ends in its caps, across its text and through its icons.
// The piece and a margin round it must be exactly what a whole redraw of the same frame gives, at 100, 125 and 200 %.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
public sealed class GlassPartialRedrawSeamTests(HeadlessSession headless)
{
    private const double Left = 40.37, Top = 50, Width = 430, Height = 46, Radius = 23;

    private static Shape Ring(double inset, Color color)
    {
        var outer = new Rect(0, 0, Width, Height);
        var inner = outer.Deflate(inset);
        return new Shape
        {
            Fill = new SolidColorBrush(color),
            Data = new GeometryGroup
            {
                FillRule = FillRule.EvenOdd,
                Children = { new RectangleGeometry(outer, Radius, Radius), new RectangleGeometry(inner, Radius - inset, Radius - inset) },
            },
        };
    }

    private static Shape Icon(double left) => new()
    {
        Width = 18, Height = 18, Stroke = Brushes.White, StrokeThickness = 1.6, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(left, 0, 0, 0), Data = Geometry.Parse("M 2,9 A 7,7 0 1 1 16,9 A 7,7 0 1 1 2,9 Z M 9,4 L 9,14 M 4,9 L 14,9"),
    };

    // A composer-like piece: the glass, then over it an inset shade, two antialiased rings, a line of text and three icons.
    private static Panel Piece()
    {
        var glass = new Border { Background = Brushes.Transparent };
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.RoundedRectangle(Radius, RoundedCornerStyle.Continuous, circularInBand: true)));
        var shade = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = -2, Blur = 4, Color = Color.FromArgb(90, 0, 0, 0) }),
        };
        var text = new TextBlock
        {
            Text = "Ask anything, the glass keeps its edges", Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(44.3, 0, 0, 0),
        };
        return new Panel
        {
            Width = Width, Height = Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Left, Top, 0, 0), UseLayoutRounding = false,
            Children = { glass, shade, Ring(1, Color.FromArgb(120, 255, 255, 255)), Ring(0.5, Color.FromArgb(160, 230, 236, 255)), Icon(14.6), text, Icon(352.2), Icon(392.7) },
        };
    }

    // The headless window's own scale (Avalonia.Headless.IHeadlessWindow.SetRenderScaling, internal), as GlassMaterialKeptBackdropReviewTests sets it.
    private static void Scale(Window window, double scale)
    {
        var type = typeof(AvaloniaHeadlessPlatformOptions).Assembly.GetType("Avalonia.Headless.IHeadlessWindow", throwOnError: true)!;
        type.GetMethod("SetRenderScaling")!.Invoke(window.PlatformImpl, [scale]);
    }

    private static int Largest(Func<int, int, Color> a, Func<int, int, Color> b, PixelRect area, out PixelPoint at)
    {
        var most = 0;
        at = default;
        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
            {
                Color p = a(x, y), q = b(x, y);
                var d = Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B)));
                if (d > most) { most = d; at = new PixelPoint(x, y); }
            }
        return most;
    }

    // The block lies under the glass (drawn before the piece) at each x, in points from the window's left: beside the left cap, in it, under the
    // text, between the text and the icons, under the icons, in the right cap and beside it.
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    [Trait("Decision", "ED-NEW-glassseams-1")] // traceability alias of ED-NEW-glass-seams-1
    public void A_partial_redraw_leaves_the_drawings_on_a_glass_piece_as_a_whole_redraw_draws_them(double scale) => headless.OnUiThread(() =>
    {
        var block = new Border
        {
            Width = 6, Height = 6, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, Top + 20, 0, 0),
        };
        using var scene = new GlassScene(new Panel { Children = { new GlassBackdropPattern(), block } }, Piece(), 640, 150);
        Scale(scene.Window, scale);
        var area = new PixelRect((int)Math.Floor((Left - 4) * scale), (int)Math.Floor((Top - 4) * scale), (int)Math.Ceiling((Width + 8) * scale), (int)Math.Ceiling((Height + 8) * scale));
        var problems = new List<string>();
        foreach (var x in new[] { 12.3, 47.1, 96.4, 170.8, 251.5, 333.2, 371.6, 405.9, 455.2, 520.7 })
        {
            block.Margin = new Thickness(x, Top + 20, 0, 0);
            foreach (var visual in scene.Window.GetVisualDescendants()) visual.InvalidateVisual();
            _ = scene.Pixels();
            block.Background = block.Background == Brushes.Black ? Brushes.White : Brushes.Black;
            var partial = scene.Pixels();
            foreach (var visual in scene.Window.GetVisualDescendants()) visual.InvalidateVisual();
            var whole = scene.Pixels();
            var apart = Largest(partial, whole, area, out var at);
            if (apart != 0) problems.Add($"block at {x}: {apart} levels at {at}");
        }
        Assert.True(problems.Count == 0, $"at {scale}: the piece is not a whole redraw's: " + string.Join("; ", problems));
    });
}
