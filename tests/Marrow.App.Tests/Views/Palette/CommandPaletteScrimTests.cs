using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Xunit;
using static Marrow.App.Tests.Fixtures.CommandPaletteRig;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B3 52bc792), :34-41 (the dimming scrim).
[Collection("Headless")]
public sealed class CommandPaletteScrimTests(HeadlessSession headless)
{
    private static Border ScrimOf(CommandPalette palette) => Assert.IsType<Border>(Assert.IsType<Panel>(palette.Child).Children[0]);

    private static (byte R, byte G, byte B, double Alpha) Wash(Border scrim)
    {
        var color = Assert.IsAssignableFrom<ISolidColorBrush>(scrim.Background).Color;
        return (color.R, color.G, color.B, Math.Round(color.A / 255.0, 2));
    }

    // Partial proof of UI-WIN-028 (the colour clause only): the geometry, hit and accessibility clauses are the test below.
    [Theory]
    [InlineData("Dark", 0.24)]
    [InlineData("Light", 0.08)]
    [Trait("Req", "UI-WIN-028")] // :37
    [Trait("Rule", "R-TEST-4")]
    public void The_scrim_is_black_at_0_24_in_dark_and_0_08_in_light_and_follows_a_theme_change(string variant, double alpha)
    {
        var start = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var other = variant == "Dark" ? ThemeVariant.Light : ThemeVariant.Dark;
        var otherAlpha = variant == "Dark" ? 0.08 : 0.24;
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, window, palette) = Build(model, start);
                var scrim = ScrimOf(palette);
                Assert.Equal((0, 0, 0, alpha), Wash(scrim)); // black, whatever the theme
                window.RequestedThemeVariant = other; // the colour scheme changes while the palette is open
                Settle(window);
                Assert.Equal((0, 0, 0, otherAlpha), Wash(scrim));
                window.RequestedThemeVariant = start;
                Settle(window);
                Assert.Equal((0, 0, 0, alpha), Wash(scrim));
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-028")] // :34-41
    [Trait("Rule", "R-TEST-4")] // UI-WIN-028 geometry, hit and accessibility clauses (the colour clause is the Theory above, the mount/unmount clause RootViewPaletteInputTests)
    public void The_scrim_fills_the_window_is_hidden_from_assistive_technology_and_a_click_anywhere_on_it_closes_the_palette()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, window, palette) = Build(model, ThemeVariant.Light, 900, 700);
                model.IsCommandPalettePresented = true;
                var scrim = ScrimOf(palette);
                Assert.Equal(new Rect(0, 0, 900, 700), scrim.Bounds); // edge to edge, over the sidebar side and the title band
                Assert.Equal(new Rect(0, 0, 900, 700), palette.Bounds);
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(scrim));

                // A click anywhere on the scrim closes it: the four corners of the window and the far side.
                foreach (var point in new[] { new Point(2, 2), new Point(897, 2), new Point(2, 697), new Point(897, 697), new Point(30, 350) })
                {
                    model.IsCommandPalettePresented = true;
                    Click(window, point);
                    Assert.False(model.IsCommandPalettePresented);
                }
            });
        });
    }
}
