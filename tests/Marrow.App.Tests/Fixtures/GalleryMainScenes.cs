using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Window;
using Marrow.Core.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// The scenes of the main window: its welcome page on an empty library, a project with no thread open, and the long thread.
internal static class GalleryMainScenes
{
    /// <summary>
    /// The real window manager over the real model, made after the library is on disk, with the main window shown. With a library
    /// the sidebar starts open (a working window); on an empty library it is as a first launch has it, closed.
    /// </summary>
    internal static (WindowManager Manager, AvaloniaWindow Window, AppModel Model) Open(GalleryStage stage, GalleryData? data, Func<AppModel, Guid?>? select = null, bool introSeen = true) =>
        stage.Native.OnUi(() =>
        {
            // The fixture's own stand-in window saved its frame under the main window's name: forget it, so the window opens at
            // its factory size (1320 x 860) and place, as on a first launch.
            UserDefaults.Standard.Current.RemoveObject("DroppyCodeMainWindow");
            if (data is not null) UserDefaults.Standard.Current.Set("sidebarStartsOpen", true);
            var manager = new WindowManager();
            var model = manager.Model;
            GalleryContent.Equip(model, data);
            model.Settings.HasSeenHydraIntro = introSeen;
            model.SelectedThreadID = select?.Invoke(model);
            manager.ShowMain();
            return (manager, manager.MainWindow!, model);
        }, TimeSpan.FromSeconds(40));

    internal static GalleryData Library(GalleryStage stage) => stage.Native.OnUi(GalleryContent.Seed, TimeSpan.FromSeconds(60));

    internal static void EmptyState(GalleryStage stage)
    {
        stage.Begin();
        var (_, window, model) = Open(stage, data: null);
        stage.Register(window);
        stage.ForEachTheme(model, window, label =>
        {
            stage.Shoot(window, "main-empty-welcome", label, 1500);
            // the page is taller than the window: the rest of the provider checklist
            if (stage.Native.OnUi(() => PageCount(window)) > 1)
            {
                stage.Native.OnUi(() => ScrollToPage(window, 1, 2));
                stage.Shoot(window, "main-empty-welcome-2", label, 900);
                stage.Native.OnUi(() => ScrollToPage(window, 0, 2));
            }
        });
    }

    internal static void NoThreadSelected(GalleryStage stage)
    {
        stage.Begin();
        var data = Library(stage);
        var (_, window, model) = Open(stage, data, _ => null);
        stage.Register(window);
        stage.BothThemes(model, window, "main-project-no-thread");
    }

    // The long thread opened, with Settings > "Show reasoning" on and every folded group of work opened by clicking, as a person does.
    private static (AvaloniaWindow Window, AppModel Model) OpenedThread(GalleryStage stage)
    {
        stage.Begin();
        var data = Library(stage);
        var (_, window, model) = Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        stage.Native.OnUi(() => model.Settings.ShowReasoning = true);
        return (window, model);
    }

    // The turns' work is folded into "Worked for ..." rows, each with a summary line that folds its rows: open them (dark pass only;
    // the state stays for the light one).
    private static void OpenWork(GalleryStage stage, AvaloniaWindow window)
    {
        Thread.Sleep(1500);
        var groups = stage.Native.OnUi(() => window.GetVisualDescendants().OfType<TextBlock>().Count(block => (block.Text ?? "").StartsWith("Worked for", StringComparison.Ordinal)));
        for (var index = 0; index < groups; index++) ClickText(stage, window, "Worked for", index);
        ClickText(stage, window, "Edited files");
        ClickText(stage, window, "earlier steps");
    }

    /// <summary>The long thread, top to bottom: the timeline is scrolled a page at a time and each page captured.</summary>
    internal static void ThreadPages(GalleryStage stage)
    {
        var (window, model) = OpenedThread(stage);
        var opened = false;
        stage.ForEachTheme(model, window, label =>
        {
            if (!opened) { OpenWork(stage, window); opened = true; }
            var pages = stage.Native.OnUi(() => PageCount(window));
            for (var page = 0; page < pages; page++)
            {
                stage.Native.OnUi(() => ScrollToPage(window, page, pages));
                stage.Shoot(window, "main-thread-" + (page + 1), label, settleMilliseconds: 1200);
            }
        });
    }

    /// <summary>The rows that open on a click: the edit row's diff and Hydra's mark (popovers), the command row (in place).</summary>
    internal static void ThreadRows(GalleryStage stage)
    {
        var (window, model) = OpenedThread(stage);
        var opened = false;
        stage.ForEachTheme(model, window, label =>
        {
            if (!opened) { OpenWork(stage, window); opened = true; }
            foreach (var (text, scene, popover) in new[] { ("Edited src/settings/ThemeToggle.tsx", "main-thread-popover-diff", true), ("Ran pnpm test", "main-thread-command-output", false) })
            {
                stage.Native.OnUi(() => ScrollToPage(window, 0, 2));
                Thread.Sleep(900);
                ClickText(stage, window, text);
                stage.Shoot(window, scene, label, settleMilliseconds: 1000, crop: popover ? GalleryCrop.WithPopups : GalleryCrop.Window);
                if (popover) stage.Escape();
                else ClickText(stage, window, text); // closes it again, so the next theme starts from the same rows
            }
        });
    }

    /// <summary>The thread with one of its Hydra heads opened in a panel beside the chat.</summary>
    internal static void HydraPanel(GalleryStage stage)
    {
        stage.Begin();
        var data = Library(stage);
        var (_, window, model) = Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        stage.Native.OnUi(() => model.UpdateThread(data.Hank.Id, thread => thread with { IsInPanel = true }));
        stage.BothThemes(model, window, "main-hydra-head-panel", settleMilliseconds: 2500);
    }

    /// <summary>A turn under way: the working line with its thinking, a running command, the start of the reply, the stop button.</summary>
    internal static void LiveTurn(GalleryStage stage)
    {
        stage.Begin();
        var data = Library(stage);
        var (_, window, model) = Open(stage, data, _ => data.Unread.Id);
        stage.Register(window);
        stage.Native.OnUi(() =>
        {
            model.Settings.ShowReasoning = true; // Settings > General > "Show thinking"
            model.UpdateThread(data.Unread.Id, thread => thread with { HydraEnabled = false });
        });
        Thread.Sleep(1500);
        var (session, started) = stage.Native.OnUi(() => GalleryLiveTurn.Start(model, data.Unread, "Now update the CI workflow to use pnpm, with the store cached."), TimeSpan.FromSeconds(30));
        N.Until(() => stage.Native.OnUi(() => started.IsCompleted), "the turn never reached its provider", 20000);
        stage.Native.OnUi(() => GalleryLiveTurn.Stream(session));
        stage.ForEachTheme(model, window, label =>
        {
            Thread.Sleep(1500);
            stage.Shoot(window, "main-thread-live-turn", label, 1500, 3500, animated: true);
            // the working card opens to the agent's thinking and its steps
            ClickText(stage, window, "running commands");
            Thread.Sleep(600);
            stage.Shoot(window, "main-thread-live-turn-open", label, 1200, 3500, animated: true);
            ClickText(stage, window, "running commands"); // folds it again for the next theme
        });
    }

    // A real click on the first (or index-th) visible text block that contains the text, scrolled into view first.
    internal static void ClickText(GalleryStage stage, AvaloniaWindow window, string contains, int index = 0)
    {
        TextBlock? Block() => stage.Native.OnUi(() => window.GetVisualDescendants().OfType<TextBlock>()
            .Where(b => b.IsEffectivelyVisible && (b.Text ?? "").Contains(contains, StringComparison.Ordinal)).Skip(index).FirstOrDefault());
        if (Block() is not { } block) return;
        stage.Native.OnUi(block.BringIntoView);
        Thread.Sleep(500);
        stage.Click(stage.CenterOf(Block()!));
        Thread.Sleep(800);
    }

    // The timeline's scroll viewer: the widest one that has something to scroll.
    private static ScrollViewer? Timeline(AvaloniaWindow window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.Viewport.Width > 500 && s.Extent.Height > 0).OrderByDescending(s => s.Extent.Height).FirstOrDefault();

    private static int PageCount(AvaloniaWindow window) =>
        Timeline(window) is { } scroll ? Math.Clamp((int)Math.Ceiling(scroll.Extent.Height / (scroll.Viewport.Height * 0.85)), 1, 6) : 1;

    // Page 0 is the top, the last page the bottom, the others spread between with a little overlap.
    private static void ScrollToPage(AvaloniaWindow window, int page, int pages)
    {
        if (Timeline(window) is not { } scroll) return;
        var most = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(0, pages <= 1 ? most : most * page / (pages - 1));
    }
}
