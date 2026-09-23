using System.Reflection;
using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalFont.swift (B2 1fbf81e), :1-139. Decision ED-175.
//
// A test never depends on the fonts installed on the machine it runs on, or on the real home folder (ED-175 (2)):
// SwiftFont.InstalledFontFamilyNames is scripted per test, and every Ghostty config is written under a
// ScratchHome (MARROW_PORT_DATA_DIR), passed as the internal home-taking overloads' seam. No real Nerd Font
// ships with the app or is guaranteed installed, so a scenario needing a configured font to ALREADY cover the
// four Nerd probe glyphs (preferred(), :10) cannot be built deterministically; that branch is exercised only at
// the mechanism level (SwiftFontTests.cs, Covers, Decision ED-175), and noted untested here for that reason.
[Collection("Headless")]
public sealed class TerminalFontTests(HeadlessSession headless)
{
    private static object? InvokePrivate(string name, params object?[] args) =>
        typeof(TerminalFont).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    private static void WriteGhosttyConfig(string home, int pathIndex, string content)
    {
        var path = TerminalFont.GhosttyConfigPaths(home)[pathIndex];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
    }

    // ---- PR-TERM-128: the two Ghostty config paths, in order ----

    [Fact]
    [Trait("Req", "PR-TERM-128")] // App/Views/Terminal/TerminalFont.swift:60-66
    public void GhosttyConfigPaths_is_Application_Support_then_the_XDG_location_under_home()
    {
        var home = Path.Combine("C:", "Users", "someone");
        var paths = TerminalFont.GhosttyConfigPaths(home);
        Assert.Equal(2, paths.Count);
        Assert.Equal(Path.Combine(home, "Library", "Application Support", "com.mitchellh.ghostty", "config"), paths[0]);
        Assert.Equal(Path.Combine(home, ".config", "ghostty", "config"), paths[1]);
    }

    [Fact]
    [Trait("Req", "PR-TERM-128")] // App/Views/Terminal/TerminalFont.swift:60-66
    public void GhosttyFont_returns_nothing_when_neither_config_file_exists()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Consolas" });
        Assert.Null(TerminalFont.GhosttyFont(home.Path));
    }

    [Fact]
    [Trait("Req", "PR-TERM-128")] // App/Views/Terminal/TerminalFont.swift:60-66
    public void GhosttyFont_tries_Application_Support_before_the_XDG_location()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Consolas", "Fira Code" });
        // Both files exist; the Application Support one wins (it is tried first).
        WriteGhosttyConfig(home.Path, 0, "font-family = Consolas\nfont-size = 11\n");
        WriteGhosttyConfig(home.Path, 1, "font-family = Fira Code\nfont-size = 20\n");
        var font = TerminalFont.GhosttyFont(home.Path);
        Assert.NotNull(font);
        Assert.Equal("Consolas", font!.FamilyName);
        Assert.Equal(11.0, font.PointSize);
    }

    [Fact]
    [Trait("Req", "PR-TERM-128")] // App/Views/Terminal/TerminalFont.swift:60-66
    public void GhosttyFont_falls_to_the_XDG_location_when_Application_Support_does_not_resolve()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        // Application Support names a font that is not installed; the XDG file's font is used instead.
        WriteGhosttyConfig(home.Path, 0, "font-family = Nowhere To Be Found\n");
        WriteGhosttyConfig(home.Path, 1, "font-family = Fira Code\nfont-size = 15\n");
        var font = TerminalFont.GhosttyFont(home.Path);
        Assert.NotNull(font);
        Assert.Equal("Fira Code", font!.FamilyName);
        Assert.Equal(15.0, font.PointSize);
    }

    // ---- PR-TERM-127: the font-family / font-size parser ----

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:33-58
    public void GhosttyFont_reads_font_family_and_font_size_from_the_first_resolving_file()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        WriteGhosttyConfig(home.Path, 0, "font-family = Fira Code\nfont-size = 14.5\n");
        var font = TerminalFont.GhosttyFont(home.Path);
        Assert.NotNull(font);
        Assert.Equal("Fira Code", font!.FamilyName);
        Assert.Equal(14.5, font.PointSize);
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:38, :50
    public void GhosttyFont_defaults_the_size_to_12_when_no_font_size_line_is_present()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        WriteGhosttyConfig(home.Path, 0, "font-family = Fira Code\n");
        Assert.Equal(12.0, TerminalFont.GhosttyFont(home.Path)!.PointSize);
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:41, :51-52
    public void GhosttyFont_skips_comment_lines_and_lines_without_an_equals_sign()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        WriteGhosttyConfig(home.Path, 0, "# font-family = Wrong Font\nthis line has no equals\nfont-family = Fira Code\nfont-size = 13\n");
        var font = TerminalFont.GhosttyFont(home.Path);
        Assert.NotNull(font);
        Assert.Equal("Fira Code", font!.FamilyName);
        Assert.Equal(13.0, font.PointSize);
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:45, :49-50
    public void GhosttyFont_keeps_only_the_first_font_family_line_and_the_last_valid_font_size_line()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code", "Consolas" });
        WriteGhosttyConfig(home.Path, 0,
            "font-family = Fira Code\nfont-family = Consolas\nfont-size = 11\nfont-size = 16\n");
        var font = TerminalFont.GhosttyFont(home.Path);
        Assert.NotNull(font);
        Assert.Equal("Fira Code", font!.FamilyName); // the first font-family line, Consolas is ignored
        Assert.Equal(16.0, font.PointSize); // the last valid font-size line
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:45, :48 (failure case: an empty value)
    public void GhosttyFont_yields_nothing_from_a_file_whose_font_family_value_is_empty()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        // The empty font-family sets the name to "", blocking every later font-family line in this file too.
        WriteGhosttyConfig(home.Path, 0, "font-family = \nfont-family = Fira Code\n");
        Assert.Null(TerminalFont.GhosttyFont(home.Path));
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:50 (failure case: a non-numeric size)
    public void GhosttyFont_ignores_a_font_size_that_does_not_parse_as_a_plain_number()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        WriteGhosttyConfig(home.Path, 0, "font-family = Fira Code\nfont-size = 13pt\n");
        Assert.Equal(12.0, TerminalFont.GhosttyFont(home.Path)!.PointSize); // the ED-038 default, unchanged
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:50 (failure case: zero and negative sizes)
    public void GhosttyFont_ignores_a_font_size_that_is_zero_or_negative()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        WriteGhosttyConfig(home.Path, 0, "font-family = Fira Code\nfont-size = 0\n");
        Assert.Equal(12.0, TerminalFont.GhosttyFont(home.Path)!.PointSize);

        using var home2 = new ScratchHome();
        WriteGhosttyConfig(home2.Path, 0, "font-family = Fira Code\nfont-size = -4\n");
        Assert.Equal(12.0, TerminalFont.GhosttyFont(home2.Path)!.PointSize);
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:50 (failure case: a kept inline comment)
    public void GhosttyFont_keeps_a_trailing_comment_as_part_of_the_value_so_it_fails_to_parse()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        WriteGhosttyConfig(home.Path, 0, "font-family = Fira Code\nfont-size = 14 # comment\n");
        // "14 # comment" is not a plain number, so it is ignored and the default of 12 is kept.
        Assert.Equal(12.0, TerminalFont.GhosttyFont(home.Path)!.PointSize);
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:46-48
    public void GhosttyFont_takes_the_first_comma_separated_family_and_trims_spaces_and_quotes()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        WriteGhosttyConfig(home.Path, 0, "font-family = \" Fira Code \" , Consolas\n");
        Assert.Equal("Fira Code", TerminalFont.GhosttyFont(home.Path)!.FamilyName);
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:40, :42-43
    public void GhosttyFont_trims_whitespace_around_the_whole_line_and_around_the_key_and_value()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        WriteGhosttyConfig(home.Path, 0, "   font-family   =   Fira Code   \n\tfont-size\t=\t17\t\n");
        var font = TerminalFont.GhosttyFont(home.Path);
        Assert.NotNull(font);
        Assert.Equal("Fira Code", font!.FamilyName);
        Assert.Equal(17.0, font.PointSize);
    }

    [Fact]
    [Trait("Req", "PR-TERM-127")] // App/Views/Terminal/TerminalFont.swift:55 (a name that never resolves)
    public void GhosttyFont_yields_nothing_when_the_configured_family_never_resolves()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(Array.Empty<string>());
        WriteGhosttyConfig(home.Path, 0, "font-family = Nothing Installed\n");
        Assert.Null(TerminalFont.GhosttyFont(home.Path));
    }

    // ---- PR-TERM-129, PR-TERM-130: the Terminal.app and iTerm2 readers are inert on Windows (ED-175 (2)) ----

    [Fact]
    [Trait("Req", "PR-TERM-129")] // App/Views/Terminal/TerminalFont.swift:68-76
    public void The_Terminal_app_reader_is_inert_on_Windows()
    {
        Assert.Null(InvokePrivate("AppleTerminalFont"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-130")] // App/Views/Terminal/TerminalFont.swift:78-90
    public void The_iTerm2_reader_is_inert_on_Windows()
    {
        Assert.Null(InvokePrivate("ITermFont"));
    }

    // ---- PR-TERM-131: the four Nerd Font probe glyphs, through SwiftFont.Covers (Decision ED-175) ----

    [Fact]
    [Trait("Req", "PR-TERM-131")] // App/Views/Terminal/TerminalFont.swift:94-101
    public void HasNerdGlyphs_is_false_for_the_bundled_plain_Cascadia_Mono_which_lacks_every_probe() => headless.OnUiThread(() =>
    {
        // The bundled file is the plain Microsoft release (CASCADIA.md), not a Nerd Font patched variant: this is
        // a real, deterministic false (SwiftFontTests.cs already proves each probe individually under ED-175).
        var cascadiaMono = SwiftFont.MonospacedSystemFont(ofSize: 12, weight: SwiftFont.Weight.Regular);
        Assert.False((bool)InvokePrivate("HasNerdGlyphs", cascadiaMono)!);
    });

    // ---- PR-TERM-132: nerdSibling(of:) ----

    [Fact]
    [Trait("Req", "PR-TERM-132")] // App/Views/Terminal/TerminalFont.swift:103-112
    public void NerdSibling_is_null_when_the_font_has_no_family_name()
    {
        // guard let family = font.familyName else { return nil } (:106): unreachable from preferred() on this
        // port (every configured font comes from Named, which always sets FamilyName), tested directly.
        Assert.Null(InvokePrivate("NerdSibling", SwiftFont.Body));
    }

    [Fact]
    [Trait("Req", "PR-TERM-132")] // App/Views/Terminal/TerminalFont.swift:103-112
    public void NerdSibling_is_null_when_no_installed_family_normalizes_to_family_plus_nf()
    {
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        var font = SwiftFont.Named("Fira Code", 12)!;
        Assert.Null(InvokePrivate("NerdSibling", font));
    }

    [Fact]
    [Trait("Req", "PR-TERM-132")] // App/Views/Terminal/TerminalFont.swift:103-112
    public void NerdSibling_matches_the_installed_family_whose_normalized_name_is_family_plus_nf()
    {
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code", "FiraCode Nerd Font" });
        var font = SwiftFont.Named("Fira Code", 13)!;
        var sibling = (SwiftFont?)InvokePrivate("NerdSibling", font);
        Assert.NotNull(sibling);
        Assert.Equal("FiraCode Nerd Font", sibling!.FamilyName);
        Assert.Equal(13.0, sibling.PointSize); // the configured font's own size, not a default
    }

    // ---- PR-TERM-133: firstInstalledNerdFont(size:) ----

    [Fact]
    [Trait("Req", "PR-TERM-133")] // App/Views/Terminal/TerminalFont.swift:114-119
    public void FirstInstalledNerdFont_is_null_when_no_family_name_contains_nerd_font()
    {
        // "NF" alone, without the words "nerd font", is not found (the packet's documented failure case).
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code", "Fira Code NF" });
        Assert.Null(InvokePrivate("FirstInstalledNerdFont", 12.0));
    }

    [Fact]
    [Trait("Req", "PR-TERM-133")] // App/Views/Terminal/TerminalFont.swift:114-119
    public void FirstInstalledNerdFont_finds_a_name_containing_nerd_font_in_any_letter_case()
    {
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Zulu NERD font Mono", "Alpha nerd FONT Mono" });
        var font = (SwiftFont?)InvokePrivate("FirstInstalledNerdFont", 12.0);
        Assert.NotNull(font);
        // AvailableFontFamilies sorts ordinal case-insensitively (Oracle O-61); "Alpha..." sorts before "Zulu...".
        Assert.Equal("Alpha nerd FONT Mono", font!.FamilyName);
        Assert.Equal(12.0, font.PointSize);
    }

    // ---- PR-TERM-134: resolve(_:size:) ----

    [Fact]
    [Trait("Req", "PR-TERM-134")] // App/Views/Terminal/TerminalFont.swift:121-130
    public void Resolve_matches_the_exact_installed_name_before_trying_the_normalized_family_search()
    {
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Geist Mono", "Geist-Mono" });
        var font = (SwiftFont?)InvokePrivate("Resolve", "Geist Mono", 12.0);
        Assert.NotNull(font);
        Assert.Equal("Geist Mono", font!.FamilyName); // the exact match, not the ambiguous normalized one
    }

    [Fact]
    [Trait("Req", "PR-TERM-134")] // App/Views/Terminal/TerminalFont.swift:121-130 ("Geist Mono NF" -> "GeistMono Nerd Font")
    public void Resolve_falls_to_the_normalized_family_match_when_the_exact_name_does_not_resolve()
    {
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "GeistMono Nerd Font" });
        var font = (SwiftFont?)InvokePrivate("Resolve", "Geist Mono NF", 12.0);
        Assert.NotNull(font);
        Assert.Equal("GeistMono Nerd Font", font!.FamilyName);
    }

    [Fact]
    [Trait("Req", "PR-TERM-134")] // App/Views/Terminal/TerminalFont.swift:121-130
    public void Resolve_is_null_when_neither_step_matches()
    {
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Consolas" });
        Assert.Null(InvokePrivate("Resolve", "Nothing Like It", 12.0));
    }

    // ---- PR-TERM-125, PR-TERM-126: preferred() and configuredFonts() end to end ----

    [Fact]
    [Trait("Req", "PR-TERM-125")] // App/Views/Terminal/TerminalFont.swift:7-16
    [Trait("Req", "PR-TERM-126")] // App/Views/Terminal/TerminalFont.swift:18-29 (no config: only Ghostty could contribute)
    public void Preferred_with_no_configured_font_and_no_installed_Nerd_Font_is_the_fallback_size_monospaced_font() => headless.OnUiThread(() =>
    {
        using var home = new ScratchHome(); // no Ghostty config files at all
        using var families = SwiftFont.InstalledFontFamilyNames.Override(Array.Empty<string>());
        var font = TerminalFont.Preferred(12, home.Path);
        Assert.Null(font.FamilyName); // the design-based fallback, not a Named() font
        var resolved = font.Resolve();
        Assert.Equal(12.0, resolved.FontSize);
        Assert.Equal(400, (int)resolved.FontWeight); // .regular
        Assert.Equal("Cascadia Mono", resolved.FontFamily.Name); // OD-0009
    });

    [Fact]
    [Trait("Req", "PR-TERM-125")] // App/Views/Terminal/TerminalFont.swift:14
    public void Preferred_uses_the_given_fallbackSize_when_nothing_is_configured()
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(Array.Empty<string>());
        headless.OnUiThread(() => Assert.Equal(16.0, TerminalFont.Preferred(16, home.Path).Resolve().FontSize));
    }

    [Fact]
    [Trait("Req", "PR-TERM-125")] // App/Views/Terminal/TerminalFont.swift:14-15
    public void Preferred_with_no_configured_font_but_an_installed_Nerd_Font_returns_it_at_the_fallback_size() => headless.OnUiThread(() =>
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Example Nerd Font" });
        var font = TerminalFont.Preferred(12, home.Path);
        Assert.Equal("Example Nerd Font", font.FamilyName);
        Assert.Equal(12.0, font.PointSize);
    });

    [Fact]
    [Trait("Req", "PR-TERM-125")] // App/Views/Terminal/TerminalFont.swift:12
    [Trait("Req", "PR-TERM-126")] // App/Views/Terminal/TerminalFont.swift:18-29 (the Ghostty reader contributed)
    public void Preferred_returns_the_configured_font_unchanged_when_it_has_no_sibling_and_none_is_installed() => headless.OnUiThread(() =>
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code" });
        WriteGhosttyConfig(home.Path, 0, "font-family = Fira Code\nfont-size = 14\n");
        var font = TerminalFont.Preferred(12, home.Path);
        Assert.Equal("Fira Code", font.FamilyName);
        Assert.Equal(14.0, font.PointSize);
    });

    [Fact]
    [Trait("Req", "PR-TERM-125")] // App/Views/Terminal/TerminalFont.swift:12
    public void Preferred_prefers_the_configured_fonts_Nerd_sibling_over_any_other_installed_Nerd_Font() => headless.OnUiThread(() =>
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(
            new[] { "Fira Code", "FiraCode Nerd Font", "Unrelated Nerd Font" });
        WriteGhosttyConfig(home.Path, 0, "font-family = Fira Code\nfont-size = 18\n");
        var font = TerminalFont.Preferred(12, home.Path);
        Assert.Equal("FiraCode Nerd Font", font.FamilyName);
        Assert.Equal(18.0, font.PointSize); // the configured font's own size (nerdSibling), not the fallback size
    });

    [Fact]
    [Trait("Req", "PR-TERM-125")] // App/Views/Terminal/TerminalFont.swift:12
    public void Preferred_falls_to_any_installed_Nerd_Font_when_the_configured_font_has_no_sibling() => headless.OnUiThread(() =>
    {
        using var home = new ScratchHome();
        using var families = SwiftFont.InstalledFontFamilyNames.Override(new[] { "Fira Code", "Some Nerd Font" });
        WriteGhosttyConfig(home.Path, 0, "font-family = Fira Code\nfont-size = 18\n");
        var font = TerminalFont.Preferred(12, home.Path);
        Assert.Equal("Some Nerd Font", font.FamilyName);
        Assert.Equal(18.0, font.PointSize); // firstInstalledNerdFont(size: first.pointSize), not the fallback size
    });
}
