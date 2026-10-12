using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// Source: DroppyCode/App/Window/WindowManager.swift (B3 52bc792), :1-222.
// OD-0151 (ED-NEW-settingsinapp-1): Settings is a panel inside the main window (Port/SettingsOverlay.cs), so the Settings window's
// centring over the main window (:40-55, the former CenteredFrame) is not ported any more and its frame-calculation tests are gone.
[Collection("Headless")]
public sealed class WindowManagerNextTests(HeadlessSession headless)
{
    // The Settings panel, open in the main window.
    private static SettingsOverlay OpenSettings(WindowManager manager)
    {
        var overlay = Assert.IsType<SettingsOverlay>(manager.SettingsOverlay);
        Assert.True(manager.IsSettingsOpen);
        Assert.Same(manager.MainWindow, overlay.Window);
        return overlay;
    }

    private static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    private void WithManager(Action<AppModelHarness, WindowManager> check) => headless.OnUiThread(() =>
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
        var manager = new WindowManager { MainContent = () => new Border() };
        try { check(harness, manager); }
        finally
        {
            manager.SettingsOverlay?.Close(); // OD-0151: Settings is a panel in the main window
            if (manager.MainWindow is { } main) { main.Content = null; main.Hide(); }
            SettingsNavigation.Shared.RequestedPage = requested;
            ThemeManager.Current = theme;
            application.RequestedThemeVariant = appearance;
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-001")] // App/Window/WindowManager.swift:21, :32-38, :80-84, :98
    [Trait("Req", "PR-WIN-003")] // WIN.json:79-81: retained Settings state, not only cached object identity.
    [Trait("Req", "PR-WIN-010")] // App/Window/WindowManager.swift:80-84
    [Trait("Req", "UI-SETTINGS-001")] // App/Views/Settings/SettingsView.swift:93-98, :118-127
    [Trait("Decision", "OD-0151")]
    public void The_default_Settings_view_keeps_search_and_page_then_consumes_a_requested_page_on_reopen() => WithManager((harness, manager) =>
    {
        manager.ShowSettings(SettingsPage.Archive);
        var overlay = OpenSettings(manager);
        var window = overlay.Window;
        Settle(window);
        var view = Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(window.Content)));
        Assert.Same(view, overlay.Content);
        Assert.Same(manager.Model, AppEnvironment.GetAppModel(view));
        // OD-0151: the panel named Settings, which the view fills (it was the 780 x 580 window titled Settings).
        Assert.Equal("Settings", Avalonia.Automation.AutomationProperties.GetName(overlay.Card));
        Assert.Equal(overlay.Card.Bounds.Size, view.Bounds.Size);
        Assert.Null(SettingsNavigation.Shared.RequestedPage);
        Assert.Contains(Nodes<TextBlock>(view), text => text.Text == "Archive");
        // SettingsView.swift:201-204: the empty Archive body, not the permanently available sidebar label.
        Assert.Single(Nodes<Control>(view), control => Avalonia.Automation.AutomationProperties.GetName(control) == "No archived threads");
        Assert.Empty(Nodes<GeneralSettingsPage>(view));
        var search = Assert.Single(Nodes<SidebarSearchField>(view));
        var input = Assert.Single(Nodes<TextBox>(search));
        input.Text = "archive";
        Settle(window);
        manager.CloseSettings();
        Assert.False(manager.IsSettingsOpen);
        Assert.True(window.IsVisible); // the main window it was in stays
        manager.ShowSettings();
        Settle(window);
        Assert.Same(overlay, OpenSettings(manager));
        Assert.Same(window, overlay.Window);
        Assert.Same(view, Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(window.Content))));
        Assert.Equal("archive", input.Text);
        Assert.Contains(Nodes<TextBlock>(view), text => text.Text == "Archive");
        Assert.Single(Nodes<Control>(view), control => Avalonia.Automation.AutomationProperties.GetName(control) == "No archived threads");
        input.Text = "";
        Settle(window);
        manager.CloseSettings();
        Assert.False(manager.IsSettingsOpen);
        manager.ShowSettings(SettingsPage.Shortcuts);
        Settle(window);
        Assert.Same(overlay, OpenSettings(manager));
        Assert.Null(SettingsNavigation.Shared.RequestedPage);
        Assert.Contains(Nodes<TextBlock>(view), text => text.Text == "Shortcuts");
        Assert.Single(Nodes<ShortcutsSettingsPage>(view));
        Assert.Empty(Nodes<GeneralSettingsPage>(view));
        Assert.Same(view, Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(window.Content))));
    });

    [Fact]
    [Trait("Req", "PR-THEME-039")] // App/Window/WindowManager.swift:149-159
    [Trait("Req", "UI-SETTINGS-001")] // App/Window/WindowManager.swift:187-192; App/Views/Settings/SettingsView.swift:93-98
    [Trait("Decision", "OD-0151")]
    public void Theme_changes_reach_the_main_window_and_a_closed_Settings_panel_without_rebuilding_it() => WithManager((harness, manager) =>
    {
        manager.Model.Settings.Theme = AppTheme.Light;
        manager.ShowMain();
        var main = manager.MainWindow!;
        Settle(main);
        var mainRoot = Assert.Single(Nodes<ThemeVariantScope>(Assert.IsAssignableFrom<Control>(main.Content)));
        manager.ShowSettings(SettingsPage.Archive);
        var overlay = OpenSettings(manager);
        Settle(main);
        var view = Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(main.Content)));
        // OD-0151: the panel's own hosted root (WindowManager.HostedRoot) lies beside the main one, in the main window.
        var settingsRoot = Assert.Single(Nodes<ThemeVariantScope>(overlay));
        Assert.NotSame(mainRoot, settingsRoot);
        Assert.Equal(2, Nodes<ThemeVariantScope>(Assert.IsAssignableFrom<Control>(main.Content)).Length);
        manager.CloseSettings();
        Assert.False(manager.IsSettingsOpen);
        manager.Model.Settings.Theme = AppTheme.Dracula;
        Settle(main);
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        manager.ShowSettings();
        Settle(main);
        Assert.Same(overlay, OpenSettings(manager));
        Assert.Equal(ThemeVariant.Dark, settingsRoot.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Dark, mainRoot.RequestedThemeVariant);
        manager.Model.Settings.Theme = AppTheme.System;
        Settle(main);
        Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Default, mainRoot.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Default, settingsRoot.RequestedThemeVariant);
        Assert.Same(view, Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(main.Content))));
        Assert.Contains(Nodes<TextBlock>(view), text => text.Text == "Archive");
    });

    [Fact]
    [Trait("Req", "PR-WIN-003")] // App/Window/WindowManager.swift:26-30, :98 (OD-0008 C1)
    [Trait("Req", "PR-WIN-007")] // App/Window/WindowManager.swift:63, :81, :93-94
    [Trait("Req", "PR-WIN-008")] // App/Window/WindowManager.swift:98-100 (OD-0008 C1)
    [Trait("Req", "PR-WIN-009")] // App/Window/WindowManager.swift:63, :97
    [Trait("Decision", "OD-0151")]
    public void Main_close_requests_the_shell_handshake_while_Settings_close_keeps_its_real_content() => WithManager((harness, manager) =>
    {
        using var name = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?> { ["CFBundleDisplayName"] = "Marrow" }, isDevelopment: false));
        var requests = 0;
        manager.MainWindowClose = () => requests++;
        manager.ShowMain();
        manager.ShowSettings(SettingsPage.Archive);
        var main = manager.MainWindow!;
        var overlay = OpenSettings(manager);
        var content = overlay.Content;
        Assert.Same(main, overlay.Window);
        Assert.Equal("Marrow", main.Title); // Settings, a panel in it, leaves the window's title alone
        Assert.True(main.CanResize);
        main.Close();
        Assert.Equal(1, requests);
        Assert.True(main.IsVisible); // Only the shell may finish quit after the Hydra wait.
        manager.CloseSettings();
        Assert.Equal(1, requests); // closing Settings closes no window
        Assert.False(manager.IsSettingsOpen);
        Assert.True(main.IsVisible);
        manager.ShowMain();
        manager.ShowSettings();
        Assert.Same(main, manager.MainWindow);
        Assert.Same(overlay, OpenSettings(manager));
        Assert.Same(content, overlay.Content);
        Assert.Same(overlay, Assert.Single(Assert.IsAssignableFrom<Panel>(main.Content).Children.OfType<SettingsOverlay>()));
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:24, :67-70 (ED-006)
    [Trait("Req", "PR-SETTINGS-001")] // App/Window/WindowManager.swift:80-84 (no autosave)
    [Trait("Decision", "OD-0151")]
    public void The_real_Settings_panel_never_overwrites_the_saved_main_frame() => WithManager((harness, manager) =>
    {
        manager.ShowMain();
        var main = manager.MainWindow!;
        main.Width = 1000; main.Height = 700;
        Settle(main);
        main.Position = new PixelPoint(-3000, -2000);
        Settle(main);
        harness.Defaults.Writes.Clear();
        manager.ShowSettings(SettingsPage.Archive);
        var overlay = OpenSettings(manager);
        Settle(main);
        manager.CloseSettings();
        Assert.False(manager.IsSettingsOpen);
        manager.ShowSettings();
        Settle(main);
        Assert.Same(overlay, OpenSettings(manager));
        Assert.Empty(harness.Defaults.Writes);
        // Settings opens in the main window without moving or resizing it.
        Assert.Equal(new PixelPoint(-3000, -2000), main.Position);
        Assert.Equal(1000, main.Width);
        Assert.Equal(700, main.Height);
        var next = new WindowManager { MainContent = () => new Border() };
        try
        {
            next.ShowMain();
            Assert.Equal(new PixelPoint(-3000, -2000), next.MainWindow!.Position);
            Assert.Equal(1000, next.MainWindow.Width);
            Assert.Equal(700, next.MainWindow.Height);
        }
        finally { next.MainWindow?.Hide(); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-017")] // App/Window/WindowManager.swift:120-139 (windows_path: size, not move)
    public void Repeated_size_loops_reset_the_window_flag_without_resetting_a_pane_drag() => WithManager((harness, manager) =>
    {
        var live = new WindowLiveResize(new AvaloniaWindow()) { IsPaneResizing = true };
        for (var loop = 0; loop < 2; loop++)
        {
            var handled = false;
            live.WndProc(0, 0x0214, 0, 0, ref handled); // WM_SIZING outside a loop cannot start it.
            Assert.False(live.IsActive);
            live.WndProc(0, 0x0231, 0, 0, ref handled);
            live.WndProc(0, 0x0214, 0, 0, ref handled);
            Assert.True(live.IsActive);
            live.WndProc(0, 0x0232, 0, 0, ref handled);
            Assert.False(live.IsActive);
            Assert.True(live.IsReshaping);
            Assert.True(live.IsPaneResizing);
            Assert.False(handled);
        }
        live.IsPaneResizing = false;
        Assert.False(live.IsReshaping);
    });

    // This fixture substitutes main content while keeping the actual SettingsView. The former RootView
    // constructor blocker is superseded by WindowManagerTitleMutationTests' real default-factory proof.
    // WindowManagerHighRiskTests covers the reflected/message-only boundary; WindowManagerNativeTests
    // separately exercises actual factory HWND registration, physical input and retained resize hooks.
    // STOP UI-SETTINGS-001: these headless views do not prove the pixels of the Settings panel's glass. OD-0151 (2026-10-11) made Settings
    // a panel inside the main window: there is no Settings HWND, caption or window corner any more. The panel's 26 pt continuous shape and
    // its backdrop's hairline on that curve are asserted in WindowManagerSettingsSurfaceTests.
    // ED-225 now binds WindowManager.swift:43, :68 (the :82 Settings window is not ported, OD-0151); WindowManagerCenteringTests covers them.
    // Apple's exact vertical bias remains UNVERIFIED OD-0001, rather than blocking the Windows binding.
}
