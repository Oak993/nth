using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PORT-ONLY tests for the foundation unit F.SwiftColor (ED-092 part 1, ED-051, ED-062, ED-095; R-VIEW-11).
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
//   SwiftColor.Clear, White, Black, Primary, Secondary, AccentColor, Yellow,
//             LabelColor, PlaceholderTextColor, ControlAccentColor
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
    [Trait("Decision", "ED-095")]
    public void NSColor_white_alpha_is_a_grey_with_that_alpha()
    {
        // App/Views/Terminal/TerminalStore.swift:51: NSColor(white: 0.12, alpha: 1), 0.12*255 = 30.6
        Assert.Equal(Argb(255, 31, 31, 31), new SwiftColor(white: 0.12, opacity: 1).Resolve(Dark));
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
    public void Color_hex_trims_whitespace_and_newlines_before_looking_for_the_hash()
    {
        // AppTheme.swift:239 hex.trimmed, Core/Support/Text.swift:132 trimmingCharacters(in: .whitespacesAndNewlines)
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("  #CBA6F7\n").Resolve(Light));
        Assert.Equal(Argb(255, 0xCB, 0xA6, 0xF7), SwiftColor.Hex("\tCBA6F7\r\n").Resolve(Light));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
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
    [Trait("Oracle", "O-16")]
    public void Color_hex_that_overflows_64_bits_saturates_to_white_until_the_oracle_settles_O_16()
    {
        // UNVERIFIED O-16: scanHexInt64 overflow is parked as saturation (UInt64.max), whose low 24 bits are FFFFFF.
        Assert.Equal(Argb(255, 0xFF, 0xFF, 0xFF), SwiftColor.Hex("123456789ABCDEF012").Resolve(Light));
    }

    // ---- Appearance pairs (NSColor dynamicProvider) ----

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Decision", "ED-095")]
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
    public void Opacity_on_an_appearance_pair_multiplies_both_members()
    {
        // 0.5 * 0.4 = 0.2, 0.2*255 = 51; 1.0 * 0.4 = 0.4, 0.4*255 = 102
        var pair = SwiftColor.Dynamic(light: new SwiftColor(white: 0, opacity: 0.5), dark: new SwiftColor(white: 1));
        var faded = pair.Opacity(0.4);
        Assert.Equal(Argb(51, 0, 0, 0), faded.Resolve(Light));
        Assert.Equal(Argb(102, 255, 255, 255), faded.Resolve(Dark));
    }

    // ---- The text tokens (ED-051, ED-062) ----

    [Fact]
    [Trait("Decision", "ED-051")]
    [Trait("Decision", "ED-095")]
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
    [Trait("Decision", "ED-092")]
    public void Yellow_is_the_documented_system_yellow_in_each_appearance()
    {
        // UNVERIFIED under OD-0001 (ED-092: system colours with no B2 value use the macOS documented values).
        // Apple Human Interface Guidelines, system yellow: light (255, 204, 0), dark (255, 214, 10).
        Assert.Equal(Argb(255, 255, 204, 0), SwiftColor.Yellow.Resolve(Light));
        Assert.Equal(Argb(255, 255, 214, 10), SwiftColor.Yellow.Resolve(Dark));
        // App/Views/Composer/EffortSlider.swift:673 Color.yellow.opacity(0.18): 0.18*255 = 45.9
        Assert.Equal(Argb(46, 255, 204, 0), SwiftColor.Yellow.Opacity(0.18).Resolve(Light));
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
    [Trait("Decision", "ED-092")]
    public void PlaceholderTextColor_is_a_distinct_named_colour_that_takes_opacity()
    {
        // App/Views/Composer/ComposerTextView.swift:204 NSColor.placeholderTextColor. Its value has no B2 source and
        // no numeric AppKit documentation, so only the opacity contract is asserted: alpha scales, channels stay.
        foreach (var theme in new[] { Light, Dark })
        {
            var baseColor = SwiftColor.PlaceholderTextColor.Resolve(theme);
            var faded = SwiftColor.PlaceholderTextColor.Opacity(0).Resolve(theme);
            Assert.Equal(Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B), faded);
        }
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
        var grey = new SwiftColor(white: 0.08);
        var faded = grey.Opacity(0.28);
        Assert.NotSame(grey, faded);
        Assert.Equal(Argb(255, 20, 20, 20), grey.Resolve(Light));

        var label = SwiftColor.LabelColor;
        _ = label.Opacity(0.7);
        Assert.Same(label, SwiftColor.LabelColor);
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
    [Trait("Decision", "ED-095")]
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
}
