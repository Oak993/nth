using System.Text.Json;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Port;

// OD-0088 (ED-250): the 22 accents of the locked palette (tests/fixtures/accent/accent-palette-locked.json, a copy of
// E:/Droppy/work/design/accent-palette-locked.json), the lift of SPEC 3.2 and the resolution of SPEC 4.2. Every expected
// hex below is transcribed from E:/Droppy/work/design/accent-build/SPEC.md, not computed by the code under test.
public sealed class AccentPaletteTests
{
    private static string Fixture(params string[] parts)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine([folder.FullName, "tests", "fixtures", .. parts]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }

    private static Color C(string hex) => Color.Parse(hex);

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_palette_is_the_locked_files_22_in_order_with_its_names_and_its_dark_and_light_hexes()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Fixture("accent", "accent-palette-locked.json")));
        var locked = document.RootElement.GetProperty("accents").EnumerateArray().Select(a => (
            Id: a.GetProperty("id").GetString() == "win" ? "windows" : a.GetProperty("id").GetString()!,
            Name: a.GetProperty("name").GetString()!,
            Signature: a.GetProperty("group").GetString() == "signature",
            Dark: a.GetProperty("dark").GetProperty("hex").GetString()!,
            Light: a.GetProperty("light").GetProperty("hex").GetString()!)).ToArray();
        Assert.Equal(22, locked.Length);
        Assert.Equal(locked, AccentPalette.All.Select(a => (a.Id, a.Name, a.IsSignature, a.Dark, a.Light)).ToArray());
        Assert.Equal(10, AccentPalette.All.Count(a => !a.IsSignature));
        Assert.Equal(AccentPalette.ClassicCount, AccentPalette.All.TakeWhile(a => !a.IsSignature).Count());
        Assert.Equal(["Windows accent", "Blue", "Purple", "Pink", "Red", "Orange", "Yellow", "Green", "Teal", "Graphite"], AccentPalette.All.Take(10).Select(a => a.Name));
        Assert.Equal(["Ocean", "Denim", "Indigo", "Lilac", "Orchid", "Rose", "Coral", "Bronze", "Sage", "Alpine", "Mint", "Slate"], AccentPalette.All.Skip(10).Select(a => a.Name));
    }

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Every_palette_hex_already_reads_so_the_lift_keeps_it_and_the_text_on_it_is_dark_in_dark_and_white_in_light()
    {
        foreach (var swatch in AccentPalette.All)
        {
            Assert.Equal(swatch.Dark, Hex(AccentPalette.Lift(C(swatch.Dark), dark: true)));
            Assert.Equal(swatch.Light, Hex(AccentPalette.Lift(C(swatch.Light), dark: false)));
            Assert.True(AccentPalette.Contrast(C(swatch.Dark), C("#4D4C4B")) >= 3.1 && AccentPalette.Contrast(C(swatch.Dark), C("#1E1E1E")) >= 3, swatch.Name);
            Assert.True(AccentPalette.Contrast(C(swatch.Light), C("#D1CFCF")) >= 3.1 && AccentPalette.Contrast(C(swatch.Light), Colors.White) >= 3, swatch.Name);
            // SPEC 2.2 --aon: in dark all 22 give #111111, in light all 22 give #FFFFFF.
            Assert.Equal("#111111", Hex(AccentPalette.OnColor(C(swatch.Dark))));
            Assert.Equal("#FFFFFF", Hex(AccentPalette.OnColor(C(swatch.Light))));
        }
    }

    [Theory]
    [InlineData("#0067C0", "#4B9FFC", "#0067C0")]
    [InlineData("#0078D7", "#409FFF", "#0073CF")]
    [InlineData("#000000", "#9C9C9C", "#000000")]
    [InlineData("#FFB900", "#FFB900", "#956A00")]
    [InlineData("#E81123", "#FF6D63", "#E4051F")]
    [InlineData("#767676", "#9C9C9C", "#727272")]
    [Trait("Decision", "ED-250")]
    public void The_windows_accent_is_lifted_as_the_spec_tabulates(string accent, string dark, string light)
    {
        Assert.Equal(dark, Hex(AccentPalette.Lift(C(accent), dark: true)));
        Assert.Equal(light, Hex(AccentPalette.Lift(C(accent), dark: false)));
        // Read through the system accent, as every accent use resolves it (SwiftColor, ED-238 amended).
        Assert.Equal(dark, Hex(SwiftColor.ControlAccentColor.Resolve(ThemeVariant.Dark, () => C(accent))));
        Assert.Equal(light, Hex(SwiftColor.ControlAccentColor.Resolve(ThemeVariant.Light, () => C(accent))));
    }

    [Theory]
    [InlineData("CatppuccinMocha", "#CBA6F7", "Mauve")]
    [InlineData("CatppuccinLatte", "#8839EF", "Mauve")]
    [InlineData("Dracula", "#BD93F9", "Purple")]
    [InlineData("TokyoNight", "#7AA2F7", "Blue")]
    [InlineData("Nord", "#88C0D0", "Frost")]
    [InlineData("Gruvbox", "#FE8019", "Orange")]
    [InlineData("GruvboxLight", "#AF3A03", "Rust")]
    [InlineData("OneDark", "#61AFEF", "Blue")]
    [InlineData("Everforest", "#A7C080", "Green")]
    [InlineData("Kanagawa", "#7E9CD8", "Wave blue")]
    [InlineData("RosePine", "#F07496", "Rose")]
    [InlineData("SolarizedDark", "#42A2EB", "Blue")]
    [InlineData("SolarizedLight", "#0077BC", "Blue")]
    [InlineData("GithubDark", "#519DFF", "Blue")]
    [InlineData("GithubLight", "#0969DA", "Blue")]
    [InlineData("Ayu", "#E6B450", "Amber")]
    [InlineData("NightOwl", "#82AAFF", "Blue")]
    [InlineData("Monokai", "#FF688E", "Pink")]
    [InlineData("Claude", "#E6805C", "Terracotta")]
    [InlineData("ClaudeLight", "#B65532", "Terracotta")]
    [InlineData("Codex", "#EA60FF", "Magenta")]
    [InlineData("Cursor", "#88C0D0", "Frost")]
    [InlineData("Matrix", "#00E676", "Green")]
    [Trait("Decision", "ED-250")]
    public void A_named_themes_own_accent_lifts_for_its_first_swatch_as_the_spec_tabulates_and_has_its_word(string name, string firstSwatch, string word)
    {
        var theme = Enum.Parse<AppTheme>(name);
        var own = theme.Spec().Accent!.Resolve(ThemeVariant.Light);
        var dark = theme.Scheme() == ThemeVariant.Dark;
        Assert.Equal(firstSwatch, Hex(AccentPalette.Lift(own, dark)));
        Assert.Equal(word, AccentPalette.OwnWord(theme));
        // SPEC 2.2: every dark named accent gives #111111; in light, Solarized Light and Claude Light give #111111.
        var expectedOn = dark || theme is AppTheme.SolarizedLight or AppTheme.ClaudeLight ? "#111111" : "#FFFFFF";
        Assert.Equal(expectedOn, Hex(AccentPalette.OnColor(own)));
    }

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_base_themes_have_no_word_of_their_own()
    {
        foreach (var theme in new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark }) Assert.Null(AccentPalette.OwnWord(theme));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("win")]
    [InlineData("Teal")]
    [InlineData("notAColour")]
    [Trait("Decision", "ED-250")]
    public void A_missing_or_unknown_id_reads_as_the_windows_accent(string? id)
    {
        Assert.Equal("windows", AccentPalette.Normalize(id));
        Assert.Null(AccentPalette.Pick(id));
    }

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_effective_accent_resolves_as_the_spec_tabulates_for_each_theme_and_pick()
    {
        Color Dark(SwiftColor c) => c.Resolve(ThemeVariant.Dark, () => C("#0067C0"));
        Color Light(SwiftColor c) => c.Resolve(ThemeVariant.Light, () => C("#0067C0"));
        // System, Light, Dark with windows: the lifted system accent.
        foreach (var theme in new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark })
        {
            Assert.Equal("#4B9FFC", Hex(Dark(AccentPalette.Effective("windows", theme))));
            Assert.Equal("#0067C0", Hex(Light(AccentPalette.Effective("windows", theme))));
            Assert.Equal("#40C8E0", Hex(Dark(AccentPalette.Effective("teal", theme))));
            Assert.Equal("#007D91", Hex(Light(AccentPalette.Effective("teal", theme))));
        }
        // A named theme with windows: its own accent raw (not lifted): Claude stays #C15F3C.
        Assert.Equal("#C15F3C", Hex(Dark(AccentPalette.Effective("windows", AppTheme.Claude))));
        Assert.Equal("#BD93F9", Hex(Dark(AccentPalette.Effective("windows", AppTheme.Dracula))));
        // A named theme with a pick: the palette hex of the appearance.
        Assert.Equal("#E298C6", Hex(Dark(AccentPalette.Effective("orchid", AppTheme.Dracula))));
        Assert.Equal("#733E67", Hex(Light(AccentPalette.Effective("orchid", AppTheme.CatppuccinLatte))));
        // One instance per pick.
        Assert.Same(AccentPalette.Pick("orchid"), AccentPalette.Pick("orchid"));
    }
}
