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
    private static AvaloniaWindow Settings(WindowManager manager) => Assert.IsType<SecondaryWindow>(
        typeof(WindowManager).GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager));

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
            if (typeof(WindowManager).GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager) is AvaloniaWindow settings) { settings.Content = null; settings.Hide(); }
            ThemeManager.Current = originalTheme;
            Application.Current.RequestedThemeVariant = originalAppearance;
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:67-70; ED-225
    public void A_first_run_main_uses_the_pointer_monitor_work_area_and_the_one_third_vertical_bias() => WithManager((manager, harness, screens, cursor) =>
    {
        manager.ShowMain();
        var main = manager.MainWindow!;
        // Work area (-1600,-1200,1600,1160), frame 1320x860: free 280x300, offsets 140x100.
        Assert.Equal(new PixelPoint(-1460, -1100), main.Position);
        Assert.Equal(new Size(1320, 860), main.ClientSize);
        Assert.Equal(cursor.Position, screens.LastPoint);
        Assert.Equal(1, screens.PointReads);
        Assert.Equal(0, screens.OwnerReads);
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
    [Trait("Req", "PR-WIN-010")] // App/Window/WindowManager.swift:80-84; ED-225
    [Trait("Req", "PR-SETTINGS-002")] // App/Window/WindowManager.swift:42-45; ED-225
    public void Settings_without_main_is_centered_on_creation_and_again_on_every_show() => WithManager((manager, harness, screens, cursor) =>
    {
        screens.Pointer = screens.Owner;
        cursor.Position = new PixelPoint(2800, 500);
        manager.ShowSettings();
        var settings = Settings(manager);
        // Work area (2000,100,1800,1180), frame 780x580: free 1020x600, offsets 510x200.
        Assert.Equal(new PixelPoint(2510, 300), settings.Position);
        Assert.Equal(2, screens.PointReads); // :82 on construction, then :43 on show
        settings.Position = new PixelPoint(7, 9);
        settings.Close();
        manager.ShowSettings();
        Assert.Same(settings, Settings(manager));
        Assert.Equal(new PixelPoint(2510, 300), settings.Position);
        Assert.Equal(3, screens.PointReads);
    });

    [Fact]
    [Trait("Req", "PR-WIN-011")] // App/Window/WindowManager.swift:42-45; ED-225
    [Trait("Req", "PR-SETTINGS-002")] // App/Window/WindowManager.swift:42-45; ED-225
    public void Hidden_and_minimized_main_use_pointer_screen_centering_instead_of_the_main_frame() => WithManager((manager, harness, screens, cursor) =>
    {
        manager.ShowMain();
        var main = manager.MainWindow!;
        main.Hide();
        screens.Pointer = screens.Owner;
        manager.ShowSettings();
        var settings = Settings(manager);
        Assert.Equal(new PixelPoint(2510, 300), settings.Position);
        manager.ShowMain();
        main.WindowState = WindowState.Minimized;
        settings.Position = new PixelPoint(0, 0);
        manager.ShowSettings();
        Assert.Equal(new PixelPoint(2510, 300), settings.Position);
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
        manager.ShowSettings();
        var window = Settings(manager);
        // 780x580 DIP is 1560x1160 pixels: free 1000x300, offsets 500x100, work origin 1920x40.
        Assert.Equal(new PixelPoint(2420, 140), window.Position);
        Assert.Equal(780, window.Width);
        Assert.Equal(580, window.Height);
        Assert.True(screens.Pointer.WorkingArea.Contains(new PixelRect(window.Position, new PixelSize(1560, 1160))));
    });

    [Fact]
    [Trait("Req", "PR-WIN-010")] // App/Window/WindowManager.swift:21, :82; ED-225 (clamping)
    public void An_exactly_fitting_work_area_keeps_all_edges_inside_without_changing_the_frame() => WithManager((manager, harness, screens, cursor) =>
    {
        screens.Pointer = new WindowCenteringScreen(new PixelRect(-1000, 0, 1000, 1000), new PixelRect(-900, 120, 780, 580));
        manager.ShowSettings();
        var window = Settings(manager);
        Assert.Equal(new PixelPoint(-900, 120), window.Position);
        Assert.Equal(new Size(780, 580), window.ClientSize);
        Assert.True(screens.Pointer.WorkingArea.Contains(new PixelRect(window.Position, new PixelSize(780, 580))));
    });
}
