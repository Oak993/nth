using System.Reflection;
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
// The pure frame calculation is tested in AppKit's global, upward-growing point space, before the existing
// Windows pixel conversion. Reflection lets these tests build and fail red before that private seam is added.
[Collection("Headless")]
public sealed class WindowManagerNextTests(HeadlessSession headless)
{
    private static Rect Centered(Rect parent, Size size, Rect? visible)
    {
        var calculation = typeof(WindowManager).GetMethod("CenteredFrame", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(calculation);
        return Assert.IsType<Rect>(calculation.Invoke(null, [parent, size, visible]));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")] // App/Window/WindowManager.swift:46-54
    [Trait("Req", "PR-WIN-011")] // App/Window/WindowManager.swift:48-53
    public void With_no_screen_negative_half_points_round_away_from_zero_and_are_not_clamped()
    {
        // midX=-810+1321/2=-149.5; midY=-800+861/2=-369.5. Subtract (390,290) and round
        // (-539.5,-659.5) to (-540,-660). :50 has no visibleFrame.
        Assert.Equal(new Rect(-540, -660, 780, 580), Centered(new Rect(-810, -800, 1321, 861), new Size(780, 580), null));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")] // App/Window/WindowManager.swift:50-52
    [Trait("Req", "PR-WIN-011")] // App/Window/WindowManager.swift:51-52
    public void A_work_area_smaller_than_Settings_uses_the_outer_min_and_preserves_the_window_size()
    {
        // maxX-width=700-780=-80; maxY-height=600-580=20. The outer min wins even below minX/minY.
        Assert.Equal(new Rect(-80, 20, 780, 580), Centered(new Rect(200, 300, 1320, 860), new Size(780, 580), new Rect(100, 200, 600, 400)));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-002")] // App/Window/WindowManager.swift:50-54
    [Trait("Req", "PR-WIN-011")] // App/Window/WindowManager.swift:50-54
    public void A_display_left_and_below_the_primary_keeps_its_own_work_area_origin()
    {
        // Rounded centre origin=(-2230,-1560), clamped to this display's (-1920,-1280), not primary (0,0).
        Assert.Equal(new Rect(-1920, -1280, 780, 580), Centered(new Rect(-2500, -1700, 1320, 860), new Size(780, 580), new Rect(-1920, -1280, 1920, 1280)));
    }

    private static AvaloniaWindow SettingsWindow(WindowManager manager) => Assert.IsType<SecondaryWindow>(
        typeof(WindowManager).GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager));

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
            if (manager.MainWindow is { } main) { main.Content = null; main.Hide(); }
            if (typeof(WindowManager).GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager) is AvaloniaWindow settings) { settings.Content = null; settings.Hide(); }
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
    public void The_default_Settings_view_keeps_search_and_page_then_consumes_a_requested_page_on_reopen() => WithManager((harness, manager) =>
    {
        manager.ShowSettings(SettingsPage.Archive);
        var window = SettingsWindow(manager);
        Settle(window);
        var view = Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(window.Content)));
        Assert.Same(manager.Model, AppEnvironment.GetAppModel(view));
        Assert.Equal(new Size(780, 580), window.ClientSize);
        Assert.Equal("Settings", window.Title);
        Assert.Null(SettingsNavigation.Shared.RequestedPage);
        Assert.Contains(Nodes<TextBlock>(view), text => text.Text == "Archive");
        // SettingsView.swift:201-204: the empty Archive body, not the permanently available sidebar label.
        Assert.Single(Nodes<Control>(view), control => Avalonia.Automation.AutomationProperties.GetName(control) == "No archived threads");
        Assert.Empty(Nodes<GeneralSettingsPage>(view));
        var search = Assert.Single(Nodes<SidebarSearchField>(view));
        var input = Assert.Single(Nodes<TextBox>(search));
        input.Text = "archive";
        Settle(window);
        window.Close();
        Assert.False(window.IsVisible);
        manager.ShowSettings();
        Settle(window);
        Assert.Same(window, SettingsWindow(manager));
        Assert.Same(view, Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(window.Content))));
        Assert.Equal("archive", input.Text);
        Assert.Contains(Nodes<TextBlock>(view), text => text.Text == "Archive");
        Assert.Single(Nodes<Control>(view), control => Avalonia.Automation.AutomationProperties.GetName(control) == "No archived threads");
        input.Text = "";
        Settle(window);
        window.Close();
        manager.ShowSettings(SettingsPage.Shortcuts);
        Settle(window);
        Assert.Null(SettingsNavigation.Shared.RequestedPage);
        Assert.Contains(Nodes<TextBlock>(view), text => text.Text == "Shortcuts");
        Assert.Single(Nodes<ShortcutsSettingsPage>(view));
        Assert.Empty(Nodes<GeneralSettingsPage>(view));
        Assert.Same(view, Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(window.Content))));
    });

    [Fact]
    [Trait("Req", "PR-THEME-039")] // App/Window/WindowManager.swift:149-159
    [Trait("Req", "UI-SETTINGS-001")] // App/Window/WindowManager.swift:187-192; App/Views/Settings/SettingsView.swift:93-98
    public void Theme_changes_reach_both_cached_windows_and_a_hidden_Settings_view_without_rebuilding_it() => WithManager((harness, manager) =>
    {
        manager.Model.Settings.Theme = AppTheme.Light;
        manager.ShowMain();
        manager.ShowSettings(SettingsPage.Archive);
        var main = manager.MainWindow!;
        var settings = SettingsWindow(manager);
        Settle(main); Settle(settings);
        var view = Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(settings.Content)));
        var mainRoot = Assert.Single(Nodes<ThemeVariantScope>(Assert.IsAssignableFrom<Control>(main.Content)));
        var settingsRoot = Assert.Single(Nodes<ThemeVariantScope>(Assert.IsAssignableFrom<Control>(settings.Content)));
        settings.Close();
        manager.Model.Settings.Theme = AppTheme.Dracula;
        Settle(main);
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        manager.ShowSettings();
        Settle(settings);
        Assert.Equal(ThemeVariant.Dark, settingsRoot.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Dark, mainRoot.RequestedThemeVariant);
        manager.Model.Settings.Theme = AppTheme.System;
        Settle(main); Settle(settings);
        Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Default, mainRoot.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Default, settingsRoot.RequestedThemeVariant);
        Assert.Same(view, Assert.Single(Nodes<SettingsView>(Assert.IsAssignableFrom<Control>(settings.Content))));
        Assert.Contains(Nodes<TextBlock>(view), text => text.Text == "Archive");
    });

    [Fact]
    [Trait("Req", "PR-WIN-003")] // App/Window/WindowManager.swift:26-30, :98 (OD-0008 C1)
    [Trait("Req", "PR-WIN-007")] // App/Window/WindowManager.swift:63, :81, :93-94
    [Trait("Req", "PR-WIN-008")] // App/Window/WindowManager.swift:98-100 (OD-0008 C1)
    [Trait("Req", "PR-WIN-009")] // App/Window/WindowManager.swift:63, :97
    public void Main_close_requests_the_shell_handshake_while_Settings_close_keeps_its_real_content() => WithManager((harness, manager) =>
    {
        using var name = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?> { ["CFBundleDisplayName"] = "Marrow" }, isDevelopment: false));
        var requests = 0;
        manager.MainWindowClose = () => requests++;
        manager.ShowMain();
        manager.ShowSettings(SettingsPage.Archive);
        var main = manager.MainWindow!;
        var settings = SettingsWindow(manager);
        var content = settings.Content;
        Assert.Equal("Marrow", main.Title);
        Assert.True(main.CanResize);
        Assert.False(settings.CanResize);
        Assert.True(settings.CanMaximize); // T45 owner caption override.
        Assert.True(settings.CanMinimize);
        main.Close();
        Assert.Equal(1, requests);
        Assert.True(main.IsVisible); // Only the shell may finish quit after the Hydra wait.
        settings.Close();
        Assert.Equal(1, requests);
        Assert.False(settings.IsVisible);
        manager.ShowMain();
        manager.ShowSettings();
        Assert.Same(main, manager.MainWindow);
        Assert.Same(settings, SettingsWindow(manager));
        Assert.Same(content, settings.Content);
    });

    [Fact]
    [Trait("Req", "PR-WIN-006")] // App/Window/WindowManager.swift:24, :67-70 (ED-006)
    [Trait("Req", "PR-SETTINGS-001")] // App/Window/WindowManager.swift:80-84 (no autosave)
    public void A_real_Settings_window_never_overwrites_the_saved_main_frame() => WithManager((harness, manager) =>
    {
        manager.ShowMain();
        var main = manager.MainWindow!;
        main.Width = 1000; main.Height = 700;
        Settle(main);
        main.Position = new PixelPoint(-3000, -2000);
        Settle(main);
        harness.Defaults.Writes.Clear();
        manager.ShowSettings(SettingsPage.Archive);
        var settings = SettingsWindow(manager);
        settings.Position = new PixelPoint(23, 45);
        Settle(settings);
        settings.Close();
        manager.ShowSettings();
        Settle(settings);
        Assert.Empty(harness.Defaults.Writes);
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
    // STOP UI-SETTINGS-001: these headless views do not prove the native Settings caption/sidebar or the Acrylic pixels; those are
    // native tests now: WindowManagerNativeTests (the sidebar is the caption region of the real HWND), GlassWindowNativeTests (Acrylic
    // under the scrim, and the corners of OD-0075 on the real Settings window). The continuous-corner geometry is asserted (ED-181
    // closed 2026-10-04): the backdrop's hairline is Apple's path of the 780 x 580 window (WindowManagerSettingsSurfaceTests); the corner
    // pixels of the production HWND stay native.
    // ED-225 now binds WindowManager.swift:43, :68, :82; WindowManagerCenteringTests covers the three sites.
    // Apple's exact vertical bias remains UNVERIFIED OD-0001, rather than blocking the Windows binding.
}
