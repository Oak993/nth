using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Panes;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W3c (work/design/web-surfaces/DESIGN.md 3.11 "Each ThreadWindow has its own workspace for its thread", 13.2
// "Multi-window"): two windows each draw the workspace of their own thread; a change in one is that thread's alone and is saved with
// it; the model's selection (the main window's) moves neither; and one thread in two windows shares its state while each window
// solves the layout for its own size.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3c-1")]
public sealed class DetailViewMultiWindowTests(HeadlessSession headless)
{
    [Fact]
    public void Two_windows_show_two_threads_and_each_keeps_its_own_workspace() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var model = h.MakeModel();
        Guid first = default, second = default;
        h.Main.OnMain(() =>
        {
            model.Settings.ShowsUsagePanel = false;
            model.Settings.HydraEnabled = false;
            var project = model.AddProject(new Project("Project", h.NewFolder("project")).Url);
            first = model.NewThread(project)!.Id;
            second = model.NewThread(project)!.Id;
        });
        h.Main.Loop.Run(async () =>
        {
            await model.Runtime(first).EnsureLoaded();
            await model.Runtime(second).EnsureLoaded();
        }, AppModelHarness.Bound);
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        h.Main.OnMain(() => model.SelectedThreadID = first);
        var one = DetailWindows.Show(h, model, pinned: null, width: 1400); // the main window follows the selection
        var two = DetailWindows.Show(h, model, pinned: second, width: 1200);
        try
        {
            DetailWindows.Pump(h, one.Window, two.Window);
            var left = Assert.IsType<PaneWorkspace>(one.Detail.Child);
            var right = Assert.IsType<PaneWorkspace>(two.Detail.Child);
            Assert.NotSame(left, right);

            // Each thread opens its own panes; the other window does not see them.
            h.Main.OnMain(() =>
            {
                model.Runtime(first).Workspace = model.Runtime(first).Workspace.Open(PaneKind.Browser);
                model.Runtime(second).Workspace = model.Runtime(second).Workspace.Open(PaneKind.BackgroundTasks).Open(PaneKind.SideChat);
            });
            DetailWindows.Pump(h, one.Window, two.Window);
            Assert.Equal(new[] { PaneKind.Browser }, left.State.GridPanes);
            Assert.False(left.State.IsSideChatOpen);
            Assert.Equal(new[] { PaneKind.BackgroundTasks }, right.State.GridPanes);
            Assert.True(right.State.IsSideChatOpen);
            Assert.NotNull(left.CardOf(PaneKind.Browser));
            Assert.Null(right.CardOf(PaneKind.Browser));
            Assert.NotNull(right.CardOf(PaneKind.BackgroundTasks));
            Assert.Null(left.CardOf(PaneKind.BackgroundTasks));

            // The model's selection is the main window's: choosing the other thread moves the main window, never the pinned one.
            h.Main.OnMain(() => model.SelectedThreadID = second);
            DetailWindows.Pump(h, one.Window, two.Window);
            Assert.Equal(new[] { PaneKind.BackgroundTasks }, left.State.GridPanes); // the main window follows to the second thread
            Assert.Equal(new[] { PaneKind.BackgroundTasks }, right.State.GridPanes);
            h.Main.OnMain(() => model.SelectedThreadID = first);
            DetailWindows.Pump(h, one.Window, two.Window);
            Assert.Equal(new[] { PaneKind.Browser }, left.State.GridPanes);
            Assert.Equal(new[] { PaneKind.BackgroundTasks }, right.State.GridPanes);

            // The user closes a pane in the pinned window: that thread's state changes, the other thread's does not.
            var card = Assert.IsType<PaneCard>(right.CardOf(PaneKind.BackgroundTasks));
            h.Main.OnMain(() => PaneWorkspaceRig.Click(card.Header.CloseButton));
            DetailWindows.Pump(h, one.Window, two.Window);
            h.Main.OnMain(() =>
            {
                Assert.False(model.Runtime(second).Workspace.IsOpen(PaneKind.BackgroundTasks));
                Assert.True(model.Runtime(second).Workspace.IsSideChatOpen);
                Assert.Equal(new[] { PaneKind.Browser }, model.Runtime(first).Workspace.GridPanes);
            });
            Assert.Equal(new[] { PaneKind.Browser }, left.State.GridPanes);

            // Saved with each thread.
            h.Main.Advance(TimeSpan.FromSeconds(10));
            DiskWriter.Shared.WaitForQueuedWrites();
            var library = AppModelHarness.LoadedLibrary();
            Assert.Equal(new[] { PaneKind.Browser }, library.Threads.Single(thread => thread.Id == first).Workspace!.State.GridPanes);
            var saved = library.Threads.Single(thread => thread.Id == second).Workspace!.State;
            Assert.Empty(saved.GridPanes);
            Assert.True(saved.IsSideChatOpen);

            // A window closed and opened again on its thread finds the workspace where it was left.
            DetailWindows.Close(h, two);
            var again = DetailWindows.Show(h, model, pinned: second, width: 1200);
            try
            {
                DetailWindows.Pump(h, one.Window, again.Window);
                var reopened = Assert.IsType<PaneWorkspace>(again.Detail.Child);
                Assert.True(reopened.State.IsSideChatOpen);
                Assert.Empty(reopened.State.GridPanes);
                Assert.Equal(new[] { PaneKind.Browser }, left.State.GridPanes);
            }
            finally
            {
                DetailWindows.Close(h, again);
            }
        }
        finally
        {
            DetailWindows.Close(h, one);
            DetailWindows.Close(h, two);
        }
    });

    [Fact]
    public void One_thread_in_two_windows_shares_its_state_and_each_window_solves_the_layout_for_its_own_size() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var model = h.MakeModel();
        Guid thread = default;
        h.Main.OnMain(() =>
        {
            model.Settings.ShowsUsagePanel = false;
            model.Settings.HydraEnabled = false;
            var project = model.AddProject(new Project("Project", h.NewFolder("project")).Url);
            thread = model.NewThread(project)!.Id;
        });
        h.Main.Loop.Run(async () => await model.Runtime(thread).EnsureLoaded(), AppModelHarness.Bound);
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var wide = DetailWindows.Show(h, model, pinned: thread, width: 1400);
        var narrow = DetailWindows.Show(h, model, pinned: thread, width: 600);
        try
        {
            DetailWindows.Pump(h, wide.Window, narrow.Window);
            h.Main.OnMain(() => model.Runtime(thread).Workspace = model.Runtime(thread).Workspace.Open(PaneKind.Browser));
            DetailWindows.Pump(h, wide.Window, narrow.Window);
            var tiles = Assert.IsType<PaneWorkspace>(wide.Detail.Child);
            var tabs = Assert.IsType<PaneWorkspace>(narrow.Detail.Child);
            Assert.Equal(new[] { PaneKind.Browser }, tiles.State.GridPanes);
            Assert.Equal(new[] { PaneKind.Browser }, tabs.State.GridPanes);
            Assert.False(tiles.IsNarrow);
            Assert.True(tabs.IsNarrow);
            Assert.Equal(new[] { PaneKind.Chat, PaneKind.Browser }, tabs.NarrowStrip.Kinds);
        }
        finally
        {
            DetailWindows.Close(h, wide);
            DetailWindows.Close(h, narrow);
        }
    });
}
