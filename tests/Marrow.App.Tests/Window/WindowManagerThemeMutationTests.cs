using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Window;

// Source: WindowManager.swift:151-159. Both the hosted tree and the application appearance change initially and later.
// CLOSED STOP NATIVE-THEME (2026-10-04): the hosted/process/framework ThemeVariant checks here are joined by
// WindowManagerNativeTests.Immersive_dark_mode_follows_the_theme_on_both_factory_windows, which reads DWM's immersive dark mode back
// from both real factory HWNDs for Dark, Light, Dracula and System. The Win32 context menus are the owner's ruling OD-0042 ("Windows
// menu theme"), not the app's theme, so the record's "native menus out of step" wording is superseded for them.
[Collection("Headless")]
public sealed class WindowManagerThemeMutationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-THEME-039")]
    public void An_initial_dark_theme_is_applied_to_the_first_host_and_the_process_before_any_theme_change() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        manager.Model.Settings.Theme = AppTheme.Dark;
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        WindowManagerMutationScope.Settle(window);
        var host = Assert.Single(LogicalQuery.SelfAndDescendants<HostedRoot>(Assert.IsAssignableFrom<Control>(window.Content)));
        Assert.Equal(ThemeVariant.Dark, host.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
    });

    [Fact]
    [Trait("Req", "PR-THEME-039")]
    public void Dark_light_and_system_changes_update_both_cached_windows_and_a_framework_window() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        manager.SettingsContent = () => new Border();
        manager.ShowMain();
        manager.ShowSettings();
        var main = Assert.IsType<ThreadWindow>(manager.MainWindow);
        var settings = WindowManagerMutationScope.Settings(manager);
        var foreign = new Avalonia.Controls.Window();
        try
        {
            foreign.Show();
            foreach (var (theme, expected) in new[]
            {
                (AppTheme.Dark, ThemeVariant.Dark), (AppTheme.Light, ThemeVariant.Light),
                (AppTheme.Dracula, ThemeVariant.Dark), (AppTheme.System, ThemeVariant.Default),
            })
            {
                manager.Model.Settings.Theme = theme;
                WindowManagerMutationScope.Settle(main);
                WindowManagerMutationScope.Settle(settings);
                Assert.Equal(expected, Application.Current!.RequestedThemeVariant);
                // OD-0140: Settings is a panel in the main window, with a host of its own beside the main content's
                Assert.Same(main, settings);
                var hosts = LogicalQuery.SelfAndDescendants<HostedRoot>(Assert.IsAssignableFrom<Control>(main.Content)).ToArray();
                Assert.Equal(2, hosts.Length);
                Assert.Contains(hosts, host => Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(manager.SettingsOverlay!.Stage).Contains(host));
                foreach (var host in hosts) Assert.Equal(expected, host.RequestedThemeVariant);
                if (expected != ThemeVariant.Default) Assert.Equal(expected, foreign.ActualThemeVariant);
                Assert.Equal(Application.Current.ActualThemeVariant, foreign.ActualThemeVariant);
            }
        }
        finally { foreign.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-039")]
    public void Reattaching_the_same_host_reinstalls_the_initial_process_appearance_observer() => WindowManagerMutationScope.Run(headless, (_, manager) =>
    {
        manager.Model.Settings.Theme = AppTheme.Dark;
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        var content = Assert.IsAssignableFrom<Control>(window.Content);
        window.Content = null;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        window.Content = content;
        WindowManagerMutationScope.Settle(window);
        Assert.Equal(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
        manager.Model.Settings.Theme = AppTheme.Light;
        WindowManagerMutationScope.Settle(window);
        Assert.Equal(ThemeVariant.Light, Application.Current.RequestedThemeVariant);
    });
}
