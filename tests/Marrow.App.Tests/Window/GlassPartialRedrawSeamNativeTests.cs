using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using Shape = Avalonia.Controls.Shapes.Path;

namespace Marrow.App.Tests.Window.Native;

// ED-NEW-glass-seams-1, native evidence for the renderer the app ships on (Avalonia's Win32 default, ANGLE over Direct3D 11, Skia's GPU backend):
// a partial redraw leaves every antialiased drawing on a glass piece as a whole redraw of the same frame draws it, with no growth of the frame's region
// (the material redraws a piece whole only on Skia's CPU rasterizer, which clips a path's edges to the frame's clip; GlassPartialRedrawSeamTests holds
// that one headless). A composer-like piece (the glass, two antialiased capsule rings, a line of text and three stroked icons, at a fractional x) lies
// over a backdrop with a small block under it; the block changes at several places, so the region the change reaches ends in the piece's caps, across
// its text and through its icons, and the composed screen over the piece is compared with the screen after a whole redraw: 0 levels. Then the piece
// gains an inset shade (a blurred path): Skia's GPU blur mask follows the clip by at most a level (measured offscreen on ANGLE, 1 level at 125 %),
// which this records and bounds at 1. Needs a free interactive desktop; the runner takes native.lock around the run.
[Collection("WindowManagerNative")]
public sealed class GlassPartialRedrawSeamNativeTests(WindowManagerNativeSession native)
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

    private static Panel Piece()
    {
        var glass = new Border { Background = Brushes.Transparent };
        SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.RoundedRectangle(Radius, RoundedCornerStyle.Continuous, circularInBand: true)));
        var text = new TextBlock
        {
            Text = "Ask anything, the glass keeps its edges", Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(44.3, 0, 0, 0),
        };
        return new Panel
        {
            Width = Width, Height = Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Left, Top, 0, 0), UseLayoutRounding = false,
            Children = { glass, Ring(1, Color.FromArgb(120, 255, 255, 255)), Ring(0.5, Color.FromArgb(160, 230, 236, 255)), Icon(14.6), text, Icon(352.2), Icon(392.7) },
        };
    }

    private static int Largest(ScreenGrab a, ScreenGrab b, out (int X, int Y) at)
    {
        var most = 0;
        at = default;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                var (p, q) = (a.At(x, y), b.At(x, y));
                var d = Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B)));
                if (d > most) { most = d; at = (x, y); }
            }
        return most;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glassseams-1")] // traceability alias of ED-NEW-glass-seams-1
    public void On_a_real_window_a_partial_redraw_leaves_the_drawings_on_a_glass_piece_as_a_whole_redraw_draws_them() => native.Run(subject =>
    {
        var (window, root, block, piece) = native.OnUi(() =>
        {
            subject.Main.Hide();
            subject.Settings.Hide();
            var block = new Border
            {
                Width = 6, Height = 6, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, Top + 20, 0, 0),
            };
            var piece = Piece();
            var root = new Panel { Background = new SolidColorBrush(Color.FromRgb(58, 64, 82)), Children = { new Stripes(), block, piece } };
            var window = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                Width = 640, Height = 150, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Content = root,
            };
            window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
            native.Register(window);
            window.Show();
            return (window, root, block, piece);
        });
        Thread.Sleep(1500); // the scene settles: layout, the material's first frames (its GPU programs), the window's own updates
        ScreenGrab Grab() => native.OnUi(() =>
        {
            var scale = window.RenderScaling;
            var origin = window.PointToScreen(new Point(Left - 4, Top - 4));
            return ScreenGrab.Of(origin.X, origin.Y, (int)Math.Ceiling((Width + 8) * scale), (int)Math.Ceiling((Height + 8) * scale));
        });
        void Whole()
        {
            native.OnUi(() => { foreach (var visual in root.GetSelfAndVisualDescendants()) visual.InvalidateVisual(); });
            Thread.Sleep(400);
        }
        var cases = new List<string>();
        int Worst()
        {
            var worst = 0;
            foreach (var x in new[] { 12.3, 47.1, 96.4, 170.8, 251.5, 333.2, 371.6, 405.9, 455.2, 520.7 })
            {
                native.OnUi(() => block.Margin = new Thickness(x, Top + 20, 0, 0));
                Thread.Sleep(300);
                Whole();
                native.OnUi(() => block.Background = block.Background == Brushes.Black ? Brushes.White : Brushes.Black);
                Thread.Sleep(400);
                var partial = Grab();
                Whole();
                var whole = Grab();
                var apart = Largest(partial, whole, out var at);
                cases.Add($"block at {x}: {apart} levels at {at}");
                worst = Math.Max(worst, apart);
            }
            return worst;
        }
        var drawings = Worst();
        native.OnUi(() => piece.Children.Insert(1, new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = -2, Blur = 4, Color = Color.FromArgb(90, 0, 0, 0) }),
        }));
        Thread.Sleep(400);
        var shaded = Worst();
        Assert.True(drawings == 0, $"the rings, text and icons on the piece are {drawings} levels from a whole redraw: " + string.Join("; ", cases));
        Assert.True(shaded <= 1, $"with the inset shade the piece is {shaded} levels from a whole redraw: " + string.Join("; ", cases));
        GC.KeepAlive(window);
    }, factType: typeof(GlassPartialRedrawSeamNativeTests));
}
