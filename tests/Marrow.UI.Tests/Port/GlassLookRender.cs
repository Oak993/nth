// ED-NEW-glasslook-1 (OD-0107 and its amendment): renders the port's glass over the backdrops of the 2026-10-11 glass-look comparison, at the same
// places and sizes as the two MIT repos' own demos were rendered over them in headless Chrome (rizzytoday/liquid-glass, dashersw/liquid-glass-js;
// E:\Droppy\work\scratch\glasslook-cl-1011: make_backdrops.py, web\, compare.py). Opt-in: it does nothing unless MARROW_GLASSLOOK_DIR names a
// folder holding layout.json and the backdrops it lists (wallpaper.png, darkui.png and the measuring greys); the gate never sets it. Writes app_<backdrop>_<theme>[_text].png into that folder.
// MARROW_GLASSLOOK_LAYOUT names another layout file there (the composer's), and MARROW_GLASSLOOK_LOOK draws every piece with a named look (release or
// regular, clear or premium, tinted, liquid: ED-NEW-glasslook-1 and -3), written as app-<look>_<backdrop>_<theme>[_text].png.
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassLookRender(HeadlessSession headless)
{
    private static GlassShape ShapeOf((string Name, double X, double Y, double W, double H, double R, string Kind) p) => p.Kind switch
    {
        "capsule" => GlassShape.Capsule(RoundedCornerStyle.Continuous),
        "circle" => GlassShape.Circle,
        "composer" => GlassShape.RoundedRectangle(p.R, RoundedCornerStyle.Continuous, circularInBand: true),
        _ => GlassShape.RoundedRectangle(p.R, RoundedCornerStyle.Continuous),
    };

    private static void Render(string folder, string backdrop, (string Name, double X, double Y, double W, double H, double R, string Kind)[] pieces, bool light, bool text, string? named)
    {
        using var source = new Bitmap(Path.Combine(folder, backdrop + ".png"));
        var panel = new Panel { Children = { new Image { Source = source, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } } };
        foreach (var p in pieces)
        {
            var glass = new Border
            {
                Width = p.W, Height = p.H, Margin = new Thickness(p.X, p.Y, 0, 0), Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(p.Kind is "rounded" or "composer" ? p.R : Math.Min(p.W, p.H) / 2),
            };
            var look = named switch
            {
                "release" or "regular" => GlassLook.Release, "liquid" => GlassLook.Liquid, "premium" or "clear" => GlassLook.Premium,
                "tinted" => GlassLook.TintedCandidate, _ => null,
            };
            SwiftGlass.SetEffect(glass, look is null ? GlassEffect.Regular(ShapeOf(p)) : GlassEffect.Regular(ShapeOf(p)).WithLook(look));
            panel.Children.Add(glass);
            if (!text) continue;
            // The app's label colour on glass (SwiftColor.LabelColor: black or white at 0.85), body size.
            panel.Children.Add(new TextBlock
            {
                Text = p.Name == "panel" ? "Settings card\nBody text on glass" : p.Name == "circle" ? "Aa" : "Ask anything",
                FontSize = 15, Foreground = new SolidColorBrush(light ? Color.FromArgb(217, 0, 0, 0) : Color.FromArgb(217, 255, 255, 255)),
                Width = p.W, TextAlignment = p.Kind == "composer" ? TextAlignment.Left : TextAlignment.Center, Padding = new Thickness(p.Kind == "composer" ? 20 : 0, 0, 0, 0), Margin = new Thickness(p.X, p.Y + p.H / 2 - (p.Name == "panel" ? 20 : 10), 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            });
        }
        var window = new Window
        {
            Width = source.PixelSize.Width, Height = source.PixelSize.Height, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
            Background = Brushes.Black, Content = panel,
        };
        window.Show();
        try
        {
            for (var i = 0; i < 4; i++) GlassScene.Drain();
            using var frame = window.CaptureRenderedFrame()!;
            frame.Save(Path.Combine(folder, $"app{(named is null ? "" : "-" + named)}_{backdrop}_{(light ? "light" : "dark")}{(text ? "_text" : "")}.png"), new PngBitmapEncoderOptions());
        }
        finally { window.Close(); }
    }

    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void Renders_the_port_glass_over_the_glass_look_backdrops() => headless.OnUiThread(() =>
    {
        if (Environment.GetEnvironmentVariable("MARROW_GLASSLOOK_DIR") is not { Length: > 0 } folder) return;
        var layoutFile = Environment.GetEnvironmentVariable("MARROW_GLASSLOOK_LAYOUT") is { Length: > 0 } other ? other : "layout.json";
        var named = Environment.GetEnvironmentVariable("MARROW_GLASSLOOK_LOOK") is { Length: > 0 } n ? n : null;
        using var layout = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, layoutFile)));
        var pieces = layout.RootElement.GetProperty("pieces").EnumerateArray().Select(e => (
            e.GetProperty("name").GetString()!, e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble(), e.GetProperty("w").GetDouble(),
            e.GetProperty("h").GetDouble(), e.GetProperty("r").GetDouble(), e.GetProperty("kind").GetString()!)).ToArray();
        var backdrops = layout.RootElement.TryGetProperty("backdrops", out var listed) ? listed.EnumerateArray().Select(e => e.GetString()!).ToArray() : ["wallpaper", "darkui"];
        foreach (var backdrop in backdrops)
            foreach (var light in new[] { false, true })
                foreach (var text in backdrop is "wallpaper" or "darkui" ? new[] { false, true } : [false])
                    Render(folder, backdrop, pieces, light, text, named);
    });
}
