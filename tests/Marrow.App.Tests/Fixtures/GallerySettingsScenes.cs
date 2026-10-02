using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// The Settings window, one page at a time, over the same library the thread scenes use (so Providers, Models, Hydra and Archive show
// something). A page longer than the window is also captured scrolled to its end, as -2.
internal static class GallerySettingsScenes
{
    private static readonly SettingsPage[] Pages =
    [
        SettingsPage.General, SettingsPage.Appearance, SettingsPage.Providers, SettingsPage.Models, SettingsPage.Mcp,
        SettingsPage.Hydra, SettingsPage.SourceControl, SettingsPage.Shortcuts, SettingsPage.Archive, SettingsPage.About,
    ];

    internal static void PageSlice(GalleryStage stage, int skip, int take)
    {
        stage.Begin();
        var data = GalleryMainScenes.Library(stage);
        var (manager, model) = stage.Native.OnUi(() =>
        {
            var manager = new WindowManager();
            GalleryContent.Equip(manager.Model, data);
            manager.Model.Settings.HasSeenHydraIntro = true;
            manager.ShowSettings(Pages[skip]);
            return (manager, manager.Model);
        }, TimeSpan.FromSeconds(40));
        var window = (AvaloniaWindow)typeof(WindowManager).GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        stage.Register(window);
        stage.ForEachTheme(model, window, label =>
        {
            foreach (var page in Pages.Skip(skip).Take(take))
            {
                stage.Native.OnUi(() => SettingsNavigation.Shared.RequestedPage = page);
                Thread.Sleep(900);
                var name = "settings-" + SwiftEnum<SettingsPage>.RawValue(page);
                stage.Shoot(window, name, label, 1200);
                if (stage.Native.OnUi(() => ScrollToEnd(window)))
                    stage.Shoot(window, name + "-2", label, 800);
                stage.Native.OnUi(() => ScrollToStart(window));
            }
        });
    }

    // The page's scroll viewer: the one with the most to scroll. True when the page is longer than the window.
    private static ScrollViewer? Page(AvaloniaWindow window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.Viewport.Width > 300).OrderByDescending(s => s.Extent.Height - s.Viewport.Height).FirstOrDefault();

    private static bool ScrollToEnd(AvaloniaWindow window)
    {
        if (Page(window) is not { } scroll || scroll.Extent.Height - scroll.Viewport.Height < 24) return false;
        scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
        return true;
    }

    private static void ScrollToStart(AvaloniaWindow window)
    {
        if (Page(window) is { } scroll) scroll.Offset = default;
    }
}
