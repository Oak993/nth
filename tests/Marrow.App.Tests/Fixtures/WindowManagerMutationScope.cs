using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Support;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Fixtures;

// Test-only scope. Existing AppModelHarness binds every persistent store to the test data directory.
// The real application entry point, bootstrap, provider commands and user settings are never called.
public static class WindowManagerMutationScope
{
    public static void Run(HeadlessSession headless, Action<AppModelHarness, WindowManager> check, bool useDefaultMain = false) => headless.OnUiThread(() =>
    {
        var application = Application.Current!;
        var appearance = application.RequestedThemeVariant;
        var theme = ThemeManager.Current;
        using var harness = new AppModelHarness(withRuntime: false);
        using var cursor = WindowCenteringTestHooks.OverrideCursor(new WindowCenteringCursor());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
        using var updates = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
        var requested = SettingsNavigation.Shared.RequestedPage;
        var manager = new WindowManager();
        if (!useDefaultMain) manager.MainContent = () => new Border();
        try { check(harness, manager); }
        finally
        {
            if (manager.MainWindow is { } main) { main.Content = null; main.Hide(); }
            manager.SettingsOverlay?.Close(); // OD-0151: Settings is a panel in the main window
            SettingsNavigation.Shared.RequestedPage = requested;
            ThemeManager.Current = theme;
            application.RequestedThemeVariant = appearance;
            Dispatcher.UIThread.RunJobs();
        }
    });

    // OD-0151: Settings is a panel in the main window (SettingsOverlay); these give the window it is open in.
    public static Avalonia.Controls.Window? SettingsOrNull(WindowManager manager) => manager.SettingsOverlay?.Window;

    public static Avalonia.Controls.Window Settings(WindowManager manager)
    {
        Assert.True(manager.SettingsOverlay is { IsOpen: true }, "Settings is open");
        Assert.Same(manager.MainWindow, manager.SettingsOverlay!.Window);
        return manager.SettingsOverlay.Window;
    }

    public static void Settle(Avalonia.Controls.Window window)
    {
        for (var pass = 0; pass < 5; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }
}
