// Platform callbacks are observed only on headless test windows.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class WindowManagerSettingsPresentationTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-WIN-011")]
    public void Cached_show_centers_before_the_platform_show_request()
    {
        // RESOLVED DISPLAY-PASS (ED-284): AppKit's setFrame(display: false) has no Windows analogue, so that clause is
        // recorded NOT_APPLICABLE; the observable part, that the origin is set before the window is shown (:36 before :37), is
        // what this test asserts (the Rise open shows the window cloaked at its final position, so no frame is ever displayed at an
        // old one).
        // WindowManager.swift:36 precedes :37. B14 cannot hide behind a correct final Position.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 3000, 2000), new PixelRect(0, 0, 3000, 1960), primary: true);
            using var screens = new SettingsScreenScope(main, [primary], primary);
            main.Position = new PixelPoint(150, 120);
            settings.Position = new PixelPoint(13, 17);
            using var platform = new SettingsWindowPlatformScope(settings);
            var positionsAtShow = new List<PixelPoint>();
            platform.Spy.BeforeShow = () => positionsAtShow.Add(settings.Position);

            manager.ShowSettings();

            Assert.Equal(new PixelPoint(420, 260), Assert.Single(positionsAtShow));
            Assert.True(platform.Spy.Calls.IndexOf("Move") >= 0);
            Assert.True(platform.Spy.Calls.IndexOf("Move") < platform.Spy.Calls.IndexOf("Show"));
            Assert.Equal(new PixelPoint(420, 260), settings.Position);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "PR-WIN-011")]
    public void Visible_inactive_settings_receives_explicit_activation()
    {
        // WindowManager.swift:58-59; B15. Show() on an already-visible window cannot supply activation.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var settings = Settings(manager);
            Drain(settings);
            Deactivate(settings);
            Assert.False(settings.IsActive);
            MainOf(manager).Position = new PixelPoint(150, 120);
            settings.Position = new PixelPoint(13, 17);
            using var platform = new SettingsWindowPlatformScope(settings);

            manager.ShowSettings();
            Drain(settings);

            Assert.Equal(1, platform.Spy.Calls.Count(call => call == "Activate"));
            Assert.True(settings.IsVisible);
            Assert.True(settings.IsActive);
            Assert.Same(settings, Settings(manager));
            Assert.Equal(new PixelPoint(420, 260), settings.Position);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "PR-WIN-010")]
    public void Settings_factory_itself_installs_fixed_sizing_and_native_chrome_properties()
    {
        // RESOLVED (RECORD != SWIFT): WIN.json PR-WIN-010 says "owned top-level window" and PR-SETTINGS-001 "child window",
        // but WindowManager.swift:80-84, :57-60 build an independent NSWindow with no parent, so the port sets no owner; the
        // test below (Settings_and_main_windows_have_no_owner_relationship) asserts that absence.
        // WindowManager.swift:80-84,92-110. Border content prevents SettingsView's probe masking missing Configure.
        WithManager((_, manager) =>
        {
            manager.ShowSettings();
            var settings = Settings(manager);
            Assert.Equal("Settings", settings.Title);
            Assert.Equal(780, settings.Width);
            Assert.Equal(580, settings.Height);
            Assert.True(settings.MinWidth <= 780);
            Assert.True(settings.MinHeight <= 580);
            Assert.False(settings.CanResize);
            Assert.True(settings.CanMinimize);
            Assert.True(settings.CanMaximize); // T45 owner caption override.
            Assert.True(settings.ExtendClientAreaToDecorationsHint);
            Assert.Equal(WindowDecorations.Full, settings.WindowDecorations);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(settings.Background).Color);
            Assert.True(settings.IsVisible);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "PR-WIN-010")]
    public void Settings_and_main_windows_have_no_owner_relationship()
    {
        // WindowManager.swift:80-84 builds the Settings window with makeWindow, an independent NSWindow, and :57-60 only
        // activates and orders it front: it is not a child of the main window (no addChildWindow, no level change). The
        // records' words "owned" and "child window" are not in the source (RECORD != SWIFT), so the port sets no Owner.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            Assert.True(main.IsVisible);
            Assert.True(settings.IsVisible);
            Assert.Null(settings.Owner);
            Assert.Null(main.Owner);
            Assert.Empty(settings.OwnedWindows);
            Assert.Empty(main.OwnedWindows);
            Assert.False(settings.Topmost);
            Assert.False(main.Topmost);
            // closing and reopening it again leaves it unowned (every show re-centres it, :36)
            settings.Close();
            manager.ShowSettings();
            Assert.Null(settings.Owner);
            Assert.Empty(main.OwnedWindows);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "PR-WIN-010")]
    public void Settings_movement_close_and_reopen_do_not_write_frame_preferences()
    {
        // WindowManager.swift:80-84 has no autosave name or FrameAutosave attachment.
        WithManager((harness, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var settings = Settings(manager);
            var content = settings.Content;
            Drain(MainOf(manager));
            Drain(settings);
            harness.Defaults.Writes.Clear();

            settings.Position = new PixelPoint(83, 117);
            settings.Close();
            Assert.False(settings.IsVisible);
            manager.ShowSettings();
            Drain(settings);

            Assert.Same(settings, Settings(manager));
            Assert.Same(content, settings.Content);
            Assert.True(settings.IsVisible);
            Assert.Empty(harness.Defaults.Writes);
        }, realSettings: false);
    }
}
