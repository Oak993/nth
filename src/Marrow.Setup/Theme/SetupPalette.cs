using Avalonia.Media;
using Marrow.Setup.Platform;

namespace Marrow.Setup.Theme;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3; the canvas's tokens; unit U1, ED-NEW-inst-u1-1). The window's colours for one look:
/// the Mica tint (74 % at the top to 82 % at the bottom), the solid tint used without Mica (the bottom tint over Windows'
/// own base, as the headless reference renders use), the inset rim and top highlight, the three text inks, the caption
/// buttons, the accents, and how thick the window edge is (2 px in a contrast theme, which uses Windows' colours and no
/// glass or shadows).
/// </summary>
public sealed record SetupPalette(
    string Name,
    bool Glass,
    Color TintTop,
    Color TintBottom,
    Color WindowsBase,
    Color Edge,
    Color TopLight,
    double EdgeThickness,
    Color Ink1,
    Color Ink2,
    Color Ink3,
    Color CaptionHover,
    Color CaptionHoverInk,
    Color CloseHover,
    Color CloseHoverInk,
    Color CaptionDisabledInk,
    double CaptionDisabledOpacity,
    Color Warn,
    Color Bad,
    Color Good,
    Color Link,
    Color Focus,
    Color Highlight,
    Color HighlightInk)
{
    /// <summary>The window without Mica: the bottom tint laid over Windows' own base colour, opaque.</summary>
    public Color Solid => Over(TintBottom, WindowsBase);

    /// <summary>The canvas's dark tokens (:root).</summary>
    public static SetupPalette Dark { get; } = new(
        "dark", true,
        Rgba(30, 32, 38, .74), Rgba(22, 24, 29, .82), Hex(0x202020), Rgba(255, 255, 255, .11), Rgba(255, 255, 255, .13), 1,
        Hex(0xEEF0F4), Hex(0xA9B0BD), Hex(0x8A919F),
        Rgba(255, 255, 255, .07), Hex(0xEEF0F4), Hex(0xC42B1C), Colors.White, Hex(0xEEF0F4), .35,
        Hex(0xF0B35A), Hex(0xFF9D85), Hex(0x74D39D), Hex(0xA9B0BD), Hex(0x8FB2FF), Hex(0xF6F7F9), Hex(0x14161C));

    /// <summary>The canvas's light tokens (prefers-color-scheme: light).</summary>
    public static SetupPalette Light { get; } = new(
        "light", true,
        Rgba(246, 247, 250, .76), Rgba(238, 240, 244, .84), Hex(0xF3F3F3), Rgba(20, 28, 48, .12), Rgba(255, 255, 255, .9), 1,
        Hex(0x1D2027), Hex(0x525968), Hex(0x5F6676),
        Rgba(0, 0, 0, .06), Hex(0x1D2027), Hex(0xC42B1C), Colors.White, Hex(0x1D2027), .35,
        Hex(0x9A5B06), Hex(0xA3361F), Hex(0x1F6E45), Hex(0x525968), Hex(0x2F62D0), Hex(0x3A404D), Colors.White);

    /// <summary>
    /// A Windows contrast theme: its own colours (the canvas's Contrast › High is Windows 10's High Contrast Black). Window
    /// background, window text for every ink and the 2 px edge, the highlight for hover, the hyperlink colour for links,
    /// warnings and focus, and the disabled-text colour for a paused Close.
    /// </summary>
    public static SetupPalette HighContrast(SystemColorSet system) => new(
        "contrast", false,
        system.Window, system.Window, system.Window, system.WindowText, Colors.Transparent, 2,
        system.WindowText, system.WindowText, system.WindowText,
        system.Highlight, system.HighlightText, system.Highlight, system.HighlightText, system.GrayText, 1,
        system.HotLight, system.HotLight, system.WindowText, system.HotLight, system.HotLight, system.Highlight, system.HighlightText);

    /// <summary>The palette for what Windows is set to now.</summary>
    public static SetupPalette For(SystemLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        return look.HighContrast ? HighContrast(look.Colors) : look.Dark ? Dark : Light;
    }

    public static Color Rgba(byte r, byte g, byte b, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), r, g, b);

    public static Color Hex(uint rgb) => Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    /// <summary>Source-over: <paramref name="top"/> laid on an opaque <paramref name="bottom"/>.</summary>
    public static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        return Color.FromRgb(Mix(top.R, bottom.R, a), Mix(top.G, bottom.G, a), Mix(top.B, bottom.B, a));
    }

    /// <summary>A colour between <paramref name="from"/> (0) and <paramref name="to"/> (1), channel by channel.</summary>
    public static Color Lerp(Color from, Color to, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(Mix(to.A, from.A, t), Mix(to.R, from.R, t), Mix(to.G, from.G, t), Mix(to.B, from.B, t));
    }

    private static byte Mix(byte top, byte bottom, double alpha) => (byte)Math.Round(top * alpha + bottom * (1 - alpha));
}
