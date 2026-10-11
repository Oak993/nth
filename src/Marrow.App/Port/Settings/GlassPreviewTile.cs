// PORT-ONLY, owner-requested (OD-0107, ED-NEW-glasslook-2): the Glass section's live preview tile. A small colourful scene (bands of colour, a few
// lines and letters, so the bezel's bend and fringe can be seen) with a composer pill and a glass button over it. Both are real glass pieces of the app's
// material, so they show the user's Glass settings exactly as every surface does, live, while a slider moves.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Settings;

internal sealed class GlassPreviewTile : Decorator
{
    internal const double TileHeight = 132;

    /// <summary>The composer pill of the tile (the composer's look under the user's tuning).</summary>
    internal Border Pill { get; }

    /// <summary>The round glass button of the tile (a button's look under the user's tuning).</summary>
    internal Border Button { get; }

    internal GlassPreviewTile()
    {
        Height = TileHeight;
        Margin = new Thickness(16, 12, 16, 12);
        var scene = new Panel { ClipToBounds = true };
        // the bands: the warm and cool ends of a wallpaper, so the fringe's red and blue show at the bezel
        scene.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xFF, 0x7A, 0x59), 0), new GradientStop(Color.FromRgb(0xF5, 0xC2, 0x42), 0.3),
                    new GradientStop(Color.FromRgb(0x3E, 0xC6, 0xA8), 0.62), new GradientStop(Color.FromRgb(0x4F, 0x6B, 0xFF), 1),
                },
            },
        });
        // the lines: straight edges the bezel visibly bends
        for (var i = 0; i < 9; i++)
            scene.Children.Add(new Border
            {
                Width = 3, Background = new SolidColorBrush(Color.FromArgb(i % 2 == 0 ? (byte)230 : (byte)140, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(24 + i * 46, 0, 0, 0),
            });
        scene.Children.Add(new Border { Height = 3, Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 40)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 38, 0, 0) });
        scene.Children.Add(new Border { Height = 3, Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 40)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 92, 0, 0) });
        scene.Children.Add(new TextBlock
        {
            Text = "Aa Bb Cc Dd Ee Ff Gg Hh Ii Jj", FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 52, 0, 0),
        });

        var label = new TextBlock { Text = "Ask anything", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        SwiftFont.System(14, SwiftFont.Weight.Regular).Apply(label);
        label.Foreground = Chrome.PrimaryText.Brush(label);
        Pill = new Border
        {
            Width = 260, Height = 46, CornerRadius = new CornerRadius(23), Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 56, 0),
            Child = label,
        };
        SwiftGlass.SetEffect(Pill, GlassEffect.Regular(GlassShape.RoundedRectangle(23, RoundedCornerStyle.Continuous, circularInBand: true)).OnSurface(GlassSurface.Composer));
        Button = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(276, 0, 0, 0),
        };
        SwiftGlass.SetEffect(Button, GlassEffect.Regular(GlassShape.Circle).Interactive().OnSurface(GlassSurface.Button));
        scene.Children.Add(Pill);
        scene.Children.Add(Button);
        Child = scene;
        Avalonia.Automation.AutomationProperties.SetName(this, "Glass preview");
    }
}
