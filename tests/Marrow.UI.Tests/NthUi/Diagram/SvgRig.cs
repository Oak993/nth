using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.NthUi.Diagram;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi.Diagram;

/// <summary>A sanitised picture in a headless window, drawn for real, so a test reads the pixels the reader would see.</summary>
internal sealed class SvgRig : IDisposable
{
    public const double Pad = 16;

    public SvgRig(string markup, double? width = null, bool dark = false, bool adapt = false, NthSvgLimits? limits = null, double window = 260, double height = 200)
    {
        var parsed = NthSvgSanitizer.Parse(markup, limits);
        Assert.True(parsed.Ok, parsed.Error);
        Canvas = new NthSvgCanvas
        {
            Document = parsed.Document,
            Adapt = adapt,
            RequestedWidth = width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Thread = dark ? NthUiRig.DarkThread : NthUiRig.LightThread;
        Window = new Window
        {
            Width = window,
            Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new Border { Padding = new Thickness(Pad), Background = new SolidColorBrush(Thread), Child = Canvas },
        };
        Window.Show();
        Pump();
    }

    public NthSvgCanvas Canvas { get; }

    public Window Window { get; }

    public Color Thread { get; }

    public void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    public RenderedPixels Capture()
    {
        Canvas.InvalidateVisual();
        Pump();
        return RenderedPixels.Capture(Window);
    }

    /// <summary>The colour at a point of the picture (its own pixels, from the canvas's top left).</summary>
    public static Color At(RenderedPixels pixels, double x, double y)
    {
        var (r, g, b) = pixels.At((int)Math.Round(x + Pad), (int)Math.Round(y + Pad));
        return Color.FromRgb((byte)r, (byte)g, (byte)b);
    }

    public void Dispose() => Window.Close();
}
