using System.Reflection;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Theme;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A real <see cref="SettingsView"/> in the environment the Settings tests share (a throwaway harness, a manual frame source, no
/// wallpaper store, no update feed), shown in a headless window. <see cref="Run"/> hands the view and its model to the check.
/// </summary>
public static class SettingsViewRig
{
    public static void Run(HeadlessSession headless, Action<SettingsView, AppModel, Control> check) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var updates = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
        var model = new AppModel();
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var view = new SettingsView();
        host.Child = view;
        var window = new Avalonia.Controls.Window { Content = host, Width = 780, Height = 580 };
        try
        {
            window.Show();
            Drain(window);
            check(view, model, Sidebar(view));
            Drain(window);
        }
        finally
        {
            window.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            SettingsNavigation.Shared.RequestedPage = null;
        }
    });

    public static void Drain(Avalonia.Controls.Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    public static void Sync() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    /// <summary>The page the window is showing (its view state).</summary>
    public static SettingsPage CurrentPage(SettingsView view)
    {
        var state = typeof(SettingsView).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        return (SettingsPage)state.GetType().GetProperty("Page")!.GetValue(state)!;
    }

    private static Control Sidebar(SettingsView view) =>
        (Control)typeof(SettingsView).GetField("sidebar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;

    /// <summary>The titles of the sidebar rows now listed, top to bottom.</summary>
    public static string[] RowTitles(Control sidebar) =>
        LogicalQuery.SelfAndDescendants<SidebarRow>(sidebar)
            .Select(row => LogicalQuery.SelfAndDescendants<TextBlock>(row).Select(text => text.Text ?? "").First(text => text.Length > 0))
            .ToArray();

    /// <summary>Types <paramref name="query"/> into the sidebar search field.</summary>
    public static void Search(Control sidebar, string query)
    {
        var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(sidebar));
        field.Text = query;
        Sync();
    }
}
