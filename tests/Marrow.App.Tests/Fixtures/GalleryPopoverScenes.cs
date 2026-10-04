using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Composer;
using Marrow.UI.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// ED-NEW-glass-g2 (4): the popover sites the reference video shows, opened as a person opens them (real clicks), for a
// side-by-side with the Mac frames: the composer's model chip (its effort popover), the toolbar's "Run a project script" menu (a
// PopoverMenu, 2-chat-use.mov 131.5 s; the "Open in another app" one reads the system's icons, which a test may not), a sidebar row's "More" menu (151.0 s) and the composer's usage ring (55.0 s). Each is
// shot as the popups alone and with the window round them.
internal static class GalleryPopoverScenes
{
    internal static void Sites(GalleryStage stage)
    {
        stage.Begin();
        var data = GalleryMainScenes.Library(stage);
        var (_, window, model) = GalleryMainScenes.Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        stage.ForEachTheme(model, window, label =>
        {
            Thread.Sleep(1500);
            Site(stage, window, label, "glass-effort-popover", () => stage.Find<ModelEffortButton>(window, _ => true));
            Site(stage, window, label, "glass-run-menu", () => stage.Find<Button>(window, button => ToolTip.GetTip(button) as string == "Run a project script"));
            Site(stage, window, label, "glass-usage-popover", () => stage.Find<Button>(window, button =>
                AutomationProperties.GetName(button) is "Context window" or "Remaining credits" or "Usage limits"));
            // A sidebar row shows its "More" button while the pointer is on it.
            var title = stage.FindText(window, data.Main.Title);
            if (title is not null)
            {
                stage.Hover(stage.CenterOf(title));
                Thread.Sleep(500);
                Site(stage, window, label, "glass-sidebar-row-menu", () => stage.Find<RowActionsButton>(window, _ => true) is { } more
                    ? stage.Native.OnUi(() => more.GetVisualDescendants().OfType<Button>().FirstOrDefault()) : null, park: false);
            }
        });
    }

    private static void Site(GalleryStage stage, AvaloniaWindow window, string label, string scene, Func<Control?> find, bool park = true)
    {
        if (find() is not { } control)
        {
            Console.WriteLine($"Gallery: {scene}-{label}: its button was not found");
            return;
        }
        stage.Click(stage.CenterOf(control));
        Thread.Sleep(1400); // the popover grows in from its arrow
        if (stage.Native.OnUi(() => stage.Popups.Open.Count) == 0)
        {
            Console.WriteLine($"Gallery: {scene}-{label}: no popover opened");
            return;
        }
        stage.Shoot(window, scene, label, 1000, 4000, GalleryCrop.PopupsOnly, animated: true);
        stage.Shoot(window, scene + "-in-window", label, 600, 4000, GalleryCrop.WithPopups, animated: true);
        stage.Escape();
        Thread.Sleep(500);
        if (stage.Native.OnUi(() => stage.Popups.Open.Count) > 0) stage.Escape();
        if (park) stage.Park();
    }
}
