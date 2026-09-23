using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Theme;

// Source-derived tests for DroppyCode/UI/Theme/AppTheme.swift (B2 1fbf81e), unit UI.Theme.AppTheme,
// part 1 of 2 (work packet part1.md, PR-THEME-001 to PR-THEME-024). Every expected value is transcribed
// from the cited Swift lines, or from the record's "deviation" field (OD-0008 B2 for the System theme's
// detail), never from the C# under test.
//
// API shape assumed (R-NAME-1 to R-NAME-6, R-ENUM-1): `enum AppTheme` with [RawValue] on every member in
// Swift declaration order; its computed members as extension methods `Id()`, `DisplayName()`, `Detail()`;
// and an internal `Palette()` extension method (the Swift `private var palette`, kept internal rather than
// private across the two-part split so this part's tests can reach it; UI/Theme/AppTheme.swift:109-140)
// returning a `ThemePalette` with `Scheme` (Avalonia `ThemeVariant?`, null for "follows the system"),
// `Accent`, `Success`, `Warning`, `Danger` (nullable hex strings, no "#") and `Surface` (a hex string).
public sealed class AppThemeTests
{
    // UI/Theme/AppTheme.swift:15-40, in declaration order.
    private static readonly string[] RawValuesInOrder =
    [
        "system", "light", "dark", "catppuccinMocha", "catppuccinLatte", "dracula", "tokyoNight",
        "nord", "gruvbox", "gruvboxLight", "oneDark", "everforest", "kanagawa", "rosePine",
        "solarizedDark", "solarizedLight", "githubDark", "githubLight", "ayu", "nightOwl",
        "monokai", "claude", "claudeLight", "codex", "cursor", "matrix",
    ];

    [Fact]
    [Trait("Req", "PR-THEME-001")] // UI/Theme/AppTheme.swift:14-42
    public void AllCases_lists_exactly_26_themes_in_declaration_order_with_id_equal_to_raw_value()
    {
        var cases = SwiftEnum<AppTheme>.AllCases;

        Assert.Equal(26, cases.Count);
        Assert.Equal(RawValuesInOrder, cases.Select(SwiftEnum<AppTheme>.RawValue));
        Assert.Equal(RawValuesInOrder, cases.Select(theme => theme.Id())); // :42 var id: String { rawValue }
    }

    [Fact]
    [Trait("Req", "PR-THEME-001")] // UI/Theme/AppTheme.swift:44-134 (every case reachable from its raw value)
    public void FromRawValue_resolves_every_theme_and_rejects_an_unknown_token()
    {
        foreach (var theme in SwiftEnum<AppTheme>.AllCases)
            Assert.Equal(theme, SwiftEnum<AppTheme>.FromRawValue(SwiftEnum<AppTheme>.RawValue(theme)));

        Assert.Null(SwiftEnum<AppTheme>.FromRawValue("Light")); // case-sensitive, not "light"
        Assert.Null(SwiftEnum<AppTheme>.FromRawValue("catppuccin-mocha")); // unknown token
    }

    [Theory]
    [Trait("Req", "PR-THEME-002")] // UI/Theme/AppTheme.swift:44-73
    [InlineData(AppTheme.System, "System")]
    [InlineData(AppTheme.Light, "Light")]
    [InlineData(AppTheme.Dark, "Dark")]
    [InlineData(AppTheme.CatppuccinMocha, "Catppuccin Mocha")]
    [InlineData(AppTheme.CatppuccinLatte, "Catppuccin Latte")]
    [InlineData(AppTheme.Dracula, "Dracula")]
    [InlineData(AppTheme.TokyoNight, "Tokyo Night")]
    [InlineData(AppTheme.Nord, "Nord")]
    [InlineData(AppTheme.Gruvbox, "Gruvbox")]
    [InlineData(AppTheme.GruvboxLight, "Gruvbox Light")]
    [InlineData(AppTheme.OneDark, "One Dark")]
    [InlineData(AppTheme.Everforest, "Everforest")]
    [InlineData(AppTheme.Kanagawa, "Kanagawa")]
    [InlineData(AppTheme.SolarizedDark, "Solarized Dark")]
    [InlineData(AppTheme.SolarizedLight, "Solarized Light")]
    [InlineData(AppTheme.GithubDark, "GitHub Dark")]
    [InlineData(AppTheme.GithubLight, "GitHub Light")]
    [InlineData(AppTheme.Ayu, "Ayu")]
    [InlineData(AppTheme.NightOwl, "Night Owl")]
    [InlineData(AppTheme.Monokai, "Monokai")]
    [InlineData(AppTheme.Claude, "Claude")]
    [InlineData(AppTheme.ClaudeLight, "Claude Light")]
    [InlineData(AppTheme.Codex, "Codex")]
    [InlineData(AppTheme.Cursor, "Cursor")]
    [InlineData(AppTheme.Matrix, "Matrix")]
    public void DisplayName_matches_the_Swift_switch_verbatim(AppTheme theme, string expected) =>
        Assert.Equal(expected, theme.DisplayName());

    // Rose Pine carries U+00E9, tested on its own line since InlineData cannot hold the escape comment cleanly.
    [Fact]
    [Trait("Req", "PR-THEME-002")] // UI/Theme/AppTheme.swift:59 case .rosePine: "Rosé Pine"
    public void DisplayName_of_rosePine_carries_the_accented_e()
    {
        var expected = "Ros\U000000E9 Pine"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        Assert.Equal(expected, AppTheme.RosePine.DisplayName());
    }

    [Theory]
    [Trait("Req", "PR-THEME-003")] // UI/Theme/AppTheme.swift:76-105
    [InlineData(AppTheme.Light, "Light · System accent")]
    [InlineData(AppTheme.Dark, "Dark · System accent")]
    [InlineData(AppTheme.CatppuccinMocha, "Dark · Mauve")]
    [InlineData(AppTheme.CatppuccinLatte, "Light · Mauve")]
    [InlineData(AppTheme.Dracula, "Dark · Purple")]
    [InlineData(AppTheme.TokyoNight, "Dark · Blue")]
    [InlineData(AppTheme.Nord, "Dark · Frost")]
    [InlineData(AppTheme.Gruvbox, "Dark · Orange")]
    [InlineData(AppTheme.GruvboxLight, "Light · Rust")]
    [InlineData(AppTheme.OneDark, "Dark · Blue")]
    [InlineData(AppTheme.Everforest, "Dark · Green")]
    [InlineData(AppTheme.Kanagawa, "Dark · Wave blue")]
    [InlineData(AppTheme.RosePine, "Dark · Rose")]
    [InlineData(AppTheme.SolarizedDark, "Dark · Blue")]
    [InlineData(AppTheme.SolarizedLight, "Light · Blue")]
    [InlineData(AppTheme.GithubDark, "Dark · Blue")]
    [InlineData(AppTheme.GithubLight, "Light · Blue")]
    [InlineData(AppTheme.Ayu, "Dark · Amber")]
    [InlineData(AppTheme.NightOwl, "Dark · Blue")]
    [InlineData(AppTheme.Monokai, "Dark · Pink")]
    [InlineData(AppTheme.Claude, "Dark · Terracotta")]
    [InlineData(AppTheme.ClaudeLight, "Light · Terracotta")]
    [InlineData(AppTheme.Codex, "Dark · Magenta")]
    [InlineData(AppTheme.Cursor, "Dark · Frost")]
    [InlineData(AppTheme.Matrix, "Dark · Green")]
    public void Detail_matches_the_Swift_switch_verbatim_with_middle_dot_separator(AppTheme theme, string expected)
    {
        // U+00B7 MIDDLE DOT with one ASCII space either side, per the InlineData literals above.
        Assert.Equal(expected, theme.Detail());
    }

    [Fact]
    [Trait("Decision", "OD-0008-B2")] // System theme's detail: "Follows your Mac" upstream (:78) -> "Follows Windows"
    public void Detail_of_system_reads_Follows_Windows_by_OD_0008_B2_not_the_upstream_Mac_wording()
    {
        Assert.Equal("Follows Windows", AppTheme.System.Detail());
        Assert.NotEqual("Follows your Mac", AppTheme.System.Detail());
    }

    [Fact]
    [Trait("Req", "PR-THEME-004")] // UI/Theme/AppTheme.swift:114 case .system: (nil, nil, "1E1E20", nil, nil, nil)
    public void Palette_of_system_has_no_fixed_scheme_no_accent_and_surface_1E1E20()
    {
        var palette = AppTheme.System.Palette();

        Assert.Null(palette.Scheme);
        Assert.Null(palette.Accent);
        Assert.Equal("1E1E20", palette.Surface);
        Assert.Null(palette.Success);
        Assert.Null(palette.Warning);
        Assert.Null(palette.Danger);
    }

    [Fact]
    [Trait("Req", "PR-THEME-005")] // UI/Theme/AppTheme.swift:115 case .light: (.light, nil, "FFFFFF", nil, nil, nil)
    public void Palette_of_light_is_fixed_light_with_no_accent_and_surface_FFFFFF()
    {
        var palette = AppTheme.Light.Palette();

        Assert.Equal(ThemeVariant.Light, palette.Scheme);
        Assert.Null(palette.Accent);
        Assert.Equal("FFFFFF", palette.Surface);
        Assert.Null(palette.Success);
        Assert.Null(palette.Warning);
        Assert.Null(palette.Danger);
    }

    [Fact]
    [Trait("Req", "PR-THEME-006")] // UI/Theme/AppTheme.swift:116 case .dark: (.dark, nil, "1E1E20", nil, nil, nil)
    public void Palette_of_dark_is_fixed_dark_with_no_accent_and_surface_1E1E20()
    {
        var palette = AppTheme.Dark.Palette();

        Assert.Equal(ThemeVariant.Dark, palette.Scheme);
        Assert.Null(palette.Accent);
        Assert.Equal("1E1E20", palette.Surface);
        Assert.Null(palette.Success);
        Assert.Null(palette.Warning);
        Assert.Null(palette.Danger);
    }

    [Fact]
    [Trait("Req", "PR-THEME-007")] // UI/Theme/AppTheme.swift:117
    public void Palette_of_catppuccinMocha_is_dark_with_the_Mauve_row() =>
        AssertRow(AppTheme.CatppuccinMocha, ThemeVariant.Dark, "CBA6F7", "1E1E2E", "A6E3A1", "F9E2AF", "F38BA8");

    [Fact]
    [Trait("Req", "PR-THEME-008")] // UI/Theme/AppTheme.swift:118
    public void Palette_of_catppuccinLatte_is_light_with_the_Mauve_row() =>
        AssertRow(AppTheme.CatppuccinLatte, ThemeVariant.Light, "8839EF", "EFF1F5", "40A02B", "DF8E1D", "D20F39");

    [Fact]
    [Trait("Req", "PR-THEME-009")] // UI/Theme/AppTheme.swift:119
    public void Palette_of_dracula_is_dark_with_the_Purple_row() =>
        AssertRow(AppTheme.Dracula, ThemeVariant.Dark, "BD93F9", "282A36", "50FA7B", "FFB86C", "FF5555");

    [Fact]
    [Trait("Req", "PR-THEME-010")] // UI/Theme/AppTheme.swift:120
    public void Palette_of_tokyoNight_is_dark_with_the_Blue_row() =>
        AssertRow(AppTheme.TokyoNight, ThemeVariant.Dark, "7AA2F7", "1A1B26", "9ECE6A", "E0AF68", "F7768E");

    [Fact]
    [Trait("Req", "PR-THEME-011")] // UI/Theme/AppTheme.swift:121 (shares accent 88C0D0 and detail with cursor)
    public void Palette_of_nord_is_dark_with_the_Frost_row() =>
        AssertRow(AppTheme.Nord, ThemeVariant.Dark, "88C0D0", "2E3440", "A3BE8C", "EBCB8B", "BF616A");

    [Fact]
    [Trait("Req", "PR-THEME-012")] // UI/Theme/AppTheme.swift:122
    public void Palette_of_gruvbox_is_dark_with_the_Orange_row() =>
        AssertRow(AppTheme.Gruvbox, ThemeVariant.Dark, "FE8019", "282828", "B8BB26", "FABD2E", "FB4934");

    [Fact]
    [Trait("Req", "PR-THEME-013")] // UI/Theme/AppTheme.swift:123
    public void Palette_of_gruvboxLight_is_light_with_the_Rust_row() =>
        AssertRow(AppTheme.GruvboxLight, ThemeVariant.Light, "AF3A03", "FBF1C7", "79740E", "B57614", "9D0006");

    [Fact]
    [Trait("Req", "PR-THEME-014")] // UI/Theme/AppTheme.swift:124
    public void Palette_of_oneDark_is_dark_with_the_Blue_row() =>
        AssertRow(AppTheme.OneDark, ThemeVariant.Dark, "61AFEF", "282C34", "98C379", "E5C07B", "E06C75");

    [Fact]
    [Trait("Req", "PR-THEME-015")] // UI/Theme/AppTheme.swift:125 (success equals accent, do not deduplicate)
    public void Palette_of_everforest_has_success_equal_to_its_own_accent() =>
        AssertRow(AppTheme.Everforest, ThemeVariant.Dark, "A7C080", "2D353B", "A7C080", "DBBC7F", "E67E80");

    [Fact]
    [Trait("Req", "PR-THEME-016")] // UI/Theme/AppTheme.swift:126
    public void Palette_of_kanagawa_is_dark_with_the_Wave_blue_row() =>
        AssertRow(AppTheme.Kanagawa, ThemeVariant.Dark, "7E9CD8", "1F1F28", "98BB6C", "D7A657", "E82424");

    [Fact]
    [Trait("Req", "PR-THEME-017")] // UI/Theme/AppTheme.swift:127 (danger equals accent, shipped that way)
    public void Palette_of_rosePine_has_danger_equal_to_its_own_accent() =>
        AssertRow(AppTheme.RosePine, ThemeVariant.Dark, "EB6F92", "191724", "9CCFD8", "F6C177", "EB6F92");

    [Fact]
    [Trait("Req", "PR-THEME-018")] // UI/Theme/AppTheme.swift:128
    public void Palette_of_solarizedDark_is_dark_with_the_Blue_row() =>
        AssertRow(AppTheme.SolarizedDark, ThemeVariant.Dark, "268BD2", "002B36", "859900", "B58900", "DC322F");

    [Fact]
    [Trait("Req", "PR-THEME-019")] // UI/Theme/AppTheme.swift:129 (only scheme and surface differ from solarizedDark)
    public void Palette_of_solarizedLight_differs_from_solarizedDark_only_in_scheme_and_surface()
    {
        var light = AppTheme.SolarizedLight.Palette();
        var dark = AppTheme.SolarizedDark.Palette();

        Assert.Equal(ThemeVariant.Light, light.Scheme);
        Assert.Equal("FDF6E3", light.Surface);
        Assert.Equal(dark.Accent, light.Accent);
        Assert.Equal(dark.Success, light.Success);
        Assert.Equal(dark.Warning, light.Warning);
        Assert.Equal(dark.Danger, light.Danger);
    }

    [Fact]
    [Trait("Req", "PR-THEME-020")] // UI/Theme/AppTheme.swift:130 (shares success/warning/danger with codex)
    public void Palette_of_githubDark_is_dark_with_the_Blue_row() =>
        AssertRow(AppTheme.GithubDark, ThemeVariant.Dark, "4493F8", "0D1117", "3FB950", "D29922", "F85149");

    [Fact]
    [Trait("Req", "PR-THEME-021")] // UI/Theme/AppTheme.swift:131 (surface equals the light theme's, but sets accent/status)
    public void Palette_of_githubLight_sets_an_accent_and_status_hues_despite_sharing_the_light_themes_surface()
    {
        var githubLight = AppTheme.GithubLight.Palette();
        var light = AppTheme.Light.Palette();

        Assert.Equal("FFFFFF", githubLight.Surface);
        Assert.Equal(light.Surface, githubLight.Surface);
        Assert.NotNull(githubLight.Accent);
        Assert.NotNull(githubLight.Success);
        AssertRow(AppTheme.GithubLight, ThemeVariant.Light, "0969DA", "FFFFFF", "1A7F37", "9A6700", "CF222E");
    }

    [Fact]
    [Trait("Req", "PR-THEME-022")] // UI/Theme/AppTheme.swift:132
    public void Palette_of_ayu_is_dark_with_the_Amber_row() =>
        AssertRow(AppTheme.Ayu, ThemeVariant.Dark, "E6B450", "0F1419", "AAD94C", "FFB454", "F58572");

    [Fact]
    [Trait("Req", "PR-THEME-023")] // UI/Theme/AppTheme.swift:133
    public void Palette_of_nightOwl_is_dark_with_the_Blue_row() =>
        AssertRow(AppTheme.NightOwl, ThemeVariant.Dark, "82AAFF", "011627", "C5E478", "ECC48D", "EF5350");

    [Fact]
    [Trait("Req", "PR-THEME-024")] // UI/Theme/AppTheme.swift:134 (danger equals accent, as in rosePine)
    public void Palette_of_monokai_has_danger_equal_to_its_own_accent() =>
        AssertRow(AppTheme.Monokai, ThemeVariant.Dark, "F92672", "272822", "A6E22E", "FD971F", "F92672");

    private static void AssertRow(
        AppTheme theme, ThemeVariant scheme, string accent, string surface, string success, string warning, string danger)
    {
        var palette = theme.Palette();

        Assert.Equal(scheme, palette.Scheme);
        Assert.Equal(accent, palette.Accent);
        Assert.Equal(surface, palette.Surface);
        Assert.Equal(success, palette.Success);
        Assert.Equal(warning, palette.Warning);
        Assert.Equal(danger, palette.Danger);
    }
}
