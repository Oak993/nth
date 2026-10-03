using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PORT-ONLY tests for the foundation unit F.SwiftColor (ED-092 part 1, ED-051, ED-062; R-VIEW-11, R-STR-7, R-NUM-1).
// SwiftColor stands in for SwiftUI Color and AppKit NSColor. Expected values are transcribed from the pinned Swift
// (B2 1fbf81e, cited per test) and from the recorded decisions, never from running the port.
//
// Byte convention: Resolve gives an Avalonia Color (8 bits per channel). Every expected channel below is
// component * 255 rounded to the nearest integer, and every case is chosen so that no product lands on a .5 tie,
// so the expectation does not depend on a midpoint rule. Where a value is written as a product it is spelled out
// in the comment beside it.
//
// The API these tests fix (the implementation must provide exactly this surface):
//   new SwiftColor(red:, green:, blue:, opacity: 1)   Color(red:green:blue:), Color(.sRGB, ...), NSColor(srgbRed:...)
//   new SwiftColor(white:, opacity: 1)                Color(white:), Color(white:opacity:), NSColor(white:alpha:)
//   SwiftColor.Rgb(uint hex)                          Core/Models/MCP.swift:255-263 Color(rgb:)
//   SwiftColor.Hex(string hex)                        UI/Theme/AppTheme.swift:236-248 Color(hex:)
//   SwiftColor.Dynamic(light, dark)                   NSColor(name:dynamicProvider:) as an appearance pair
//   SwiftColor.Clear, White, Black, Primary, Secondary, AccentColor, Yellow, LabelColor, PlaceholderTextColor,
//   ControlAccentColor
//   SwiftColor Opacity(double)                        Color.opacity(_:)
//   Avalonia.Media.Color Resolve(ThemeVariant)        the colour for a light or dark appearance
//   IBrush Brush(Control)                             a solid brush that follows the control's ActualThemeVariant
[Collection("Headless")]
public sealed class SwiftColorTests(HeadlessSession headless)
{
    private static readonly ThemeVariant Light = ThemeVariant.Light;
    private static readonly ThemeVariant Dark = ThemeVariant.Dark;

    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    private static Window Show(Control content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // ---- Fixed sRGB colours ----

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-VIEW-11")]
    public void Color_red_green_blue_is_opaque_sRGB_and_the_same_in_light_and_dark()
    {
        // App/Views/Composer/EffortSlider.swift:876: Color(red: 0.93, green: 0.58, blue: 0.44)
        // 0.93*255 = 237.15, 0.58*255 = 147.9, 0.44*255 = 112.2
        var claude = new SwiftColor(red: 0.93, green: 0.58, blue: 0.44);
        Assert.Equal(Argb(255, 237, 148, 112), claude.Resolve(Light));
        Assert.Equal(Argb(255, 237, 148, 112), claude.Resolve(Dark));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Color_red_green_blue_from_byte_fractions_lands_on_the_exact_bytes()
    {
        // App/Views/Settings/AboutUpdateViews.swift:391: Color(red: 0x67 / 255, green: 0xB9 / 255, blue: 0xEE / 255)
        var color = new SwiftColor(red: 0x67 / 255.0, green: 0xB9 / 255.0, blue: 0xEE / 255.0);
        Assert.Equal(Argb(255, 0x67, 0xB9, 0xEE), color.Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Color_sRGB_with_an_opacity_keeps_that_alpha()
    {
        // UI/Theme/AppTheme.swift:185: NSColor(srgbRed: 0x1E / 255.0, green: 0x1E / 255.0, blue: 0x20 / 255.0, alpha: 1.0)
        var surface = new SwiftColor(red: 0x1E / 255.0, green: 0x1E / 255.0, blue: 0x20 / 255.0, opacity: 1.0);
        Assert.Equal(Argb(255, 0x1E, 0x1E, 0x20), surface.Resolve(Dark));
        // 0.4*255 = 102
        var translucent = new SwiftColor(red: 1, green: 0, blue: 0, opacity: 0.4);
        Assert.Equal(Argb(102, 255, 0, 0), translucent.Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Color_white_is_an_opaque_grey_with_all_three_channels_equal()
    {
        // App/Views/Terminal/TerminalPanel.swift:107: Color(white: 0.985) light, Color(white: 0.08) dark
        // 0.985*255 = 251.175, 0.08*255 = 20.4
        Assert.Equal(Argb(255, 251, 251, 251), new SwiftColor(white: 0.985).Resolve(Light));
        Assert.Equal(Argb(255, 20, 20, 20), new SwiftColor(white: 0.08).Resolve(Dark));
        // App/Views/Settings/ShortcutsSettingsPage.swift:353: Color(white: 0.07), 0.07*255 = 17.85
        Assert.Equal(Argb(255, 18, 18, 18), new SwiftColor(white: 0.07).Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-106")]
    public void NSColor_white_alpha_is_a_grey_with_that_alpha()
    {
        // ED-106: NSColor(white:alpha:) is an sRGB grey with every channel equal to white, served by the same
        // constructor as Color(white:) and Color(white:opacity:); there is no gamma 2.2 factory. The binding spec
        // record PR-TERM-045 says the terminal background "matches the panel's own fill exactly", and the panel fill
        // is the sRGB Color(white:) at App/Views/Terminal/TerminalPanel.swift:107. UNVERIFIED under OD-0001, no O-nn.
        // App/Views/Terminal/TerminalStore.swift:51: NSColor(white: 0.12, alpha: 1) in the light branch (isDark false),
        // 0.12*255 = 30.6
        Assert.Equal(Argb(255, 31, 31, 31), new SwiftColor(white: 0.12, opacity: 1).Resolve(Light));
        // App/Views/Terminal/TerminalStore.swift:50: NSColor(white: 0.08, alpha: 1) dark, NSColor(white: 0.985, alpha: 1)
        // light. 0.08*255 = 20.4, 0.985*255 = 251.175
        Assert.Equal(Argb(255, 20, 20, 20), new SwiftColor(white: 0.08, opacity: 1).Resolve(Dark));
        Assert.Equal(Argb(255, 251, 251, 251), new SwiftColor(white: 0.985, opacity: 1).Resolve(Light));
        // 0.2*255 = 51, 0.4*255 = 102
        Assert.Equal(Argb(102, 51, 51, 51), new SwiftColor(white: 0.2, opacity: 0.4).Resolve(Light));
    }

    // ---- The Core hex extension (Core/Models/MCP.swift:255-263) ----

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Color_rgb_takes_the_low_24_bits_as_red_green_blue_and_is_opaque()
    {
        // Core/Models/MCPCatalog.swift:12, :29, :44: colorValue 0x24292F, 0xFC6D26, 0x2EAD33
        Assert.Equal(Argb(255, 0x24, 0x29, 0x2F), SwiftColor.Rgb(0x24292F).Resolve(Light));
        Assert.Equal(Argb(255, 0xFC, 0x6D, 0x26), SwiftColor.Rgb(0xFC6D26).Resolve(Dark));
        Assert.Equal(Argb(255, 0x2E, 0xAD, 0x33), SwiftColor.Rgb(0x2EAD33).Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Color_rgb_ignores_the_top_byte_and_never_reads_it_as_alpha()
    {
        // MCP.swift:258-260 mask each channel with & 0xFF after the shift; the init passes no opacity (opaque).
        Assert.Equal(Argb(255, 0x12, 0x34, 0x56), SwiftColor.Rgb(0x80123456).Resolve(Light));
        Assert.Equal(Argb(255, 0, 0, 0), SwiftColor.Rgb(0xFF000000).Resolve(Light));
    }

    // ---- Color(hex:) (UI/Theme/AppTheme.swift:236-248) ----

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-NUM-1")]
    public void Color_hex_reads_six_digits_with_or_without_a_leading_hash()
    {
        // AppTheme.swift:237 documents "CBA6F7" or "#CBA6F7" in sRGB; :240 drops one leading #.
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("CBA6F7").Resolve(Light));
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("#CBA6F7").Resolve(Dark));
        // AppTheme.swift:157-158: the System swatch halves
        Assert.Equal(Argb(255, 0xFF, 0xFF, 0xFF), SwiftColor.Hex("FFFFFF").Resolve(Light));
        Assert.Equal(Argb(255, 0x1E, 0x1E, 0x20), SwiftColor.Hex("1E1E20").Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-STR-7")]
    [Trait("Rule", "R-NUM-1")]
    public void Color_hex_trims_whitespace_and_newlines_before_looking_for_the_hash()
    {
        // AppTheme.swift:239 hex.trimmed, Core/Support/Text.swift:132 trimmingCharacters(in: .whitespacesAndNewlines)
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("  #CBA6F7\n").Resolve(Light));
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("\tCBA6F7\r\n").Resolve(Light));
        // R-STR-7: Foundation's whitespace includes U+200B, so the trim removes it and the hash is then dropped.
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("\u200B#CBA6F7\n").Resolve(Light));
        // Negative control (R-TEST-4): String.Trim() keeps U+200B, so a port that used it would not find the hash
        // first, the scanner would stop at the hash, and the colour would be black.
        Assert.Equal("\u200B#CBA6F7", "\u200B#CBA6F7\n".Trim());
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-NUM-1")]
    public void Color_hex_scans_like_Foundation_Scanner()
    {
        // AppTheme.swift:242 Scanner(string: text).scanHexInt64(&value); Core/Port SwiftScanner.ScanHexInt64 documents
        // the rules: leading whitespace skipped, optional 0x, digits up to the first non-hex character.
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("# CBA6F7").Resolve(Light));
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("0xCBA6F7").Resolve(Light));
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("cba6f7").Resolve(Light));
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("CBA6F7zz").Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-NUM-1")]
    public void Color_hex_has_no_shorthand_and_no_alpha_channel()
    {
        // AppTheme.swift:243-246: the value's low 24 bits are red, green, blue; opacity is always 1.
        // "CBA" is the number 0xCBA, so red 0x00, green 0x0C, blue 0xBA.
        Assert.Equal(Argb(255, 0x00, 0x0C, 0xBA), SwiftColor.Hex("CBA").Resolve(Light));
        // Eight digits: the top byte is shifted out of every channel, not read as alpha.
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("80CBA6F7").Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-NUM-1")]
    public void Color_hex_that_scans_no_digit_is_opaque_black()
    {
        // AppTheme.swift:241 value starts at 0 and a failed scan leaves it there.
        Assert.Equal(Argb(255, 0, 0, 0), SwiftColor.Hex("zz").Resolve(Light));
        Assert.Equal(Argb(255, 0, 0, 0), SwiftColor.Hex("").Resolve(Light));
        Assert.Equal(Argb(255, 0, 0, 0), SwiftColor.Hex("#").Resolve(Dark));
        // Only one hash is dropped (:240), so the scanner meets the second one and reads nothing.
        Assert.Equal(Argb(255, 0, 0, 0), SwiftColor.Hex("##CBA6F7").Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-NUM-1")]
    [Trait("Oracle", "O-16")]
    public void Color_hex_that_overflows_64_bits_saturates_to_white_until_the_oracle_settles_O_16()
    {
        // UNVERIFIED O-16: scanHexInt64 overflow is parked as saturation (UInt64.max), whose low 24 bits are FFFFFF.
        Assert.Equal(Argb(255, 0xFF, 0xFF, 0xFF), SwiftColor.Hex("123456789ABCDEF012").Resolve(Light));
    }

    // ---- Appearance pairs (NSColor dynamicProvider) ----

    [Fact]
    [Trait("Decision", "ED-092")]
    public void An_appearance_pair_resolves_its_light_member_in_light_and_its_dark_member_in_dark()
    {
        // UI/Theme/AppTheme.swift:182-187: the System surface, 1E1E20 in darkAqua and white otherwise.
        var surface = SwiftColor.Dynamic(
            light: new SwiftColor(red: 1.0, green: 1.0, blue: 1.0, opacity: 1.0),
            dark: new SwiftColor(red: 0x1E / 255.0, green: 0x1E / 255.0, blue: 0x20 / 255.0, opacity: 1.0));
        Assert.Equal(Argb(255, 0xFF, 0xFF, 0xFF), surface.Resolve(Light));
        Assert.Equal(Argb(255, 0x1E, 0x1E, 0x20), surface.Resolve(Dark));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-VAL-1")]
    public void Opacity_on_an_appearance_pair_multiplies_both_members_and_leaves_the_pair_unchanged()
    {
        // Chrome.swift:71-73 and :79 derive overlay(x) and secondaryText from the one shared primaryText value, so
        // fading a pair returns a new pair and leaves the original and its members as they were (R-VAL-1).
        var light = new SwiftColor(white: 0, opacity: 0.4);
        var dark = new SwiftColor(white: 1);
        var pair = SwiftColor.Dynamic(light: light, dark: dark);
        // light 0.4*255 = 102; dark opaque white
        Assert.Equal(Argb(102, 0, 0, 0), pair.Resolve(Light));
        Assert.Equal(Argb(255, 255, 255, 255), pair.Resolve(Dark));

        var faded = pair.Opacity(0.4);
        // 0.4 * 0.4 = 0.16, 0.16*255 = 40.8; 1.0 * 0.4 = 0.4, 0.4*255 = 102
        Assert.Equal(Argb(41, 0, 0, 0), faded.Resolve(Light));
        Assert.Equal(Argb(102, 255, 255, 255), faded.Resolve(Dark));

        Assert.Equal(Argb(102, 0, 0, 0), pair.Resolve(Light));
        Assert.Equal(Argb(255, 255, 255, 255), pair.Resolve(Dark));
        Assert.Equal(Argb(102, 0, 0, 0), light.Resolve(Light));
        Assert.Equal(Argb(102, 0, 0, 0), light.Resolve(Dark));
        Assert.Equal(Argb(255, 255, 255, 255), dark.Resolve(Light));
        Assert.Equal(Argb(255, 255, 255, 255), dark.Resolve(Dark));
    }

    // ---- The text tokens (ED-051, ED-062) ----

    [Fact]
    [Trait("Decision", "ED-051")]
    public void LabelColor_is_the_ED_051_primary_text_pair()
    {
        // UI/Chrome/Chrome.swift:76 primaryText = Color(nsColor: .labelColor);
        // ED-051: rgba(0,0,0,0.85) light, rgba(255,255,255,0.85) dark. 0.85*255 = 216.75
        Assert.Equal(Argb(217, 0, 0, 0), SwiftColor.LabelColor.Resolve(Light));
        Assert.Equal(Argb(217, 255, 255, 255), SwiftColor.LabelColor.Resolve(Dark));
    }

    [Fact]
    [Trait("Decision", "ED-051")]
    [Trait("Decision", "ED-092")]
    public void Primary_as_a_foreground_style_is_the_same_primary_text_pair()
    {
        // ED-092: .primary as a foreground style is the ED-051 token. 0.85*255 = 216.75
        Assert.Equal(Argb(217, 0, 0, 0), SwiftColor.Primary.Resolve(Light));
        Assert.Equal(Argb(217, 255, 255, 255), SwiftColor.Primary.Resolve(Dark));
        Assert.Equal(SwiftColor.LabelColor.Resolve(Light), SwiftColor.Primary.Resolve(Light));
        Assert.Equal(SwiftColor.LabelColor.Resolve(Dark), SwiftColor.Primary.Resolve(Dark));
    }

    [Fact]
    [Trait("Decision", "ED-062")]
    [Trait("Decision", "ED-092")]
    public void Secondary_is_primary_text_at_seventy_percent()
    {
        // UI/Chrome/Chrome.swift:79 secondaryText = primaryText.opacity(0.7); ED-062; ED-092 .secondary is that token.
        // 0.85 * 0.7 = 0.595, 0.595*255 = 151.725
        Assert.Equal(Argb(152, 0, 0, 0), SwiftColor.Secondary.Resolve(Light));
        Assert.Equal(Argb(152, 255, 255, 255), SwiftColor.Secondary.Resolve(Dark));
        Assert.Equal(SwiftColor.LabelColor.Opacity(0.7).Resolve(Light), SwiftColor.Secondary.Resolve(Light));
        Assert.Equal(SwiftColor.LabelColor.Opacity(0.7).Resolve(Dark), SwiftColor.Secondary.Resolve(Dark));
    }

    [Fact]
    [Trait("Decision", "ED-062")]
    [Trait("Decision", "ED-092")]
    public void Opacity_on_an_already_faded_token_multiplies_the_exact_alpha_not_a_rounded_byte()
    {
        // App/Views/Settings/HydraCookbookPanel.swift:104, :110 and MCPSettingsPage.swift:264:
        // Chrome.secondaryText.opacity(0.8), with secondaryText = primaryText.opacity(0.7) (Chrome.swift:79).
        // Exact: 0.85 * 0.7 * 0.8 = 0.476, 0.476*255 = 121.38, so 121.
        // Rounding to a byte at each step would give 217*0.7 = 151.9 -> 152, then 152*0.8 = 121.6 -> 122.
        Assert.Equal(Argb(121, 0, 0, 0), SwiftColor.Secondary.Opacity(0.8).Resolve(Light));
        Assert.Equal(Argb(121, 255, 255, 255), SwiftColor.Secondary.Opacity(0.8).Resolve(Dark));
    }

    [Fact]
    [Trait("Decision", "ED-051")]
    [Trait("Decision", "ED-092")]
    public void Chrome_overlay_opacities_multiply_the_primary_text_alpha()
    {
        // UI/Chrome/Chrome.swift:71-73 overlay(x) = primaryText.opacity(x).
        // App/Views/Composer/EffortSlider.swift:673 overlay(0.06): 0.85*0.06 = 0.051, *255 = 13.005
        Assert.Equal(Argb(13, 0, 0, 0), SwiftColor.LabelColor.Opacity(0.06).Resolve(Light));
        // overlay(0.1): 0.085*255 = 21.675
        Assert.Equal(Argb(22, 255, 255, 255), SwiftColor.LabelColor.Opacity(0.1).Resolve(Dark));
        // UI/Theme/AppTheme.swift:153 Color.primary.opacity(0.22): 0.187*255 = 47.685
        Assert.Equal(Argb(48, 0, 0, 0), SwiftColor.Primary.Opacity(0.22).Resolve(Light));
    }

    // ---- Named system colours ----

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Clear_white_and_black_are_the_documented_fixed_values()
    {
        // NSColor.clear is grayscale 0 with alpha 0; white is grayscale 1, black grayscale 0, both opaque.
        foreach (var theme in new[] { Light, Dark })
        {
            Assert.Equal(Argb(0, 0, 0, 0), SwiftColor.Clear.Resolve(theme));
            Assert.Equal(Argb(255, 255, 255, 255), SwiftColor.White.Resolve(theme));
            Assert.Equal(Argb(255, 0, 0, 0), SwiftColor.Black.Resolve(theme));
        }
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void White_and_black_with_opacity_keep_their_channels_and_scale_alpha()
    {
        // App/Views/Composer/EffortSlider.swift:788 Color.white.opacity(0.55) and (0.32): 0.55*255 = 140.25, 0.32*255 = 81.6
        Assert.Equal(Argb(140, 255, 255, 255), SwiftColor.White.Opacity(0.55).Resolve(Light));
        Assert.Equal(Argb(82, 255, 255, 255), SwiftColor.White.Opacity(0.32).Resolve(Dark));
        // App/Views/Chat/HydraPanel.swift:120 .black.opacity(0.65): 0.65*255 = 165.75
        Assert.Equal(Argb(166, 0, 0, 0), SwiftColor.Black.Opacity(0.65).Resolve(Light));
        Assert.Equal(0, SwiftColor.Clear.Opacity(0.5).Resolve(Light).A);
    }

    [Fact]
    [Trait("Decision", "ED-051")]
    [Trait("Decision", "ED-092")]
    public void AccentColor_and_controlAccentColor_are_the_Windows_personalization_accent() => headless.OnUiThread(() =>
    {
        // UI/Chrome/Chrome.swift:80, :87 and :83 fall back to the system control accent; ED-051: on Windows that is the
        // personalization accent, which Avalonia exposes as the platform's AccentColor1. B2 ships no AccentColor asset,
        // so Color.accentColor is the same system accent.
        var accent = Application.Current!.PlatformSettings!.GetColorValues().AccentColor1;
        foreach (var theme in new[] { Light, Dark })
        {
            Assert.Equal(accent, SwiftColor.AccentColor.Resolve(theme));
            Assert.Equal(accent, SwiftColor.ControlAccentColor.Resolve(theme));
        }
        // Opacity multiplies whatever alpha the accent has (255 * 0.4 = 102 for an opaque accent).
        var fadedAlpha = (byte)Math.Round(accent.A * 0.4, MidpointRounding.AwayFromZero);
        Assert.Equal(Color.FromArgb(fadedAlpha, accent.R, accent.G, accent.B), SwiftColor.AccentColor.Opacity(0.4).Resolve(Light));
    });

    [Fact]
    [Trait("Decision", "ED-051")]
    [Trait("Decision", "ED-092")]
    public void Opacity_on_an_already_faded_accent_multiplies_the_factor() => headless.OnUiThread(() =>
    {
        // ED-092: .opacity(x) multiplies alpha. App/Views/Chat/HydraProgressBar.swift:350-351 fades a tint that is
        // Chrome.accent (App/Views/Chat/ThreadTimeline.swift:2127), so the accent's own factor must compose.
        // Exact: 0.5 * 0.5 = 0.25; for the opaque headless accent 0.25*255 = 63.75, so 64.
        // Replacing the factor instead would give 0.5*255 = 127.5.
        var accent = Application.Current!.PlatformSettings!.GetColorValues().AccentColor1;
        Assert.Equal(255, accent.A);
        foreach (var theme in new[] { Light, Dark })
        {
            Assert.Equal(Color.FromArgb(64, accent.R, accent.G, accent.B), SwiftColor.AccentColor.Opacity(0.5).Opacity(0.5).Resolve(theme));
            Assert.Equal(Color.FromArgb(64, accent.R, accent.G, accent.B), SwiftColor.ControlAccentColor.Opacity(0.5).Opacity(0.5).Resolve(theme));
        }
    });

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-VIEW-11")]
    public void An_appearance_pair_resolves_a_derived_variant_through_the_variant_it_inherits()
    {
        // UI/Theme/AppTheme.swift:183: isDark = appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua, so every
        // appearance that best matches dark takes the dark member and every one that best matches light the light one.
        // A custom Avalonia variant inherits from Dark or Light; it resolves as the variant it inherits.
        var darkish = new ThemeVariant("CustomDark", ThemeVariant.Dark);
        var darkerStill = new ThemeVariant("CustomDarkChild", darkish);
        var lightish = new ThemeVariant("CustomLight", ThemeVariant.Light);

        // 0.85*255 = 216.75
        Assert.Equal(Argb(217, 255, 255, 255), SwiftColor.LabelColor.Resolve(darkish));
        Assert.Equal(Argb(217, 255, 255, 255), SwiftColor.LabelColor.Resolve(darkerStill));
        Assert.Equal(Argb(217, 0, 0, 0), SwiftColor.LabelColor.Resolve(lightish));

        // AppTheme.swift:182-187, the System surface pair: dark sRGB 1E 1E 20, light white.
        var surface = SwiftColor.Dynamic(
            new SwiftColor(red: 1.0, green: 1.0, blue: 1.0, opacity: 1.0),
            new SwiftColor(red: 0x1E / 255.0, green: 0x1E / 255.0, blue: 0x20 / 255.0, opacity: 1.0));
        Assert.Equal(Argb(255, 0x1E, 0x1E, 0x20), surface.Resolve(darkish));
        Assert.Equal(Argb(255, 0x1E, 0x1E, 0x20), surface.Resolve(darkerStill));
        Assert.Equal(Argb(255, 255, 255, 255), surface.Resolve(lightish));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-VIEW-11")]
    public void A_brush_under_a_derived_dark_variant_takes_the_dark_member() => headless.OnUiThread(() =>
    {
        // AppTheme.swift:183 as above, through the control's ActualThemeVariant.
        var target = new Border();
        var scope = new ThemeVariantScope { RequestedThemeVariant = new ThemeVariant("CustomDark", ThemeVariant.Dark), Child = target };
        var window = Show(scope);

        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(SwiftColor.LabelColor.Brush(target));
        Assert.Equal(Argb(217, 255, 255, 255), brush.Color);

        scope.RequestedThemeVariant = new ThemeVariant("CustomLight", ThemeVariant.Light);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argb(217, 0, 0, 0), brush.Color);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-104")]
    public void Yellow_is_the_documented_system_yellow_pair()
    {
        // UNVERIFIED under OD-0001, no O-nn, per ED-104: Apple HIG Default values for Color.yellow, light sRGB
        // (255, 204, 0) and dark (255, 214, 0), opaque; the increased contrast values are not modelled.
        // App/Views/Composer/EffortSlider.swift:669 .foregroundStyle(isOn ? Color.yellow : Chrome.secondaryText)
        Assert.Equal(Argb(255, 255, 204, 0), SwiftColor.Yellow.Resolve(Light));
        Assert.Equal(Argb(255, 255, 214, 0), SwiftColor.Yellow.Resolve(Dark));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-104")]
    public void Yellow_with_opacity_scales_the_alpha_of_both_members()
    {
        // UNVERIFIED under OD-0001, no O-nn, per ED-104.
        // App/Views/Composer/EffortSlider.swift:673 Circle().fill(isOn ? Color.yellow.opacity(0.18) : ...)
        // 0.18*255 = 45.9
        Assert.Equal(Argb(46, 255, 204, 0), SwiftColor.Yellow.Opacity(0.18).Resolve(Light));
        Assert.Equal(Argb(46, 255, 214, 0), SwiftColor.Yellow.Opacity(0.18).Resolve(Dark));
    }

    [Fact]
    [Trait("Decision", "ED-051")]
    [Trait("Decision", "ED-105")]
    public void PlaceholderTextColor_is_the_pinned_quarter_alpha_pair()
    {
        // UNVERIFIED under OD-0001, no O-nn, per ED-105: pinned literals in the ED-051 family, light rgba(0,0,0,0.25)
        // and dark rgba(255,255,255,0.25), not derived from labelColor or from a Windows brush.
        // App/Views/Composer/ComposerTextView.swift:204 .foregroundColor: NSColor.placeholderTextColor
        // 0.25*255 = 63.75
        Assert.Equal(Argb(64, 0, 0, 0), SwiftColor.PlaceholderTextColor.Resolve(Light));
        Assert.Equal(Argb(64, 255, 255, 255), SwiftColor.PlaceholderTextColor.Resolve(Dark));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void REVIEW_Red_is_the_documented_system_red_pair_not_its_light_member_alone()
    {
        // REVIEW (unit/parity-sidebarview pass 1): App/Views/Sidebar/SidebarView.swift:927 Text("Delete").foregroundStyle(.red).
        // ED-092: a system colour with no B2 value takes the macOS documented values; Color.red, like Color.yellow
        // (ED-104), has distinct 'Default (light)' and 'Default (dark)' columns in the HIG system colours table, the dark
        // member the lighter of the two. SwiftColor.Red resolves to the light member (255, 59, 48) in both appearances.
        // Only the pair's shape is asserted here (which table edition is current is not this test's question).
        var light = SwiftColor.Red.Resolve(Light);
        var dark = SwiftColor.Red.Resolve(Dark);
        Assert.Equal(255, light.R);
        Assert.NotEqual(light, dark);
        Assert.True(dark.G > light.G && dark.B > light.B, $"dark {dark} is not the lighter member of {light}");
    }

    // ---- Opacity arithmetic and immutability ----

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-VAL-1")]
    public void Opacity_multiplies_alpha_and_chains_multiplicatively()
    {
        var red = new SwiftColor(red: 1, green: 0, blue: 0);
        // 0.4*255 = 102
        Assert.Equal(Argb(102, 255, 0, 0), red.Opacity(0.4).Resolve(Light));
        // 0.5 * 0.5 = 0.25, 0.25*255 = 63.75
        Assert.Equal(Argb(64, 255, 0, 0), red.Opacity(0.5).Opacity(0.5).Resolve(Light));
        Assert.Equal(Argb(255, 255, 0, 0), red.Opacity(1).Resolve(Light));
        Assert.Equal(Argb(0, 255, 0, 0), red.Opacity(0).Resolve(Light));
        // App/Views/Tour/TourView.swift:129-130: Color(white: 0.08).opacity(0.28) and (0.55).
        // 0.08*255 = 20.4; 0.28*255 = 71.4; 0.55*255 = 140.25
        Assert.Equal(Argb(71, 20, 20, 20), new SwiftColor(white: 0.08).Opacity(0.28).Resolve(Dark));
        Assert.Equal(Argb(140, 20, 20, 20), new SwiftColor(white: 0.08).Opacity(0.55).Resolve(Dark));
        // An existing alpha is multiplied, not replaced: 0.5 * 0.5 = 0.25
        Assert.Equal(Argb(64, 255, 255, 255), new SwiftColor(white: 1, opacity: 0.5).Opacity(0.5).Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-VAL-1")]
    public void Opacity_returns_a_new_value_and_never_changes_the_receiver()
    {
        // 0.08*255 = 20.4; 0.28*255 = 71.4
        var grey = new SwiftColor(white: 0.08);
        var faded = grey.Opacity(0.28);
        Assert.Equal(Argb(71, 20, 20, 20), faded.Resolve(Light));
        Assert.Equal(Argb(255, 20, 20, 20), grey.Resolve(Light));

        // 0.85*255 = 216.75
        var label = SwiftColor.LabelColor;
        _ = label.Opacity(0.7);
        Assert.Equal(Argb(217, 0, 0, 0), label.Resolve(Light));
        Assert.Equal(Argb(217, 255, 255, 255), label.Resolve(Dark));
        Assert.Equal(Argb(217, 0, 0, 0), SwiftColor.LabelColor.Resolve(Light));
        Assert.Equal(Argb(217, 255, 255, 255), SwiftColor.LabelColor.Resolve(Dark));
    }

    // ---- Brushes that follow the control's theme ----

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-VIEW-11")]
    public void A_brush_follows_the_controls_theme_when_it_flips() => headless.OnUiThread(() =>
    {
        var target = new Border();
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = target };
        var window = Show(scope);

        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(SwiftColor.LabelColor.Brush(target));
        Assert.Equal(Argb(217, 0, 0, 0), brush.Color);

        scope.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argb(217, 255, 255, 255), brush.Color);

        scope.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argb(217, 0, 0, 0), brush.Color);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-062")]
    [Trait("Decision", "ED-092")]
    public void A_brush_of_a_faded_pair_follows_the_theme_with_the_faded_alpha() => headless.OnUiThread(() =>
    {
        var target = new TextBlock();
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = target };
        var window = Show(scope);

        // Chrome.swift:79 secondaryText: 0.595*255 = 151.725
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(SwiftColor.Secondary.Brush(target));
        Assert.Equal(Argb(152, 255, 255, 255), brush.Color);

        scope.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argb(152, 0, 0, 0), brush.Color);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-092")]
    public void A_brush_of_a_fixed_colour_keeps_its_colour_across_a_flip() => headless.OnUiThread(() =>
    {
        var target = new Border();
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = target };
        var window = Show(scope);

        // Core/Models/MCPCatalog.swift:29 colorValue 0xFC6D26
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(SwiftColor.Rgb(0xFC6D26).Brush(target));
        Assert.Equal(Argb(255, 0xFC, 0x6D, 0x26), brush.Color);

        scope.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argb(255, 0xFC, 0x6D, 0x26), brush.Color);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-092")]
    public void A_brush_of_an_appearance_pair_takes_the_member_for_the_controls_current_theme() => headless.OnUiThread(() =>
    {
        // UI/Theme/AppTheme.swift:182-187 the System surface pair.
        var surface = SwiftColor.Dynamic(light: SwiftColor.Hex("FFFFFF"), dark: SwiftColor.Hex("1E1E20"));
        var target = new Border();
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = target };
        var window = Show(scope);

        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(surface.Brush(target));
        Assert.Equal(Argb(255, 0x1E, 0x1E, 0x20), brush.Color);

        scope.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argb(255, 0xFF, 0xFF, 0xFF), brush.Color);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-104")]
    public void A_yellow_brush_follows_the_controls_theme_when_it_flips() => headless.OnUiThread(() =>
    {
        // UNVERIFIED under OD-0001, no O-nn, per ED-104.
        // App/Views/Composer/EffortSlider.swift:669 Color.yellow: green 204 in light, 214 in dark.
        var target = new Border();
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = target };
        var window = Show(scope);

        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(SwiftColor.Yellow.Brush(target));
        Assert.Equal(Argb(255, 255, 204, 0), brush.Color);

        scope.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argb(255, 255, 214, 0), brush.Color);

        scope.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argb(255, 255, 204, 0), brush.Color);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-105")]
    public void A_placeholder_brush_follows_the_controls_theme_when_it_flips() => headless.OnUiThread(() =>
    {
        // UNVERIFIED under OD-0001, no O-nn, per ED-105.
        // App/Views/Composer/ComposerTextView.swift:204 NSColor.placeholderTextColor; 0.25*255 = 63.75
        var target = new TextBlock();
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = target };
        var window = Show(scope);

        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(SwiftColor.PlaceholderTextColor.Brush(target));
        Assert.Equal(Argb(64, 255, 255, 255), brush.Color);

        scope.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Argb(64, 0, 0, 0), brush.Color);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-051")]
    [Trait("Decision", "ED-092")]
    public void An_accent_brush_follows_the_system_accent_when_the_platform_colours_change() => headless.OnUiThread(() =>
    {
        // UI/Chrome/Chrome.swift:75 "Dynamic system colors resolve at draw time", :80 Color(nsColor: .controlAccentColor):
        // the control accent changes when the user picks a new system accent, and what is drawn with it follows.
        // ED-051: on Windows that is the personalization accent, which Avalonia reports as PlatformColorValues.AccentColor1
        // and announces through IPlatformSettings.ColorValuesChanged.
        //
        // Headless limit (Avalonia 12.1.2, read from the shipped assemblies): the headless platform binds
        // DefaultPlatformSettings, whose GetColorValues always returns new PlatformColorValues { ThemeVariant = Light }
        // (accent #FF0078D7), and IPlatformSettings cannot be implemented by user code, so a test cannot install
        // settings whose GetColorValues changes. A test can raise ColorValuesChanged on the live settings, through
        // DefaultPlatformSettings.OnColorValuesChanged, with the new values; on Windows the event carries the values
        // GetColorValues then returns. So the brushes must take the accent the event carries.
        var settings = Application.Current!.PlatformSettings!;
        var original = settings.GetColorValues();
        var target = new Border();
        var window = Show(target);

        var accentBrush = Assert.IsAssignableFrom<ISolidColorBrush>(SwiftColor.AccentColor.Brush(target));
        var controlBrush = Assert.IsAssignableFrom<ISolidColorBrush>(SwiftColor.ControlAccentColor.Brush(target));
        var fadedBrush = Assert.IsAssignableFrom<ISolidColorBrush>(SwiftColor.AccentColor.Opacity(0.4).Brush(target));
        Assert.Equal(original.AccentColor1, accentBrush.Color);
        Assert.Equal(original.AccentColor1, controlBrush.Color);

        var changed = new PlatformColorValues
        {
            ThemeVariant = original.ThemeVariant,
            ContrastPreference = original.ContrastPreference,
            AccentColor1 = Color.FromArgb(255, 0xE8, 0x11, 0x23),
            AccentColor2 = original.AccentColor2,
            AccentColor3 = original.AccentColor3,
        };
        try
        {
            RaiseColorValuesChanged(settings, changed);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Argb(255, 0xE8, 0x11, 0x23), accentBrush.Color);
            Assert.Equal(Argb(255, 0xE8, 0x11, 0x23), controlBrush.Color);
            // 0.4*255 = 102
            Assert.Equal(Argb(102, 0xE8, 0x11, 0x23), fadedBrush.Color);
        }
        finally
        {
            RaiseColorValuesChanged(settings, original);
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        Assert.Equal(original.AccentColor1, accentBrush.Color);
    });

    private static void RaiseColorValuesChanged(IPlatformSettings settings, PlatformColorValues values)
    {
        var raise = settings.GetType().GetMethod("OnColorValuesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(raise);
        raise.Invoke(settings, [values]);
    }
}
