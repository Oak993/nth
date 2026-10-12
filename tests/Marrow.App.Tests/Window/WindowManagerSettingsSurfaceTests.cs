// Radius/style markers and managed input are not native rendering proof.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Common;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// OD-0151 (ED-NEW-settingsinapp-1): Settings is a panel inside the main window (Port/SettingsOverlay.cs). Its glass is the panel's own
// backdrop, the app's glass over the app (SettingsOverlay.Backdrop); SettingsView's window-glass backdrop is hidden there.
[Collection("Headless")]
public sealed class WindowManagerSettingsSurfaceTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    // The backdrop Settings shows: the panel's, the only one visible in it.
    private static WindowBackdrop ShownBackdrop(WindowManager manager)
    {
        var overlay = Overlay(manager);
        Assert.False(View(manager).RiseBackdrop.IsVisible);
        Assert.Same(overlay.Backdrop, Assert.Single(Nodes<WindowBackdrop>(overlay.Card), backdrop => backdrop.IsVisible));
        return overlay.Backdrop;
    }

    // The main window shown in an appearance before Settings is built in it: an appearance change while the panel is open turns it over
    // (AppearanceTurn, OD-0088) on the frame clock, which these tests do not step.
    private static HostWindow MainIn(WindowManager manager, ThemeVariant appearance)
    {
        manager.ShowMain();
        var main = MainOf(manager);
        main.RequestedThemeVariant = appearance;
        Drain(main);
        return main;
    }

    private static void Settle(SettingsRiseRig rig, HostWindow window)
    {
        for (var i = 0; i < 30; i++) { rig.Frame(1.0 / 60); Drain(window); }
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Decision", "OD-0151")]
    public void A_live_opacity_change_reaches_the_actual_retained_Settings_backdrop()
    {
        // SettingsView.swift:119, ui_contract opacity binding; Chrome's light scrim at 0.75 is 0.59.
        WithManager((_, manager) =>
        {
            manager.Model.Settings.BackdropOpacity = 0.5;
            MainIn(manager, ThemeVariant.Light);
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager);
            Drain(window);
            var backdrop = ShownBackdrop(manager);
            Assert.Equal(ThemeVariant.Light, backdrop.ActualThemeVariant);
            SettingsBackdropAssertions.GlassOpacity(backdrop, 0.18);
            manager.Model.Settings.BackdropOpacity = 0.75;
            Drain(window);
            SettingsBackdropAssertions.GlassOpacity(backdrop, 0.59);
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Req", "PR-WIN-010")]
    [Trait("Decision", "OD-0151")]
    public void Actual_Settings_content_is_clipped_to_the_panel_s_26_point_continuous_shape_and_the_backdrop_fills_the_whole_panel()
    {
        // ui_contract.json:15428-15430; SettingsView.swift:118-122 as quoted there. OD-0151: the source's
        // `.clipShape(RoundedRectangle(cornerRadius: 26, style: .continuous))` is the panel's own shape, drawn by the app inside the main window
        // (no window rectangle for DWM to round any more).
        WithManager((_, manager) =>
        {
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager); Drain(window);
            var overlay = Overlay(manager);
            var view = View(manager);
            var card = Assert.IsAssignableFrom<Border>(overlay.Card);
            var size = card.Bounds.Size;
            Assert.True(size.Width > 0 && size.Height > 0);
            Assert.True(card.ClipToBounds);
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(card));
            Assert.Equal(new CornerRadius(Chrome.WindowCornerRadius), SwiftShapes.StatedCornerRadius(card));
            Assert.NotNull(card.Clip); // cut to Apple's curve
            // The content fills the panel from its top-left corner.
            Assert.Equal(size, view.Bounds.Size);
            Assert.Equal(new Point(0, 0), view.TranslatePoint(new Point(0, 0), card));
            var frame = Assert.IsType<SwiftShapeBorder>(view.Child);
            Assert.Equal(default, frame.CornerRadius);
            Assert.True(frame.ClipToBounds);
            Assert.Equal(size, frame.Bounds.Size);
            ZoomDrawn.Covers(new Avalonia.Size(size.Width * AppZoom.BaseScale, size.Height * AppZoom.BaseScale), frame, window); // drawn at the base scale (ED-NEW-textsize-2)
            // The panel's backdrop fills the panel out to its edge: its layers are the rectangle the panel's shape clips, and its hairline is
            // the inner stroke of that 26 pt shape (WindowBackdrop roundedHairline, ED-338).
            var backdrop = ShownBackdrop(manager);
            Assert.Equal(size, backdrop.Bounds.Size);
            Assert.Equal(new Point(0, 0), backdrop.TranslatePoint(new Point(0, 0), card));
            var layers = Assert.IsType<Panel>(backdrop.Child);
            var hairline = Assert.IsType<SwiftShapeBorder>(layers.Children[2]);
            Assert.Equal(new CornerRadius(Chrome.WindowCornerRadius), hairline.CornerRadius);
            Assert.Equal(RoundedCornerStyle.Continuous, hairline.CornerStyle);
            Assert.All(Nodes<SwiftShapeBorder>(backdrop).Where(layer => !ReferenceEquals(layer, hairline)), layer => Assert.Equal(default, layer.CornerRadius));
            Assert.False(Assert.Single(Nodes<WindowChromeProbe>(view)).IsHitTestVisible);
        });
    }

    [Theory]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Decision", "OD-0151")]
    [InlineData(false, 0.18, 0.09, 0.59)]
    [InlineData(true, 0.26, 0.13, 0.63)]
    public void Actual_Settings_backdrop_follows_initial_live_and_hidden_opacity(bool dark, double stock, double quarter, double threeQuarter)
    {
        // SettingsOverlay.cs supplies the setting to the panel's WindowBackdrop (as SettingsView.cs:169-172 does to its own).
        // Chrome.swift:211-215,249-259 (Chrome.cs:369-375,438-461): independently tabulated scrim values.
        WithManager((_, manager) =>
        {
            using var rig = new SettingsRiseRig();
            manager.Model.Settings.BackdropOpacity = 0.5;
            MainIn(manager, dark ? ThemeVariant.Dark : ThemeVariant.Light);
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager);
            Settle(rig, window);
            var overlay = Overlay(manager);
            var view = View(manager);
            var backdrop = ShownBackdrop(manager);
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, backdrop.ActualThemeVariant);
            SettingsBackdropAssertions.GlassOpacity(backdrop, stock);

            manager.Model.Settings.BackdropOpacity = 0.25; Drain(window);
            SettingsBackdropAssertions.GlassOpacity(backdrop, quarter);
            manager.Model.Settings.BackdropOpacity = 0; Drain(window);
            SettingsBackdropAssertions.GlassOpacity(backdrop, 0);
            manager.Model.Settings.BackdropOpacity = 0.75; Drain(window);
            SettingsBackdropAssertions.GlassOpacity(backdrop, threeQuarter);
            manager.Model.Settings.BackdropOpacity = 0.98; Drain(window);
            SettingsBackdropAssertions.Solid(backdrop, dark ? Colors.Black : Colors.White);

            manager.Model.Settings.BackdropOpacity = 0.25; Drain(window);
            manager.CloseSettings(); Settle(rig, window);
            Assert.False(manager.IsSettingsOpen); Assert.False(overlay.IsVisible); Assert.True(window.IsVisible);
            manager.Model.Settings.BackdropOpacity = 0.75;
            manager.ShowSettings(); Settle(rig, window);
            Assert.True(manager.IsSettingsOpen);
            Assert.Same(view, View(manager));
            Assert.Same(backdrop, ShownBackdrop(manager));
            SettingsBackdropAssertions.GlassOpacity(backdrop, threeQuarter);
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Decision", "OD-0151")]
    public void The_sidebar_background_inside_the_panel_is_not_a_caption_region_so_a_press_there_neither_drags_the_main_window_nor_closes_Settings()
    {
        // ui_contract.json:15437; SettingsView.cs:166 installs the background over the 212-point sidebar. In the Settings window it was the
        // window's caption region; inside the main window it would drag the main window from inside the panel, so the panel turns it off.
        WithManager((_, manager) =>
        {
            using var rig = new SettingsRiseRig();
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager);
            Settle(rig, window);
            var overlay = Overlay(manager);
            var drag = Assert.Single(Nodes<WindowDragArea>(View(manager)));
            Assert.Equal(212, drag.Bounds.Width);
            Assert.Equal(overlay.Card.Bounds.Height, drag.Bounds.Height, 3); // the sidebar runs the panel's full height
            Assert.False(drag.IsHitTestVisible);
            var point = At(drag, window, new Point(180, 18)); // Empty top strip of the sidebar.
            Assert.NotSame(drag, window.InputHitTest(point));
            AssertHitWithin(window, overlay.Card, point); // the panel, not the dim round it (a press there closes Settings)
            using var platform = new SettingsWindowPlatformScope(window);
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Drain(window);
            Assert.DoesNotContain("BeginMoveDrag", platform.Spy.Calls);
            Assert.True(manager.IsSettingsOpen);
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Decision", "OD-0151")]
    public void Search_and_page_controls_receive_input_without_starting_a_window_drag()
    {
        // SettingsView.cs:158-166,198-209,403: search/page buttons sit over the sidebar's drag background.
        WithManager((_, manager) =>
        {
            using var rig = new SettingsRiseRig();
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager);
            Settle(rig, window);
            var view = View(manager);
            var input = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            using var platform = new SettingsWindowPlatformScope(window);
            Click(window, input);
            Assert.True(input.IsFocused);
            var appearance = Assert.Single(Nodes<SidebarRow>(view).SelectMany(Nodes<Button>),
                button => AutomationProperties.GetName(button) == "Appearance");
            Click(window, appearance);
            Assert.Contains(Nodes<TextBlock>(view), text => text.Text == "Transparency");
            Assert.DoesNotContain("BeginMoveDrag", platform.Spy.Calls);
            Assert.True(manager.IsSettingsOpen); // input inside the panel keeps it open
        });
    }
}
