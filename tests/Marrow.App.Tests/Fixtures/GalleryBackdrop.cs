using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// The known backdrop the gallery's glass windows lie over: a soft, photo-like gradient in mid tones (a blue-grey sky into a warm
// ground, a few blurred lights), so the Acrylic reads as material and not as a flat fill, in the dark and the light theme alike.
internal static class GalleryBackdrop
{
    /// <summary>A topmost window with no decorations over the working area, shown without taking the foreground.</summary>
    internal static AvaloniaWindow Cover(PixelRect work, double scale)
    {
        var window = new AvaloniaWindow
        {
            WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = work.Position,
            Width = work.Width / scale, Height = work.Height / scale, Content = Picture(),
        };
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
        return window;
    }

    private static Control Picture()
    {
        var sky = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.35, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x6F, 0x8F, 0xB4), 0), new GradientStop(Color.FromRgb(0x8E, 0x9C, 0xAE), 0.45),
                new GradientStop(Color.FromRgb(0xB0, 0x9A, 0x86), 0.78), new GradientStop(Color.FromRgb(0x8A, 0x6E, 0x5E), 1),
            },
        };
        var grid = new Grid { Background = sky };
        grid.Children.Add(Light(0.18, 0.22, 900, Color.FromRgb(0xF2, 0xD5, 0xA0)));
        grid.Children.Add(Light(0.78, 0.30, 760, Color.FromRgb(0x9F, 0xC4, 0xE8)));
        grid.Children.Add(Light(0.52, 0.80, 1000, Color.FromRgb(0xD8, 0x8C, 0x70)));
        grid.Children.Add(Light(0.92, 0.88, 640, Color.FromRgb(0x5E, 0x7A, 0x6A)));
        return grid;
    }

    // A soft blob: a radial gradient that falls to nothing at its edge, placed by fractions of the window.
    private static Control Light(double x, double y, double size, Color color)
    {
        var fade = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(0xB0, color.R, color.G, color.B), 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1) },
        };
        var canvas = new Canvas { IsHitTestVisible = false };
        var blob = new Ellipse { Width = size, Height = size, Fill = fade };
        canvas.Children.Add(blob);
        var host = new Grid { IsHitTestVisible = false };
        host.SizeChanged += (_, e) =>
        {
            Canvas.SetLeft(blob, e.NewSize.Width * x - size / 2);
            Canvas.SetTop(blob, e.NewSize.Height * y - size / 2);
        };
        host.Children.Add(canvas);
        return host;
    }
}
