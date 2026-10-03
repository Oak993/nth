using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.UI.Chrome;
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

    private static readonly (int X, int Y)[] Corners =
    [
        (0, 0), (1, 1), (5, 5), (Width - 1, 0), (Width - 2, 1), (Width - 6, 5),
        (0, Height - 1), (1, Height - 2), (5, Height - 6), (Width - 1, Height - 1), (Width - 2, Height - 2), (Width - 6, Height - 6),
    ];

    // The scrim over the grey: black in the dark theme, white in the light one (Chrome.swift:256-257), as a fraction of 255.
    private static double Over(double alpha, bool dark) => (Grey.R * (1 - alpha) + (dark ? 0 : 255) * alpha) / 255.0;

    private static void AssertCorners(Avalonia.Controls.Window window, double expected, string when)
    {
        var levels = FramePixels.Levels(window, Corners);
        for (var index = 0; index < levels.Length; index++)
            Assert.True(Math.Abs(levels[index] - expected) <= 0.012, $"{when}: corner pixel {Corners[index]} is {levels[index]:0.###}, the scrim over the grey is {expected:0.###}");
    }

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
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Decision", "ED-236")] // App/Views/Settings/SettingsView.swift:118-122; OD-0075
    public void The_settings_windows_four_corners_outside_the_26_point_curve_wear_its_scrim(bool dark) => WithManager((_, manager) =>
    {
        manager.Model.Settings.BackdropOpacity = 0.5;
        var window = OpenOnGrey(manager, dark);
        Assert.Equal(new Avalonia.Size(Width, Height), window.ClientSize);
        AssertCorners(window, Over(WindowBackdrop.Scrim(0.5, dark), dark), "at the default glass");
    });

    [Fact]
    [Trait("Decision", "ED-236")] // UI/Chrome/Chrome.swift:211-224; OD-0075
    public void The_settings_corner_scrim_follows_the_backdrop_opacity_the_theme_and_the_solid_window() => WithManager((_, manager) =>
    {
        manager.Model.Settings.BackdropOpacity = 0.25;
        var window = OpenOnGrey(manager, dark: true);
        AssertCorners(window, Over(WindowBackdrop.Scrim(0.25, true), true), "at 0.25 dark");
        manager.Model.Settings.BackdropOpacity = 0.75; Drain(window);
        AssertCorners(window, Over(WindowBackdrop.Scrim(0.75, true), true), "at 0.75 dark");
        window.RequestedThemeVariant = ThemeVariant.Light; Drain(window);
        AssertCorners(window, Over(WindowBackdrop.Scrim(0.75, false), false), "at 0.75 light");
        manager.Model.Settings.BackdropOpacity = 1; Drain(window);
        AssertCorners(window, 1.0, "solid light: the window is white, corners too");
        window.RequestedThemeVariant = ThemeVariant.Dark; Drain(window);
        AssertCorners(window, 0.0, "solid dark: the window is black, corners too");
    });

    [Fact]
    [Trait("Decision", "ED-236")] // App/Views/Settings/SettingsView.swift:118-122; OD-0074 (Rise fades the glass in)
    public void The_wedge_sits_under_the_clipped_frame_takes_no_pointer_and_fades_in_with_the_glass_of_the_Rise_open() => WithManager((_, manager) =>
    {
        manager.ShowSettings(SettingsPage.Archive);
        var window = Settings(manager);
        Drain(window);
        var view = View(manager);
        var wedge = view.RiseWedge;
        var layers = Assert.IsAssignableFrom<Panel>(view.Child);
        var frame = Assert.IsType<Border>(layers.Children[0]);
        Assert.Same(wedge, layers.Children[1]);
        Assert.True(wedge.ZIndex < frame.ZIndex, "painted first, under the frame whose 26 pt clip the glass and its edge stop at");
        Assert.Equal(new CornerRadius(26), frame.CornerRadius);
        Assert.True(frame.ClipToBounds);
        Assert.False(wedge.IsHitTestVisible);
        Assert.Equal(new Avalonia.Size(Width, Height), wedge.Bounds.Size);
        Assert.Equal(new Point(0, 0), wedge.TranslatePoint(new Point(0, 0), view));
        // Rise (OD-0074) fades the glass in over the first 80 ms: the corners belong to the glass, or they would show their scrim before it.
        Assert.Contains(wedge, SettingsRiseLayout.Gather(view).Glass);
        Assert.Contains(view.RiseBackdrop, SettingsRiseLayout.Gather(view).Glass);
    });
}
