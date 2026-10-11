using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Common;
using Xunit;

namespace Marrow.App.Tests.Window;

// Source: DroppyCode/App/Views/Settings/SettingsView.swift:118-122 (the backdrop, clipped to the 26 pt shape), UI/Chrome/Chrome.swift:8, :211-224;
// OD-0075 (owner, 2026-10-02: "the wedge is filled with the window's scrim"; settles OD-0072); ED-236 UNRESOLVED (a); OD-0074 (the Rise open fades
// the glass in, and the corners belong to the glass).
//
// The Settings window is the same kind of window as the main one: system Acrylic in a rectangle, a 26 pt shape drawn over it. Headless, its
// background is a known grey standing in for the blur, so a corner's colour is the scrim over the grey (what the real Acrylic shows through the
// fill is a native check).
[Collection("Headless")]
public sealed class WindowManagerCornerWedgeTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private const int Width = 780, Height = 580;
    private static readonly Color Grey = Color.FromRgb(128, 128, 128);

    // ED-NEW-textsize-2: the window's content is drawn at the base scale, so the 26 pt shape is 26 x 1.3 pixels and the 1 pt hairline 1.3 pixels:
    // it covers the outermost pixel of each edge and part of the next, so the first clear edge row is the third (inset 2).
    private const double Radius = Chrome.WindowCornerRadius * AppZoom.BaseScale;

    private static CornerSeam.Pair[] Pairs() =>
        CornerSeam.Pairs(Width, Height, Radius, [2, 3, 4, 5, 6, 8, 10], edgeInset: 2)
            .Concat(CornerSeam.Pairs(Width, Height, Radius, [3, 4, 5, 6, 7], edgeInset: 3)).ToArray();

    // The window's chrome has made it clear (the system's Acrylic is behind it): the known grey stands in for that blur.
    private static Avalonia.Controls.Window OpenOnGrey(WindowManager manager, bool dark)
    {
        manager.ShowSettings(SettingsPage.Archive);
        var window = Settings(manager);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Drain(window);
        window.Background = new SolidColorBrush(Grey);
        Drain(window);
        return window;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [Trait("Decision", "ED-236")] // App/Views/Settings/SettingsView.swift:118-122; OD-0075
    public void The_settings_windows_four_corners_outside_the_26_point_curve_are_the_body_just_inside_it(bool dark, bool busy) => WithManager((_, manager) =>
    {
        manager.Model.Settings.BackdropOpacity = 0.5;
        var window = OpenOnGrey(manager, dark);
        Assert.Equal(new Avalonia.Size(Width, Height), window.ClientSize);
        if (busy) { window.Background = Marrow.App.Tests.Views.RootViewCornerWedgeTests.Stripes(); Drain(window); }
        CornerSeam.AssertSeamless(window, Pairs(), $"{(dark ? "dark" : "light")}, {(busy ? "striped" : "grey")} backdrop", busy ? null : (Grey.R, Grey.G, Grey.B));
    });

    [Fact]
    [Trait("Decision", "ED-236")] // UI/Chrome/Chrome.swift:211-224; OD-0075
    public void The_settings_corners_are_the_body_at_every_backdrop_opacity_in_both_themes_and_the_solid_window() => WithManager((_, manager) =>
    {
        manager.Model.Settings.BackdropOpacity = 0.25;
        var window = OpenOnGrey(manager, dark: true);
        CornerSeam.AssertSeamless(window, Pairs(), "at 0.25 dark");
        manager.Model.Settings.BackdropOpacity = 0.75; Drain(window);
        CornerSeam.AssertSeamless(window, Pairs(), "at 0.75 dark");
        // OD-0088, OD-0076 (ED-254): the Settings window turns over on a change of appearance, so the new appearance is read once the 0.32 s
        // turnover has run (on a frame clock and a clock the test steps).
        using var rig = new SettingsRiseRig();
        void TurnOver() { for (var frame = 0; frame < 40; frame++) { rig.Frame(1.0 / 60); Drain(window); } }
        window.RequestedThemeVariant = ThemeVariant.Light; Drain(window); TurnOver();
        CornerSeam.AssertSeamless(window, Pairs(), "at 0.75 light");
        manager.Model.Settings.BackdropOpacity = 1; Drain(window);
        CornerSeam.AssertSeamless(window, Pairs(), "solid light: the window is white, corners too");
        window.RequestedThemeVariant = ThemeVariant.Dark; Drain(window); TurnOver();
        CornerSeam.AssertSeamless(window, Pairs(), "solid dark: the window is black, corners too");
    });

    [Fact]
    [Trait("Decision", "ED-236")] // App/Views/Settings/SettingsView.swift:118-122; OD-0074 (Rise fades the glass in)
    public void The_corners_fade_in_with_the_glass_of_the_Rise_open_because_they_are_the_glass() => WithManager((_, manager) =>
    {
        manager.ShowSettings(SettingsPage.Archive);
        var window = Settings(manager);
        Drain(window);
        var view = View(manager);
        // Rise (OD-0074) fades the glass in over the first 80 ms: the backdrop is the glass out to the window's rectangle, so there is no separate
        // corner layer to show its scrim before it.
        Assert.Contains(view.RiseBackdrop, SettingsRiseLayout.Gather(view).Glass);
        ZoomDrawn.Covers(new Avalonia.Size(Width, Height), view.RiseBackdrop, window);
    });
}
