// Controlled-platform integration tests, not physical-monitor proof.
using Avalonia;
using Avalonia.Platform;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class WindowManagerSettingsScreenTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-002")] // WindowManager.swift:46-54, logical rounding precedes pixel conversion.
    [Trait("Req", "PR-WIN-011")]
    public void Mixed_DPI_centering_uses_both_parent_coordinates_and_rounds_before_pixel_conversion()
    {
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            using var platform = new SettingsWindowPlatformScope(main);
            platform.Spy.DesktopScale = 1.5;
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), primary: true);
            var secondary = SettingsScreenScope.Monitor(new PixelRect(1920, 0, 3200, 1800), new PixelRect(1960, 80, 3000, 1640), 1.5);
            using var screens = new SettingsScreenScope(main, [primary, secondary], secondary);
            main.Position = new PixelPoint(2100, 100);
            manager.ShowSettings();
            Assert.Equal(new PixelPoint(2505, 311), settings.Position);
            Assert.Same(platform.Platform, screens.Impl.LastWindow);
            Assert.Equal(780, settings.Width); Assert.Equal(580, settings.Height);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")] // WindowManager.swift:48, .rounded() is half away from zero.
    [Trait("Req", "PR-WIN-011")]
    public void A_positive_half_point_center_rounds_up_before_the_workarea_clamp()
    {
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            main.Width = 1321; main.Height = 861; Drain(main);
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 3000, 2000), new PixelRect(0, 0, 3000, 1960), primary: true);
            using var screens = new SettingsScreenScope(main, [primary], primary);
            main.Position = new PixelPoint(100, 80);
            manager.ShowSettings();
            // AppKit x = 100 + 1321/2 - 780/2 = 370.5, rounded to 371.
            // AppKit y = 2000 - 80 - 861/2 - 580/2 = 1199.5, rounded to 1200.
            Assert.Equal(new PixelPoint(371, 220), settings.Position);
        }, realSettings: false);
    }

    [Theory]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-WIN-011")]
    [InlineData(1.0, 2370, 240)]
    [InlineData(1.25, 2438, 275)]
    [InlineData(1.5, 2505, 311)]
    [InlineData(2.0, 2640, 380)]
    public void Secondary_monitor_and_main_DPI_determine_settings_origin(double scale, int x, int y)
    {
        // WindowManager.swift:46-54. Point-space rounding precedes the final physical-pixel conversion.
        // At 150%, AppKit y rounds -66.666... to -67, then the Windows top is 310.5 pixels -> 311.
        WithManager((_, manager) =>
        {
            manager.ShowMain();
            manager.ShowSettings();
            var main = MainOf(manager);
            var settings = Settings(manager);
            settings.Hide();
            using var platform = new SettingsWindowPlatformScope(main);
            platform.Spy.DesktopScale = scale;
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), primary: true);
            var secondary = SettingsScreenScope.Monitor(new PixelRect(1920, 0, 3200, 1800), new PixelRect(1960, 80, 3000, 1640), scale);
            using var screens = new SettingsScreenScope(main, [primary, secondary], secondary);
            main.Position = new PixelPoint(2100, 100);
            Assert.Equal(scale, main.DesktopScaling);
            Assert.Equal(1.0, settings.DesktopScaling); // A deliberate discriminator against using the child's DPI.
            Assert.Same(primary, main.Screens.Primary);
            var before = screens.Impl.WindowLookups;

            manager.ShowSettings();

            Assert.True(screens.Impl.WindowLookups > before); // B12 must reach the real main.Screens path.
            Assert.Same(platform.Platform, screens.Impl.LastWindow);
            Assert.Equal(new PixelPoint(x, y), settings.Position);
            Assert.Equal(780, settings.Width);
            Assert.Equal(580, settings.Height);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-WIN-011")]
    public void Workarea_edges_exclude_taskbars_on_the_main_monitor()
    {
        // WindowManager.swift:50-52 uses visibleFrame, not full screen bounds. B18 and B13.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            using var platform = new SettingsWindowPlatformScope(main);
            platform.Spy.DesktopScale = 2;
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), primary: true);
            var secondary = SettingsScreenScope.Monitor(new PixelRect(1920, 0, 3200, 1800), new PixelRect(1960, 80, 3000, 1640), 2);
            using var screens = new SettingsScreenScope(main, [primary, secondary], secondary);
            main.Position = new PixelPoint(4300, 1300);

            manager.ShowSettings();

            // Work right=4960 and bottom=1720; the Settings frame is 1560x1160 physical pixels.
            // Bounds would instead yield (3560,640), so that mutation cannot share this answer.
            Assert.Equal(new PixelPoint(3400, 560), settings.Position);
            Assert.Same(platform.Platform, screens.Impl.LastWindow);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-WIN-011")]
    public void Missing_main_screen_uses_primary_workarea()
    {
        // WindowManager.swift:50 explicitly falls back from main.screen to NSScreen.main.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(80, 40, 1800, 960), primary: true);
            using var screens = new SettingsScreenScope(main, [primary], selected: null);
            main.Position = new PixelPoint(1800, 900);

            manager.ShowSettings();

            Assert.True(screens.Impl.WindowLookups > 0);
            Assert.Equal(new PixelPoint(1100, 420), settings.Position);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-WIN-011")]
    public void No_screens_skips_the_clamp_but_keeps_source_rounding()
    {
        // WindowManager.swift:48-54, with no screen. Neither (0,0) nor a guessed display is substituted.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            main.Width = 1321; main.Height = 861; Drain(main);
            using var screens = new SettingsScreenScope(main, [], selected: null);
            main.Position = new PixelPoint(-810, -800);

            manager.ShowSettings();

            Assert.Null(main.Screens.Primary);
            Assert.True(screens.Impl.WindowLookups > 0);
            Assert.Equal(new PixelPoint(-540, -660), settings.Position);
            Assert.Equal(780, settings.Width); Assert.Equal(580, settings.Height);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-WIN-011")]
    public void Negative_origin_monitor_uses_its_left_and_top_workarea_edges()
    {
        // WindowManager.swift:50-52. A display above/left of primary retains its signed global origin.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), primary: true);
            var secondary = SettingsScreenScope.Monitor(new PixelRect(-2560, -1440, 2560, 1440), new PixelRect(-2520, -1400, 2480, 1360));
            using var screens = new SettingsScreenScope(main, [primary, secondary], secondary);
            main.Position = new PixelPoint(-3000, -1800);

            manager.ShowSettings();

            Assert.Equal(new PixelPoint(-2520, -1400), settings.Position);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-WIN-011")]
    public void Smaller_workarea_preserves_outer_min_without_resizing_settings()
    {
        // WindowManager.swift:51-52: min(max(...), farEdge-frameSize), even when frame is too large.
        // AppKit's maxY corresponds to the Windows TOP; do not assert the Windows bottom edge here.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            var screen = SettingsScreenScope.Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(100, 200, 600, 400), primary: true);
            using var screens = new SettingsScreenScope(main, [screen], screen);
            main.Position = new PixelPoint(200, 300);

            manager.ShowSettings();

            Assert.Equal(new PixelPoint(-80, 200), settings.Position);
            Assert.Equal(780, settings.Width); Assert.Equal(580, settings.Height);
            Assert.Equal(700, settings.Position.X + settings.Width); // Source outer min pins the physical right edge.
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-WIN-011")]
    public void Outer_frame_extents_are_used_for_both_parent_and_settings()
    {
        // WindowManager.swift:46-47 takes NSWindow.frame, not the hosted content's frame.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            using var parentPlatform = new SettingsWindowPlatformScope(main);
            using var childPlatform = new SettingsWindowPlatformScope(settings);
            parentPlatform.SetOuterFrame(new Avalonia.Size(main.ClientSize.Width + 20, main.ClientSize.Height + 40));
            childPlatform.SetOuterFrame(new Avalonia.Size(settings.ClientSize.Width + 10, settings.ClientSize.Height + 20));
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 3000, 2000), new PixelRect(0, 0, 3000, 1960), primary: true);
            using var screens = new SettingsScreenScope(main, [primary], primary);
            main.Position = new PixelPoint(100, 80);

            manager.ShowSettings();

            // (1340-790)/2=275; (900-600)/2=150. Content-only math yields (370,220).
            Assert.Equal(new PixelPoint(375, 230), settings.Position);
            Assert.Equal(780, settings.Width); Assert.Equal(580, settings.Height);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-WIN-011")]
    public void Reopening_requeries_the_main_windows_current_monitor()
    {
        // WindowManager.swift:34-37 reuses the window but recomputes its center on every request.
        WithManager((_, manager) =>
        {
            manager.ShowMain();
            var main = MainOf(manager);
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), primary: true);
            var secondary = SettingsScreenScope.Monitor(new PixelRect(-2560, -1440, 2560, 1440), new PixelRect(-2520, -1400, 2480, 1360));
            using var screens = new SettingsScreenScope(main, [primary, secondary], primary);
            main.Position = new PixelPoint(200, 100);
            manager.ShowSettings(); var settings = Settings(manager);
            Assert.Equal(new PixelPoint(470, 240), settings.Position);
            settings.Hide();
            var before = screens.Impl.WindowLookups;
            screens.Impl.WindowScreen = secondary;
            main.Position = new PixelPoint(-2300, -1300);

            manager.ShowSettings();

            Assert.Same(settings, Settings(manager));
            Assert.True(screens.Impl.WindowLookups > before);
            Assert.Equal(new PixelPoint(-2030, -1160), settings.Position);
        }, realSettings: false);
    }

    // ENGINEERING DECISION (ED-NEW-windowmanager, WindowInvisibleBorder): the frame AppKit centres and clamps is the visible
    // window, and an HWND of the factory's chrome keeps an invisible strip (8 px at 100 %) on its left, right and bottom. The
    // strip is stood in for here (a headless window has no HWND); the real HWND is WindowManagerNativeTests.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-SETTINGS-002 and PR-WIN-011: the arithmetic with a strip; the HWND itself is native
    public void The_invisible_strip_of_an_HWND_stays_outside_the_frame_that_is_centred_over_the_main_window()
    {
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            using var strip = WindowInvisibleBorder.Pixels.Override((_, scale) => (int)Math.Round(8 * scale));
            using var parentPlatform = new SettingsWindowPlatformScope(main);
            parentPlatform.SetOuterFrame(new Avalonia.Size(main.ClientSize.Width + 16, main.ClientSize.Height + 8)); // the same strip
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 3000, 2000), new PixelRect(0, 0, 3000, 1960), primary: true);
            using var screens = new SettingsScreenScope(main, [primary], primary);
            main.Position = new PixelPoint(100, 80);

            manager.ShowSettings();

            // The visible windows are centred on one another: main's visible frame is 1320 x 860 at (108, 80), Settings' is
            // 780 x 580, so its visible left is 108 + 660 - 390 = 378 and its top 80 + 430 - 290 = 220; its HWND starts
            // 8 px left of its visible window (370) and at the same top. Without the strip the origin would be (378, 224).
            Assert.Equal(new PixelPoint(370, 220), settings.Position);
        }, realSettings: false);
    }

    [Theory]
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-SETTINGS-002 and PR-WIN-011 (the clamp, WindowManager.swift:50-52); the HWND itself is native
    [InlineData(-600, 0, -8, 136)] // clamped at the left: the VISIBLE window touches the working area (HWND 8 px outside it)
    [InlineData(1700, 950, 1132, 460)] // clamped at the right and the bottom: the visible window ends at 1920 and 1040
    public void The_clamp_keeps_the_visible_window_in_the_working_area_and_lets_the_invisible_strip_outside(int mainX, int mainY, int x, int y)
    {
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            using var strip = WindowInvisibleBorder.Pixels.Override((_, scale) => (int)Math.Round(8 * scale));
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), primary: true);
            using var screens = new SettingsScreenScope(main, [primary], primary);
            main.Position = new PixelPoint(mainX, mainY);

            manager.ShowSettings();

            Assert.Equal(new PixelPoint(x, y), settings.Position);
            // visible left >= 0 and visible right <= 1920, as AppKit's clamp of the frame (:51)
            Assert.True(settings.Position.X + 8 >= 0 && settings.Position.X + 8 + 780 <= 1920);
        }, realSettings: false);
    }
}
