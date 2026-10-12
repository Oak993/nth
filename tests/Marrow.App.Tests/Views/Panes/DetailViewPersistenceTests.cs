using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W3c (work/design/web-surfaces/DESIGN.md 3.11, 13.3 "persistence round trip"): the window draws what the thread
// saved. What the user did (panes opened, a gutter dragged, a pane expanded) is on disk after the library's save and a restart draws
// the same state at the same rectangles at once; a layout saved on a larger monitor is kept whole while a smaller window fits it
// (tabs when the window is too narrow) and a bigger window brings it back.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3c-1")]
public sealed class DetailViewPersistenceTests(HeadlessSession headless)
{
    private static Guid NewThread(AppModelHarness h, AppModel model, Func<ChatThread, ChatThread>? shape = null)
    {
        Guid id = default;
        h.Main.OnMain(() =>
        {
            model.Settings.ShowsUsagePanel = false;
            model.Settings.HydraEnabled = false;
            var project = model.AddProject(new Project("Project", h.NewFolder("project")).Url);
            id = model.NewThread(project)!.Id;
            if (shape is not null) model.UpdateThread(id, shape);
        });
        h.Main.Loop.Run(async () => await model.Runtime(id).EnsureLoaded(), AppModelHarness.Bound);
        return id;
    }

    // The app saves its library as it quits: the main actor here is the window's, so the library's timed save would wait on a clock
    // the test does not run.
    private static void Flush(AppModelHarness h, AppModel model)
    {
        h.Main.OnMain(model.SaveBeforeQuit);
        DiskWriter.Shared.WaitForQueuedWrites();
    }

    private static PaneLayoutResult Solved(PaneWorkspaceState state, double width, double height) =>
        PaneLayout.Solve(state, width - PaneWorkspace.EdgeMargin, height - PaneWorkspace.TopBand - PaneWorkspace.EdgeMargin);

    [Fact]
    public void A_restart_draws_the_exact_layout_the_user_left_at_once() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = h.MakeModel();
        var id = NewThread(h, model);
        h.Main.OnMain(() => model.SelectedThreadID = id);
        var first = DetailWindows.Show(h, model, pinned: null, width: 1500);
        PaneWorkspaceState left;
        try
        {
            DetailWindows.Pump(h, first.Window);
            var workspace = Assert.IsType<PaneWorkspace>(first.Detail.Child);
            // The user opens panes with the workspace's own controls, drags the chat gutter, and expands one.
            h.Main.OnMain(() =>
            {
                workspace.Open(PaneKind.BackgroundTasks);
                workspace.Open(PaneKind.Artifacts);
                workspace.Open(PaneKind.Browser);
                workspace.Open(PaneKind.SideChat);
            });
            DetailWindows.Pump(h, first.Window);
            for (var i = 0; i < 80 && workspace.IsMoving; i++) DetailWindows.Pump(h, first.Window);
            var splitter = workspace.SplitterOf(PaneGutterKeys.Chat);
            var centre = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), first.Window) ?? default;
            h.Main.OnMain(() =>
            {
                first.Window.MouseDown(centre, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                first.Window.MouseMove(centre + new Point(-90, 0), RawInputModifiers.LeftMouseButton);
                first.Window.MouseUp(centre + new Point(-90, 0), MouseButton.Left, RawInputModifiers.None);
            });
            DetailWindows.Pump(h, first.Window);
            h.Main.OnMain(() => workspace.ToggleExpanded(PaneKind.Browser));
            DetailWindows.Pump(h, first.Window);
            left = workspace.State;
            Assert.Equal(PaneKind.Browser, left.Expanded);
            Assert.True(left.Ratios.ContainsKey(PaneGutterKeys.Chat), "the drag was kept as a ratio");
            h.Main.OnMain(() => Assert.Equal(left, model.Runtime(id).Workspace));
            Flush(h, model);
        }
        finally
        {
            DetailWindows.Close(h, first);
        }

        // The restart: a new model reads library.json; its window draws that state, at once, at the rectangles the solver gives it.
        var restarted = h.MakeModel();
        h.Main.OnMain(() => restarted.SelectedThreadID = id);
        var second = DetailWindows.Show(h, restarted, pinned: null, width: 1500);
        try
        {
            DetailWindows.Pump(h, second.Window);
            var workspace = Assert.IsType<PaneWorkspace>(second.Detail.Child);
            Assert.Equal(left, workspace.State);
            Assert.False(workspace.IsMoving, "a restart draws the layout at once, no spring");
            var expected = Solved(left, 1500, 900);
            Assert.Equal(expected, workspace.Layout);
            foreach (var kind in new[] { PaneKind.BackgroundTasks, PaneKind.Artifacts, PaneKind.Browser, PaneKind.SideChat })
                Assert.NotNull(workspace.CardOf(kind));
            var area = new Rect(0, PaneWorkspace.TopBand, 1500 - PaneWorkspace.EdgeMargin, 900 - PaneWorkspace.TopBand - PaneWorkspace.EdgeMargin);
            var browser = workspace.CardOf(PaneKind.Browser)!.Bounds;
            Assert.True(Math.Abs(browser.Width - area.Width) <= 1 && Math.Abs(browser.Height - area.Height) <= 1, $"{browser} fills {area}: the expanded pane");
        }
        finally
        {
            DetailWindows.Close(h, second);
        }
    });

    [Fact]
    public void A_layout_saved_on_a_larger_monitor_fits_a_smaller_window_and_a_bigger_one_brings_it_back() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var saved = new StoredWorkspace
        {
            State = PaneWorkspaceState.Default.Open(PaneKind.BackgroundTasks).Open(PaneKind.Artifacts).Open(PaneKind.Browser).Open(PaneKind.SideChat)
                .WithRatio(PaneGutterKeys.Chat, 0.25).WithRatio(PaneGutterKeys.Side, 0.6).WithRatio(PaneGutterKeys.Cols, 0.7).Select(PaneKind.Artifacts),
        };
        var model = h.MakeModel();
        var id = NewThread(h, model, thread => thread with { Workspace = saved });
        h.Main.OnMain(() => model.SelectedThreadID = id);
        var shown = DetailWindows.Show(h, model, pinned: null, width: 2560, height: 1300);
        try
        {
            DetailWindows.Pump(h, shown.Window);
            var workspace = Assert.IsType<PaneWorkspace>(shown.Detail.Child);
            Assert.Equal(saved.State, workspace.State);
            Assert.False(workspace.IsNarrow);
            Assert.Equal(Solved(saved.State, 2560, 1300), workspace.Layout);
            var big = workspace.Layout;

            // A laptop window: the same panes, as tabs; the state is not touched.
            h.Main.OnMain(() => shown.Window.Width = 700);
            DetailWindows.Pump(h, shown.Window);
            Assert.True(workspace.IsNarrow);
            Assert.Equal(new[] { PaneKind.Chat, PaneKind.BackgroundTasks, PaneKind.Artifacts, PaneKind.Browser, PaneKind.SideChat }, workspace.NarrowStrip.Kinds);
            Assert.Equal(PaneKind.Artifacts, workspace.NarrowSurface);
            Assert.Equal(saved.State, workspace.State);
            h.Main.OnMain(() => Assert.Equal(saved.State, model.Runtime(id).Workspace));

            // A middle window: tiles again, every pane at or above its minimum and the chat at 400 or more; the state still the same.
            h.Main.OnMain(() => shown.Window.Width = 1100);
            DetailWindows.Pump(h, shown.Window);
            Assert.False(workspace.IsNarrow);
            Assert.True(workspace.Layout!.Chat.Width >= PaneKinds.ChatMinWidth - 1e-6);
            Assert.Equal(saved.State, workspace.State);

            // The big window brings the saved layout back exactly.
            h.Main.OnMain(() => shown.Window.Width = 2560);
            DetailWindows.Pump(h, shown.Window);
            Assert.Equal(big, workspace.Layout);
            Flush(h, model);
            Assert.Equal(saved.State, AppModelHarness.LoadedLibrary().Threads.Single(thread => thread.Id == id).Workspace!.State);
        }
        finally
        {
            DetailWindows.Close(h, shown);
        }
    });
}
