using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
                ThreadTimelineRig.AwaitHistory(h, runtime);
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

    private static ScrollViewer Scroller(ChatView view) => LogicalQuery.SelfAndDescendants<ScrollViewer>(Timeline(view)).First();

    private static Button? JumpButton(ChatView view) => LogicalQuery.SelfAndDescendants<JumpToLatestButton>(view).SingleOrDefault() is { } jump ? LogicalQuery.SelfAndDescendants<Button>(jump).FirstOrDefault() : null;

    private static void Settle(AppModelHarness h, ManualFrameSource frames, Avalonia.Controls.Window window, int milliseconds)
    {
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 50)
        {
            h.Main.OnMain(() => window.UpdateLayout()); h.Main.Advance(TimeSpan.FromMilliseconds(50)); frames.Advance(TimeSpan.FromMilliseconds(50)); Drain(h);
        }
    }

    [Fact]
    [Trait("Req", "UI-CHAT-006")] // ChatView.swift:1085-1104; ThreadTimeline.swift:402-409,673-677,689-712; the cousin's scroll-to-bottom button (map/CATALOGUE.md timeline.scroll-to-bottom-button)
    public void The_jump_button_shows_once_the_end_is_out_of_sight_glides_to_the_end_when_pressed_and_goes_at_once_when_a_row_is_followed()
        => WithChat((view, runtime, model, h, window, frames) =>
        {
            h.Main.OnMain(() => SetEntries(runtime, TimelineMount.Turns(8, sentences: 10).ToList())); Settle(h, frames, window, 2500);
            var scroll = Scroller(view);
            double Distance() => scroll.Extent.Height - scroll.Offset.Y - scroll.Viewport.Height;
            Assert.True(Distance() < 2); Assert.Null(JumpButton(view));
            // :402-409 the end sentinel is one point tall: while it is on screen (the reader is at the end, or within the 48 the timeline puts back) there is no button; scrolled well away, there is.
            h.Main.OnMain(() => scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height - 150)); Settle(h, frames, window, 50);
            var button = JumpButton(view); Assert.NotNull(button);
            // It arrives scaling up from 0.6 with opacity and a 10 point rise, on the spring of response 0.38 and damping 0.78, and rests settled.
            var jump = LogicalQuery.SelfAndDescendants<JumpToLatestButton>(view).Single(); var circle = LogicalQuery.SelfAndDescendants<ChromeCircleButton>(jump).Single();
            Assert.True(circle.Opacity < 0.9); Assert.Equal("Jump to latest", ToolTip.GetTip(button));
            Assert.Equal("arrow.down", LogicalQuery.SelfAndDescendants<SwiftImage>(circle).First().SymbolName);
            Settle(h, frames, window, 100); Assert.InRange(circle.Opacity, 0.01, 0.999);
            Settle(h, frames, window, 1500); Assert.Equal(1, circle.Opacity);
            // Pressed: it glides to the end over a third of a second (the timeline's own smooth glide) and re-pins; the button leaves once the end is on screen.
            h.Main.OnMain(() => scroll.Offset = new Vector(0, 100)); Settle(h, frames, window, 400);
            var before = scroll.Offset.Y;
            Field<TimelineScrollTracking>(Timeline(view), "tracking").IsPinnedToBottom = false;
            h.Main.OnMain(() => JumpButton(view)!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))); Drain(h);
            Assert.True(Field<TimelineScrollTracking>(Timeline(view), "tracking").IsPinnedToBottom && Distance() > 100, "re-pinned the moment it is pressed, before the glide has gone anywhere");
            Settle(h, frames, window, 100); Assert.True(scroll.Offset.Y > before + 20 && Distance() > 100, "gliding: " + scroll.Offset.Y + " of " + (scroll.Extent.Height - scroll.Viewport.Height));
            Settle(h, frames, window, 700); Assert.True(Distance() < 2); Assert.True(Field<TimelineScrollTracking>(Timeline(view), "tracking").IsPinnedToBottom);
            Settle(h, frames, window, 800); Assert.Null(JumpButton(view));
            // :689-712 a reader a little above the end (still following) has the button; a new row lands and brings them to it: the button goes with no animation at all.
            h.Main.OnMain(() => scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height - 30)); Settle(h, frames, window, 1500);
            Assert.NotNull(JumpButton(view));
            h.Main.OnMain(() => SetEntries(runtime, runtime.Entries.Concat(TimelineMount.Turns(1, "new-", sentences: 4)).ToList())); Drain(h);
            Assert.False(Field<TimelineScrollState>(view, "scrollState").ShowsJumpButton);
            Assert.True(JumpButton(view) is null || JumpButton(view)!.GetVisualParent() is null, "removed at once, not faded");
        });

    [Fact]
    [Trait("Req", "UI-CHAT-006")] // ThreadTimeline.swift:402-409 (the end sentinel's spring, response 0.38 and damping 0.78, both ways); ChatView.swift:1089-1103 (the scale, opacity and offset of each side; 10 points of bottom padding in a zero-height frame at the top of the composer)
    public void The_jump_button_comes_and_goes_on_the_sentinels_spring_through_its_scale_opacity_and_rise_and_sits_ten_points_above_the_composer()
        => WithChat((view, runtime, model, h, window, frames) =>
        {
            h.Main.OnMain(() => SetEntries(runtime, TimelineMount.Turns(8, sentences: 10).ToList())); Settle(h, frames, window, 2500);
            var scroll = Scroller(view); var end = scroll.Extent.Height - scroll.Viewport.Height; var spring = Animation.Spring(response: 0.38, dampingFraction: 0.78);
            Assert.True(end > 600); Assert.Null(JumpButton(view));
            // The reader leaves the end: the sentinel goes out of sight, and the button begins to arrive from scale 0.6, opacity 0, 10 points low.
            h.Main.OnMain(() => { scroll.Offset = new Vector(0, end - 150); window.UpdateLayout(); }); Drain(h);
            var jump = LogicalQuery.SelfAndDescendants<JumpToLatestButton>(view).Single(); var circle = LogicalQuery.SelfAndDescendants<ChromeCircleButton>(jump).Single();
            // :1094-1101 the transition is on the zero-height frame (the circle and its 10 points of bottom padding), whose centre is the point at the top of the composer.
            var frame = Assert.IsAssignableFrom<Control>(circle.Parent); Assert.Equal(0, frame.Bounds.Height);
            double Scale() => Assert.IsType<Avalonia.Media.TransformGroup>(frame.RenderTransform).Children.OfType<Avalonia.Media.ScaleTransform>().Single().ScaleX;
            double Rise() => Assert.IsType<Avalonia.Media.TransformGroup>(frame.RenderTransform).Children.OfType<Avalonia.Media.TranslateTransform>().Single().Y;
            Assert.Equal(0, frame.Opacity); Assert.Equal(0.6, Scale(), 6); Assert.Equal(10, Rise(), 6);
            // Where the circle is while it arrives: scaled about the frame's centre and lowered by the offset, so its centre sits (1 - scale) * (10 + radius) + the offset below its place
            // at rest: 19.6 points at the start, where scaling about the circle's own centre could give 10 (the reference runtime's rise fits the first, 2-chat-use.mov 10.357-10.478).
            var radius = circle.Bounds.Height / 2; double Centre() => circle.TranslatePoint(new Point(circle.Bounds.Width / 2, radius), window)!.Value.Y;
            var resting = window.Bounds.Height; // replaced below, once the circle has settled
            var below = Centre();
            // Each frame is the spring's own progress at that time: opacity, scale from 0.6 to 1 and the rise from 10 to 0 move together.
            var elapsed = 0;
            foreach (var step in new[] { 40, 60, 100, 200 })
            {
                h.Main.Advance(TimeSpan.FromMilliseconds(step)); frames.Advance(TimeSpan.FromMilliseconds(step)); Drain(h); elapsed += step;
                var progress = spring.Progress(elapsed / 1000.0);
                Assert.Equal(progress, frame.Opacity, 4); Assert.Equal(0.6 + 0.4 * progress, Scale(), 4); Assert.Equal(10 * (1 - progress), Rise(), 4);
            }
            h.Main.Advance(TimeSpan.FromSeconds(2)); frames.Advance(TimeSpan.FromSeconds(2)); Drain(h);
            Assert.Equal(1, frame.Opacity); Assert.Equal(1, Scale(), 6); Assert.Equal(0, Rise(), 6);
            resting = Centre(); Assert.Equal(resting + (1 - 0.6) * (10 + radius) + 10, below, 0.6); // at the start
            Assert.True(below - resting > 15, $"the circle starts {below - resting} points below its place; the offset alone is 10");
            // :1094-1095 the button's bottom is 10 points above the top of the composer, in a frame of no height there, centred on the column.
            var composerTop = LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.ComposerArea>(view).Single().TranslatePoint(default, window)!.Value.Y;
            Assert.Equal(0, jump.Bounds.Height); Assert.Equal(composerTop, jump.TranslatePoint(default, window)!.Value.Y, 0.5);
            Assert.Equal(composerTop - 10, circle.TranslatePoint(new Point(0, circle.Bounds.Height), window)!.Value.Y, 0.5);
            Assert.Equal(window.Bounds.Width / 2, circle.TranslatePoint(new Point(circle.Bounds.Width / 2, 0), window)!.Value.X, 1.5);
            // The reader returns to the end: it leaves on the same spring, to scale 0.85, opacity 0 and 6 points low.
            h.Main.OnMain(() => { scroll.Offset = new Vector(0, end); window.UpdateLayout(); }); Drain(h);
            Assert.Equal(1, frame.Opacity);
            elapsed = 0;
            foreach (var step in new[] { 40, 60, 100 })
            {
                h.Main.Advance(TimeSpan.FromMilliseconds(step)); frames.Advance(TimeSpan.FromMilliseconds(step)); Drain(h); elapsed += step;
                var progress = spring.Progress(elapsed / 1000.0);
                Assert.Equal(1 - progress, frame.Opacity, 4); Assert.Equal(1 - 0.15 * progress, Scale(), 4); Assert.Equal(6 * progress, Rise(), 4);
                Assert.Equal(resting + 0.15 * progress * (10 + radius) + 6 * progress, Centre(), 0.6); // leaving: scaled about the same point, then lowered by the offset
            }
            h.Main.Advance(TimeSpan.FromSeconds(2)); frames.Advance(TimeSpan.FromSeconds(2)); Drain(h);
            Assert.Null(JumpButton(view));
        });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-159 (the timeline's half: every measured frame hands its travel to the chrome, which maps it from 10 to 44 points, quantized to 0.04, and draws nothing at rest); ThreadTimeline.swift:488,1376; Chrome.swift:568-579,585-624. The veil's gradients are the Chrome unit's, and the capture comparison is not made here.
    public void Scrolling_the_timeline_drives_the_veil_from_ten_to_forty_four_points_of_travel_in_quarter_hundredths_and_nothing_at_rest()
        => WithChat((view, runtime, model, h, window, frames) =>
        {
            h.Main.OnMain(() => SetEntries(runtime, TimelineMount.Turns(8, sentences: 10).ToList())); Settle(h, frames, window, 2500);
            var scroll = Scroller(view); var chrome = Field<ChromeScrollModel>(view, "scrollChrome"); var veil = Assert.Single(LogicalQuery.SelfAndDescendants<PaneTopVeil>(view));
            var layer = Assert.IsType<Panel>(veil.Child);
            // At the end of a long thread the content has gone under the chrome row: the veil is drawn in full.
            Assert.Equal(1, chrome.Progress); Assert.Equal(1, layer.Opacity); Assert.Equal(2, layer.Children.Count);
            var seen = new SortedSet<double>();
            for (var offset = 0; offset <= 60; offset++)
            {
                h.Main.OnMain(() => scroll.Offset = new Vector(0, offset)); Settle(h, frames, window, 50);
                Assert.Equal(Math.Round(Math.Clamp((offset - 10) / 34.0, 0, 1) / 0.04, MidpointRounding.AwayFromZero) * 0.04, chrome.Progress, 6); // .rounded(): halves go up
                seen.Add(chrome.Progress);
                Assert.Equal(0.08 + 0.92 * chrome.Progress, layer.Opacity, 6);
                Assert.Equal(chrome.Progress > 0 ? 2 : 0, layer.Children.Count);
            }
            // The travel is mapped from 10 points (nothing yet) to 44 (all of it), and written at most 26 times over the whole range.
            Assert.True(seen.Count <= 26, $"{seen.Count} distinct values");
            Assert.Contains(0.0, seen); Assert.Contains(1.0, seen);
            h.Main.OnMain(() => scroll.Offset = new Vector(0, 0)); Settle(h, frames, window, 200);
            Assert.Equal(0, chrome.Progress); Assert.Empty(layer.Children);
        });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-158 (the slide happens only on a measured pane with no resize, no panel or sidebar hold and no thread switch); ChatView.swift:122-123,713-720,737-771. The slide mechanism itself is ChatViewPart1Tests' and ChatViewFollowupTests'.
    public void A_dock_slides_the_conversation_only_when_nothing_holds_the_slide()
        => WithChat((view, runtime, model, h, window, frames) =>
        {
            double Offset() => ChatView.GetReserveSlideOffset(Timeline(view));
            void Dock(bool on) { h.Main.OnMain(() => model.Settings.ShowsUsagePanel = on); Drain(h); }
            void Rest() { Settle(h, frames, window, 1500); }
            // Normal: the padding is the final reserve at once and the conversation glides over on its offset.
            Dock(true); Assert.True(Offset() < 0, $"offset {Offset()}"); Rest(); Assert.Equal(0, Offset());
            Dock(false); Assert.True(Offset() > 0); Rest();
            // A window being dragged to a new size: no glide, the offset is 0 from the first frame.
            var resize = AppEnvironment.GetWindowLiveResize(view);
            typeof(WindowLiveResize).GetProperty(nameof(WindowLiveResize.IsActive))!.SetValue(resize, true);
            Dock(true); Assert.Equal(0, Offset()); Assert.True(Timeline(view).Margin.Left > 0, "the padding still lands in one layout");
            typeof(WindowLiveResize).GetProperty(nameof(WindowLiveResize.IsActive))!.SetValue(resize, false);
            Dock(false); Rest();
            // The sidebar being dragged: held too.
            h.Main.OnMain(() => model.Sidebar.BeginDrag()); Drain(h);
            Dock(true); Assert.Equal(0, Offset());
            h.Main.OnMain(() => model.Sidebar.EndDrag()); Drain(h);
        });
}
