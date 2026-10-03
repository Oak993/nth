using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// The accent is `NSColor.controlAccentColor` on the Mac (Chrome.swift:80, :87) and the Windows personalization accent here
// (ED-051). AppKit's accent is always a colour that reads on its appearance (Graphite is a mid grey, and every accent is
// a little brighter in Dark). Windows lets the user choose any colour: on the owner's PC it is #000000, which made the
// selected "Daily" chip of Settings > General, the Markdown links of a chat (LinkParagraphView.swift:485-486, the accent
// with no underline) and every accent fill vanish on the dark glass (E:\Droppy\work\gallery\settings-general-dark.png,
// main-thread-2-dark.png, 2026-10-02). ED-238 (3) as amended by ED-250 (OD-0088, SPEC 3.2): an accent with under 3.1:1 on
// the Settings card (#4D4C4B / #D1CFCF) or under 3:1 on the appearance's window background (macOS 27 UI Kit, Window
// backgrounds: Dark #1E1E1E, Light #FFFFFF) has its OKLCH lightness moved (up in Dark, down in Light) at fixed hue to the
// first colour that has both, so ED-238's floor of 3:1 on the window still holds; every accent that already has both, and
// every theme's own accent (AppTheme.Accent), is untouched.
public sealed class AccentLegibilityTests
{
    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Contrast(Color a, Color b)
    {
        var (high, low) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (high + 0.05) / (low + 0.05);
    }

    private static readonly Color DarkWindow = Color.FromRgb(0x1E, 0x1E, 0x1E);
    private static readonly Color LightWindow = Colors.White;
    private static readonly Color DarkCard = Color.FromRgb(0x4D, 0x4C, 0x4B);
    private static readonly Color LightCard = Color.FromRgb(0xD1, 0xCF, 0xCF);

    private static Color Accent(ThemeVariant theme, Color platform, double opacity = 1) =>
        (opacity == 1 ? SwiftColor.AccentColor : SwiftColor.AccentColor.Opacity(opacity)).Resolve(theme, () => platform);

    [Fact]
    [Trait("Decision", "ED-238")]
    [Trait("Decision", "ED-250")]
    public void A_black_accent_is_lifted_to_a_readable_grey_on_the_dark_window()
    {
        var resolved = Accent(ThemeVariant.Dark, Colors.Black);
        Assert.Equal(255, resolved.A);
        Assert.Equal(resolved.R, resolved.G); // lifted at fixed hue: still a neutral grey
        Assert.Equal(resolved.R, resolved.B);
        Assert.True(Contrast(resolved, DarkWindow) >= 3); // ED-238's floor on the window
        Assert.InRange(Contrast(resolved, DarkCard), 3.1, 3.2); // the first step that reaches 3.1:1 on the card (ED-250)
        Assert.Equal(Color.FromRgb(0x9C, 0x9C, 0x9C), resolved); // SPEC 3.2: #000000 becomes #9C9C9C in Dark
        Assert.NotEqual(Colors.Black, resolved);
        // The same accent through Color.accentColor and NSColor.controlAccentColor: one rule.
        Assert.Equal(resolved, SwiftColor.ControlAccentColor.Resolve(ThemeVariant.Dark, () => Colors.Black));
    }

    [Fact]
    [Trait("Decision", "ED-238")]
    public void A_black_accent_stays_black_on_the_light_window_where_it_reads()
    {
        Assert.Equal(Colors.Black, Accent(ThemeVariant.Light, Colors.Black));
    }

    [Fact]
    [Trait("Decision", "ED-238")]
    [Trait("Decision", "ED-250")]
    public void A_white_accent_is_darkened_on_the_light_window_and_kept_on_the_dark_one()
    {
        var light = Accent(ThemeVariant.Light, Colors.White);
        Assert.True(Contrast(light, LightWindow) >= 3);
        Assert.True(Contrast(light, LightCard) >= 3.1);
        Assert.Equal(light.R, light.G);
        Assert.Equal(light.R, light.B);
        Assert.Equal(Colors.White, Accent(ThemeVariant.Dark, Colors.White));
    }

    [Theory]
    [InlineData("Dark", "#4B9FFC")]
    [InlineData("Light", "#0067C0")]
    [Trait("Decision", "ED-238")]
    [Trait("Decision", "ED-250")]
    public void An_accent_that_already_reads_is_kept_exactly(string name, string hex)
    {
        var appearance = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var reads = Color.Parse(hex); // 3.1:1 on the card and 3:1 on the window of its appearance
        Assert.Equal(reads, Accent(appearance, reads));
    }

    [Theory]
    [InlineData("Dark", "#409FFF")]
    [InlineData("Light", "#0073CF")]
    [Trait("Decision", "ED-250")]
    public void The_headless_windows_blue_is_lifted_for_the_card_as_the_spec_tabulates(string name, string hex)
    {
        var appearance = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var windowsBlue = Color.FromRgb(0x00, 0x78, 0xD7); // it reads on both windows but has under 3.1:1 on the card in both appearances
        Assert.Equal(Color.Parse(hex), Accent(appearance, windowsBlue));
    }

    [Fact]
    [Trait("Decision", "ED-238")]
    [Trait("Decision", "ED-250")]
    public void A_pale_accent_is_darkened_keeping_its_hue_toward_the_light_window()
    {
        var yellow = Color.FromRgb(0xFF, 0xEB, 0x3B);
        var resolved = Accent(ThemeVariant.Light, yellow);
        Assert.True(Contrast(resolved, LightWindow) >= 3);
        Assert.InRange(Contrast(resolved, LightCard), 3.1, 3.2);
        Assert.True(resolved.R > resolved.B && resolved.G > resolved.B); // still yellow, not grey
    }

    [Fact]
    [Trait("Decision", "ED-238")]
    public void A_faded_accent_fades_the_lifted_colour_so_the_selected_chip_has_a_fill_to_see()
    {
        // TokenActivitySection: the selected chip is Chrome.Accent.Opacity(0.16) behind Chrome.Accent text (Components.swift:84-85).
        var opaque = Accent(ThemeVariant.Dark, Colors.Black);
        var faded = Accent(ThemeVariant.Dark, Colors.Black, 0.16);
        Assert.Equal(Color.FromArgb(41, opaque.R, opaque.G, opaque.B), faded);
    }
}
