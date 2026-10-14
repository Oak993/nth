using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
            // A thread with rows, so a pass of the body is work that leaves a trace: the timeline counts the passes of its body.
            var turns = TimelineMount.Turns(3, sentences: 4).ToList();
            h.Main.OnMain(() => SetEntries(runtime, turns)); Drain(h);
            h.Main.OnMain(() => window.UpdateLayout()); Drain(h);
            var timeline = Timeline(view); Assert.Equal(3, LogicalQuery.SelfAndDescendants<DisplayBlockView>(view).Count());
            var id = runtime.ThreadID; var passes = timeline.DrawPasses; Assert.True(passes > 0);
            // The thread's title, its effort and its mode change (the chat view re-renders for each, the timeline reads none of them): no pass of the body.
            h.Main.OnMain(() => model.UpdateThread(id, thread => thread with { Title = "A new title", HasCustomTitle = true })); Drain(h);
            Assert.Equal(passes, timeline.DrawPasses);
            h.Main.OnMain(() => model.UpdateThread(id, thread => thread with { Effort = thread.Effort == "high" ? "low" : "high" })); Drain(h);
            Assert.Equal(passes, timeline.DrawPasses);
            h.Main.OnMain(() => model.UpdateThread(id, thread => thread with { InteractionMode = InteractionMode.Plan })); Drain(h);
            Assert.Equal(passes, timeline.DrawPasses); Assert.Same(timeline, Timeline(view));
            // The project's name is one of the eight: it is handed down again, and the timeline's body runs once for it.
            h.Main.OnMain(() => model.UpdateProject(model.Project(model.Thread(id)!.ProjectID)!.Id, project => project with { Name = "Renamed" })); Drain(h);
            Assert.Equal(passes + 1, timeline.DrawPasses); Assert.Same(timeline, Timeline(view));
            // A title once more is still nothing to it.
            h.Main.OnMain(() => model.UpdateThread(id, thread => thread with { Title = "Another title" })); Drain(h);
            Assert.Equal(passes + 1, timeline.DrawPasses);
        });

    private static void SetEntries(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries) =>
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));

    private static TimelineMinimapColumn? Rail(ChatView view) => LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(view).SingleOrDefault();

    [Fact]
    [Trait("Req", "UI-CHAT-003")] // Partial proof UI-CHAT-003 and PR-CHAT-158: when the chat shows the rail
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
            var shifted = false; var rowWidth = -1.0;
            for (var step = 0; step < 14 && Rail(view) is { } showing; step++)
            {
                Assert.Equal(edge, showing.TranslatePoint(default, window)!.Value.X, 0.6);
                // The padding lands once, at the final width, and the glide is a transform: the rows wrap once and keep their width through every frame of it (no re-wrap per frame).
                var width = LogicalQuery.SelfAndDescendants<DisplayBlockView>(view).First().Bounds.Width; if (rowWidth < 0) rowWidth = width; Assert.Equal(rowWidth, width, 3);
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
    [Trait("Req", "UI-CHAT-003")] // Partial proof UI-CHAT-003: the rail is shown by default on the unmeasured first frame
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

    // 1 - e^(-w t) (1 + w t) with w = 2 pi / response: SwiftUI's `.smooth(duration:)`, a critically damped spring of that response, written out here (never the port's own Animation).
    private static double Smooth(double response, double seconds) => seconds <= 0 ? 0 : 1 - Math.Exp(-2 * Math.PI / response * seconds) * (1 + 2 * Math.PI / response * seconds);

    [Fact]
    [Trait("Req", "UI-CHAT-006")] // ThreadTimeline.swift:673-677 `withAnimation(.smooth(duration: 0.35)) { proxy.scrollTo(end) }` on the press; ChatView.swift:1085-1090 the button is ChromeCircleButton(symbol: "arrow.down") whose glyph is Chrome.iconFont (Chrome.swift:423, callout semibold, 12 pt per ED-059)
    public void Pressing_the_jump_button_glides_the_scroll_to_the_end_along_the_smooth_0_35_second_curve_and_its_glyph_is_the_chrome_icon_font()
        => WithChat((view, runtime, model, h, window, frames) =>
        {
            h.Main.OnMain(() => SetEntries(runtime, TimelineMount.Turns(8, sentences: 10).ToList())); Settle(h, frames, window, 2500);
            var scroll = Scroller(view);
            h.Main.OnMain(() => scroll.Offset = new Vector(0, 100)); Settle(h, frames, window, 400);
            var button = JumpButton(view); Assert.NotNull(button);
            // The glyph: arrow.down in Chrome.iconFont, the callout size (12 pt) in the semibold weight.
            var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(button!).First();
            Assert.Equal("arrow.down", glyph.SymbolName);
            var font = SwiftImage.GetFont(glyph)!.Resolve();
            Assert.Equal(12, font.FontSize); Assert.Equal(Avalonia.Media.FontWeight.SemiBold, font.FontWeight);
            var start = scroll.Offset.Y; var end = scroll.Extent.Height - scroll.Viewport.Height;
            Assert.True(end - start > 600, $"{end - start} points to glide");
            h.Main.OnMain(() => JumpButton(view)!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))); Drain(h);
            // Part way at 50, 100, 150 and 200 ms, and at the end by 400: the offset is the start plus the distance times smooth(0.35)'s progress, so another duration or curve (0.2 s,
            // 0.6 s, linear, an ease) is 90 to 250 points off at 100 ms.
            var elapsed = 0;
            foreach (var step in new[] { 50, 50, 50, 50, 200 })
            {
                Settle(h, frames, window, step); elapsed += step;
                var expected = start + (end - start) * Smooth(0.35, elapsed / 1000.0);
                Assert.True(Math.Abs(scroll.Offset.Y - expected) <= 0.06 * (end - start) + 4, $"at {elapsed} ms the offset is {scroll.Offset.Y:F0}, smooth(0.35) has {expected:F0}, from {start:F0} to {end:F0}");
            }
            Assert.True(end - scroll.Offset.Y < 3, $"{end - scroll.Offset.Y} points short of the end after 400 ms");
        });

    [Fact]
    [Trait("Req", "UI-CHAT-006")] // When it shows, the press and the hide on a followed row (the spring's values and the place are proven apart, below): ChatView.swift:1085-1104; ThreadTimeline.swift:402-409,673-677,689-712; the reference recording's scroll-to-bottom button (map/CATALOGUE.md timeline.scroll-to-bottom-button)
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
            var jump = LogicalQuery.SelfAndDescendants<JumpToLatestButton>(view).Single(); var circle = LogicalQuery.SelfAndDescendants<ChromeCircleButton>(jump).Single(); var frame = (Control)circle.Parent!; // the transition is on the zero-height frame
            Assert.True(frame.Opacity < 0.9); Assert.Equal("Jump to latest", ToolTip.GetTip(button));
            Assert.Equal("arrow.down", LogicalQuery.SelfAndDescendants<SwiftImage>(circle).First().SymbolName);
            Settle(h, frames, window, 100); Assert.InRange(frame.Opacity, 0.01, 0.999);
            Settle(h, frames, window, 1500); Assert.Equal(1, frame.Opacity);
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
    [Trait("Req", "PR-CHAT-021")] // ThreadTimeline.swift:416-420 and ComposerView.swift:52-55 (the same column as the composer: "messages line up with its edges"); map/CATALOGUE.md C 9.000 (rows x 1004-1824), C 8.403-13.900 (the composer pill x 1003-1824)
    public void The_messages_line_up_with_the_composer_at_every_pane_width()
        => WithChat((view, runtime, model, h, window, frames) =>
        {
            h.Main.OnMain(() => SetEntries(runtime, TimelineMount.Turns(3, sentences: 4).ToList())); Settle(h, frames, window, 2200);
            foreach (var width in new[] { 2290.0, 1400.0, 1000.0, 860.0, 700.0 })
            {
                h.Main.OnMain(() => { window.Width = width; window.UpdateLayout(); }); Drain(h); Settle(h, frames, window, 400);
                var rows = LogicalQuery.SelfAndDescendants<DisplayBlockView>(view).ToList(); Assert.NotEmpty(rows);
                var column = LogicalQuery.SelfAndDescendants<Decorator>(LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.ComposerArea>(view).Single()).First(candidate => candidate.MaxWidth == 820);
                var edge = column.TranslatePoint(default, window)!.Value.X; var wide = column.Bounds.Width;
                Assert.Equal(Math.Min(820, window.Bounds.Width - 40), wide, 1);
                Assert.All(rows, row => { Assert.Equal(edge, row.TranslatePoint(default, window)!.Value.X, 1); Assert.Equal(wide, row.Bounds.Width, 1); });
            }
        });

    [Fact]
    [Trait("Req", "UI-CHAT-006")] // The spring's values both ways and the button's place (when it shows, the press and the hide are proven apart, above): ThreadTimeline.swift:402-409 (the end sentinel's spring, response 0.38 and damping 0.78, both ways); ChatView.swift:1089-1103 (the scale, opacity and offset of each side; 10 points of bottom padding in a zero-height frame at the top of the composer)
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
                // ED-NEW-perf-motion-9 (3): the opacity is the drawn one, the spring's progress held to 0..1 where it passes its end.
                Assert.Equal(Math.Clamp(progress, 0, 1), frame.Opacity, 4); Assert.Equal(0.6 + 0.4 * progress, Scale(), 4); Assert.Equal(10 * (1 - progress), Rise(), 4);
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
                Assert.Equal(Math.Clamp(1 - progress, 0, 1), frame.Opacity, 4); Assert.Equal(1 - 0.15 * progress, Scale(), 4); // ED-NEW-perf-motion-9 (3) Assert.Equal(6 * progress, Rise(), 4);
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
            Dock(false); Rest();
            // A panel's grip held (a panel being resized), and the terminal's: held. The padding lands in one layout all the same.
            var grips = Field<PanelResizeState>(view, "panelResize");
            h.Main.OnMain(() => grips.Begin(new Size(300, 200))); Drain(h);
            Dock(true); Assert.Equal(0, Offset()); Assert.True(Timeline(view).Margin.Left > 0);
            h.Main.OnMain(() => grips.End()); Drain(h); Dock(false); Rest();
            h.Main.OnMain(() => runtime.IsTerminalResizing = true); Drain(h);
            Dock(true); Assert.Equal(0, Offset());
            h.Main.OnMain(() => runtime.IsTerminalResizing = false); Drain(h); Dock(false); Rest();
            // A thread switch: the scene is of another thread than the runtime's, so nothing glides.
            var scene = typeof(ChatView).GetField("sceneThreadID", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var own = scene.GetValue(view); scene.SetValue(view, Guid.NewGuid());
            Dock(true); Assert.Equal(0, Offset());
            scene.SetValue(view, own); Dock(false); Rest();
            // Nothing holding it: it glides again.
            Dock(true); Assert.True(Offset() < 0, $"offset {Offset()}"); Rest();
        });

    // PR-CHAT-159 whole (ChatView.swift:143-145; ThreadTimeline.swift:488,1376; Chrome.swift:568-629). The video holds the veil at full strength (2-chat-use.mov C 8.382 against
    // C 8.403, one frame with the veil and one without, window top at video row 30; E:\Droppy\work\reference-video\frames\gap-R2-02\scrim_profile.json: the veil's strength
    // against the row, 1 px = 1 pt). The Swift's formulas at the quantized progress 0.88 (travel 40: raw 0.882) reproduce that profile with an rms of 0.014 and a worst row of
    // 0.025 (progress 1.0 would give 0.053 and 0.073), so the frame is a scroll position of 40 points; the capture below renders the port's veil over black and over white at the
    // same progress, solves the strength of each row from the two (out = backdrop x (1 - s) + colour x s), and compares it row by row with the frame's. The same capture at travel 0, 20
    // and 44 in both themes is compared, row by row and channel by channel over black and over white, with the Swift's composite (the colour: the black or white scrim and the theme's glass tint;
    // the light theme and the mid travel have no frame: OD-0098, the Swift is the proof).
    private static readonly (int Row, double Strength)[] VideoScrim =
    [
        (32, 0.452), (36, 0.463), (40, 0.461), (44, 0.461), (48, 0.469), (52, 0.433), (56, 0.403), (60, 0.371), (64, 0.324), (68, 0.281), (72, 0.236), (76, 0.199), (80, 0.173),
        (84, 0.145), (88, 0.097), (92, 0.061), (96, 0.029),
    ];

    // The Swift's veil composited at a row of its 72 points over a flat backdrop (one channel, 0 to 255), in a theme (Chrome.swift:587-623): the scrim (black over dark, white over light, at
    // 0.14 + 0.18 x progress / 0.30 + 0.26 x progress) and over it the tint (Chrome.glassTint at 0.30 / 0.18 of its own alpha), each held over the top 28 percent, eased to 0.45 at 62 and to
    // nothing at the foot. The whole veil's `.opacity(0.08 + 0.92 x progress)` (:622) sits on a ZStack with no compositing group, so it scales each of the two layers (the reading of
    // ThreadTimelineVeilTests): the backdrop is covered by the scrim at opacity x scrim, then by the tint at opacity x tint. Composing the two as a group and applying the opacity once
    // would change the backdrop by up to 4.0 of 255 grey levels (lighter, dark theme) and 4.6 (darker, light theme), at progress 0.4 (the tolerance below, 1.5 on the held rows, tells the two apart): UNVERIFIED on the Mac (OD-0001), ED-290-4.
    private static double[] VeilOver(double progress, bool dark, double row, double backdrop)
    {
        var u = row / 72.0;
        var held = u <= 0.28 ? 1 : u <= 0.62 ? 1 - 0.55 * (u - 0.28) / 0.34 : 0.45 * (1 - (u - 0.62) / 0.38);
        var tint = Marrow.UI.Chrome.Chrome.GlassTint.Resolve(dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        var scrimAlpha = (dark ? 0.14 + 0.18 * progress : 0.30 + 0.26 * progress) * held;
        var scrimColour = dark ? 0.0 : 255.0;
        var tintAlpha = (dark ? 0.30 : 0.18) * tint.A / 255.0 * held;
        var opacity = 0.08 + 0.92 * progress;
        return new[] { (double)tint.R, tint.G, tint.B }.Select(channel =>
        {
            var underTint = backdrop * (1 - opacity * scrimAlpha) + opacity * scrimAlpha * scrimColour;
            return underTint * (1 - opacity * tintAlpha) + opacity * tintAlpha * channel;
        }).ToArray();
    }

    // The port's veil over a flat backdrop at a quantized progress: red, green and blue of each of its 72 rows (a column through the middle).
    private static double[][] RenderedRows(double progress, bool dark, Avalonia.Media.Color backdrop)
    {
        var model = new ChromeScrollModel();
        model.Update(travel: 10 + 34 * progress);
        Assert.Equal(progress, model.Progress, 6);
        var window = new Avalonia.Controls.Window
        {
            Width = 200, Height = 100, Background = new Avalonia.Media.SolidColorBrush(backdrop),
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light,
            Content = new PaneTopVeil(model) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
        var rows = Enumerable.Range(0, 72).Select(y => new double[] { pixels.GetPixel(100, y).Red, pixels.GetPixel(100, y).Green, pixels.GetPixel(100, y).Blue }).ToArray();
        window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs();
        return rows;
    }

    // The strength of each row (out = backdrop x (1 - s) + colour x s: over white and over black the two differ by 255 (1 - s)).
    private static double[] RenderedStrength(double progress, bool dark)
    {
        var black = RenderedRows(progress, dark, Avalonia.Media.Colors.Black);
        var white = RenderedRows(progress, dark, Avalonia.Media.Colors.White);
        return Enumerable.Range(0, 72).Select(y => 1 - (white[y].Average() - black[y].Average()) / 255.0).ToArray();
    }

    [Fact]
    [Trait("Req", "PR-CHAT-159")] // App/Views/Chat/ChatView.swift:143-145; ThreadTimeline.swift:488, :1376; UI/Chrome/Chrome.swift:568-629
    public void The_veil_over_the_scrolled_conversation_follows_the_travel_in_both_themes_and_matches_the_frames_strength_row_by_row()
    {
        // (1) The mounted column: the timeline hands its travel to the chrome (travel 0, 20 and 44 -> progress 0, 0.28 and 1) and the veil over the pane draws from it.
        WithChat((view, runtime, model, h, window, frames) =>
        {
            h.Main.OnMain(() => SetEntries(runtime, TimelineMount.Turns(8, sentences: 10).ToList())); Settle(h, frames, window, 2500);
            var scroll = Scroller(view); var chrome = Field<ChromeScrollModel>(view, "scrollChrome"); var veil = Assert.Single(LogicalQuery.SelfAndDescendants<PaneTopVeil>(view));
            Assert.False(veil.IsHitTestVisible); // :624
            Assert.Equal(Chrome.VeilHeight, 72);
            var layer = Assert.IsType<Panel>(veil.Child);
            foreach (var (travel, progress) in new[] { (0, 0.0), (20, 0.28), (44, 1.0) })
            {
                h.Main.OnMain(() => scroll.Offset = new Vector(0, travel)); Settle(h, frames, window, 100);
                Assert.Equal(progress, chrome.Progress, 6); // ThreadTimeline.swift:488, Chrome.swift:568-579: from 10 to 44 points, quantized to 0.04
                Assert.Equal(0.08 + 0.92 * progress, layer.Opacity, 6);
                Assert.Equal(progress > 0 ? 2 : 0, layer.Children.Count); // nothing is drawn at rest
                Assert.Equal(72, layer.Height);
            }
            // :575 `if quantized != progress { progress = quantized }`: swept down from 44 to 0 a point at a time, the progress is written exactly when its quantized value moves, never again.
            var writes = 0; var moves = 0; var last = chrome.Progress;
            ((System.ComponentModel.INotifyPropertyChanged)chrome).PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChromeScrollModel.Progress)) writes++; };
            for (var offset = 44; offset >= 0; offset--)
            {
                h.Main.OnMain(() => scroll.Offset = new Vector(0, offset)); Settle(h, frames, window, 50);
                if (chrome.Progress != last) { moves++; last = chrome.Progress; }
            }
            Assert.InRange(moves, 20, 26);
            Assert.Equal(moves, writes);
        });
        // (2) The capture: the veil's strength row by row against the frame, then every row's colour over black and over white against the Swift's composite at travel 0, 20 and 44 and the
        // frame's travel in both themes (the colour, not only how much of the backdrop is covered: a veil of the wrong base or tint covers the same share).
        headless.OnUiThread(() =>
        {
        var previous = Marrow.UI.Theme.ThemeManager.Current;
        try
        {
            Marrow.UI.Theme.ThemeManager.Current = Marrow.UI.Theme.AppTheme.Monokai; // the recorder's theme
            var frame = RenderedStrength(0.88, dark: true);
            var squares = VideoScrim.Select(sample => Math.Pow(frame[sample.Row - 30] - sample.Strength, 2)).ToArray();
            var worst = VideoScrim.Max(sample => Math.Abs(frame[sample.Row - 30] - sample.Strength));
            Assert.True(Math.Sqrt(squares.Average()) < 0.03, "rms " + Math.Sqrt(squares.Average()));
            Assert.True(worst < 0.05, "worst row " + worst);
            Assert.InRange(frame[71], -0.02, 0.02); // clear at the foot (one grey level of rounding)
            foreach (var dark in new[] { true, false })
            {
                Marrow.UI.Theme.ThemeManager.Current = dark ? Marrow.UI.Theme.AppTheme.Monokai : Marrow.UI.Theme.AppTheme.ClaudeLight;
                foreach (var progress in new[] { 0.28, 0.4, 0.88, 1.0 })
                {
                    var onBlack = RenderedRows(progress, dark, Avalonia.Media.Colors.Black);
                    var onWhite = RenderedRows(progress, dark, Avalonia.Media.Colors.White);
                    for (var row = 0; row < 72; row += 3)
                    {
                        // The held top 28 percent (rows 0 to 19) is flat and drawn to within a grey level and a half: that tells the layer-by-layer composite from the grouped one, up to 4.0 (dark, lighter) or 4.6 (light, darker) levels
                        // away. Below it the gradient ramps and the renderer dithers it (about 2.5 levels either way on the headless Skia).
                        var tolerance = row <= 19 ? 1.5 : 3.5;
                        var expectedBlack = VeilOver(progress, dark, row + 0.5, 0);
                        var expectedWhite = VeilOver(progress, dark, row + 0.5, 255);
                        for (var channel = 0; channel < 3; channel++)
                        {
                            Assert.True(Math.Abs(onBlack[row][channel] - expectedBlack[channel]) <= tolerance, $"{(dark ? "dark" : "light")} progress {progress} row {row} channel {channel} over black: drawn {onBlack[row][channel]}, Swift {expectedBlack[channel]:F1}");
                            Assert.True(Math.Abs(onWhite[row][channel] - expectedWhite[channel]) <= tolerance, $"{(dark ? "dark" : "light")} progress {progress} row {row} channel {channel} over white: drawn {onWhite[row][channel]}, Swift {expectedWhite[channel]:F1}");
                        }
                    }
                }
                // Travel 0: nothing is drawn at all, the backdrop shows through untouched.
                var restBlack = RenderedRows(0, dark, Avalonia.Media.Colors.Black); var restWhite = RenderedRows(0, dark, Avalonia.Media.Colors.White);
                Assert.All(restBlack.SelectMany(row => row), value => Assert.Equal(0, value));
                Assert.All(restWhite.SelectMany(row => row), value => Assert.Equal(255, value));
            }
            // :571-576 the same model, alone: the quantum is 0.04 and the progress is written only when its quantized value moves.
            var alone = new ChromeScrollModel(); var changes = 0;
            ((System.ComponentModel.INotifyPropertyChanged)alone).PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChromeScrollModel.Progress)) changes++; };
            alone.Update(3); Assert.Equal(0, changes); // before the start offset 10
            alone.Update(11); Assert.Equal(1, changes); // 0.04
            alone.Update(11.2); alone.Update(12); Assert.Equal(1, changes); // 0.88 and 1.47 steps: the same step
            alone.Update(12.1); Assert.Equal(2, changes); // 1.54 steps: the next one
            alone.Update(200); Assert.Equal(1, alone.Progress); // clamped
        }
        finally { Marrow.UI.Theme.ThemeManager.Current = previous; }
        });
    }
}
