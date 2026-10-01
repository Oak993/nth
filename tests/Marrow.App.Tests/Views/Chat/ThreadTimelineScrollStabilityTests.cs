using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Finding F-1 (porting/reality/F1_TIMELINE_ED_DRAFT.md): the port keeps an eager row stack instead of the Swift lazy
// stack and its repair machinery (ThreadTimeline.swift:747-893, :1153-1235). These tests check the behaviour that
// machinery protects, on the real timeline with a real runtime: after the conversation changes shape the pane is never
// blank, a pinned reader stays at the end, and a reader who scrolled up stays where they are (PR-CHAT-170).
[Collection("Headless")]
public sealed class ThreadTimelineScrollStabilityTests(HeadlessSession headless)
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string LongReply = string.Join(" ", Enumerable.Repeat("This answer is long enough to wrap across several lines in the column.", 6));

    // The harness's manual SwiftClock (AppModelHarness binds one): Settle advances it with the frame clock, as real time
    // moves both, so timers such as the 150 ms scroll-freeze lift (ThreadTimeline.swift:727-747) run.
    [ThreadStatic] private static Marrow.Testing.ManualTimeProvider? clock;

    private static TimelineEntry Entry(string id, TimelineItem.Content content, Guid turn) => new(new TimelineItem(turn, content, id));

    // Finished turns: a question, a long answer and the turn's summary (Timeline.swift :25 turnEnd).
    private static IEnumerable<TimelineEntry> Turns(int count, string prefix = "")
    {
        for (var i = 0; i < count; i++)
        {
            var turn = Guid.NewGuid();
            yield return Entry(prefix + "user-" + i, new TimelineItem.Content.User(new UserMessage { Text = "Question number " + i + " about the conversation layout." }), turn);
            yield return Entry(prefix + "reply-" + i, new TimelineItem.Content.Assistant(new AssistantMessage { Text = LongReply }), turn);
            yield return Entry(prefix + "end-" + i, new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = turn, Status = TurnStatus.Completed, Duration = 2, FilesChanged = 0, Additions = 0, Deletions = 0 }), turn);
        }
    }

    private static void SetEntries(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries) =>
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));

    private static void AppendTurn(ThreadRuntime runtime) => SetEntries(runtime, runtime.Entries.Concat(Turns(1, "extra-")).ToList());

    private void WithLongThread(int turns, Action<ThreadTimeline, ThreadRuntime, Avalonia.Controls.Window, ManualFrameSource> check) => headless.OnUiThread(() =>
    {
        // FollowUpEditorTests' note: LoginEnvironment.Load is completed once on a pool thread first, so a later
        // Load().Wait() on the headless UI thread returns at once instead of blocking until its timeout.
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var h = new AppModelHarness();
        clock = h.Main.Time;
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model = new Marrow.App.AppModel();
        var id = Guid.NewGuid();
        DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id));
        DocumentPrefetch.Shared.Warm([id], urgent: true)!.Completion.GetAwaiter().GetResult();
        var runtime = new ThreadRuntime(id, model);
        var view = new ThreadTimeline(runtime, new ChromeScrollModel(), new TimelineScrollState(), "Project", null, false, 480);
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = view;
        var window = new Avalonia.Controls.Window { Content = host, Width = 960, Height = 480 };
        AppEnvironment.SetWindowLiveResize(host, new Marrow.App.Window.WindowLiveResize(window));
        try
        {
            window.Show();
            Settle(window, frames);
            SetEntries(runtime, Turns(turns));
            Settle(window, frames);
            check(view, runtime, window, frames);
        }
        finally
        {
            clock = null;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    // Two seconds of frames and clock time, laying out between steps.
    private static void Settle(Avalonia.Controls.Window window, ManualFrameSource frames)
    {
        for (var pass = 0; pass < 40; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            frames.Advance(TimeSpan.FromMilliseconds(50));
            clock?.Advance(TimeSpan.FromMilliseconds(50));
            window.UpdateLayout();
        }
    }

    // The live scroll view (the outermost one in the timeline).
    private static ScrollViewer Scroller(ThreadTimeline view) => view.GetVisualDescendants().OfType<ScrollViewer>().First();

    private static string Where(ScrollViewer s) => FormattableString.Invariant($"offset {s.Offset.Y:F0} viewport {s.Viewport.Height:F0} extent {s.Extent.Height:F0}");

    private static bool AtEnd(ScrollViewer scroller) => scroller.Extent.Height - (scroller.Offset.Y + scroller.Viewport.Height) < 2;

    private static IEnumerable<TextBlock> TextsOnScreen(ThreadTimeline view, ScrollViewer scroller)
    {
        var viewport = new Rect(scroller.Bounds.Size);
        return view.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Bounds.Height > 0 && !string.IsNullOrEmpty(text.Text))
            .Select(text => (text, origin: text.TranslatePoint(default, scroller)))
            .Where(pair => pair.origin is { } o && new Rect(o, pair.text.Bounds.Size).Intersects(viewport))
            .OrderBy(pair => pair.origin!.Value.Y).Select(pair => pair.text);
    }

    private static bool SomeTextOnScreen(ThreadTimeline view, ScrollViewer scroller) => TextsOnScreen(view, scroller).Any();

    // The oldest question laid out in the scroll view (rows outside the window are not built; the minimap rail is not counted).
    private static int OldestQuestion(ThreadTimeline view) => Scroller(view).GetVisualDescendants().OfType<TextBlock>()
        .Select(text => System.Text.RegularExpressions.Regex.Match(text.Text ?? text.Inlines?.Text ?? "", @"^Question number (\d+) "))
        .Where(match => match.Success).Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).Min();

    private static bool IsReaderScrolling(ThreadTimeline view)
    {
        var tracking = typeof(ThreadTimeline).GetField("tracking", Hidden)!.GetValue(view)!;
        return (bool)tracking.GetType().GetProperty("IsReaderScrolling")!.GetValue(tracking)!;
    }

    // Wheel notches over the first row on screen, as a person scrolls (a programmatic offset is not a reader's scroll).
    // Returns how far the timeline moved.
    private static double Wheel(ThreadTimeline view, Avalonia.Controls.Window window, int notches)
    {
        // Let the freeze from opening the thread lift (150 ms after the last movement, ThreadTimeline.swift:727-747), so
        // the first notch lands on a row; the freeze each notch starts is the case under test.
        var lift = System.Diagnostics.Stopwatch.StartNew();
        while (IsReaderScrolling(view) && lift.ElapsedMilliseconds < 3000)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Assert.False(IsReaderScrolling(view), "the scroll freeze never lifted");
        var start = Scroller(view).Offset.Y;
        var target = TextsOnScreen(view, Scroller(view)).First();
        var point = target.TranslatePoint(new Point(4, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        for (var notch = 0; notch < notches; notch++)
        {
            window.MouseWheel(point, new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();
        }
        return start - Scroller(view).Offset.Y;
    }

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:770-835 (repairLayout's goal: never blank, a pinned reader on the end)
    public void A_long_thread_opens_at_the_end_with_rows_on_screen() => WithLongThread(30, (view, _, _, _) =>
    {
        var scroller = Scroller(view);
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "the thread should be taller than the pane: " + Where(scroller));
        Assert.True(AtEnd(scroller), Where(scroller));
        Assert.True(SomeTextOnScreen(view, scroller), "blank pane: " + Where(scroller));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:440 .defaultScrollAnchor(.bottom, for: .sizeChanges) while pinned; :578-590 a pinned reader at rest is put back on the end after a reflow
    public void Narrowing_and_widening_the_window_keeps_a_pinned_reader_on_the_end_with_rows_on_screen() => WithLongThread(30, (view, _, window, frames) =>
    {
        window.Width = 560;
        Settle(window, frames);
        Assert.True(AtEnd(Scroller(view)), "narrow: " + Where(Scroller(view)));
        Assert.True(SomeTextOnScreen(view, Scroller(view)), "narrow blank: " + Where(Scroller(view)));
        // Widening shrinks the content under the offset; the anchor must not take the shrink off twice.
        window.Width = 1200;
        Settle(window, frames);
        Assert.True(AtEnd(Scroller(view)), "wide: " + Where(Scroller(view)));
        Assert.True(SomeTextOnScreen(view, Scroller(view)), "wide blank: " + Where(Scroller(view)));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:686-709 a row landing at the end brings a reader at the end down to it
    public void A_new_turn_arriving_keeps_a_pinned_reader_on_the_end() => WithLongThread(20, (view, runtime, window, frames) =>
    {
        AppendTurn(runtime);
        Settle(window, frames);
        Assert.True(AtEnd(Scroller(view)), Where(Scroller(view)));
        Assert.True(SomeTextOnScreen(view, Scroller(view)), "blank: " + Where(Scroller(view)));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:686-709 "a reader who scrolled up to read is left there"; :440 top anchor for size changes while not pinned
    public void A_new_turn_arriving_leaves_a_reader_scrolled_up_mid_thread_where_they_were() => WithLongThread(30, (view, runtime, window, frames) =>
    {
        Assert.True(Wheel(view, window, 30) > 1000, "the wheel did not move the reader up: " + Where(Scroller(view)));
        Settle(window, frames);
        var anchor = TextsOnScreen(view, Scroller(view)).First();
        var offset = Scroller(view).Offset.Y;
        AppendTurn(runtime);
        Settle(window, frames);
        Assert.False(AtEnd(Scroller(view)), "the reader was pulled to the end: " + Where(Scroller(view)));
        Assert.Equal(offset, Scroller(view).Offset.Y, 0);
        Assert.Same(anchor, TextsOnScreen(view, Scroller(view)).First());
    });

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:742-748, :770-835: a reflow while reading mid-thread never leaves the pane blank
    public void Resizing_while_scrolled_up_mid_thread_never_leaves_the_pane_blank() => WithLongThread(30, (view, _, window, frames) =>
    {
        Assert.True(Wheel(view, window, 30) > 1000, "the wheel did not move the reader up: " + Where(Scroller(view)));
        Settle(window, frames);
        foreach (var width in new[] { 560.0, 1200.0, 760.0 })
        {
            window.Width = width;
            Settle(window, frames);
            Assert.True(SomeTextOnScreen(view, Scroller(view)), FormattableString.Invariant($"blank at width {width}: ") + Where(Scroller(view)));
            Assert.False(AtEnd(Scroller(view)), FormattableString.Invariant($"pulled to the end at width {width}: ") + Where(Scroller(view)));
        }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:594-607 the initial window lands a beat later, above the viewport, "while the reader is already looking at the end"; :380-382 a visible pill loads a page
    public void A_long_thread_loads_its_initial_window_above_the_viewport_and_the_reader_stays_on_the_end() => WithLongThread(60, (view, _, _, _) =>
    {
        // 60 turns are 60 blocks. The first paint shows the newest TimelineWindow.FirstPaint; its earlier-messages pill
        // is on screen, so a page loads at once (:380-382), and the 250 ms task's window (TimelineWindow.Initial) is
        // then already covered. Either way at least the initial window is laid out, above the reader.
        Assert.InRange(OldestQuestion(view), 0, 60 - TimelineWindow.Initial);
        Assert.True(AtEnd(Scroller(view)), Where(Scroller(view)));
        Assert.True(SomeTextOnScreen(view, Scroller(view)), "blank: " + Where(Scroller(view)));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:366-382 history loads itself as the reader nears the top; :556-571 it lands above the viewport, the reader's place kept
    public void Earlier_messages_load_above_without_moving_the_reader_to_the_top_or_the_end() => WithLongThread(90, (view, _, window, frames) =>
    {
        var oldest = OldestQuestion(view);
        for (var round = 0; round < 30 && OldestQuestion(view) == oldest; round++)
        {
            Wheel(view, window, 5);
            Settle(window, frames);
        }
        Assert.True(OldestQuestion(view) < oldest, "no earlier page loaded while scrolling up (oldest " + oldest + ")");
        var scroller = Scroller(view);
        Assert.True(SomeTextOnScreen(view, scroller), "blank: " + Where(scroller));
        Assert.False(AtEnd(scroller), "the reader was sent to the end: " + Where(scroller));
        // The page went in above what the reader was looking at, so they are not left at the very top of it.
        Assert.True(scroller.Offset.Y > scroller.Viewport.Height, "the page did not land above the viewport: " + Where(scroller));
    });

    // A turn in flight: the question, a run of tool steps and the answer so far (no turnEnd yet).
    private static IEnumerable<TimelineEntry> RunningTurn(Guid turn)
    {
        yield return Entry("live-user", new TimelineItem.Content.User(new UserMessage { Text = "Run the checks and summarise." }), turn);
        for (var step = 0; step < 8; step++)
            yield return Entry("live-tool-" + step, new TimelineItem.Content.Tool(new ToolCall { Title = "Step " + step, ToolCallKind = ToolCall.Kind.Command }), turn);
        yield return Entry("live-reply", new TimelineItem.Content.Assistant(new AssistantMessage { Text = LongReply }), turn);
    }

    private static void SetPhase(ThreadRuntime runtime, RuntimePhase phase) =>
        typeof(ThreadRuntime).GetProperty("Phase")!.SetValue(runtime, phase);

    private static void FinishTurn(ThreadRuntime runtime, Guid turn)
    {
        SetPhase(runtime, RuntimePhase.Idle);
        SetEntries(runtime, runtime.Entries.Append(Entry("live-end", new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = turn, Status = TurnStatus.Completed, Duration = 9, FilesChanged = 0, Additions = 0, Deletions = 0 }), turn)).ToList());
    }

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:624-638 "The turn's end folds its steps into its answer ... A reader at the end is put back on the end once that has laid out"
    public void A_finishing_turn_reshapes_its_block_and_a_pinned_reader_stays_on_the_end() => WithLongThread(20, (view, runtime, window, frames) =>
    {
        var turn = Guid.NewGuid();
        SetPhase(runtime, RuntimePhase.Running);
        SetEntries(runtime, runtime.Entries.Concat(RunningTurn(turn)).ToList());
        Settle(window, frames);
        Assert.True(AtEnd(Scroller(view)), "running: " + Where(Scroller(view)));
        FinishTurn(runtime, turn);
        Settle(window, frames);
        Assert.True(AtEnd(Scroller(view)), "finished: " + Where(Scroller(view)));
        Assert.True(SomeTextOnScreen(view, Scroller(view)), "blank after the fold: " + Where(Scroller(view)));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:624-638 and :686-709: a reader who scrolled up is not moved by the turn ending; :542-556 never left past the end
    public void A_finishing_turn_leaves_a_reader_scrolled_up_mid_thread_where_they_were() => WithLongThread(30, (view, runtime, window, frames) =>
    {
        var turn = Guid.NewGuid();
        SetPhase(runtime, RuntimePhase.Running);
        SetEntries(runtime, runtime.Entries.Concat(RunningTurn(turn)).ToList());
        Settle(window, frames);
        Assert.True(Wheel(view, window, 40) > 1500, "the wheel did not move the reader up: " + Where(Scroller(view)));
        Settle(window, frames);
        var anchor = TextsOnScreen(view, Scroller(view)).First();
        var offset = Scroller(view).Offset.Y;
        FinishTurn(runtime, turn);
        Settle(window, frames);
        Assert.False(AtEnd(Scroller(view)), "the reader was pulled to the end: " + Where(Scroller(view)));
        Assert.True(SomeTextOnScreen(view, Scroller(view)), "blank after the fold: " + Where(Scroller(view)));
        Assert.Equal(offset, Scroller(view).Offset.Y, 0);
        Assert.Same(anchor, TextsOnScreen(view, Scroller(view)).First());
    });

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:615-623 the reader coming back (window to the front) checks the timeline; :1295-1348 FrontMonitor
    public void Changes_made_while_the_window_was_minimised_are_on_screen_at_the_end_when_it_returns() => WithLongThread(20, (view, runtime, window, frames) =>
    {
        window.WindowState = WindowState.Minimized;
        Settle(window, frames);
        // Unseen: three turns arrive and one more runs to its end.
        AppendTurn(runtime);
        Settle(window, frames);
        var turn = Guid.NewGuid();
        SetPhase(runtime, RuntimePhase.Running);
        SetEntries(runtime, runtime.Entries.Concat(RunningTurn(turn)).ToList());
        Settle(window, frames);
        FinishTurn(runtime, turn);
        Settle(window, frames);
        window.WindowState = WindowState.Normal;
        Settle(window, frames);
        Assert.True(AtEnd(Scroller(view)), Where(Scroller(view)));
        Assert.True(SomeTextOnScreen(view, Scroller(view)), "blank on return: " + Where(Scroller(view)));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // ThreadTimeline.swift:2320-2325 ScrollFreeze turns off hit-testing on the rows only; the NSScrollView still takes every wheel event
    public void Every_wheel_notch_scrolls_even_while_the_rows_are_frozen() => WithLongThread(30, (view, _, window, _) =>
    {
        // One notch is 50 points; the first freezes the rows for 150 ms, and the other nine come inside that window.
        var moved = Wheel(view, window, 10);
        Assert.True(IsReaderScrolling(view), "the rows should be frozen while the wheel turns");
        Assert.Equal(500, moved, 0);
    });
}
