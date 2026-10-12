using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Marrow.App.Views.Composer;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Fixtures;

internal static class ComposerCentreProbe
{
    internal static void Prepare(Avalonia.Controls.Window window, double scale, bool dark)
    {
        window.Height = 300;
        window.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(48, 56, 70));
        window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        ((Border)window.Content!).Margin = new Thickness(0, 0, 0, 64);
        window.SetRenderScaling(scale);
    }

    internal static double OutlineCentre(ComposerView view, Avalonia.Controls.Window window)
    {
        var pixels = DictationProbe.Frame(window);
        var rect = Frame(view, window);
        var scale = window.RenderScaling;
        var x = (int)(rect.Center.X * scale);
        bool Painted(int y) => RowIsOutline(pixels, rect, scale, x, y, ((Avalonia.Media.SolidColorBrush)window.Background!).Color);
        var top = Enumerable.Range((int)Math.Floor(rect.Top * scale) - 3, 8).First(Painted);
        var bottom = Enumerable.Range((int)Math.Ceiling(rect.Bottom * scale) - 4, 8).Last(Painted);
        return (top + bottom + 1) / 2.0;
    }
    internal static bool RowIsOutline((int Stride, byte[] Bytes, PixelSize Size) pixels, Rect rect, double scale, int x, int y, Avalonia.Media.Color backdrop)
    {
        // ED-NEW-glass-c10-3 / OD-0147; lead decision 2026-10-12 20:10:
        // compare against the prepared uniform backdrop plus the design's shadow, not a shadowed reference row.
        // Independent replay of glass-ui/index.html FS_GLASS, composer [6,18,.20,.14]; do not read port shader constants.
        var logicalY = (y + .5) / scale;
        double Distance(double drop) => Math.Max(rect.Top - (logicalY - drop), logicalY - drop - rect.Bottom);
        static double Shadow(double distance, double sigma, double alpha) => alpha * .5 * (1 - Math.Tanh(Math.Clamp(.85 * distance / sigma, -8, 8)));
        var shadow = Shadow(Distance(6), 18, .20);
        shadow = 1 - (1 - shadow) * (1 - Shadow(Distance(1), 2.5, .14));
        // ED-NEW-glass-c10-5: pure shadow baseline; shape coverage is part of the measured foreground.
        int Expected(int channel) => (int)Math.Round((channel == 0 ? backdrop.R : channel == 1 ? backdrop.G : backdrop.B) * (1 - shadow));
        return Enumerable.Range(0, 3).Max(c => Math.Abs(pixels.Bytes[y * pixels.Stride + x * 4 + c] - Expected(c))) >= 2;
    }

    internal static double InkCentre(ComposerView view, Avalonia.Controls.Window window)
    {
        return InkCentre(DictationProbe.Box(view), window);
    }

    internal static double InkCentre(Control box, Avalonia.Controls.Window window)
    {
        void Tick() { for (var k = 0; k < 3; k++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
        Tick();
        var with = DictationProbe.Frame(window);
        var opacity = box.Opacity;
        box.Opacity = 0;
        Tick();
        var without = DictationProbe.Frame(window);
        box.Opacity = opacity;
        Tick();
        var region = Frame(box, window);
        var scale = window.RenderScaling;
        var first = int.MaxValue;
        var last = -1;
        for (var y = Math.Max(0, (int)Math.Floor(region.Top * scale)); y < Math.Min(with.Size.Height, Math.Ceiling(region.Bottom * scale)); y++)
        for (var x = Math.Max(0, (int)Math.Floor(region.Left * scale)); x < Math.Min(with.Size.Width, Math.Ceiling(region.Right * scale)); x++)
        {
            var at = y * with.Stride + x * 4;
            if (Enumerable.Range(0, 3).Max(c => Math.Abs(with.Bytes[at + c] - without.Bytes[at + c])) < 10) continue;
            first = Math.Min(first, y); last = Math.Max(last, y);
        }
        Xunit.Assert.True(last >= first, "no painted text");
        return (first + last + 1) / 2.0;
    }

    internal static void Save(ComposerView view, Avalonia.Controls.Window window, string name, double centre, IDictionary<string, double> controls)
    {
        if (Environment.GetEnvironmentVariable("T46_CENTRE_DUMP") is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        var rect = Frame(view, window);
        File.WriteAllText(Path.Combine(folder, name + ".json"), System.Text.Json.JsonSerializer.Serialize(new { scale = window.RenderScaling, composer = rect, centre, ink = InkCentre(view, window), controls }));
    }
}
