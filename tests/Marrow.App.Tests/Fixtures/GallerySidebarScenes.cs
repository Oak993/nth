using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Window;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// The sidebar with several projects and threads: the column with a selected thread, its Hydra heads and the settled group opened
// (as a person opens them, by clicking), then the sidebar's other presentations (Settings > Appearance > Sidebar: the floating panel, panel only).
internal static class GallerySidebarScenes
{
    internal static void Column(GalleryStage stage)
    {
        stage.Begin();
        var data = GalleryMainScenes.Library(stage);
        var (_, window, model) = GalleryMainScenes.Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        var opened = false;
        stage.ForEachTheme(model, window, label =>
        {
            Thread.Sleep(1500);
            if (!opened)
            {
                GalleryMainScenes.ClickText(stage, window, "2 heads");
                GalleryMainScenes.ClickText(stage, window, "Settled");
                opened = true;
            }
            stage.Shoot(window, "sidebar-column", label, 1200);
        });
    }

    internal static void Presentations(GalleryStage stage)
    {
        stage.Begin();
        var data = GalleryMainScenes.Library(stage);
        var (_, window, model) = GalleryMainScenes.Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        stage.ForEachTheme(model, window, label =>
        {
            Thread.Sleep(1500);
            // Floating: with the column collapsed (the toolbar's list button) the thread list floats as a panel over the chat
            stage.Native.OnUi(() => model.Settings.SidebarMode = SidebarMode.Floating);
            stage.Native.OnUi(() => { if (model.Sidebar.IsVisible) model.Sidebar.Toggle(); });
            Thread.Sleep(1500);
            stage.Front(window);
            stage.Shoot(window, "sidebar-floating-panel", label, 1200);
            // Panel only: no column and no toolbar button; the floating panel is the sidebar
            stage.Native.OnUi(() => model.Settings.SidebarMode = SidebarMode.PanelOnly);
            Thread.Sleep(1500);
            stage.Shoot(window, "sidebar-panel-only", label, 1200);
            stage.Native.OnUi(() => model.Settings.SidebarMode = SidebarMode.Column);
            stage.Native.OnUi(() => { if (!model.Sidebar.IsVisible) model.Sidebar.Toggle(); });
            Thread.Sleep(900);
        });
    }
}
