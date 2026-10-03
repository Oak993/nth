using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Tests need the internal Palette() accessor (Swift's private palette, :109-140) across the two-part split.
[assembly: InternalsVisibleTo("Marrow.UI.Tests")]

namespace Marrow.UI.Theme;

// Source: DroppyCode/UI/Theme/AppTheme.swift (B2 1fbf81e), :1-140. Part 1 of 2 (work packet part1.md,
// PR-THEME-001 to PR-THEME-024): the enum, id, displayName, detail and the private palette table.
// Part 2 (part2.md, PR-THEME-025 to PR-THEME-037, UI-THEME-007) ports :141-248: the public scheme, swatch
// and spec accessors, the ThemeSpec struct, the ThemeManager enum and the Color(hex:) extension (the last
// of which already has its C# home in Port/SwiftColor.cs's SwiftColor.Hex, cited there against these lines).
//
// :14 enum AppTheme: String, Codable, CaseIterable, Identifiable, Sendable
/// <summary>
/// Every theme Droppy Code offers: the system look plus the palettes the big coding agents ship (Catppuccin,
/// Dracula, Tokyo Night, Nord, Gruvbox, One Dark, Everforest, Kanagawa, Rose Pine, Solarized, GitHub, Ayu,
/// Night Owl, Monokai, Claude, Codex, Cursor, Matrix). R-ENUM-1: a C# enum with [RawValue] on every member,
/// in Swift declaration order (SwiftEnum&lt;AppTheme&gt; supplies allCases, rawValue and init(rawValue:)).
/// </summary>
public enum AppTheme
{
    [RawValue("system")] System, // :15
    [RawValue("light")] Light, // :16
    [RawValue("dark")] Dark, // :17
    [RawValue("catppuccinMocha")] CatppuccinMocha, // :18
    [RawValue("catppuccinLatte")] CatppuccinLatte, // :19
    [RawValue("dracula")] Dracula, // :20
    [RawValue("tokyoNight")] TokyoNight, // :21
    [RawValue("nord")] Nord, // :22
    [RawValue("gruvbox")] Gruvbox, // :23
    [RawValue("gruvboxLight")] GruvboxLight, // :24
    [RawValue("oneDark")] OneDark, // :25
    [RawValue("everforest")] Everforest, // :26
    [RawValue("kanagawa")] Kanagawa, // :27
    [RawValue("rosePine")] RosePine, // :28
    [RawValue("solarizedDark")] SolarizedDark, // :29
    [RawValue("solarizedLight")] SolarizedLight, // :30
    [RawValue("githubDark")] GithubDark, // :31
    [RawValue("githubLight")] GithubLight, // :32
    [RawValue("ayu")] Ayu, // :33
    [RawValue("nightOwl")] NightOwl, // :34
    [RawValue("monokai")] Monokai, // :35
    [RawValue("claude")] Claude, // :36
    [RawValue("claudeLight")] ClaudeLight, // :37
    [RawValue("codex")] Codex, // :38
    [RawValue("cursor")] Cursor, // :39
    [RawValue("matrix")] Matrix, // :40
}

/// <summary>
/// UI/Theme/AppTheme.swift:109-140 <c>private var palette</c>: the raw hex table a theme paints with. Kept
/// internal (R-NAME-6) rather than private for the duration of the two-part split, since only this file's own
/// members read it once part 2 adds scheme/swatch/spec; [InternalsVisibleTo] above lets this part's tests
/// reach it directly.
/// </summary>
/// <param name="Scheme">
/// The scheme the theme fixes; null follows the system (Avalonia <see cref="ThemeVariant"/>, matching how
/// Port/SwiftColor.cs already resolves darkAqua vs aqua for AppTheme.swift:183).
/// </param>
/// <param name="Accent">A hex string with no "#", or null to keep the system control accent.</param>
/// <param name="Surface">A hex string with no "#": always set, tints the glass.</param>
/// <param name="Success">A hex string with no "#", or null to keep the framework green.</param>
/// <param name="Warning">A hex string with no "#", or null to keep the framework orange.</param>
/// <param name="Danger">A hex string with no "#", or null to keep the framework red.</param>
internal readonly record struct ThemePalette(
    ThemeVariant? Scheme, string? Accent, string Surface, string? Success, string? Warning, string? Danger);

/// <summary>The computed members of <see cref="AppTheme"/> (R-NAME-4).</summary>
public static class AppThemeExtensions
{
    // :42 var id: String { rawValue }
    public static string Id(this AppTheme theme) => SwiftEnum<AppTheme>.RawValue(theme);

    // :44-73 var displayName: String
    public static string DisplayName(this AppTheme theme) => theme switch
    {
        AppTheme.System => "System", // :46
        AppTheme.Light => "Light", // :47
        AppTheme.Dark => "Dark", // :48
        AppTheme.CatppuccinMocha => "Catppuccin Mocha", // :49
        AppTheme.CatppuccinLatte => "Catppuccin Latte", // :50
        AppTheme.Dracula => "Dracula", // :51
        AppTheme.TokyoNight => "Tokyo Night", // :52
        AppTheme.Nord => "Nord", // :53
        AppTheme.Gruvbox => "Gruvbox", // :54
        AppTheme.GruvboxLight => "Gruvbox Light", // :55
        AppTheme.OneDark => "One Dark", // :56
        AppTheme.Everforest => "Everforest", // :57
        AppTheme.Kanagawa => "Kanagawa", // :58
        AppTheme.RosePine => "Ros\U000000E9 Pine", // :59 U+00E9 LATIN SMALL LETTER E WITH ACUTE
        AppTheme.SolarizedDark => "Solarized Dark", // :60
        AppTheme.SolarizedLight => "Solarized Light", // :61
        AppTheme.GithubDark => "GitHub Dark", // :62
        AppTheme.GithubLight => "GitHub Light", // :63
        AppTheme.Ayu => "Ayu", // :64
        AppTheme.NightOwl => "Night Owl", // :65
        AppTheme.Monokai => "Monokai", // :66
        AppTheme.Claude => "Claude", // :67
        AppTheme.ClaudeLight => "Claude Light", // :68
        AppTheme.Codex => "Codex", // :69
        AppTheme.Cursor => "Cursor", // :70
        AppTheme.Matrix => "Matrix", // :71
    };

    // :76-105 var detail: String. Short picker subtitle, e.g. "Dark · Mauve".
    public static string Detail(this AppTheme theme) => theme switch
    {
        // DEVIATION OD-0008-B2: upstream :78 reads "Follows your Mac"; the port names the OS it runs on.
        AppTheme.System => "Follows Windows",
        AppTheme.Light => "Light · System accent", // :79
        AppTheme.Dark => "Dark · System accent", // :80
        AppTheme.CatppuccinMocha => "Dark · Mauve", // :81
        AppTheme.CatppuccinLatte => "Light · Mauve", // :82
        AppTheme.Dracula => "Dark · Purple", // :83
        AppTheme.TokyoNight => "Dark · Blue", // :84
        AppTheme.Nord => "Dark · Frost", // :85
        AppTheme.Gruvbox => "Dark · Orange", // :86
        AppTheme.GruvboxLight => "Light · Rust", // :87
        AppTheme.OneDark => "Dark · Blue", // :88
        AppTheme.Everforest => "Dark · Green", // :89
        AppTheme.Kanagawa => "Dark · Wave blue", // :90
        AppTheme.RosePine => "Dark · Rose", // :91
        AppTheme.SolarizedDark => "Dark · Blue", // :92
        AppTheme.SolarizedLight => "Light · Blue", // :93
        AppTheme.GithubDark => "Dark · Blue", // :94
        AppTheme.GithubLight => "Light · Blue", // :95
        AppTheme.Ayu => "Dark · Amber", // :96
        AppTheme.NightOwl => "Dark · Blue", // :97
        AppTheme.Monokai => "Dark · Pink", // :98
        AppTheme.Claude => "Dark · Terracotta", // :99
        AppTheme.ClaudeLight => "Light · Terracotta", // :100
        AppTheme.Codex => "Dark · Magenta", // :101
        AppTheme.Cursor => "Dark · Frost", // :102
        AppTheme.Matrix => "Dark · Green", // :103
    };

    // :109-140 private var palette. scheme: nil follows the system. accent/success/warning/danger: nil keeps
    // the system hue; surface always tints the glass (:107-108 doc comment).
    //
    // The Swift tuple's Dark/Light schemes are SwiftUI's ColorScheme; ThemeVariant.Dark/Light are the exact
    // Avalonia counterparts Port/SwiftColor.cs already resolves against (its IsDark helper cites this same
    // switch's line 183 in the part-2 spec accessor).
    internal static ThemePalette Palette(this AppTheme theme) => theme switch
    {
        AppTheme.System => new ThemePalette(null, null, "1E1E20", null, null, null), // :114
        AppTheme.Light => new ThemePalette(ThemeVariant.Light, null, "FFFFFF", null, null, null), // :115
        AppTheme.Dark => new ThemePalette(ThemeVariant.Dark, null, "1E1E20", null, null, null), // :116
        AppTheme.CatppuccinMocha => new ThemePalette(ThemeVariant.Dark, "CBA6F7", "1E1E2E", "A6E3A1", "F9E2AF", "F38BA8"), // :117
        AppTheme.CatppuccinLatte => new ThemePalette(ThemeVariant.Light, "8839EF", "EFF1F5", "40A02B", "DF8E1D", "D20F39"), // :118
        AppTheme.Dracula => new ThemePalette(ThemeVariant.Dark, "BD93F9", "282A36", "50FA7B", "FFB86C", "FF5555"), // :119
        AppTheme.TokyoNight => new ThemePalette(ThemeVariant.Dark, "7AA2F7", "1A1B26", "9ECE6A", "E0AF68", "F7768E"), // :120
        AppTheme.Nord => new ThemePalette(ThemeVariant.Dark, "88C0D0", "2E3440", "A3BE8C", "EBCB8B", "BF616A"), // :121
        AppTheme.Gruvbox => new ThemePalette(ThemeVariant.Dark, "FE8019", "282828", "B8BB26", "FABD2E", "FB4934"), // :122
        AppTheme.GruvboxLight => new ThemePalette(ThemeVariant.Light, "AF3A03", "FBF1C7", "79740E", "B57614", "9D0006"), // :123
        AppTheme.OneDark => new ThemePalette(ThemeVariant.Dark, "61AFEF", "282C34", "98C379", "E5C07B", "E06C75"), // :124
        AppTheme.Everforest => new ThemePalette(ThemeVariant.Dark, "A7C080", "2D353B", "A7C080", "DBBC7F", "E67E80"), // :125
        AppTheme.Kanagawa => new ThemePalette(ThemeVariant.Dark, "7E9CD8", "1F1F28", "98BB6C", "D7A657", "E82424"), // :126
        AppTheme.RosePine => new ThemePalette(ThemeVariant.Dark, "EB6F92", "191724", "9CCFD8", "F6C177", "EB6F92"), // :127
        AppTheme.SolarizedDark => new ThemePalette(ThemeVariant.Dark, "268BD2", "002B36", "859900", "B58900", "DC322F"), // :128
        AppTheme.SolarizedLight => new ThemePalette(ThemeVariant.Light, "268BD2", "FDF6E3", "859900", "B58900", "DC322F"), // :129
        AppTheme.GithubDark => new ThemePalette(ThemeVariant.Dark, "4493F8", "0D1117", "3FB950", "D29922", "F85149"), // :130
        AppTheme.GithubLight => new ThemePalette(ThemeVariant.Light, "0969DA", "FFFFFF", "1A7F37", "9A6700", "CF222E"), // :131
        AppTheme.Ayu => new ThemePalette(ThemeVariant.Dark, "E6B450", "0F1419", "AAD94C", "FFB454", "F58572"), // :132
        AppTheme.NightOwl => new ThemePalette(ThemeVariant.Dark, "82AAFF", "011627", "C5E478", "ECC48D", "EF5350"), // :133
        AppTheme.Monokai => new ThemePalette(ThemeVariant.Dark, "F92672", "272822", "A6E22E", "FD971F", "F92672"), // :134
        // :135-139 PR-THEME-025 to PR-THEME-029 (proven in part 2's AppThemeSecondTests).
        AppTheme.Claude => new ThemePalette(ThemeVariant.Dark, "C15F3C", "1A1816", "51A556", "C9A227", "D97757"), // :135
        AppTheme.ClaudeLight => new ThemePalette(ThemeVariant.Light, "C15F3C", "FAF9F5", "2E7D32", "9A6700", "B3261E"), // :136
        AppTheme.Codex => new ThemePalette(ThemeVariant.Dark, "D946EF", "101014", "3FB950", "D29922", "F85149"), // :137
        AppTheme.Cursor => new ThemePalette(ThemeVariant.Dark, "88C0D0", "181818", "3FA266", "F1B467", "E34671"), // :138
        AppTheme.Matrix => new ThemePalette(ThemeVariant.Dark, "00E676", "000000", "00E676", "FFD600", "FF5252"), // :139
    };

    // :143-144 var scheme: ColorScheme? { palette.scheme }
    public static ThemeVariant? Scheme(this AppTheme theme) => theme.Palette().Scheme;

    // :146-176 var swatch: some View. R-VIEW-1: a code-first Avalonia shape, built once per call.
    public static Control Swatch(this AppTheme theme) => new ThemeSwatch(theme);

    // PR-THEME-031 windows_path: "Pick one fixed green, orange and red for the three system-hue themes and
    // keep them constant; the asymmetry with the accent (which follows the OS) is deliberate and must be
    // preserved." Swift's Color.green/.orange/.red are themselves the systemGreen/systemOrange/systemRed HIG
    // Default tokens, which are dynamic between light and dark; this port fixes one value each per the
    // windows_path above rather than modelling that dynamism. Values are Apple's published HIG Default light
    // values (the same family Port/SwiftColor.cs's Yellow already pins for .yellow, ED-104).
    // UNVERIFIED under OD-0001, no O-nn, per the windows_path text above (same status as ED-104/105/106).
    private static readonly SwiftColor FrameworkGreen = SwiftColor.Rgb(0x34C759); // systemGreen HIG Default light: 52,199,89
    private static readonly SwiftColor FrameworkOrange = SwiftColor.Rgb(0xFF9500); // systemOrange HIG Default light: 255,149,0
    private static readonly SwiftColor FrameworkRed = SwiftColor.Rgb(0xFF3B30); // systemRed HIG Default light: 255,59,48

    // :178-199 var spec: ThemeSpec
    public static ThemeSpec Spec(this AppTheme theme)
    {
        var palette = theme.Palette();
        // :181-190 if self == .system, a dynamic pair matching NSColor(name:nil, dynamicProvider:); else the
        // surface hex.
        var surface = theme == AppTheme.System
            ? SwiftColor.Dynamic(light: SwiftColor.White, dark: SwiftColor.Hex("1E1E20")) // :184-187
            : SwiftColor.Hex(palette.Surface); // :189
        return new ThemeSpec(
            Scheme: palette.Scheme, // :192
            Accent: palette.Accent is not null ? SwiftColor.Hex(palette.Accent) : null, // :193, PR-THEME-030
            Surface: surface, // :194
            Success: palette.Success is not null ? SwiftColor.Hex(palette.Success) : FrameworkGreen, // :195, PR-THEME-031
            Warning: palette.Warning is not null ? SwiftColor.Hex(palette.Warning) : FrameworkOrange, // :196, PR-THEME-031
            Danger: palette.Danger is not null ? SwiftColor.Hex(palette.Danger) : FrameworkRed); // :197, PR-THEME-031
    }
}

/// <summary>
/// UI/Theme/AppTheme.swift:202-211 <c>struct ThemeSpec</c>: what a theme paints. R-VAL-1: struct to immutable
/// record. <see cref="Accent"/> is nullable (PR-THEME-030: nil means the system control accent, never a
/// substituted colour); <see cref="Success"/>, <see cref="Warning"/> and <see cref="Danger"/> are never null,
/// since AppTheme.Spec() already resolved their nil case to a fixed hue (PR-THEME-031).
/// </summary>
public sealed record ThemeSpec(
    ThemeVariant? Scheme,
    SwiftColor? Accent,
    SwiftColor Surface,
    SwiftColor Success,
    SwiftColor Warning,
    SwiftColor Danger);

/// <summary>
/// UI/Theme/AppTheme.swift:213-234 <c>enum ThemeManager</c>. Mirrors AppSettings.theme, which is the source of
/// truth (:213-216); this unit ports only the manager's own storage and broadcast, not the AppSettings mirror
/// (a higher-layer concern, out of this file's records).
/// </summary>
public static class ThemeManager
{
    // :221 static let didChange = Notification.Name("ThemeManager.didChange"). R-NOTE-1: a plain event, raised
    // synchronously where Swift posts (NotificationCenter.default.post(name:didChange, object: nil) carries no
    // payload, so this event carries none either).
    public static event Action? DidChange;

    // :223 nonisolated(unsafe) static var current: AppTheme = .system. R-ISO-5: a plain static field, no lock.
    private static AppTheme current = AppTheme.System;

    /// <summary>
    /// :223-228 <c>current</c>'s <c>didSet</c>: rebuilds <see cref="Spec"/> and posts <see cref="DidChange"/> on
    /// every assignment, with no equality guard, even a reassignment of the value already held
    /// (PR-THEME-036, upstream defect 60, reproduced deliberately per port_impacts.md entry 174).
    /// </summary>
    public static AppTheme Current
    {
        get => current;
        set
        {
            current = value;
            themeSpec = value.Spec();
            Spec = Picked(themeSpec); // :225; DEVIATION OD-0088: with the user's accent (see Picked)
            Accent = NewAccent();
            DidChange?.Invoke(); // :226-227, nil object: this event carries no payload either
        }
    }

    // DEVIATION OD-0088 (ED-250): the accent is the user's preference (Port/AccentPalette.cs). A pick other than the
    // default replaces the theme's accent in the spec everything reads (Chrome.accent, the window tint); the default
    // ("windows") keeps the theme's own, nil for System, Light and Dark, exactly as the source.
    // The current theme's spec as AppTheme.spec builds it, before the pick.
    private static ThemeSpec themeSpec = AppTheme.System.Spec();

    private static ThemeSpec Picked(ThemeSpec spec) => AccentPalette.Pick(AccentChoice.Current) is { } pick ? spec with { Accent = pick } : spec;

    /// <summary>PORT-ONLY (OD-0088): <see cref="AccentChoice.Current"/> changed: the spec is rebuilt with it and
    /// <see cref="DidChange"/> posted, as a theme change does (:225-227).</summary>
    internal static void AccentChanged()
    {
        Spec = Picked(themeSpec); // the theme's own colours (surface and the rest) stay the same values
        Accent = NewAccent();
        DidChange?.Invoke();
    }

    /// <summary>
    /// PORT-ONLY (OD-0088, ED-250): Chrome.accent (Chrome.swift:85, :87) as one colour per spec, which is the spec's accent,
    /// else the system control accent, whenever it is resolved, so a brush made from it follows a later pick or theme. A new
    /// instance for each spec, as the source's computed property gives a new value with each theme.
    /// </summary>
    internal static SwiftColor Accent { get; private set; } = NewAccent();

    private static SwiftColor NewAccent() => SwiftColor.Live(() => Spec.Accent ?? SwiftColor.ControlAccentColor);

    // :230-233 nonisolated(unsafe) private(set) static var spec: ThemeSpec = AppTheme.system.spec. PR-THEME-037:
    // built once per change here, in Current's setter, and simply read by everything else (Chrome.accent and
    // friends), never rebuilt on the read path.
    public static ThemeSpec Spec { get; private set; } = AppTheme.System.Spec();

    /// <summary>
    /// PORT-ONLY (OD-0088, ED-254): Chrome.glassTint (Chrome.swift:96, the spec's surface) whenever it is resolved, so the
    /// window's and the sheet's wash made from it follow a later theme, easing over 0.32 s as the accent does (SPEC 6.5).
    /// </summary>
    internal static SwiftColor LiveSurface { get; } = SwiftColor.Live(() => Spec.Surface);
}

/// <summary>
/// UI/Theme/AppTheme.swift:149-176 <c>var swatch</c>'s rendered shape: the theme picker's circular badge.
/// R-VIEW-1: built once in the constructor, tracking the two ambient inputs the Swift computed property reads
/// through <c>Color(nsColor:)</c>/<c>Color.accentColor</c>: the light/dark appearance (only for the rim, since
/// the fill's own colours are all fixed hex or the raw accent, none of which vary by appearance) and, for the
/// three themes with no fixed accent (system, light, dark; :164's fallback runs for all three), the OS control
/// accent (ED-051).
/// </summary>
public sealed class ThemeSwatch : Ellipse
{
    private readonly AppTheme theme;
    private readonly LinearGradientBrush gradient = new();
    private IPlatformSettings? settings;

    public ThemeSwatch(AppTheme theme)
    {
        this.theme = theme;
        // Circle() fills whatever frame its caller gives it: the defining geometry of an unstretched Ellipse is its own
        // bounds deflated by half the stroke, so the 0.5 rim lies inside them as strokeBorder's does (Stretch.Fill would
        // scale the geometry to the full bounds and centre the rim on the edge).
        Stretch = Stretch.None;
        Fill = gradient;
        // :152 let rim = Circle().strokeBorder(Color.primary.opacity(0.22), lineWidth: 0.5), applied by both
        // branches at :162 and :174. SwiftColor.Brush(Control) already tracks ActualThemeVariantChanged itself
        // (Color.primary is a light/dark pair), so the rim needs no update logic of its own here.
        // ENGINEERING DECISION: Avalonia draws a Shape's Stroke centered on the path, not inset like SwiftUI's
        // strokeBorder; at StrokeThickness 0.5 the difference is a sub-pixel 0.25, not modelled separately.
        Stroke = SwiftColor.Primary.Opacity(0.22).Brush(this);
        StrokeThickness = 0.5;
        // ENGINEERING DECISION: the swatch is a picker badge, not an interactive control in its own right
        // (UI-THEME-007 "not interactive"); it lets clicks fall through to whatever row or button hosts it.
        IsHitTestVisible = false;
        if (UsesSystemAccent)
        {
            AttachedToVisualTree += (_, _) => Subscribe();
            DetachedFromVisualTree += (_, _) => Unsubscribe();
        }
        Update();
    }

    // :164 `palette.accent.map(Color.init(hex:)) ?? Color.accentColor` runs in the non-system branch for every
    // theme whose own accent is nil, i.e. light and dark as well as system (:154's branch never reads an
    // accent at all, so system itself does not subscribe).
    private bool UsesSystemAccent => theme != AppTheme.System && theme.Palette().Accent is null;

    private void Subscribe()
    {
        if (settings is not null) return;
        settings = PlatformSettings;
        settings.ColorValuesChanged += OnColorValuesChanged;
        Update();
    }

    private void Unsubscribe()
    {
        if (settings is null) return;
        settings.ColorValuesChanged -= OnColorValuesChanged;
        settings = null;
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues values) => Update();

    private static IPlatformSettings PlatformSettings =>
        Application.Current?.PlatformSettings
        ?? throw new InvalidOperationException("The system accent needs a running Avalonia application.");

    private void Update()
    {
        gradient.GradientStops.Clear();
        if (theme == AppTheme.System)
        {
            // :154-162: a hard-split disc, white on the left half and #1E1E20 on the right, leading to trailing.
            gradient.StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative); // :160 .leading
            gradient.EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative); // :160 .trailing
            var white = SwiftColor.Hex("FFFFFF").Resolve(ThemeVariant.Light); // :157, Fixed kind: no variant to pass
            var slate = SwiftColor.Hex("1E1E20").Resolve(ThemeVariant.Light); // :158, Fixed kind: no variant to pass
            gradient.GradientStops.Add(new GradientStop(white, 0));
            gradient.GradientStops.Add(new GradientStop(white, 0.5));
            gradient.GradientStops.Add(new GradientStop(slate, 0.5));
            gradient.GradientStops.Add(new GradientStop(slate, 1));
        }
        else
        {
            // :163-174: surface to accent, diagonal, held to 0.42, top-leading to bottom-trailing.
            gradient.StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative); // :172 .topLeading
            gradient.EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative); // :172 .bottomTrailing
            var palette = theme.Palette();
            // :164 palette.accent.map(Color.init(hex:)) ?? Color.accentColor. Both a hex colour (Fixed kind)
            // and the accent (Accent kind) resolve the same regardless of the variant passed, so ThemeVariant.Light
            // here is a placeholder Resolve requires, not a meaningful input.
            var accentColor = palette.Accent is not null
                ? SwiftColor.Hex(palette.Accent).Resolve(ThemeVariant.Light)
                : SwiftColor.AccentColor.Resolve(ThemeVariant.Light);
            var surfaceColor = SwiftColor.Hex(palette.Surface).Resolve(ThemeVariant.Light);
            gradient.GradientStops.Add(new GradientStop(surfaceColor, 0));
            gradient.GradientStops.Add(new GradientStop(surfaceColor, 0.42));
            gradient.GradientStops.Add(new GradientStop(accentColor, 1));
        }
    }
}
