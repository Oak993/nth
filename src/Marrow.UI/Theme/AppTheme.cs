using System.Runtime.CompilerServices;
using Avalonia.Styling;
using Marrow.Core.Port;

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
        // :135-139 land with part 2's records (PR-THEME-025 to PR-THEME-029); values transcribed now only so
        // this switch stays exhaustive (CS8509 is not suppressed, per Directory.Build.props's ED-075 note).
        AppTheme.Claude => new ThemePalette(ThemeVariant.Dark, "C15F3C", "1A1816", "51A556", "C9A227", "D97757"), // :135
        AppTheme.ClaudeLight => new ThemePalette(ThemeVariant.Light, "C15F3C", "FAF9F5", "2E7D32", "9A6700", "B3261E"), // :136
        AppTheme.Codex => new ThemePalette(ThemeVariant.Dark, "D946EF", "101014", "3FB950", "D29922", "F85149"), // :137
        AppTheme.Cursor => new ThemePalette(ThemeVariant.Dark, "88C0D0", "181818", "3FA266", "F1B467", "E34671"), // :138
        AppTheme.Matrix => new ThemePalette(ThemeVariant.Dark, "00E676", "000000", "00E676", "FFD600", "FF5252"), // :139
    };
}
