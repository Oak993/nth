using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Composer;
using Marrow.UI.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// ED-357 (4): the popover sites the reference video shows, opened as a person opens them (real clicks), for a
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
            var title = stage.Find<TextBlock>(window, block => block.IsEffectivelyVisible && (block.Text ?? "").StartsWith(data.Main.Title, StringComparison.Ordinal)
                && block.TranslatePoint(default, window) is { X: < 250 }); // the sidebar row's, not the toolbar's title
            if (title is not null)
            {
                var at = stage.CenterOf(title);
                stage.Hover(at);
                Thread.Sleep(900);
                var more = stage.Native.OnUi(() => window.GetVisualDescendants().OfType<RowActionsButton>()
                    .Where(button => button.IsEffectivelyVisible)
                    .OrderBy(button => Math.Abs(button.PointToScreen(new Point(0, button.Bounds.Height / 2)).Y - at.Y)).FirstOrDefault());
                var target = more is null ? null : stage.Native.OnUi(() => more.GetVisualDescendants().OfType<Button>().FirstOrDefault());
                Log($"sidebar-{label}: title at {at}; More buttons {stage.Native.OnUi(() => window.GetVisualDescendants().OfType<RowActionsButton>().Count())}; the row's at {(target is null ? "none" : stage.CenterOf(target).ToString())}");
                if (target is not null)
                {
                    // The pointer stays on the row (its More button shows only while it is there); the button is pressed as
                    // a screen reader presses it, its own Click (a SendInput click there left no popover open in this harness).
                    stage.Native.OnUi(() => (Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(target)
                        .GetProvider<Avalonia.Automation.Provider.IInvokeProvider>())!.Invoke());
                    Thread.Sleep(1400);
                    Log($"sidebar-{label}: after pressing More, {stage.Native.OnUi(() => stage.Popups.Open.Count)} popovers open");
                    if (stage.Native.OnUi(() => stage.Popups.Open.Count) > 0)
                    {
                        stage.Shoot(window, "glass-sidebar-row-menu", label, 1000, 4000, GalleryCrop.PopupsOnly, animated: true);
                        stage.Shoot(window, "glass-sidebar-row-menu-in-window", label, 600, 4000, GalleryCrop.WithPopups, animated: true);
                        stage.Escape();
                        stage.Native.OnUi(() => { foreach (var popup in stage.Popups.Open) Marrow.UI.Port.Presentation.Presenter.Current.SetPresented(popup, false); });
                        Thread.Sleep(500);
                    }
                }
            }
        });
    }

    // What a scene could not do, in the gallery's folder beside its PNGs (the child's console is not shown).
    private static void Log(string line)
    {
        Console.WriteLine("Gallery: " + line);
        File.AppendAllText(System.IO.Path.Combine(GalleryRun.OutputFolder(), "popover-sites.log"), DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
    }

    private static void Site(GalleryStage stage, AvaloniaWindow window, string label, string scene, Func<Control?> find, bool park = true)
    {
        if (find() is not { } control)
        {
            Log($"{scene}-{label}: its button was not found");
            return;
        }
        stage.Click(stage.CenterOf(control));
        Thread.Sleep(1400); // the popover grows in from its arrow
        if (stage.Native.OnUi(() => stage.Popups.Open.Count) == 0)
        {
            Log($"{scene}-{label}: no popover opened");
            return;
        }
        stage.Shoot(window, scene, label, 1000, 4000, GalleryCrop.PopupsOnly, animated: true);
        stage.Shoot(window, scene + "-in-window", label, 600, 4000, GalleryCrop.WithPopups, animated: true);
        // Closed as a person would (Escape, else the same button again), and at last through the presenter, so the next site
        // opens alone.
        stage.Escape();
        Thread.Sleep(500);
        if (stage.Native.OnUi(() => stage.Popups.Open.Count) > 0 && find() is { } again) stage.Click(stage.CenterOf(again));
        Thread.Sleep(600);
        stage.Native.OnUi(() => { foreach (var popup in stage.Popups.Open) Marrow.UI.Port.Presentation.Presenter.Current.SetPresented(popup, false); });
        Thread.Sleep(500);
        if (park) stage.Park();
    }
}
