using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Panes;

// Web surfaces W5 (DESIGN 3.1 "Transcript", 3.10 "View transcript opens the job's transcript in a Transcript pane"; ED-NEW-web-w5-1): the
// Transcript pane reads another chat's timeline beside the thread you are in, with no composer; View transcript opens it; the Background
// tasks pane is registered with the workspace and shows "Clear done" in its header when something has finished.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w5-1")]
public sealed class TranscriptPaneTests(HeadlessSession headless)
{
    private static ChatThread Head(Project project, Guid lead, string task, long index) =>
        new(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
        {
            ParentThreadID = lead,
            IsInPanel = true,
            Title = task,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) with { Index = index, Task = task, HydraHeadInfoKind = HydraHeadInfo.Kind.Native, NativeID = "n" + index },
        };

    private static (AppModel Model, ChatThread Lead, ChatThread HeadA, ChatThread HeadB) Make(AppModelHarness h)
    {
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var a = Head(project, lead.Id, "Fix the build", 1);
        var b = Head(project, lead.Id, "Port the tests", 2);
        h.WriteLibrary([project], [lead, a, b]);
        return (h.MakeModel(), lead, a, b);
    }

    private static void Pump(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public void Showing_a_transcript_opens_the_Transcript_pane_on_it_and_a_second_one_swaps_what_it_reads() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var (model, lead, a, b) = Make(h);
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() => runtime = model.Runtime(lead.Id));
        Assert.False(runtime.Workspace.IsOpen(PaneKind.Transcript));
        Assert.Null(TranscriptPane.TargetOf(runtime).ThreadId);

        h.Main.OnMain(() => TranscriptPane.Show(runtime, a.Id, "Fix the build"));
        Assert.True(runtime.Workspace.IsOpen(PaneKind.Transcript));
        Assert.Equal((a.Id, "Fix the build"), (TranscriptPane.TargetOf(runtime).ThreadId, TranscriptPane.TargetOf(runtime).Title));

        var raised = 0;
        TranscriptPane.TargetOf(runtime).Changed += () => raised++;
        h.Main.OnMain(() => TranscriptPane.Show(runtime, a.Id, "Fix the build")); // the same again: nothing changes
        Assert.Equal(0, raised);
        h.Main.OnMain(() => TranscriptPane.Show(runtime, b.Id, "Port the tests"));
        Assert.Equal(1, raised);
        Assert.Equal(b.Id, TranscriptPane.TargetOf(runtime).ThreadId);
        Assert.Single(runtime.Workspace.GridPanes, kind => kind == PaneKind.Transcript);
    });

    [Fact]
    public void A_job_with_no_transcript_opens_nothing() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var (model, lead, _, _) = Make(h);
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() => runtime = model.Runtime(lead.Id));
        var job = new BackgroundJob { Id = "terminal:1", Kind = BackgroundJobKind.Terminal, Title = "dev", KindLabel = "Terminal", StartedAt = DateTimeOffset.UtcNow };
        h.Main.OnMain(() => TranscriptPane.Show(runtime, job));
        Assert.False(runtime.Workspace.IsOpen(PaneKind.Transcript));
    });

    [Fact]
    public void The_pane_reads_the_chosen_chat_with_the_jobs_title_in_its_header_and_an_Open_chat_action() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var (model, lead, a, b) = Make(h);
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() => runtime = model.Runtime(lead.Id));
        var window = new AvaloniaWindow { Width = 420, Height = 640 };
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); // the window closes while it is still the main actor
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        try
        {
            // The timeline reads the app's model and the window's live-resize model from the window, as it does in the app.
            AppEnvironment.SetAppModel(window, model);
            h.Main.OnMain(() => AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window)));
            var content = TranscriptPane.Create(PaneKind.Transcript, runtime)!;
            Assert.Null(content.Title);
            Assert.Empty(content.Actions);
            var view = Assert.IsType<TranscriptView>(content.Body);
            Assert.Null(view.ShownThread);
            Assert.Null(view.Timeline);
            window.Content = view;
            window.Show();
            Pump(h, window);

            TranscriptPane.TargetOf(runtime).Set(a.Id, "Fix the build");
            Pump(h, window);
            Assert.Equal(a.Id, view.ShownThread);
            Assert.NotNull(view.Timeline);
            Assert.Equal(("Fix the build", "Transcript"), (content.Title, content.Subtitle));
            var open = Assert.Single(content.Actions);
            Assert.Equal("Open chat", open.Name);
            open.Invoke();
            Assert.Equal(a.Id, model.SelectedThreadID);

            var first = view.Timeline;
            TranscriptPane.TargetOf(runtime).Set(b.Id, "Port the tests");
            Pump(h, window);
            Assert.Equal(b.Id, view.ShownThread);
            Assert.NotSame(first, view.Timeline);
            Assert.Equal("Port the tests", content.Title);

            // A chat that no longer exists reads as nothing chosen.
            TranscriptPane.TargetOf(runtime).Set(Guid.NewGuid(), "Gone");
            Pump(h, window);
            Assert.Null(view.ShownThread);
            Assert.Null(view.Timeline);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_pane_that_left_the_screen_stops_following_the_target() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var (model, lead, a, b) = Make(h);
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() => runtime = model.Runtime(lead.Id));
        var window = new AvaloniaWindow { Width = 420, Height = 640 };
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); // the window closes while it is still the main actor
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        try
        {
            // The timeline reads the app's model and the window's live-resize model from the window, as it does in the app.
            AppEnvironment.SetAppModel(window, model);
            h.Main.OnMain(() => AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window)));
            TranscriptPane.TargetOf(runtime).Set(a.Id, "Fix the build");
            var content = TranscriptPane.Create(PaneKind.Transcript, runtime)!;
            var view = Assert.IsType<TranscriptView>(content.Body);
            window.Content = view;
            window.Show();
            Pump(h, window);
            Assert.Equal(a.Id, view.ShownThread);

            window.Content = null;
            Pump(h, window);
            TranscriptPane.TargetOf(runtime).Set(b.Id, "Port the tests");
            Assert.Equal(a.Id, view.ShownThread);
            Assert.Equal("Fix the build", content.Title); // its header is no longer kept up either
        }
        finally { window.Close(); }
    });

    // ---- the Background tasks pane in the workspace ----

    [Fact]
    public void Both_panes_register_their_kinds_with_the_workspace()
    {
        BackgroundTasksPane.Register();
        TranscriptPane.Register();
        Assert.True(PaneContents.IsAvailable(PaneKind.BackgroundTasks));
        Assert.True(PaneContents.IsAvailable(PaneKind.Transcript));
    }

    [Fact]
    public void The_Background_tasks_pane_draws_the_apps_list_for_the_shown_thread_and_offers_Clear_done_once_something_has_finished() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var (model, lead, a, _) = Make(h);
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() => runtime = model.Runtime(lead.Id));
        var window = new AvaloniaWindow { Width = 420, Height = 640 };
        var keepScope = BackgroundTasksPane.Scope;
        try
        {
            BackgroundTasksPane.Scope = BackgroundJobScope.ThisThread;
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var content = BackgroundTasksPane.Create(PaneKind.BackgroundTasks, runtime)!;
            var view = Assert.IsType<BackgroundTasksView>(content.Body);
            Assert.Empty(content.Actions);
            var card = new PaneCard(PaneKind.BackgroundTasks, content);
            window.Content = card;
            window.Show();
            Pump(h, window);
            Assert.Equal("Background tasks", card.Header.TitleBlock.Inlines!.OfType<Avalonia.Controls.Documents.Run>().First().Text);

            // The head the library holds, finished, is in the Hydra panel: the list shows it as Done.
            var now = SwiftClock.Provider.Current.GetUtcNow();
            model.PublishedJobs.Publish(new BackgroundJob
            {
                Id = "tactus:1", Kind = BackgroundJobKind.Tactus, Title = "Fill the Q3 sheet", KindLabel = "Tactus", ThreadId = lead.Id, StartedAt = now.AddMinutes(-2),
                State = BackgroundJobState.Done, FinishedAt = now.AddMinutes(-1),
            });
            h.Main.OnMain(model.BackgroundJobs.Refresh);
            Pump(h, window);
            Assert.Contains(view.Stack.Cards, c => c.JobId == "tactus:1");
            Assert.Contains(view.Stack.Cards, c => c.JobId == "hydra:" + a.Id.ToString("N"));
            Assert.True(view.HasFinished);
            var clear = Assert.Single(content.Actions);
            Assert.Equal(("Clear done", "Clear done"), (clear.Name, clear.Text));

            h.Main.OnMain(clear.Invoke);
            Pump(h, window);
            Assert.DoesNotContain(model.BackgroundJobs.Jobs, job => job.Id == "tactus:1");
            Assert.Empty(content.Actions);
        }
        finally
        {
            BackgroundTasksPane.Scope = keepScope;
            window.Close();
        }
    });
}
