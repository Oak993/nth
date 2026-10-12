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
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W3a: the detail view hosts the pane workspace around the thread's chat columns
// (work/design/web-surfaces/DESIGN.md 3.11 "Pane state moves ... onto PaneWorkspaceState, which ThreadRuntime owns and the
// window reads"). The shown thread's state is drawn with that thread's contents; what the user changes is written back.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3a-1")]
public sealed class DetailViewWorkspaceTests(HeadlessSession headless)
{
    private static void Pump(AppModelHarness h, Avalonia.Controls.Window window)
    {
        h.Main.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        });
        h.Main.AdvanceMilliseconds(1);
        h.Main.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public void The_detail_view_draws_the_shown_threads_workspace_and_writes_back_what_the_user_changes() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var model = h.MakeModel();
        Guid first = default, second = default;
        var window = new Avalonia.Controls.Window { Width = 1400, Height = 900 };
        WindowLiveResize resize = null!;
        var made = new List<(PaneKind Kind, ThreadRuntime Runtime)>();
        try
        {
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = false;
                model.Settings.HydraEnabled = false;
                var project = model.AddProject(new Project("Project", h.NewFolder("project")).Url);
                first = model.NewThread(project)!.Id;
                second = model.NewThread(project)!.Id;
                resize = new WindowLiveResize(window);
                window.Show();
            });
            h.Main.Loop.Run(async () =>
            {
                await model.Runtime(first).EnsureLoaded();
                await model.Runtime(second).EnsureLoaded();
            }, AppModelHarness.Bound);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var detail = new DetailView
            {
                PaneFactory = (kind, runtime) =>
                {
                    made.Add((kind, runtime));
                    return new PaneContent(new Border(), PaneKinds.Title(kind));
                },
            };
            h.Main.OnMain(() =>
            {
                model.SelectedThreadID = first;
                AppEnvironment.SetAppModel(detail, model);
                AppEnvironment.SetWindowLiveResize(detail, resize);
                window.Content = detail;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            });
            Pump(h, window);
            var workspace = Assert.IsType<PaneWorkspace>(detail.Child);
            Assert.Same(workspace, detail.Workspace);
            Assert.Equal("ThreadColumns", workspace.Chat.Content!.GetType().Name);
            Assert.Empty(workspace.Cards);

            // Another surface opens a pane by setting the runtime's state: the workspace draws it with this thread's content.
            h.Main.OnMain(() => model.Runtime(first).Workspace = model.Runtime(first).Workspace.Open(PaneKind.Browser));
            Pump(h, window);
            Assert.True(workspace.State.IsOpen(PaneKind.Browser));
            var card = Assert.IsType<PaneCard>(workspace.CardOf(PaneKind.Browser));
            Assert.Equal((PaneKind.Browser, model.Runtime(first)), made.Single());

            // The user closes it in the header: the runtime's state follows.
            h.Main.OnMain(() => PaneWorkspaceRig.Click(card.Header.CloseButton));
            Pump(h, window);
            h.Main.OnMain(() => Assert.False(model.Runtime(first).Workspace.IsOpen(PaneKind.Browser)));

            // Each thread has its own workspace: switching draws the other thread's state at once, and back.
            h.Main.OnMain(() => model.Runtime(first).Workspace = model.Runtime(first).Workspace.Open(PaneKind.Changes));
            Pump(h, window);
            Assert.NotNull(workspace.CardOf(PaneKind.Changes));
            h.Main.OnMain(() => model.SelectedThreadID = second);
            Pump(h, window);
            Assert.Same(workspace, detail.Workspace);
            Assert.False(workspace.State.IsOpen(PaneKind.Changes));
            Assert.Empty(workspace.Cards);
            Assert.False(workspace.IsMoving);
            h.Main.OnMain(() => model.SelectedThreadID = first);
            Pump(h, window);
            Assert.True(workspace.State.IsOpen(PaneKind.Changes));
            Assert.NotNull(workspace.CardOf(PaneKind.Changes));
            Assert.False(workspace.IsMoving); // a thread switch draws at once
            Assert.Equal(model.Runtime(first), made.Last().Runtime);
            Assert.False(workspace.IsChatWorking);
        }
        finally
        {
            h.Main.OnMain(() =>
            {
                window.Hide();
                window.Content = null;
                Dispatcher.UIThread.RunJobs();
            });
        }
    });
}
