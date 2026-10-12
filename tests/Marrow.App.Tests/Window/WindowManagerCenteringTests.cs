using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// Source: DroppyCode/App/Window/WindowManager.swift (B3 52bc792), :43, :68, :82.
// ED-225 on docs/b3 supplies the otherwise unspecified NSWindow.center placement. Its bias remains UNVERIFIED.
[Collection("Headless")]
public sealed class WindowManagerCenteringTests(HeadlessSession headless)
{
    private static void Settle(AvaloniaWindow window)
    {
        for (var i = 0; i < 5; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    private void WithManager(Action<WindowManager, AppModelHarness, WindowCenteringScreens, WindowCenteringCursor> check) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness(withRuntime: false);
        var originalAppearance = Application.Current!.RequestedThemeVariant;
        var originalTheme = ThemeManager.Current;
        var pointer = new WindowCenteringScreen(new PixelRect(-1600, -1200, 1600, 1200), new PixelRect(-1600, -1200, 1600, 1160), primary: true);
        var owner = new WindowCenteringScreen(new PixelRect(2000, 0, 1800, 1280), new PixelRect(2000, 100, 1800, 1180));
        var screens = new WindowCenteringScreens(pointer, owner);
        var cursor = new WindowCenteringCursor { Position = new PixelPoint(-800, -700) };
        using var cursorScope = WindowCenteringTestHooks.OverrideCursor(cursor, required: true);
        using var ownerScope = WindowCenteringTestHooks.OverrideOwnerScreen(screens);
        using var pointerScope = WindowCenteringTestHooks.OverridePointerScreen(screens);
        var manager = new WindowManager { MainContent = () => new Border(), SettingsContent = () => new Border() };
        try { check(manager, harness, screens, cursor); }
        finally
        {
            if (manager.MainWindow is { } main) { main.Content = null; main.Hide(); }
            manager.SettingsOverlay?.Close();
            ThemeManager.Current = originalTheme;
            Application.Current.RequestedThemeVariant = originalAppearance;
            Dispatcher.UIThread.RunJobs();
        }
    });

    // WindowManager.swift:65 contentMinSize 860 x 560 is the CLIENT's minimum; Avalonia adds the standard frame's caption to the minimum tracking
    // height of a window whose client is extended under it (ED-332 (1), measured natively: 591 for 560), so the model's MinHeight
    // is taken down by that caption at the window's scale, and follows a change of scale. Stood in here as 31 px at 100 %.
    [Fact]
    [Trait("Req", "UI-LAUNCH-001")] // WindowManager.swift:65 contentMinSize; the HWND is the native test (WindowManagerNativeTests)
    [Trait("Req", "PR-LAUNCH-028")] // the same minimum
    public void The_main_minimum_height_gives_back_the_caption_Avalonia_adds_to_the_tracking_size_and_follows_the_scale() => WithManager((manager, harness, screens, cursor) =>
    {
        using var caption = Marrow.App.Port.WindowInvisibleBorder.CaptionAboveClient.Override((_, scale) => scale > 1.5 ? 40 : 31); // 31 px at 100 %, 40 px at 200 % (so a change of scale shows)
        manager.ShowMain();
        var main = manager.MainWindow!;
        Assert.Equal(860, main.MinWidth);
        Assert.Equal(1, main.RenderScaling);
        Assert.Equal(529, main.MinHeight);
        typeof(Avalonia.Controls.TopLevel).GetMethod("HandleScalingChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [2.0]);
        Assert.Equal(2, main.RenderScaling);
        Assert.Equal(560 - 40 / 2.0, main.MinHeight);
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:67-70; ED-225
    [Trait("Req", "UI-LAUNCH-001")] // the first launch's content, 69 % x 92 % of the work area, centred (ED-NEW-settingsinapp-3, OD-0151)
    [Trait("Decision", "ED-NEW-settingsinapp-3")]
    public void A_first_run_main_takes_69_by_92_percent_of_the_pointer_monitor_work_area_centred_both_ways() => WithManager((manager, harness, screens, cursor) =>
    {
        manager.ShowMain();
        var main = manager.MainWindow!;
        // Work area (-1600,-1200,1600,1160): 69 % of 1600 is 1104, 92 % of 1160 is 1067.2 (1067); free 496x93, offsets 248x46.5 (away from zero).
        Assert.Equal(new PixelPoint(-1352, -1154), main.Position);
        Assert.Equal(new Size(1104, 1067), main.ClientSize);
        Assert.Equal(cursor.Position, screens.LastPoint);
        Assert.Equal(1, screens.PointReads);
        Assert.Equal(0, screens.OwnerReads);
    });

    // ENGINEERING DECISION (ED-284, WindowInvisibleBorder): the frame ED-225 centres is the visible window; an
    // HWND of the factory's chrome keeps an invisible 8 px strip on its left, right and bottom and none above, so the HWND is
    // placed 8 px left of the centred visible window and at its top. Stood in for here; the real HWND is the native test.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-WIN-006 and UI-LAUNCH-001: arithmetic with the strip; the HWND is native
    public void The_visible_main_window_is_centred_and_its_invisible_strip_lies_outside_it() => WithManager((manager, harness, screens, cursor) =>
    {
        using var strip = Marrow.App.Port.WindowInvisibleBorder.Pixels.Override((_, scale) => (int)Math.Round(8 * scale));
        manager.ShowMain();
        // Work area (-1600,-1200,1600,1160), visible frame 1104x1067 (ED-NEW-settingsinapp-3): offsets 248x46.5: visible left -1352.
        Assert.Equal(new PixelPoint(-1360, -1154), manager.MainWindow!.Position);
    });

    // ENGINEERING DECISION (ED-284, WindowInvisibleBorder), arithmetic with a stood-in strip of 12 px (not the 8 px
    // the other tests use, so a constant 8 or a dropped term both differ): the HWND lies one strip left of the centred visible
    // window and at its top (no strip above). Proves WindowCentering.Center's left term and its absent top term; it does not
    // prove that a real HWND has such a strip (WindowManagerNativeCentringTests).
    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:67-70; ED-225, ED-284 (1)
    public void The_HWND_is_placed_one_invisible_strip_left_of_the_centred_visible_window_and_at_its_top() => WithManager((manager, harness, screens, cursor) =>
    {
        using var strip = Marrow.App.Port.WindowInvisibleBorder.Pixels.Override((_, scale) => (int)Math.Round(12 * scale));
        manager.ShowMain();
        // Pointer monitor work area (-1600,-1200,1600,1160), visible frame 1104x1067 (ED-NEW-settingsinapp-3): offsets 248x46.5: visible
        // left -1352, top -1154.
        Assert.Equal(new PixelPoint(-1364, -1154), manager.MainWindow!.Position);
    });

    // The clamp of WindowCentering.Center keeps the visible window, not the HWND, inside the working area: a frame wider and
    // taller than the work area keeps its leading edges on it, so the visible left edge is at the area's left (the HWND one
    // strip outside it) and the top at its top. Without the strip term the HWND would start at the area's left.
    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:67-70; ED-225, ED-284 (1)
    public void A_visible_main_window_larger_than_the_work_area_keeps_its_left_edge_on_it_and_the_strip_outside_it() => WithManager((manager, harness, screens, cursor) =>
    {
        using var strip = Marrow.App.Port.WindowInvisibleBorder.Pixels.Override((_, scale) => (int)Math.Round(12 * scale));
        // ED-NEW-settingsinapp-3: a first-launch window is never below the 860 x 560 minimum, so a work area of 800 x 500 is smaller than it.
        screens.Pointer = new WindowCenteringScreen(new PixelRect(0, 0, 800, 500), new PixelRect(0, 0, 800, 500), primary: true);
        cursor.Position = new PixelPoint(400, 250);
        manager.ShowMain();
        Assert.Equal(new Size(860, 560), manager.MainWindow!.ClientSize);
        Assert.Equal(new PixelPoint(-12, 0), manager.MainWindow!.Position);
    });

    // OD-0151: Settings with no main window shown opens the main window (first-launch sized and centred, the monitor read once) with the
    // Settings panel in it; no other window is made.
    [Fact]
    [Trait("Decision", "OD-0151")]
    [Trait("Decision", "ED-NEW-settingsinapp-3")]
    public void Settings_without_a_main_window_opens_the_main_window_centred_with_the_panel_in_it() => WithManager((manager, harness, screens, cursor) =>
    {
        screens.Pointer = screens.Owner;
        cursor.Position = new PixelPoint(2800, 500);
        var opened = new List<AvaloniaWindow>();
        using var watch = AvaloniaWindow.WindowOpenedEvent.AddClassHandler<AvaloniaWindow>((window, _) => opened.Add(window));
        manager.ShowSettings();
        var main = manager.MainWindow!;
        Assert.Same(main, Assert.Single(opened));
        Assert.True(manager.IsSettingsOpen);
        Assert.Same(main, manager.SettingsOverlay!.Window);
        // Work area (2000,100,1800,1180): 69 % is 1242, 92 % is 1085.6 (1086); offsets 279x47.
        Assert.Equal(new Size(1242, 1086), main.ClientSize);
        Assert.Equal(new PixelPoint(2279, 147), main.Position);
        Assert.Equal(1, screens.PointReads);
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:67-70; ED-006, ED-225
    public void A_saved_main_frame_bypasses_screen_centering_and_is_restored_without_clamping() => WithManager((manager, harness, screens, cursor) =>
    {
        manager.ShowMain();
        manager.MainWindow!.Width = 1000; manager.MainWindow.Height = 700;
        Settle(manager.MainWindow);
        manager.MainWindow.Position = new PixelPoint(-5000, -3000);
        Settle(manager.MainWindow);
        var reads = cursor.Reads;
        var next = new WindowManager { MainContent = () => new Border() };
        try
        {
            next.ShowMain();
            Assert.Equal(new PixelPoint(-5000, -3000), next.MainWindow!.Position);
            Assert.Equal(1000, next.MainWindow.Width);
            Assert.Equal(700, next.MainWindow.Height);
            Assert.Equal(reads, cursor.Reads);
        }
        finally { if (next.MainWindow is { } main) { main.Content = null; main.Hide(); } }
    });

    [Fact]
    [Trait("Req", "PR-WIN-010")] // App/Window/WindowManager.swift:82; ED-225 (owner monitor)
    [Trait("Req", "UI-SETTINGS-001")] // App/Window/WindowManager.swift:80-84; ED-225
    public void A_window_with_an_owner_uses_that_owners_monitor_and_never_reads_the_pointer() => WithManager((manager, harness, screens, cursor) =>
    {
        var owner = new AvaloniaWindow();
        var window = new AvaloniaWindow { Width = 780, Height = 580 };
        try
        {
            owner.Show(); window.Show(owner);
            WindowCenteringTestHooks.Center(window, new Size(780, 580));
            Assert.Equal(new PixelPoint(2510, 300), window.Position);
            Assert.Equal(1, screens.OwnerReads);
            Assert.Same(owner, screens.LastOwner);
            Assert.Equal(0, screens.PointReads);
            Assert.Equal(0, cursor.Reads);
        }
        finally { window.Close(); owner.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-010")] // App/Window/WindowManager.swift:21, :82; ED-225
    public void Monitor_scaling_converts_the_fixed_logical_frame_to_pixels_before_centering() => WithManager((manager, harness, screens, cursor) =>
    {
        screens.Pointer = new WindowCenteringScreen(new PixelRect(1920, 0, 2560, 1600), new PixelRect(1920, 40, 2560, 1460), scale: 2);
        // OD-0151: Settings has no window of its own; ED-225's centring is proved on a plain window of the old Settings frame.
        var window = new AvaloniaWindow { Width = 780, Height = 580 };
        WindowCenteringTestHooks.Center(window, new Size(780, 580));
        // 780x580 DIP is 1560x1160 pixels: free 1000x300, offsets 500x100, work origin 1920x40.
        Assert.Equal(new PixelPoint(2420, 140), window.Position);
        Assert.Equal(780, window.Width);
        Assert.Equal(580, window.Height);
        Assert.True(screens.Pointer.WorkingArea.Contains(new PixelRect(window.Position, new PixelSize(1560, 1160))));
        // The first-launch main window on that monitor: 69 % x 92 % of 1280 x 730 DIP is 883.2 x 671.6 (883 x 672), centred both ways.
        manager.ShowMain();
        var main = manager.MainWindow!;
        Assert.Equal(new Size(883, 672), main.ClientSize);
        Assert.Equal(new PixelPoint(1920 + (2560 - 1766) / 2, 40 + (1460 - 1344) / 2), main.Position);
    });

    [Fact]
    [Trait("Req", "PR-WIN-010")] // App/Window/WindowManager.swift:21, :82; ED-225 (clamping)
    public void An_exactly_fitting_work_area_keeps_all_edges_inside_without_changing_the_frame() => WithManager((manager, harness, screens, cursor) =>
    {
        screens.Pointer = new WindowCenteringScreen(new PixelRect(-1000, 0, 1000, 1000), new PixelRect(-900, 120, 780, 580));
        // OD-0151: Settings has no window of its own; ED-225's clamp is proved on a plain window of the old Settings frame.
        var window = new AvaloniaWindow { Width = 780, Height = 580 };
        WindowCenteringTestHooks.Center(window, new Size(780, 580));
        Assert.Equal(new PixelPoint(-900, 120), window.Position);
        Assert.Equal(780, window.Width);
        Assert.Equal(580, window.Height);
        Assert.True(screens.Pointer.WorkingArea.Contains(new PixelRect(window.Position, new PixelSize(780, 580))));
    });
}
