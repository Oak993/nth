// Radius/style markers and managed input are not native rendering proof.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class WindowManagerSettingsSurfaceTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    public void A_live_opacity_change_reaches_the_actual_retained_Settings_backdrop()
    {
        // SettingsView.swift:119, ui_contract opacity binding; Chrome's light scrim at 0.75 is 0.59.
        WithManager((_, manager) =>
        {
            manager.Model.Settings.BackdropOpacity = 0.5;
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager);
            window.RequestedThemeVariant = ThemeVariant.Light;
            Drain(window);
            var backdrop = Assert.Single(Nodes<WindowBackdrop>(View(manager)));
            SettingsBackdropAssertions.GlassOpacity(backdrop, 0.18);
            manager.Model.Settings.BackdropOpacity = 0.75;
            Drain(window);
            SettingsBackdropAssertions.GlassOpacity(backdrop, 0.59);
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Req", "PR-WIN-010")]
    public void Actual_Settings_content_has_26_point_clip_and_full_window_backdrop()
    {
        // ui_contract.json:15428-15430; SettingsView.swift:118-122 as quoted there.
        WithManager((_, manager) =>
        {
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager); Drain(window);
            var view = View(manager);
            Assert.Equal(new Avalonia.Size(780, 580), window.ClientSize);
            Assert.Equal(780, view.Width); Assert.Equal(580, view.Height);
            // OD-0075: the frame is the first layer; under it (ZIndex -1) the wedge between the window's rectangle and the 26 pt shape (WindowManagerCornerWedgeTests).
            var frame = Assert.IsType<SwiftShapeBorder>(Assert.IsType<Panel>(view.Child).Children[0]);
            Assert.Equal(new CornerRadius(26), frame.CornerRadius); // Literal oracle; I01 changes production 26 to 25.
            // SettingsView.swift:118-122 `.clipShape(RoundedRectangle(cornerRadius: 26, style: .continuous))` (ED-181 closed): the frame
            // clips its content to Apple's continuous path of the 780 x 580 window, not to a circular arc. The probes are the build
            // spec's (SPEC.md 5.1-3): at 22.6 pt along the top the circle is 0.22 pt below the edge, Apple's curve 0.585 pt.
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(frame));
            Assert.Equal(RoundedCornerStyle.Continuous, frame.CornerStyle);
            Assert.True(frame.ClipToBounds);
            var outline = SwiftShapes.Outline(new Rect(0, 0, 780, 580), frame.CornerRadius, frame.CornerStyle);
            Assert.All(outline.Corners, k => Assert.Equal(CornerKind.Continuous, k)); // 2 * 1.528665 * 26 = 79.5 <= 580
            var clip = Assert.IsAssignableFrom<Avalonia.Media.Geometry>(frame.Clip);
            Assert.Same(clip, SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, 780, 580), 26, RoundedCornerStyle.Continuous)); // the cached shared path
            foreach (var (x, y) in new[] { (22.6, 0.40), (757.4, 0.40), (22.6, 579.6), (757.4, 579.6), (0.40, 22.6), (779.6, 22.6), (0.40, 557.4), (779.6, 557.4) })
                Assert.False(clip.FillContains(new Point(x, y)), $"({x}, {y}) is inside the circle but outside Apple's curve");
            foreach (var (x, y) in new[] { (22.6, 0.75), (757.4, 0.75), (22.6, 579.25), (757.4, 579.25) })
                Assert.True(clip.FillContains(new Point(x, y)), $"({x}, {y}) is inside Apple's curve");
            Assert.False(clip.FillContains(new Point(1, 1))); // the corner itself is cut away
            var layers = Assert.IsType<Panel>(frame.Child);
            var backdrop = Assert.IsType<WindowBackdrop>(layers.Children[0]);
            Assert.Equal(new Avalonia.Size(780, 580), backdrop.Bounds.Size);
            Assert.Equal(new Point(0, 0), backdrop.TranslatePoint(new Point(0, 0), view));
            Assert.False(Assert.Single(Nodes<WindowChromeProbe>(view)).IsHitTestVisible);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
        });
    }

    [Theory]
    [Trait("Req", "UI-SETTINGS-001")]
    [InlineData(false, 0.18, 0.09, 0.59)]
    [InlineData(true, 0.26, 0.13, 0.63)]
    public void Actual_Settings_backdrop_follows_initial_live_and_hidden_opacity(bool dark, double stock, double quarter, double threeQuarter)
    {
        // SettingsView.cs:169-172 supplies the setting to the real WindowBackdrop.
        // Chrome.swift:211-215,249-259 (Chrome.cs:369-375,438-461): independently tabulated scrim values.
        WithManager((_, manager) =>
        {
            manager.Model.Settings.BackdropOpacity = 0.5;
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager);
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Drain(window);
            var view = View(manager);
            var backdrop = Assert.Single(Nodes<WindowBackdrop>(view));
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
            window.Close(); Assert.False(window.IsVisible);
            manager.Model.Settings.BackdropOpacity = 0.75;
            manager.ShowSettings(); Drain(window);
            Assert.Same(view, View(manager));
            Assert.Same(backdrop, Assert.Single(Nodes<WindowBackdrop>(view)));
            SettingsBackdropAssertions.GlassOpacity(backdrop, threeQuarter);
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    public void Sidebar_background_is_a_hit_tested_caption_region()
    {
        // Headless hit testing proves the managed caption region; the production Settings HWND reports it as HTCAPTION to the system
        // (WindowManagerNativeTests.The_actual_Settings_sidebar_background_reaches_native_caption_hit_testing, a real native hit test),
        // and its caption buttons are driven by real mouse input in the same class (RESOLVED SETTINGS-NATIVE-SURFACE).
        // ui_contract.json:15437; SettingsView.cs:166 installs the background over the 212-point sidebar.
        WithManager((_, manager) =>
        {
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager); Drain(window);
            var drag = Assert.Single(Nodes<WindowDragArea>(View(manager)));
            Assert.Equal(212, drag.Bounds.Width);
            Assert.Equal(580, drag.Bounds.Height);
            Assert.Equal(Win32Properties.Win32HitTestValue.Caption, Win32Properties.GetNonClientHitTestResult(drag));
            var point = At(drag, window, new Point(180, 18)); // Empty top strip beyond the leading caption controls.
            Assert.Same(drag, window.InputHitTest(point));
            window.MouseMove(point); Drain(window);
            Assert.True(drag.IsPointerOver);
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-001")]
    public void Search_and_page_controls_receive_input_without_starting_a_window_drag()
    {
        // SettingsView.cs:158-166,198-209,403: search/page buttons sit over the sidebar's drag background.
        WithManager((_, manager) =>
        {
            manager.ShowSettings(SettingsPage.Archive);
            var window = Settings(manager); Drain(window);
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
        });
    }

}
