// Platform callbacks are observed only on headless test windows.
using Marrow.App.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Window;

// OD-0151 (ED-NEW-settingsinapp-1): Settings is a panel inside the main window (Port/SettingsOverlay.cs), so there is no Settings window
// to own, to centre over the main window or to give a frame of its own; these tests hold the panel to what the window did.
[Collection("Headless")]
public sealed class WindowManagerSettingsPresentationTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-windows-45")]
    [Trait("Decision", "OD-0151")]
    public void Settings_opened_first_opens_in_the_main_window_and_ShowMain_keeps_its_panel_and_content()
    {
        WithManager((_, manager) =>
        {
            manager.ShowSettings(); var window = Settings(manager); var overlay = Overlay(manager); var content = overlay.Content;
            var main = MainOf(manager);
            Assert.Same(main, window); // Settings asked for first shows the main window with the panel in it
            Assert.True(main.IsVisible);
            manager.ShowMain();
            Assert.Same(main, MainOf(manager));
            Assert.Same(overlay, Overlay(manager));
            Assert.Same(content, overlay.Content);
            Assert.True(manager.IsSettingsOpen); // showing the main window leaves Settings open in it
            Assert.True(main.IsVisible);
            Assert.False(main.Topmost);
            Assert.Empty(main.OwnedWindows); // no Settings window to own
            manager.CloseSettings(); Assert.False(manager.IsSettingsOpen); manager.ShowSettings();
            Assert.Same(main, Settings(manager)); Assert.Same(overlay, Overlay(manager)); Assert.Same(content, overlay.Content);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "PR-WIN-011")]
    [Trait("Decision", "OD-0151")]
    public void A_visible_inactive_main_window_receives_explicit_activation_when_Settings_is_asked_for()
    {
        // WindowManager.swift:37, :58-59; B15. Show() on an already-visible window cannot supply activation. Settings is presented in the main
        // window (OD-0151), so asking for it again activates that window.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = Settings(manager);
            Drain(main);
            Deactivate(main);
            Assert.False(main.IsActive);
            using var platform = new SettingsWindowPlatformScope(main);

            manager.ShowSettings();
            Drain(main);

            Assert.Equal(1, platform.Spy.Calls.Count(call => call == "Activate"));
            Assert.True(main.IsVisible);
            Assert.True(main.IsActive);
            Assert.Same(main, Settings(manager));
            Assert.Same(MainOf(manager), main);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "PR-WIN-010")]
    [Trait("Decision", "ED-NEW-windows-45")]
    [Trait("Decision", "OD-0151")]
    public void Settings_opens_in_main_with_no_owned_window_on_every_open_and_a_close_keeps_main_visible()
    {
        // Owner instruction 2026-10-08 (Settings cannot be lost behind Main) is met by OD-0151: Settings is inside Main.
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var window = Settings(manager);
            Assert.Same(main, window);
            Assert.True(main.IsVisible);
            Assert.True(manager.IsSettingsOpen);
            Assert.Null(main.Owner);
            Assert.Empty(main.OwnedWindows);
            Assert.False(main.Topmost);
            main.Activate();
            manager.CloseSettings();
            Assert.False(manager.IsSettingsOpen);
            Assert.True(main.IsVisible);
            manager.ShowSettings();
            Assert.Same(main, Settings(manager));
            Assert.Empty(main.OwnedWindows);
        }, realSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-001")]
    [Trait("Req", "PR-WIN-010")]
    [Trait("Decision", "OD-0151")]
    public void Settings_close_and_reopen_do_not_write_frame_preferences()
    {
        // WindowManager.swift:80-84 has no autosave name or FrameAutosave attachment; the panel has no frame, and opening and closing it
        // leaves the main window's frame alone.
        WithManager((harness, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = Settings(manager);
            var overlay = Overlay(manager);
            var content = overlay.Content;
            Drain(main);
            harness.Defaults.Writes.Clear();

            manager.CloseSettings();
            Assert.False(manager.IsSettingsOpen);
            Assert.True(main.IsVisible);
            manager.ShowSettings();
            Drain(main);

            Assert.Same(main, Settings(manager));
            Assert.Same(overlay, Overlay(manager));
            Assert.Same(content, overlay.Content);
            Assert.True(manager.IsSettingsOpen);
            Assert.Empty(harness.Defaults.Writes);
        }, realSettings: false);
    }
}
