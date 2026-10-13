// PORT-ONLY: web surfaces W20, Windows high contrast for nth's glass (E:/Droppy/work/design/web-surfaces/DESIGN.md 10 "High
// contrast: glass falls back to solid system colours; splitter hairlines become 1 DIP system lines"; ED-NEW-web-w20-1). No Swift
// counterpart: macOS has "Increase contrast", Windows has contrast themes whose colours the user picks. The setting is an
// inherited environment value like Reduce Motion (SystemReduceMotion): every window the app opens gets the system's palette on
// its root (null while high contrast is off), re-read on WM_SETTINGCHANGE and WM_SYSCOLORCHANGE. A surface that turns solid
// (ContrastSurface.MatchTheme: pane cards, rails, popover cards, Visus's sheets and windows) marks its subtree; a colour that
// opts in with SwiftColor.InHighContrast(role) draws the role's system colour there and is exactly the colour it was anywhere
// else (glass that stays glass keeps its own colours). Only the app installs it (MarrowApplication); headless tests set the
// property themselves.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>What a colour is for, so high contrast can draw the system colour for that job instead.</summary>
public enum ContrastRole
{
    /// <summary>Not a high-contrast colour: drawn as it is in every mode.</summary>
    None,

    /// <summary>Words and glyphs (the system's WindowText).</summary>
    Text,

    /// <summary>Hairlines, rims and outlines (WindowText: a 1 DIP system line).</summary>
    Line,

    /// <summary>Glass, wells, chips and hover tints: the solid surface they sit on (Window).</summary>
    Surface,

    /// <summary>Focus, the chosen item's outline, an active drop target (Highlight).</summary>
    Highlight,

    /// <summary>Words on a Highlight fill (HighlightText).</summary>
    HighlightText,

    /// <summary>Words that are off (GrayText).</summary>
    Disabled,

    /// <summary>Links (HotLight).</summary>
    Link,
}

/// <summary>The system colours of a Windows contrast theme (GetSysColor), as nth draws them.</summary>
public sealed record HighContrastPalette(
    Color Window,
    Color WindowText,
    Color Highlight,
    Color HighlightText,
    Color GrayText,
    Color HotLight)
{
    /// <summary>
    /// Windows' "High Contrast Black" scheme (and the Tactus canvas's high-contrast Highlight, #1AEBFF): what headless tests
    /// and a PC whose colours cannot be read draw.
    /// </summary>
    public static HighContrastPalette Black { get; } = new(
        Window: Color.FromRgb(0x00, 0x00, 0x00),
        WindowText: Color.FromRgb(0xFF, 0xFF, 0xFF),
        Highlight: Color.FromRgb(0x1A, 0xEB, 0xFF),
        HighlightText: Color.FromRgb(0x00, 0x00, 0x00),
        GrayText: Color.FromRgb(0x3F, 0xF2, 0x3F),
        HotLight: Color.FromRgb(0xFF, 0xFF, 0x00));

    /// <summary>Windows' "High Contrast White" scheme (the light contrast theme, "Desert" family).</summary>
    public static HighContrastPalette White { get; } = new(
        Window: Color.FromRgb(0xFF, 0xFF, 0xFF),
        WindowText: Color.FromRgb(0x00, 0x00, 0x00),
        Highlight: Color.FromRgb(0x37, 0x00, 0x6E),
        HighlightText: Color.FromRgb(0xFF, 0xFF, 0xFF),
        GrayText: Color.FromRgb(0x60, 0x00, 0x00),
        HotLight: Color.FromRgb(0x00, 0x00, 0x9F));

    /// <summary>The system colour for a role (None: transparent, which a caller never draws).</summary>
    public Color For(ContrastRole role) => role switch
    {
        ContrastRole.Text or ContrastRole.Line => WindowText,
        ContrastRole.Surface => Window,
        ContrastRole.Highlight => Highlight,
        ContrastRole.HighlightText => HighlightText,
        ContrastRole.Disabled => GrayText,
        ContrastRole.Link => HotLight,
        _ => Colors.Transparent,
    };

    /// <summary>A solid brush of the role's colour.</summary>
    public IBrush Brush(ContrastRole role) => new SolidColorBrush(For(role));

    /// <summary>Whether the theme's surface is dark (its Window colour's relative luminance under one half).</summary>
    public bool IsDark => Luminance(Window) < 0.5;

    /// <summary>WCAG relative luminance of an sRGB colour.</summary>
    public static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>WCAG contrast ratio of two opaque colours.</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}

/// <summary>PORT-ONLY. Windows' high contrast setting, applied to every open window (see the file note).</summary>
public sealed class SystemHighContrast
{
    private readonly Func<HighContrastPalette?> read;
    private readonly List<WeakReference<Avalonia.Controls.Window>> windows = [];

    /// <param name="read">Reads the system: the palette while a contrast theme is on, else null.</param>
    public SystemHighContrast(Func<HighContrastPalette?> read) => this.read = read;

    /// <summary>The app's instance (<see cref="Install"/> makes it read Windows; until then it follows no window).</summary>
    public static Ambient<SystemHighContrast> Shared { get; } = new(new SystemHighContrast(() => null));

    private static bool installed;

    /// <summary>App start-up: every window opened from now on follows Windows' contrast theme.</summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        Shared.SetProcessDefault(new SystemHighContrast(ReadWindows));
        Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<Avalonia.Controls.Window>((window, _) => Shared.Current.Follow(window), RoutingStrategies.Direct);
    }

    /// <summary>Sets the palette on <paramref name="window"/>'s root and keeps it in step until the window closes.</summary>
    public void Follow(Avalonia.Controls.Window window)
    {
        windows.RemoveAll(entry => !entry.TryGetTarget(out _));
        windows.Add(new WeakReference<Avalonia.Controls.Window>(window));
        window.Closed += (_, _) => windows.RemoveAll(entry => !entry.TryGetTarget(out var alive) || ReferenceEquals(alive, window));
        EnvironmentValues.SetAccessibilityHighContrast(window, read());
    }

    /// <summary>WM_SETTINGCHANGE or WM_SYSCOLORCHANGE: reads the setting again and applies it to every open window.</summary>
    public void Refresh()
    {
        var palette = read();
        foreach (var entry in windows.ToArray())
            if (entry.TryGetTarget(out var window) && !Equals(EnvironmentValues.GetAccessibilityHighContrast(window), palette))
                EnvironmentValues.SetAccessibilityHighContrast(window, palette);
    }

    // SPI_GETHIGHCONTRAST's HCF_HIGHCONTRASTON, then the theme's colours (GetSysColor: COLOR_WINDOW 5, COLOR_WINDOWTEXT 8,
    // COLOR_HIGHLIGHT 13, COLOR_HIGHLIGHTTEXT 14, COLOR_GRAYTEXT 17, COLOR_HOTLIGHT 26).
    private static HighContrastPalette? ReadWindows()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var info = new HighContrastInfo { Size = (uint)Marshal.SizeOf<HighContrastInfo>() };
        if (!SystemParametersInfo(0x0042, info.Size, ref info, 0) || (info.Flags & 0x1) == 0) return null;
        static Color Sys(int index)
        {
            var value = GetSysColor(index); // 0x00BBGGRR
            return Color.FromRgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
        }
        return new HighContrastPalette(Sys(5), Sys(8), Sys(13), Sys(14), Sys(17), Sys(26));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrastInfo
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref HighContrastInfo info, uint update);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);
}

/// <summary>
/// A surface that turns solid under a contrast theme also draws its content for that surface's lightness: words and glyphs that
/// have no role (a status colour, a code well) resolve as the dark appearance on a dark contrast theme and the light one on a
/// light theme, whatever nth's own theme is, so nothing dark lands on a black surface; and colours with a role take the system's
/// colours inside it (EnvironmentValues.AccessibilityContrastSurface). Off: both are inherited again.
/// </summary>
public static class ContrastSurface
{
    /// <summary>Matches <paramref name="surface"/>'s appearance to the contrast theme in its tree (or clears what it set).</summary>
    /// <remarks>The appearance is the inherited ActualThemeVariant (ThemeVariantScope's public handle on the one property every
    /// element inherits), set on the surface itself as a ThemeVariantScope sets it on its own element, so no element is added.</remarks>
    public static void MatchTheme(Control surface)
    {
        var palette = EnvironmentValues.GetAccessibilityHighContrast(surface);
        if (palette is null)
        {
            if (surface.GetValue(MatchedProperty))
            {
                surface.SetValue(MatchedProperty, false);
                surface.ClearValue(ThemeVariantScope.ActualThemeVariantProperty);
                surface.ClearValue(EnvironmentValues.AccessibilityContrastSurfaceProperty);
            }
            return;
        }
        var variant = palette.IsDark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        surface.SetValue(MatchedProperty, true);
        surface.SetValue(EnvironmentValues.AccessibilityContrastSurfaceProperty, true); // colours with a role take the system's here
        if (!Equals(surface.ActualThemeVariant, variant)) surface.SetValue(ThemeVariantScope.ActualThemeVariantProperty, variant);
    }

    /// <summary>Whether <see cref="MatchTheme"/> has set <paramref name="surface"/>'s appearance now.</summary>
    public static bool IsMatched(Control surface) => surface.GetValue(MatchedProperty);

    // Whether MatchTheme set the surface's appearance (so turning contrast off clears only its own setting).
    private static readonly AttachedProperty<bool> MatchedProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ContrastMatched", typeof(ContrastSurface));
}

/// <summary>
/// The chosen item's high-contrast outline (DESIGN 10, lean rule 10 "no state shown by colour alone"): a chosen tab or toggle is
/// a tint in nth's glass, which a contrast theme flattens to the surface; there it gets a 2 DIP inset Highlight ring instead.
/// The ring is an inset box shadow, so it costs no layout. Set <see cref="ChosenProperty"/> wherever the tint is set.
/// </summary>
public static class ContrastCue
{
    /// <summary>The ring's width.</summary>
    public const double RingWidth = 2;

    public static readonly AttachedProperty<bool> ChosenProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>("Chosen", typeof(ContrastCue));

    static ContrastCue()
    {
        ChosenProperty.Changed.AddClassHandler<Border>((border, _) => Apply(border));
        EnvironmentValues.AccessibilityHighContrastProperty.Changed.AddClassHandler<Border>((border, _) =>
        {
            if (border.IsSet(ChosenProperty)) Apply(border);
        });
    }

    public static bool GetChosen(Border border) => border.GetValue(ChosenProperty);

    /// <summary>Marks <paramref name="border"/> chosen or not; under high contrast a chosen one draws the ring.</summary>
    public static void SetChosen(Border border, bool chosen)
    {
        if (border.IsSet(ChosenProperty) && GetChosen(border) == chosen) return;
        border.SetValue(ChosenProperty, chosen);
        if (!chosen) Apply(border); // false is the default: a first SetValue(false) raises nothing
    }

    /// <summary>
    /// A toggle button's outline: under a contrast theme a 1 DIP Highlight line when it is on and none (still 1 DIP, so nothing
    /// moves between the two) when it is off; otherwise <paramref name="restBrush"/> at <paramref name="restThickness"/>, as
    /// the surface draws it. The caller runs it again when the theme or the toggle changes.
    /// </summary>
    public static void Toggle(Avalonia.Controls.Primitives.TemplatedControl control, bool on, IBrush? restBrush, Thickness restThickness)
    {
        if (EnvironmentValues.GetAccessibilityHighContrast(control) is { } palette)
        {
            control.BorderThickness = new Thickness(1);
            control.BorderBrush = on ? palette.Brush(ContrastRole.Highlight) : Brushes.Transparent;
            return;
        }
        control.BorderThickness = restThickness;
        control.BorderBrush = restBrush;
    }

    /// <summary>Whether <paramref name="border"/> draws the ring now.</summary>
    public static bool ShowsRing(Border border) => GetChosen(border) && EnvironmentValues.GetAccessibilityHighContrast(border) is not null;

    private static void Apply(Border border)
    {
        if (EnvironmentValues.GetAccessibilityHighContrast(border) is { } palette && GetChosen(border))
            border.BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, Spread = RingWidth, Color = palette.Highlight });
        else if (border.BoxShadow != default)
            border.BoxShadow = default;
    }
}
