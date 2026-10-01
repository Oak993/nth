using System.Linq;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Terminal;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Chat/ChatView.swift:82-83 (the drawer's directory: worktree, else project, else home)
// feeding TerminalPanel.swift:109 (a thread with no shell gets exactly one, "Terminal 1", in that directory). The
// drawer is opened in a real chat view for a thread of each kind.
[Collection("Headless")]
public sealed class TerminalPanelDirectoryTests(HeadlessSession headless)
{
    private static void Drain(AppModelHarness h)
    {
        h.Main.RunUntilIdle();
        h.Main.OnMain(() => Dispatcher.UIThread.RunJobs());
        h.Main.RunUntilIdle();
    }

    // Shows the chat of one thread, opens its drawer, and returns the directory of the one session the drawer opened.
    private void OpenDrawerFor(Func<AppModelHarness, (Project[] Projects, ChatThread Thread, string Expected)> library) =>
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var cursor = Marrow.UI.Chrome.DragHandleView.CursorPlatform.Override(new ChatNoCursor());
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            var (projects, thread, expected) = library(h);
            h.WriteLibrary(projects, [thread]);
            AppModelHarness.WriteDocument(thread.Id);
            var model = h.MakeModel();
            AvaloniaWindow? window = null;
            try
            {
                ThreadRuntime runtime = null!;
                h.Main.OnMain(() =>
                {
                    model.Settings.ShowsUsagePanel = false;
                    model.Settings.HydraEnabled = false;
                    runtime = model.Runtime(thread.Id);
                    var view = new ChatView(runtime);
                    window = new AvaloniaWindow { Content = view, Width = 1400, Height = 900 };
                    AppEnvironment.SetAppModel(window, model);
                    AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
                    ChatHeadlessFixtures.Track(window);
                    window.Show();
                    window.UpdateLayout();
                });
                Drain(h);
                Assert.Empty(model.Terminals.Sessions(thread.Id));
                h.Main.OnMain(() => runtime.IsTerminalVisible = true);
                Drain(h);
                h.Main.OnMain(() => window!.UpdateLayout());
                Drain(h);
                var session = Assert.Single(model.Terminals.Sessions(thread.Id));
                Assert.Equal("Terminal 1", session.Title);
                Assert.Equal(expected, session.Directory);
            }
            finally
            {
                h.Main.OnMain(() => { window?.Close(); model.Terminals.TerminateAll(); });
                Drain(h);
            }
        });

    [Fact]
    [Trait("Req", "PR-TERM-004")] // App/Views/Chat/ChatView.swift:82-83 thread.worktreePath ?? project.path
    public void The_drawers_first_shell_starts_in_the_threads_worktree_when_it_has_one() => OpenDrawerFor(h =>
    {
        var project = AppModelSamples.Project("Terminal directory", h.NewFolder("project-dir"));
        var worktree = h.NewFolder("worktree-dir");
        return ([project], AppModelSamples.Thread(project) with { WorktreePath = worktree }, worktree);
    });

    [Fact]
    [Trait("Req", "PR-TERM-004")] // App/Views/Chat/ChatView.swift:82-83 falls back to project.path
    public void The_drawers_first_shell_starts_in_the_project_folder_when_the_thread_has_no_worktree() => OpenDrawerFor(h =>
    {
        var project = AppModelSamples.Project("Terminal directory", h.NewFolder("project-only"));
        return ([project], AppModelSamples.Thread(project), project.Path);
    });

    [Fact]
    [Trait("Req", "PR-TERM-004")] // App/Views/Chat/ChatView.swift:83 else LoginEnvironment.homeDirectory
    public void The_drawers_first_shell_starts_in_the_home_folder_when_the_thread_has_neither_a_worktree_nor_a_project() => OpenDrawerFor(h =>
    {
        var orphan = AppModelSamples.Thread(AppModelSamples.Project("Gone", h.NewFolder("gone-project")));
        return ([], orphan, LoginEnvironment.HomeDirectory);
    });
}
