// PORT-ONLY: Tactus, an app's own icon wherever the pill, its card, the chat's strip and Settings name the app
// (ED-NEW-pcctl-46; DESIGN.md 4.5 "one row per app, with icon", 10.4 the approval body's "App icon, name and path", 10.7
// the strip's "app chips (icon, state dot, x)", 4.7 Settings "Apps: list with icon"; canvas pc-control-ui `.ai`). The App
// gives the source (an app's key to its icon, from the shell); with none, or for an app whose icon cannot be read, the
// canvas's lettered tile stands in: the app's first letter on a colour picked from its name, the corners 24 % of the side.
// The source is ambient, so a test can give its own for its own flow.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Port;

namespace Marrow.UI.Port.PcControl;

/// <summary>An app's icon by its key ("exe:&lt;path&gt;" or "aumid:&lt;id&gt;"); null when there is none to show.</summary>
public interface IPcAppIconSource
{
    IImage? Icon(string key);
}

public static class PcAppIcons
{
    private static readonly Color[] Tiles =
    [
        Color.FromRgb(0x1D, 0x6F, 0x42), Color.FromRgb(0x0F, 0x6C, 0xBD), Color.FromRgb(0x5B, 0x5F, 0x6A),
        Color.FromRgb(0x8A, 0x4B, 0x2E), Color.FromRgb(0x5C, 0x3D, 0x8F), Color.FromRgb(0x2F, 0x6B, 0x7A),
    ];

    /// <summary>The app's icon source (the App's shell icons; none by default).</summary>
    public static Ambient<IPcAppIconSource> Source { get; } = new(new NoIcons());

    /// <summary>The key of an app known by its exe path (PcAppIdentity.Key for an app without a package id).</summary>
    public static string KeyOf(string exePath) => "exe:" + exePath.ToLowerInvariant();

    /// <summary>
    /// The app's icon at <paramref name="size"/> DIP when its key gives one, else its lettered tile (<paramref name="name"/>'s
    /// first letter). Decorative: the name is always written next to it.
    /// </summary>
    public static Control Tile(string name, string? key, double size)
    {
        Control tile = key is { Length: > 0 } && Source.Current.Icon(key) is { } icon
            ? new Image { Source = icon, Width = size, Height = size, Stretch = Stretch.Uniform }.With(i => RenderOptions.SetBitmapInterpolationMode(i, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality))
            : Lettered(name, size);
        tile.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(tile, AccessibilityView.Raw);
        return tile;
    }

    /// <summary>The tile's colour for an app: a stable pick from the sum of its name's lower-cased characters.</summary>
    public static IBrush Brush(string name) => new ImmutableSolidColorBrush(Tiles[name.ToLowerInvariant().Sum(c => c) % Tiles.Length]);

    private static Border Lettered(string name, double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.24),
        BoxShadow = CardLook.TileRim,
        Background = Brush(name),
        Child = new TextBlock
        {
            Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?",
            FontFamily = CardLook.Text,
            FontSize = size * 0.5,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private sealed class NoIcons : IPcAppIconSource
    {
        public IImage? Icon(string key) => null;
    }
}
