// ED-356 (OD-0107 and its amendment): renders the port's glass over backdrops taken from the Mac video frames, for the side-by-side
// calibration against the same frames (E:\Droppy\work\scratch\glass-g1\prep.py makes the inputs, stack.py the comparisons). Opt-in: it does
// nothing unless MARROW_GLASS_CALIBRATION names the folder of the inputs; the gate never sets it. The layouts are the Mac frames' own,
// measured on them at 1 px = 1 pt: the composer pill 820 x 46 (r 22, circular in its band; ComposerView.swift:1298-1304), the title
// capsules 32 x 32 circles tinted with the theme surface at 0.3 (Chrome.swift:180-182), the head card 400 x 267 (r 22) over its black 0.3 scrim
// and under the theme surface at 0.22 (HydraPanel.swift:99-121). The theme is Monokai, as recorded.
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
using Marrow.UI.Theme;
using Xunit;
using SwiftChrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMacCalibration(HeadlessSession headless)
{
    private static Border Placed(double x, double y, double width, double height) => new()
    {
        Width = width, Height = height, Margin = new Thickness(x, y, 0, 0), Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
    };

    private static void Render(string folder, string tag, Func<Control[]> pieces)
    {
        using var source = new Bitmap(Path.Combine(folder, tag + "_backdrop.png"));
        var panel = new Panel { Children = { new Image { Source = source, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } } };
        foreach (var piece in pieces()) panel.Children.Add(piece);
        var window = new Window { Width = source.PixelSize.Width, Height = source.PixelSize.Height, RequestedThemeVariant = ThemeVariant.Dark, Background = Brushes.Black, Content = panel };
        window.Show();
        try
        {
            for (var i = 0; i < 4; i++) GlassScene.Drain();
            using var frame = window.CaptureRenderedFrame()!;
            frame.Save(Path.Combine(folder, tag + "_port.png"), new PngBitmapEncoderOptions());
        }
        finally { window.Close(); }
    }

    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void Renders_the_port_glass_over_the_Mac_frames_backdrops() => headless.OnUiThread(() =>
    {
        if (Environment.GetEnvironmentVariable("MARROW_GLASS_CALIBRATION") is not { Length: > 0 } folder) return;
        var previous = ThemeManager.Current;
        try
        {
            ThemeManager.Current = AppTheme.Monokai;
            foreach (var tag in new[] { "pill01", "pill29", "pill30" })
                Render(folder, tag, () =>
                {
                    var pill = Placed(20, 20, 820, 46);
                    pill.CornerRadius = new CornerRadius(22);
                    SwiftGlass.SetEffect(pill, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)));
                    return [pill];
                });
            Render(folder, "caps02", () =>
            {
                Control Circle(double x)
                {
                    var circle = Placed(x, 20, 32, 32);
                    circle.CornerRadius = new CornerRadius(16);
                    SwiftGlass.SetEffect(circle, GlassEffect.Regular(GlassShape.Circle).Tint(SwiftChrome.GlassTint, 0.3));
                    return circle;
                }
                return [Circle(20), Circle(104)];
            });
            // ED-358: the head card over frames 18 and 31's backdrops (frame 01 there, levelled on a ring past the shadow's reach; the card at
            // 60, 60 in a 520 x 347 crop, so the shadow's reach above and beside is in it), and the
            // card as g1 drew it (the scrim's BoxShadow at the colour's full alpha, outside the box only) over frame 18's, for the before and after.
            foreach (var (card, before) in new[] { ("card18", false), ("card31", false), ("card18-before", true), ("card18g3", false) })
            {
                Render(folder, card, () =>
                {
                    var scrim = Placed(60, 60, 400, 267);
                    scrim.Background = new SolidColorBrush(Color.FromArgb(77, 0, 0, 0));
                    scrim.CornerRadius = new CornerRadius(22);
                    var glass = Placed(60, 60, 400, 267);
                    glass.CornerRadius = new CornerRadius(22);
                    SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
                    glass.Background = SwiftChrome.GlassTint.Opacity(0.22).Brush(glass);
                    if (before)
                    {
                        scrim.BoxShadow = SwiftShadow.BoxShadows(Color.FromArgb(255, 0, 0, 0), radius: 28, y: 10);
                        return [scrim, glass];
                    }
                    // The scrim's shadow is its own coverage at the fill's alpha, under it (SwiftShadow.Caster, as HydraPanel draws it).
                    return [new SwiftShadow.Caster(scrim, Color.FromArgb(255, 0, 0, 0), radius: 28, y: 10), scrim, glass];
                });
            }
        }
        finally { ThemeManager.Current = previous; }
    });
}
