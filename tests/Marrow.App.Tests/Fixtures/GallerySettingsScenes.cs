using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Window.Native;

// Settings, one page at a time, over the same library the thread scenes use (so Providers, Models, Hydra and Archive show
// something). OD-0140: Settings is the panel inside the main window, so the main window is shot with the panel open. A page longer than
// the panel is also captured scrolled to its end, as -2.
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
        var (manager, model, window, settings) = stage.Native.OnUi(() =>
        {
            var manager = new WindowManager();
            GalleryContent.Equip(manager.Model, data);
            manager.Model.Settings.HasSeenHydraIntro = true;
            manager.ShowSettings(Pages[skip]);
            var overlay = manager.SettingsOverlay ?? throw new InvalidOperationException("Settings did not open as the panel in the main window.");
            if (!manager.IsSettingsOpen || manager.MainWindow is not { } main || !ReferenceEquals(main, overlay.Window))
                throw new InvalidOperationException("Settings is not open in the main window.");
            return (manager, manager.Model, main, overlay.Content);
        }, TimeSpan.FromSeconds(40));
        stage.Register(window);
        stage.ForEachTheme(model, window, label =>
        {
            foreach (var page in Pages.Skip(skip).Take(take))
            {
                stage.Native.OnUi(() => SettingsNavigation.Shared.RequestedPage = page);
                Thread.Sleep(900);
                var name = "settings-" + SwiftEnum<SettingsPage>.RawValue(page);
                stage.Shoot(window, name, label, 1200);
                if (stage.Native.OnUi(() => ScrollToEnd(settings)))
                    stage.Shoot(window, name + "-2", label, 800);
                stage.Native.OnUi(() => ScrollToStart(settings));
            }
        });
        GC.KeepAlive(manager);
    }

    // The page's scroll viewer, inside the Settings panel (not the main window's own lists under it): the one with the most to scroll. True
    // when the page is longer than the panel.
    private static ScrollViewer? Page(Control settings) =>
        settings.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.Viewport.Width > 300).OrderByDescending(s => s.Extent.Height - s.Viewport.Height).FirstOrDefault();

    private static bool ScrollToEnd(Control settings)
    {
        if (Page(settings) is not { } scroll || scroll.Extent.Height - scroll.Viewport.Height < 24) return false;
        scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
        return true;
    }

    private static void ScrollToStart(Control settings)
    {
        if (Page(settings) is { } scroll) scroll.Offset = default;
    }
}
