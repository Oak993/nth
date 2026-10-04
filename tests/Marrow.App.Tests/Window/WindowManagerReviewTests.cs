// REVIEW (adversarial review of unit/blitz-windowmanager, 2026-10-04). Controlled-platform test, not physical-monitor proof.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class WindowManagerReviewTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    // WindowManager.swift:46-49: the Settings window's centre is put on the main window's centre (main.frame, the visible
    // window). ED-284 (1) models the factory HWND's invisible strip as lying on the left, right and bottom only
    // and compensates for the bottom strip by adding it to the Settings frame. A MAXIMISED Win32 window is different: Windows
    // places its HWND with the resize border outside the monitor on all FOUR sides, and Avalonia 12.1.2's WM_NCCALCSIZE for a
    // maximised extended-client window insets the client by that border on all four sides too (decompiled
    // Avalonia.Win32.WindowImpl, WM_NCCALCSIZE, ShowCmd == Maximize: Adjust for WS_BORDER | WS_THICKFRAME without WS_CAPTION,
    // top not zeroed), and Window.FrameSize/Position are GetWindowRect's. So a maximised main window on a 1920 x 1040 working
    // area is an HWND at (-8, -8) of 1936 x 1056 around a client of 1920 x 1040. Its visible centre is (960, 520); the Settings
    // window's visible frame (780 x 580) must be centred there: visible left 570 (HWND 562) and visible top 230 (HWND 230).
    // The port's arithmetic centres the Settings HWND (796 x 588) on the main HWND's centre and so leaves the visible
    // Settings window strip/2 = 4 px ABOVE the main window's centre (HWND top 226).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW: one case of PR-SETTINGS-002 (a visible, not minimised, maximised main window)
    public void REVIEW_Settings_is_centred_over_a_maximised_main_window_whose_HWND_strip_lies_on_all_four_sides()
    {
        WithManager((_, manager) =>
        {
            manager.ShowMain(); manager.ShowSettings();
            var main = MainOf(manager); var settings = Settings(manager);
            settings.Hide();
            using var strip = WindowInvisibleBorder.Pixels.Override((_, scale) => (int)Math.Round(8 * scale));
            main.Width = 1920; main.Height = 1040; Drain(main);
            using var parentPlatform = new SettingsWindowPlatformScope(main);
            parentPlatform.SetOuterFrame(new Avalonia.Size(1920 + 16, 1040 + 16)); // maximised: the strip on all four sides
            var primary = SettingsScreenScope.Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), primary: true);
            using var screens = new SettingsScreenScope(main, [primary], primary);
            main.Position = new PixelPoint(-8, -8);
            main.WindowState = WindowState.Maximized; // the state that makes the strip lie on all four sides
            Assert.True(main.IsVisible);
            Assert.NotEqual(WindowState.Minimized, main.WindowState);

            manager.ShowSettings();

            Assert.Equal(new PixelPoint(562, 230), settings.Position);
        }, realSettings: false);
    }
}
