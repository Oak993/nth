using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// The timeline as the chat view hosts it (DroppyCode/App/Views/Chat/ChatView.swift:97-123,143-145,713-768; ThreadTimeline.swift:26-35,176-208): a real chat view on a
// real thread of a real app model, in a shown window.
[Collection("Headless")]
public sealed class ThreadTimelineChatTests(HeadlessSession headless)
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;

    private static ThreadTimeline Timeline(ChatView view) => Assert.Single(LogicalQuery.SelfAndDescendants<ThreadTimeline>(view));

    private static void Drain(AppModelHarness h)
    {
        h.Main.RunUntilIdle();
        h.Main.OnMain(() => Dispatcher.UIThread.RunJobs());
        h.Main.RunUntilIdle();
    }

    private void WithChat(Action<ChatView, ThreadRuntime, AppModel, AppModelHarness, Avalonia.Controls.Window, ManualFrameSource> check, double width = 1400, double height = 900) =>
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            var project = new Project("Project", h.NewFolder("project"));
            h.WriteLibrary([project], []);
            var model = h.MakeModel();
            Avalonia.Controls.Window? window = null;
            try
            {
                ChatView view = null!;
                ThreadRuntime runtime = null!;
                h.Main.OnMain(() =>
                {
                    model.Settings.ShowsUsagePanel = false;
                    model.Settings.HydraEnabled = false;
                    runtime = model.Runtime(model.NewThread(project)!.Id);
                    view = new ChatView(runtime);
                    window = new Avalonia.Controls.Window { Content = view, Width = width, Height = height };
                    AppEnvironment.SetAppModel(window, model);
                    AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
                    ChatHeadlessFixtures.Track(window);
                    window.Show();
                    window.UpdateLayout();
                });
                Drain(h);
                check(view, runtime, model, h, window!, frames);
            }
            finally
            {
                h.Main.OnMain(() => { window?.Close(); model.Terminals.TerminateAll(); });
                Drain(h);
            }
        });

    [Fact]
    [Trait("Req", "PR-CHAT-160")] // ThreadTimeline.swift:26-35; ChatView.swift:111 .equatable(): the timeline follows only when one of its eight handed-down values changed
    public void A_title_effort_or_mode_change_does_not_draw_the_timeline_again_and_a_value_it_was_handed_does()
        => WithChat((view, runtime, model, h, window, frames) =>
        {
            var timeline = Timeline(view);
            // An empty thread draws its prompt afresh whenever the timeline draws: the instance stands in for a render count.
            var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(view));
            var id = runtime.ThreadID;
            // The thread's title, its effort and its mode change (the chat view re-renders for each, the timeline reads none of them).
            h.Main.OnMain(() => model.UpdateThread(id, thread => thread with { Title = "A new title", HasCustomTitle = true })); Drain(h);
            h.Main.OnMain(() => model.UpdateThread(id, thread => thread with { Effort = thread.Effort == "high" ? "low" : "high" })); Drain(h);
            h.Main.OnMain(() => model.UpdateThread(id, thread => thread with { InteractionMode = InteractionMode.Plan })); Drain(h);
            Assert.Same(timeline, Timeline(view)); Assert.Same(prompt, Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(view)));
            // The project's name is one of the eight: it is handed down again, and the timeline follows it (the prompt reads "What should we build in <project>?").
            h.Main.OnMain(() => model.UpdateProject(model.Project(model.Thread(id)!.ProjectID)!.Id, project => project with { Name = "Renamed" })); Drain(h);
            Assert.Same(timeline, Timeline(view));
            var fresh = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(view));
            Assert.NotSame(prompt, fresh);
            Assert.Contains("Renamed", string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(fresh).Select(text => text.Text)), StringComparison.Ordinal);
        });

    private static void SetEntries(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries) =>
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));

    private static TimelineMinimapColumn? Rail(ChatView view) => LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(view).SingleOrDefault();

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-003 and PR-CHAT-158: when the chat shows the rail (ChatView.swift:108-110) and that it holds the column's leading edge through a dock's slide (ThreadTimeline.swift:201-207)
    public void The_chat_shows_the_rail_for_two_sent_messages_in_a_pane_900_wide_after_the_reserves_and_it_holds_its_edge_while_a_dock_takes_it()
        => WithChat((view, runtime, model, h, window, frames) =>
        {
            var turns = TimelineMount.Turns(3, sentences: 4).ToList();
            h.Main.OnMain(() => SetEntries(runtime, turns)); Drain(h);
            h.Main.OnMain(() => window.UpdateLayout()); Drain(h);
            // Shown: two or more sent messages, nothing docked on the leading side, room for the column and the rail (:108-110).
            Assert.NotNull(Rail(view));
            // The room after both reserves counts: a pane 899 wide leaves too little, 900 enough.
            h.Main.OnMain(() => { window.Width = 899; window.UpdateLayout(); }); Drain(h); h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Drain(h);
            Assert.Null(Rail(view));
            h.Main.OnMain(() => { window.Width = 900; window.UpdateLayout(); }); Drain(h); h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Drain(h);
            Assert.NotNull(Rail(view));
            h.Main.OnMain(() => { window.Width = 1400; window.UpdateLayout(); }); Drain(h); h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Drain(h);
            var rail = Rail(view)!; var edge = rail.TranslatePoint(default, window)!.Value.X;
            // A panel docks on the leading side: the conversation slides over to make room and the rail stays on the pane's edge through the slide, then goes.
            h.Main.OnMain(() => model.Settings.ShowsUsagePanel = true); Drain(h);
            var shifted = false;
            for (var step = 0; step < 14 && Rail(view) is { } showing; step++)
            {
                Assert.Equal(edge, showing.TranslatePoint(default, window)!.Value.X, 0.6);
                shifted |= ChatView.GetReserveSlideOffset(Timeline(view)) != 0 || ChatView.GetReserveSlideLeading(Timeline(view)) != 0;
                h.Main.Advance(TimeSpan.FromMilliseconds(30)); frames.Advance(TimeSpan.FromMilliseconds(30)); Drain(h);
            }
            Assert.True(shifted, "the conversation moved with the dock");
            h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Drain(h);
            Assert.Null(Rail(view));
            // The dock goes: the rail comes back.
            h.Main.OnMain(() => model.Settings.ShowsUsagePanel = false); Drain(h); h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Drain(h);
            Assert.NotNull(Rail(view)); Assert.Equal(edge, Rail(view)!.TranslatePoint(default, window)!.Value.X, 0.6);
        });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-003: the rail is shown by default on the unmeasured first frame (ChatView.swift:108-110 `paneSize == .zero`): the pane size is the chat view's own private measurement, set back to unmeasured here
    public void The_rail_is_allowed_while_the_pane_is_unmeasured_and_not_in_a_narrow_measured_pane()
        => WithChat((view, runtime, model, h, window, frames) =>
        {
            h.Main.OnMain(() => { window.Width = 800; window.UpdateLayout(); }); Drain(h);
            var size = Field<Size>(view, "paneSize"); Assert.True(size.Width < 900 && size.Width > 0, size.ToString());
            (string? Project, string? Directory, bool Rewind, double Height, bool Minimap)? Inputs() => Field<(string?, string?, bool, double, bool)?>(view, "timelineInputs");
            Assert.False(Inputs()!.Value.Minimap, "a measured pane 800 wide leaves no room");
            void Refresh() => typeof(ChatView).GetMethod("RefreshScene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, null);
            typeof(ChatView).GetField("paneSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(view, default(Size));
            h.Main.OnMain(Refresh); Drain(h);
            Assert.True(Inputs()!.Value.Minimap, "the first frame, before anything is measured, allows it");
            typeof(ChatView).GetField("paneSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(view, size);
            h.Main.OnMain(Refresh); Drain(h);
            Assert.False(Inputs()!.Value.Minimap);
        });
}
