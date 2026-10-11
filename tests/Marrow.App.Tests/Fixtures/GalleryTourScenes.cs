using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

/// <summary>
/// RR-22 (OD-0027): the eight welcome-tour pictures retaken from Windows Marrow (Tour.Pages names tour-welcome .. tour-themes). As the Mac's
/// own @2x tour assets are, each is a whole small window: the window is made 1720 x 1074 pixels and the app's own zoom set to 200 %, so
/// the app lays out as an 860 x 537 point window (its minimum width; the tour frame's 1.6 aspect) drawn at twice the pixels. Dark theme (the tour is drawn dark). The raw captures land in
/// the gallery folder as tour-*-dark.png; porting/tools/make_tour_assets.py crops them to exactly 1320 x 824, derives the 1x files and
/// tiles the four theme shots, and the results are shipped as Assets/Images/tour-*.png and tour-*@2x.png.
/// </summary>
internal static class GalleryTourScenes
{
    private static void Small(GalleryStage stage, AvaloniaWindow window, AppModel model) => stage.Native.OnUi(() =>
    {
        model.Settings.AppZoom = 200;
        var scale = window.RenderScaling;
        window.Width = 1720 / scale; // 860 x 537 points (the main window's minimum width) drawn at 2x; make_tour_assets.py scales it to 1320 x 824
        window.Height = 1074 / scale;
        window.Position = new PixelPoint(160, 120);
    });

    internal static void TourFirst(GalleryStage stage) => Tour(stage, first: true);

    internal static void TourSecond(GalleryStage stage) => Tour(stage, first: false);

    private static void Tour(GalleryStage stage, bool first)
    {
        stage.Begin();
        var data = GalleryMainScenes.Library(stage);
        var (manager, window, model) = GalleryMainScenes.Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        stage.Native.OnUi(() =>
        {
            model.Settings.Theme = AppTheme.Dark;
            model.Settings.SidebarMode = SidebarMode.Column;
            if (model.Sidebar.IsVisible) model.Sidebar.Toggle(); // the column hidden: the chat alone, as the Mac's tour shows it
        });
        Small(stage, window, model);
        Thread.Sleep(2500);
        stage.Front(window);

        if (first)
        {
            // Welcome: the chat itself.
            stage.Shoot(window, "tour-welcome", "dark", 1800, 6000);

            // Hydra: a head's panel beside the lead's chat.
            stage.Native.OnUi(() => model.UpdateThread(data.Hank.Id, thread => thread with { IsInPanel = true }));
            Thread.Sleep(1500);
            stage.Shoot(window, "tour-hydra", "dark", 2000, 6000);

            // Panels: two heads docked at once.
            stage.Native.OnUi(() => model.UpdateThread(data.Walter.Id, thread => thread with { IsInPanel = true }));
            Thread.Sleep(1500);
            stage.Shoot(window, "tour-panels", "dark", 2000, 6000);
            stage.Native.OnUi(() =>
            {
                model.UpdateThread(data.Hank.Id, thread => thread with { IsInPanel = false });
                model.UpdateThread(data.Walter.Id, thread => thread with { IsInPanel = false });
            });
            Thread.Sleep(900);

            // Pairs: the chip's popover turned to the model picker, pairs on top.
            OpenChip(stage, window);
            var modelButton = GalleryComposerScenes.ModelButton(stage);
            stage.Click(stage.CenterOf(modelButton));
            Thread.Sleep(1300);
            stage.Shoot(window, "tour-pairs", "dark", 1200, 5000, GalleryCrop.WithPopups);
            CloseChip(stage, window);

            // Slider: a pair's effort slider, the lead's colour blending into the heads'.
            stage.Native.OnUi(() =>
            {
                model.EnterHydraPair(data.Pair, data.Main.Id);
                model.UpdateThread(data.Main.Id, thread => thread with { Effort = "high", FastMode = false });
            });
            Thread.Sleep(900);
            OpenChip(stage, window);
            stage.Shoot(window, "tour-slider", "dark", 1500, 5000, GalleryCrop.WithPopups, animated: true);
            CloseChip(stage, window);
            stage.Native.OnUi(() => model.LeaveHydraPair(data.Main.Id));
            Thread.Sleep(600);

            return;
        }

        // Threads: the sidebar hidden, the thread list floating as a panel.
        stage.Native.OnUi(() => model.Settings.SidebarMode = SidebarMode.Floating);
        stage.Native.OnUi(() => { if (model.Sidebar.IsVisible) model.Sidebar.Toggle(); });
        Thread.Sleep(600);
        stage.Native.OnUi(() => { if (!model.Sidebar.IsVisible) model.Sidebar.Toggle(); });
        Thread.Sleep(1500);
        stage.Front(window);
        stage.Shoot(window, "tour-threads", "dark", 1500, 6000);
        stage.Native.OnUi(() => { model.Settings.SidebarMode = SidebarMode.Column; if (model.Sidebar.IsVisible) model.Sidebar.Toggle(); });
        Thread.Sleep(900);

        // Themes: the same chat in four themes, tiled two by two afterwards.
        foreach (var (theme, name) in new[] { (AppTheme.TokyoNight, "tokyoNight"), (AppTheme.Claude, "claude"), (AppTheme.CatppuccinLatte, "catppuccinLatte"), (AppTheme.RosePine, "rosePine") })
        {
            stage.Native.OnUi(() => model.Settings.Theme = theme);
            Thread.Sleep(1300);
            stage.Front(window);
            stage.Shoot(window, "tour-theme-" + name, "dark", 1500, 6000);
        }
        stage.Native.OnUi(() => model.Settings.Theme = AppTheme.Dark);
        Thread.Sleep(900);

        // Recipes: the Settings > Hydra page comes from Gallery_settings_* (settings-hydra-dark.png), whose fixed 780 x 580 page cannot be
        // zoomed inside its native shell; make_tour_assets.py takes it from the gallery folder.
    }

    private static void OpenChip(GalleryStage stage, AvaloniaWindow window)
    {
        var chip = stage.Find<ModelEffortButton>(window, _ => true) ?? throw new InvalidOperationException("The composer has no model chip.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            stage.Click(stage.CenterOf(chip));
            Thread.Sleep(1400);
            if (stage.Native.OnUi(() => stage.Popups.Open.Count) > 0) return;
        }
        throw new InvalidOperationException("The model chip did not open its popover.");
    }

    private static void CloseChip(GalleryStage stage, AvaloniaWindow window)
    {
        stage.Escape();
        Thread.Sleep(400);
        if (stage.Native.OnUi(() => stage.Popups.Open.Count) == 0) return;
        var chip = stage.Find<ModelEffortButton>(window, _ => true)!;
        stage.Click(stage.CenterOf(chip));
        Thread.Sleep(600);
    }
}
