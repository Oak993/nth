using System.Collections.Concurrent;
using Avalonia.Media;
using Marrow.UI.Theme;

namespace Marrow.UI.Port;

// PORT-ONLY (OD-0088, OD-0081, OD-0075; ED-250): the accent the user picks in Settings > Appearance. The source has no
// accent preference (Chrome.swift:80-87: the theme's accent, or the system control accent); the owner made it one
// (OD-0075) and locked its palette and picker (OD-0088: E:/Droppy/work/design/accent-palette-locked.json, the 22
// colours; E:/Droppy/work/design/accent-build/SPEC.md sections 3 and 4).
//   - The palette: ten Classic accents (the Windows accent, the default, then nine) and twelve Signature ones, each with
//     the hex drawn in Dark and in Light (already lifted by the palette; never lifted again).
//   - The lift (SPEC 3.2, the board's liftCard): a colour under 3.1:1 on the Settings card (#4D4C4B / #D1CFCF) or under
//     3:1 on the reference panel (#1E1E1E / #FFFFFF) has its OKLCH lightness moved in 0.0005 steps (up in Dark, down in
//     Light) at fixed hue, chroma kept unless sRGB cannot hold it, until the first 8-bit colour that passes both. It is
//     applied to the Windows accent wherever it is read (SwiftColor, amending ED-238 (3)) and to a named theme's own
//     accent where the picker draws it as the first swatch.
//   - The choice: one persisted id (AppSettings "accent"), "windows" by default and for anything unknown; "windows" is
//     the default accent, which under a named theme is that theme's own (SPEC 4.1, 4.2).

/// <summary>One of the 22 accents (SPEC 3.1).</summary>
/// <param name="Id">The persisted id (the palette file's <c>win</c> is persisted as <c>windows</c>).</param>
/// <param name="Name">The name the UI shows (names only, OD-0088).</param>
/// <param name="Dark">The hex drawn in Dark (for the Windows accent: the board's preview of #0067C0).</param>
/// <param name="Light">The hex drawn in Light (for the Windows accent: the board's preview).</param>
/// <param name="IsSignature">On the Signature line (behind More), not the Classic one.</param>
public sealed record AccentSwatch(string Id, string Name, string Dark, string Light, bool IsSignature)
{
    /// <summary>The hex for an appearance.</summary>
    public string Hex(bool dark) => dark ? Dark : Light;
}

/// <summary>PORT-ONLY (OD-0088): the 22 accents, the lift and the colour rules the picker uses.</summary>
public static class AccentPalette
{
    /// <summary>The default: the Windows accent under System, Light and Dark, the theme's own under a named theme.</summary>
    public const string WindowsId = "windows";

    /// <summary>The 22, Classic then Signature, in the palette file's order.</summary>
    public static IReadOnlyList<AccentSwatch> All { get; } =
    [
        new(WindowsId, "Windows accent", "#4B9FFC", "#0067C0", false),
        new("blue", "Blue", "#4F9DFF", "#006DE6", false),
        new("purple", "Purple", "#D47CD5", "#953D96", false),
        new("pink", "Pink", "#FF63A9", "#D2277F", false),
        new("red", "Red", "#FF6C6B", "#D72E37", false),
        new("orange", "Orange", "#F7821B", "#B35900", false),
        new("yellow", "Yellow", "#FFC600", "#9A6800", false),
        new("green", "Green", "#62BA46", "#2E8300", false),
        new("teal", "Teal", "#40C8E0", "#007D91", false),
        new("graphite", "Graphite", "#9C9C9C", "#727272", false),
        new("ocean", "Ocean", "#00A9D4", "#2A6686", true),
        new("denim", "Denim", "#979AB6", "#3C447E", true),
        new("indigo", "Indigo", "#948FFF", "#5341BD", true),
        new("lilac", "Lilac", "#AD92BD", "#82679B", true),
        new("orchid", "Orchid", "#E298C6", "#733E67", true),
        new("rose", "Rose", "#D9969B", "#A85966", true),
        new("coral", "Coral", "#FF9B7E", "#9C5345", true),
        new("bronze", "Bronze", "#C89152", "#7F4A00", true),
        new("sage", "Sage", "#86A481", "#647852", true),
        new("alpine", "Alpine", "#53AC8F", "#005136", true),
        new("mint", "Mint", "#59D6B9", "#007C6D", true),
        new("slate", "Slate", "#AABCD4", "#5C687D", true),
    ];

    /// <summary>How many accents the Classic line holds.</summary>
    public const int ClassicCount = 10;

    /// <summary>The index of <paramref name="id"/> in <see cref="All"/>, or -1.</summary>
    public static int IndexOf(string? id)
    {
        for (var i = 0; i < All.Count; i++) if (All[i].Id == id) return i;
        return -1;
    }

    /// <summary>A stored id as the app reads it: a known id, else <see cref="WindowsId"/> (SPEC 4.1).</summary>
    public static string Normalize(string? id) => IndexOf(id) < 0 ? WindowsId : id!;

    private static readonly ConcurrentDictionary<string, SwiftColor> Picks = new();

    /// <summary>
    /// The colour a pick paints with, or null for <see cref="WindowsId"/> (the default accent: the theme's, else the
    /// system's). A pick is the palette hex of the appearance it is drawn in, as given (no lift), one instance per id.
    /// </summary>
    public static SwiftColor? Pick(string? id)
    {
        var normal = Normalize(id);
        if (normal == WindowsId) return null;
        return Picks.GetOrAdd(normal, key =>
        {
            var swatch = All[IndexOf(key)];
            return SwiftColor.Dynamic(light: SwiftColor.Hex(swatch.Light), dark: SwiftColor.Hex(swatch.Dark));
        });
    }

    /// <summary>
    /// SPEC 4.2: the effective accent for a theme and a pick: the pick's palette colour; for <see cref="WindowsId"/> the
    /// theme's own accent, raw (AppTheme.swift:193), else the system accent (lifted when it is resolved, SwiftColor).
    /// </summary>
    public static SwiftColor Effective(string? id, AppTheme theme) =>
        Pick(id) ?? theme.Spec().Accent ?? SwiftColor.ControlAccentColor;

    // MARK: Contrast

    /// <summary>The Settings card and the reference panel the palette is legible on (palette file "surfaces").</summary>
    public static readonly Color DarkCard = Color.FromRgb(0x4D, 0x4C, 0x4B), LightCard = Color.FromRgb(0xD1, 0xCF, 0xCF);

    /// <inheritdoc cref="DarkCard"/>
    public static readonly Color DarkReference = Color.FromRgb(0x1E, 0x1E, 0x1E), LightReference = Colors.White;

    /// <summary>WCAG 2.x contrast of two opaque sRGB colours.</summary>
    public static double Contrast(Color a, Color b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static double Linear(double channel)
    {
        var value = channel / 255;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    /// <summary>SPEC 2.2 <c>--aon</c>: #FFFFFF or #111111, whichever has more contrast on <paramref name="accent"/> (white on a tie).</summary>
    public static Color OnColor(Color accent) =>
        Contrast(accent, Colors.White) >= Contrast(accent, Color.FromRgb(0x11, 0x11, 0x11)) ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11);

    private static bool Reads(Color c, bool dark) =>
        Contrast(c, dark ? DarkCard : LightCard) >= 3.1 && Contrast(c, dark ? DarkReference : LightReference) >= 3;

    // MARK: The lift (the board's liftCard, accent-d3-locked.dc.html script :735-773; tools/derive.js)

    private static readonly ConcurrentDictionary<(uint Rgb, bool Dark), Color> Lifts = new();

    /// <summary>
    /// SPEC 3.2 <c>lift(c, appearance)</c>: <paramref name="color"/> when it reads on both surfaces of the appearance,
    /// else the first 8-bit colour that does as its OKLCH lightness moves in 0.0005 steps (up in Dark, down in Light) at
    /// fixed hue, chroma kept unless sRGB cannot hold it (then the largest in-gamut chroma, by 30-step bisection). The
    /// alpha is kept.
    /// </summary>
    public static Color Lift(Color color, bool dark)
    {
        var key = ((uint)color.R << 16 | (uint)color.G << 8 | color.B, dark);
        var lifted = Lifts.GetOrAdd(key, static k =>
        {
            var c = Color.FromRgb((byte)((k.Rgb >> 16) & 0xFF), (byte)((k.Rgb >> 8) & 0xFF), (byte)(k.Rgb & 0xFF));
            if (Reads(c, k.Dark)) return c;
            var (l, chroma, hue) = ToLch(c);
            for (var i = 1; i < 2000; i++)
            {
                var x = FromLch(l + (k.Dark ? 0.0005 : -0.0005) * i, chroma, hue);
                if (Reads(x, k.Dark)) return x;
            }
            return c;
        });
        return Color.FromArgb(color.A, lifted.R, lifted.G, lifted.B);
    }

    private static (double L, double C, double H) ToLch(Color c)
    {
        var (r, g, b) = (Linear(c.R), Linear(c.G), Linear(c.B));
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        var lightness = 0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s;
        var a = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s;
        var bb = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s;
        return (lightness, Math.Sqrt(a * a + bb * bb), Math.Atan2(bb, a));
    }

    private static Color FromLch(double lightness, double chroma, double hue)
    {
        double[] LinearRgb(double cx)
        {
            var (a, b) = (cx * Math.Cos(hue), cx * Math.Sin(hue));
            var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
            var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
            var s = Math.Pow(lightness - 0.0894841775 * a - 1.291485548 * b, 3);
            return [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s, -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s];
        }
        static bool InGamut(double[] v) => v.All(x => x >= -1e-6 && x <= 1 + 1e-6);
        var cx = chroma;
        if (!InGamut(LinearRgb(chroma)))
        {
            double low = 0, high = chroma;
            for (var i = 0; i < 30; i++)
            {
                var mid = (low + high) / 2;
                if (InGamut(LinearRgb(mid))) low = mid; else high = mid;
            }
            cx = low;
        }
        static byte Encode(double x)
        {
            var y = Math.Max(0, Math.Min(1, x));
            var v = 255 * (y <= 0.0031308 ? 12.92 * y : 1.055 * Math.Pow(y, 1 / 2.4) - 0.055);
            return (byte)Math.Floor(v + 0.5); // JavaScript Math.round
        }
        var rgb = LinearRgb(cx);
        return Color.FromRgb(Encode(rgb[0]), Encode(rgb[1]), Encode(rgb[2]));
    }

    // MARK: Words (SPEC 4.3)

    /// <summary>A named theme's word for its own accent (AppTheme.swift:76-105 detail, after " · "), e.g. "Purple" for
    /// Dracula; null for System, Light and Dark, which have none.</summary>
    public static string? OwnWord(AppTheme theme)
    {
        if (theme.Spec().Accent is null) return null;
        var detail = theme.Detail();
        var dot = detail.IndexOf(" \u00B7 ", StringComparison.Ordinal);
        return dot < 0 ? null : detail[(dot + 3)..];
    }
}

/// <summary>
/// PORT-ONLY (OD-0088): the user's accent, mirrored from AppSettings ("accent") as ThemeManager mirrors the theme
/// (AppTheme.swift:213-216); setting it rebuilds ThemeManager.Spec and posts ThemeManager.DidChange.
/// </summary>
public static class AccentChoice
{
    private static string current = AccentPalette.WindowsId;

    /// <summary>The picked id, normalized (<see cref="AccentPalette.Normalize"/>).</summary>
    public static string Current
    {
        get => current;
        set
        {
            current = AccentPalette.Normalize(value);
            ThemeManager.AccentChanged();
        }
    }

    /// <summary>The stored pick at launch, set before ThemeManager.Current (whose change then carries it): no post of its own.</summary>
    public static void Restore(string? id) => current = AccentPalette.Normalize(id);
}
