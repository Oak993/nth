using Avalonia.Media;
using Marrow.Setup.Platform;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Tests.Theme;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 and the canvas's tokens; unit U1 (ED-NEW-inst-u1-1). The window's
// colours are the canvas's: the Mica tint (74 % to 82 %), the inks, Windows' red for Close; without Mica the bottom tint
// over Windows' own base, opaque; a contrast theme uses Windows' own colours, no glass, and a 2 px edge.
public sealed class SetupPaletteTests
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Dark_is_the_canvass_dark_tokens()
    {
        var dark = SetupPalette.Dark;

        Assert.Equal(Color.FromArgb(189, 30, 32, 38), dark.TintTop);
        Assert.Equal(Color.FromArgb(209, 22, 24, 29), dark.TintBottom);
        Assert.Equal(Color.Parse("#eef0f4"), dark.Ink1);
        Assert.Equal(Color.Parse("#a9b0bd"), dark.Ink2);
        Assert.Equal(Color.Parse("#8a919f"), dark.Ink3);
        Assert.Equal(Color.Parse("#f0b35a"), dark.Warn);
        Assert.Equal(Color.Parse("#c42b1c"), dark.CloseHover);
        Assert.True(dark.Glass);
        Assert.Equal(1, dark.EdgeThickness);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Light_is_the_canvass_light_tokens()
    {
        var light = SetupPalette.Light;

        Assert.Equal(Color.FromArgb(194, 246, 247, 250), light.TintTop);
        Assert.Equal(Color.FromArgb(214, 238, 240, 244), light.TintBottom);
        Assert.Equal(Color.Parse("#1d2027"), light.Ink1);
        Assert.Equal(Color.Parse("#525968"), light.Ink2);
        Assert.Equal(Color.Parse("#5f6676"), light.Ink3);
        Assert.Equal(Color.Parse("#9a5b06"), light.Warn);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_solid_tint_is_the_bottom_tint_over_Windows_base_and_opaque()
    {
        var solid = SetupPalette.Dark.Solid;

        Assert.Equal(255, solid.A);
        Assert.Equal(SetupPalette.Over(SetupPalette.Dark.TintBottom, Color.Parse("#202020")), solid);
        Assert.Equal(Color.FromRgb(24, 25, 30), solid);
        Assert.Equal(255, SetupPalette.Light.Solid.A);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_contrast_theme_uses_Windows_colours_and_a_2_px_edge()
    {
        var system = SystemColorSet.HighContrastBlack with { Window = Colors.Navy, WindowText = Colors.Yellow };
        var palette = SetupPalette.For(SystemLook.Default with { HighContrast = true, Colors = system });

        Assert.False(palette.Glass);
        Assert.Equal(2, palette.EdgeThickness);
        Assert.Equal(Colors.Navy, palette.Solid);
        Assert.Equal(Colors.Yellow, palette.Ink1);
        Assert.Equal(Colors.Yellow, palette.Ink3);
        Assert.Equal(Colors.Yellow, palette.Edge);
        Assert.Equal(system.Highlight, palette.CloseHover);
        Assert.Equal(system.GrayText, palette.CaptionDisabledInk);
        Assert.Equal(system.HotLight, palette.Link);
        Assert.Same(SetupPalette.Dark, SetupPalette.For(SystemLook.Default));
        Assert.Same(SetupPalette.Light, SetupPalette.For(SystemLook.Default with { Dark = false }));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Colours_mix_channel_by_channel()
    {
        Assert.Equal(Color.FromArgb(255, 128, 0, 128), SetupPalette.Lerp(Colors.Red, Colors.Blue, 0.5));
        Assert.Equal(Colors.Red, SetupPalette.Lerp(Colors.Red, Colors.Blue, -1));
        Assert.Equal(Colors.Blue, SetupPalette.Lerp(Colors.Red, Colors.Blue, 2));
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), SetupPalette.Hex(0x123456));
    }
}
