using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// F.FrameClock: frame-driven work runs from Avalonia's frame clock behind a seam a test drives by hand, and the
// system timer resolution is never changed (ED-083, ED-098 (1); IMPLEMENTING.md R-VIEW-10, R-TASK-3, R-GCD-3, 4.3).
// PORT-ONLY surface these tests fix, in src/Marrow.UI/Port/FrameClock.cs, namespace Marrow.UI.Port:
//   interface IFrameSource { void RequestFrame(Action<TimeSpan> frame); }
//       one call at the next frame, never inside the request, with that frame's time
//   AvaloniaFrameSource(TopLevel topLevel) : IFrameSource        TopLevel.RequestAnimationFrame, its time passed on
//   ManualFrameSource : IFrameSource { TimeSpan Now; int Pending; void Advance(TimeSpan by); }
//       Now starts at zero. Advance moves Now forward by `by`, then runs, once each and in request order, the
//       callbacks requested before the call, each with the new Now; one requested during an Advance waits for the
//       next Advance. Pending counts the callbacks waiting.
//   static FrameClock:
//       Ambient<IFrameSource> Source                             the app sets the process default at start-up
//       Task<bool> TrySleep(TimeSpan duration)                   a Task.sleep standing for a frame tick (17 ms,
//           30 ms): resumes at the first frame at which SwiftClock.Now has moved `duration` past the call; false,
//           at once, when the current Swift task is cancelled (before or during), as SwiftTask.TrySleep
//       DisplayLink DisplayLink(Action tick)                     CADisplayLink from displayLink(target:selector:)
//       T TimelineView<T>(this T control, Func<AnimationTimelineSchedule> schedule, Action<DateTimeOffset> update)
//           where T : Control                                    an AnimationTimeline that appears on attach and
//                                                                disappears on detach
//   DisplayLink { FrameRateRange? PreferredFrameRateRange; void Add(); void Invalidate(); }
//       ticks on main, outside any task, from the first frame after Add until Invalidate; with a preferred rate
//       p it ticks at the first frame at or after each Add + k/p seconds (k = 1, 2, ...)
//   FrameRateRange(float Minimum, float Maximum, float? Preferred)             CAFrameRateRange
//   AnimationTimelineSchedule(double? MinimumInterval = null, bool Paused = false)
//       TimelineView(.animation(minimumInterval:paused:))
//   AnimationTimeline(Func<AnimationTimelineSchedule> schedule, Action<DateTimeOffset> update) { Appear(); Disappear(); }
//       Appear draws once at once with SwiftDate.Now, paused or not (the body is always drawn). While appeared and
//       not paused it asks for frames and updates at the first frame at or after each entry start + k * interval
//       (k = 1, 2, ...; every frame when the interval is null), passing SwiftDate.Now at that frame. The schedule
//       is re-read after any observable state it read changes (as Tracked). Paused or gone, it asks for no frames.
// Frames, the only wake-ups, come from the frame source alone (ED-098 (1)); nothing here uses Avalonia's internal
// animation clock or a timer. The dates and durations TrySleep, DisplayLink and AnimationTimeline measure are read on
// SwiftClock and SwiftDate at those frames (ED-068), because the Swift callers compare them with stored Date stamps:
// EffortSlider.swift:1042 and :1051 (startedAt = Date.now; timeline.date.timeIntervalSince(startedAt)) and
// StreamVeil.swift:188-189 (now = .now; now >= deadline). The tests below move both clocks together.
public sealed class FrameClockTests
{
    // ManualTimeProvider's default start (tests/Marrow.Testing/ManualTimeProvider.cs).
    private static readonly DateTimeOffset Start = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int milliseconds) => Start + TimeSpan.FromMilliseconds(milliseconds);

    private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    // Frames `everyMilliseconds` apart: move the Swift clock, then deliver one frame on main with the frame
    // source's own time moved by the same amount.
    private static void Frames(TestRuntime runtime, ManualFrameSource frames, int count, int everyMilliseconds = 4)
    {
        for (var i = 0; i < count; i++)
        {
            runtime.AdvanceMilliseconds(everyMilliseconds);
            runtime.OnMain(() => frames.Advance(Ms(everyMilliseconds)));
        }
    }

    private static string RepositoryRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-098")]
    public void A_manual_frame_source_starts_at_zero_and_runs_nothing_until_it_is_advanced()
    {
        var frames = new ManualFrameSource();
        var log = new List<string>();
        Assert.Equal(TimeSpan.Zero, frames.Now);
        Assert.Equal(0, frames.Pending);
        frames.RequestFrame(time => log.Add($"a {time.TotalMilliseconds}"));
        frames.RequestFrame(time => log.Add($"b {time.TotalMilliseconds}"));
        Assert.Empty(log); // never inside the request
        Assert.Equal(2, frames.Pending);
        Assert.Equal(TimeSpan.Zero, frames.Now);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-098")]
    public void Advancing_a_manual_frame_source_moves_its_time_and_runs_each_waiting_callback_once_in_request_order_with_that_time()
    {
        var frames = new ManualFrameSource();
        var log = new List<string>();
        frames.RequestFrame(time => log.Add($"a {time.TotalMilliseconds}"));
        frames.RequestFrame(time => log.Add($"b {time.TotalMilliseconds}"));
        frames.Advance(Ms(16));
        Assert.Equal(["a 16", "b 16"], log);
        Assert.Equal(Ms(16), frames.Now);
        Assert.Equal(0, frames.Pending);
        frames.Advance(Ms(16)); // nothing waiting: nothing runs, the time still moves
        Assert.Equal(["a 16", "b 16"], log);
        Assert.Equal(Ms(32), frames.Now);
        frames.RequestFrame(time => log.Add($"c {time.TotalMilliseconds}"));
        frames.Advance(Ms(4));
        Assert.Equal(["a 16", "b 16", "c 36"], log);
        Assert.Equal(Ms(36), frames.Now);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-098")]
    public void A_frame_asked_for_during_an_advance_waits_for_the_next_advance_and_gets_that_frames_time()
    {
        var frames = new ManualFrameSource();
        var log = new List<string>();
        frames.RequestFrame(time =>
        {
            log.Add($"first {time.TotalMilliseconds}");
            frames.RequestFrame(next => log.Add($"second {next.TotalMilliseconds}"));
        });
        frames.Advance(Ms(8));
        Assert.Equal(["first 8"], log);
        Assert.Equal(1, frames.Pending);
        frames.Advance(Ms(8));
        Assert.Equal(["first 8", "second 16"], log);
        Assert.Equal(0, frames.Pending);
    }

    // A text scan of the unit's file, comments included: frames come from RequestAnimationFrame, and nothing in it
    // makes a timer, uses Avalonia's own animation clock, or touches the system timer resolution (Windows timers
    // tick every 15.6 ms).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-098")]
    public void The_frame_clock_takes_frames_from_RequestAnimationFrame_and_never_from_a_timer_or_a_changed_timer_resolution()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Marrow.UI", "Port", "FrameClock.cs"));
        Assert.Contains("RequestAnimationFrame", text, StringComparison.Ordinal);
        var banned = new[]
        {
            "timeBeginPeriod", "timeEndPeriod", "NtSetTimerResolution", "winmm", "DispatcherTimer", "System.Timers",
            "System.Threading.Timer", "new Timer(", "CreateTimer", "Task.Delay(", "Thread.Sleep(",
            "Avalonia.Animation", "IGlobalClock", "Clock.GlobalClock",
        };
        foreach (var name in banned) Assert.DoesNotContain(name, text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Decision", "ED-083")]
    public void A_frame_tick_sleep_ends_at_the_first_frame_once_its_duration_has_passed_and_never_on_time_alone()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var log = new List<string>();
        runtime.OnMain(() => MainActor.Spawn(async () =>
        {
            // App/Views/Chat/ThreadTimeline.swift:1168  try? await Task.sleep(for: .milliseconds(17))
            var slept = await FrameClock.TrySleep(TimeSpan.FromMilliseconds(17));
            log.Add($"slept={slept} main={MainActor.IsIsolated}");
        }));
        Assert.Equal(1, frames.Pending); // it waits on a frame
        Assert.Equal(0, runtime.Time.ActiveTimers); // and on no timer
        Frames(runtime, frames, count: 1, everyMilliseconds: 16); // a frame 16 ms in: too early
        Assert.Empty(log);
        Assert.Equal(1, frames.Pending);
        runtime.AdvanceMilliseconds(1); // 17 ms have passed, but no frame has come
        Assert.Empty(log);
        runtime.OnMain(() => frames.Advance(Ms(1)));
        Assert.Equal(["slept=True main=True"], log);
        Assert.Equal(0, frames.Pending); // nothing asks for frames once the sleep is over
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Decision", "ED-083")]
    public void A_frame_tick_sleep_whose_task_is_cancelled_returns_false_at_once_and_the_code_after_it_runs()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var log = new List<string>();
        var handle = runtime.OnMain(() => MainActor.Spawn(async () =>
        {
            // App/Views/Composer/HydraFusedPairIcon.swift:40, in a .task(id:) that a new id cancels
            var slept = await FrameClock.TrySleep(TimeSpan.FromMilliseconds(30));
            log.Add($"slept={slept} cancelled={SwiftTask.IsCancelled}");
        }));
        Assert.Equal(1, frames.Pending);
        runtime.OnMain(handle.Cancel);
        Assert.Equal(["slept=False cancelled=True"], log); // no frame came, no time passed
        Frames(runtime, frames, count: 1, everyMilliseconds: 40); // the frame asked for before the cancel changes nothing
        Assert.Single(log);
        Assert.Equal(0, frames.Pending);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Decision", "ED-083")]
    public void A_frame_tick_sleep_in_an_already_cancelled_task_returns_false_without_asking_for_a_frame()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var log = new List<string>();
        runtime.OnMain(() =>
        {
            var handle = MainActor.Spawn(async () =>
            {
                var slept = await FrameClock.TrySleep(TimeSpan.FromMilliseconds(17));
                log.Add($"slept={slept}");
            });
            handle.Cancel(); // before the task has started
        });
        Assert.Equal(["slept=False"], log);
        Assert.Equal(0, frames.Pending);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    public void A_display_link_ticks_on_main_outside_any_task_once_per_frame_from_add_until_invalidate()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var ticks = new List<DateTimeOffset>();
        var where = new List<string>();
        DisplayLink? link = null;
        runtime.OnMain(() =>
        {
            // UI/Markdown/StreamVeil.swift:359-360  view.displayLink(target: self, selector: #selector(tick)), then add(to: .main, forMode: .common)
            link = FrameClock.DisplayLink(() =>
            {
                ticks.Add(SwiftDate.Now);
                where.Add($"main={MainActor.IsIsolated} task={SwiftTask.IsInTask}");
            });
        });
        Assert.Equal(0, frames.Pending); // not added yet: no frames
        Frames(runtime, frames, count: 2); // 4 and 8 ms
        Assert.Empty(ticks);
        runtime.OnMain(link!.Add);
        Assert.Equal(1, frames.Pending);
        Assert.Equal(0, runtime.Time.ActiveTimers);
        Frames(runtime, frames, count: 3); // 12, 16 and 20 ms
        Assert.Equal([At(12), At(16), At(20)], ticks);
        Assert.All(where, place => Assert.Equal("main=True task=False", place));
        runtime.OnMain(link.Invalidate);
        Frames(runtime, frames, count: 2);
        Assert.Equal(3, ticks.Count);
        Assert.Equal(0, frames.Pending);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    public void A_display_link_invalidated_inside_its_own_tick_ticks_no_more_and_asks_for_no_more_frames()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var ticks = 0;
        DisplayLink? link = null;
        runtime.OnMain(() =>
        {
            // UI/Markdown/StreamVeil.swift:183-193  VeilClock.tick invalidates its link once the deadline has passed
            link = FrameClock.DisplayLink(() =>
            {
                ticks += 1;
                if (ticks == 2) link!.Invalidate();
            });
            link.Add();
        });
        Frames(runtime, frames, count: 4);
        Assert.Equal(2, ticks);
        Assert.Equal(0, frames.Pending);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    public void A_display_link_preferring_30_frames_a_second_ticks_30_times_in_a_second_of_250_Hz_frames()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var ticks = new List<DateTimeOffset>();
        runtime.OnMain(() =>
        {
            var link = FrameClock.DisplayLink(() => ticks.Add(SwiftDate.Now));
            // UI/Markdown/StreamVeil.swift:178  link.preferredFrameRateRange = CAFrameRateRange(minimum: 24, maximum: 60, preferred: 30)
            link.PreferredFrameRateRange = new FrameRateRange(24, 60, 30);
            link.Add();
        });
        Frames(runtime, frames, count: 255); // every 4 ms, to 1020 ms: entries at k / 30 s, the 31st at 1033 ms
        Assert.Equal(30, ticks.Count);
        Assert.Equal(At(36), ticks[0]); // the first frame one period (33.3 ms) after add
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    public void An_animation_timeline_draws_once_on_appear_even_while_paused_and_asks_for_no_frames_while_paused()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        AnimationTimeline? timeline = null;
        runtime.AdvanceMilliseconds(5);
        runtime.OnMain(() =>
        {
            // App/Views/Composer/EffortSlider.swift:1049-1050  paused while reduceMotion or the window is not in front; the track is still drawn
            timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 60.0, Paused: true), dates.Add);
            Assert.Empty(dates); // nothing before it appears
            timeline.Appear();
            Assert.Equal([At(5)], dates); // drawn at once, with the date it appeared
        });
        Assert.Equal(0, frames.Pending);
        Frames(runtime, frames, count: 10);
        Assert.Equal([At(5)], dates);
        runtime.OnMain(timeline!.Disappear);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-098")]
    public void An_animation_timeline_updates_at_the_first_frame_at_or_after_each_minimum_interval_entry_with_that_frames_date()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        runtime.OnMain(() =>
        {
            // App/Views/Chat/HydraProgressBar.swift:507  TimelineView(.animation(minimumInterval: 1.0 / 20.0, paused: ...))
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 20.0, Paused: false), dates.Add);
            timeline.Appear();
        });
        Assert.Equal(1, frames.Pending);
        Frames(runtime, frames, count: 29, everyMilliseconds: 7); // 7 ms apart, to 203 ms: entries at 50, 100, 150, 200 ms
        Assert.Equal([Start, At(56), At(105), At(154), At(203)], dates);
        Assert.Equal(1, frames.Pending); // it keeps asking while it runs
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-098")]
    public void An_animation_timeline_waits_for_a_frame_even_when_its_interval_has_passed_on_the_clock()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        runtime.OnMain(() =>
        {
            // App/Views/Chat/TimelineRows.swift:499  TimelineView(.animation(minimumInterval: 1.0 / 30.0, paused: ...))
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 30.0, Paused: false), dates.Add);
            timeline.Appear();
        });
        runtime.AdvanceMilliseconds(100); // three entries' worth of time, and no frame
        Assert.Equal([Start], dates);
        Assert.Equal(0, runtime.Time.ActiveTimers);
        runtime.OnMain(() => frames.Advance(Ms(100)));
        Assert.Equal([Start, At(100)], dates); // one update at the frame, not one per missed entry
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    public void An_animation_timeline_at_one_24th_of_a_second_updates_24_times_in_a_second_of_250_Hz_frames()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        runtime.OnMain(() =>
        {
            // App/Views/Chat/HydraProgressBar.swift:142  TimelineView(.animation(minimumInterval: 1.0 / 24.0, paused: ...))
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 24.0, Paused: false), dates.Add);
            timeline.Appear();
        });
        Frames(runtime, frames, count: 255); // every 4 ms, to 1020 ms: entries at k / 24 s, the 25th at 1041.7 ms
        Assert.Equal(25, dates.Count); // the appear draw and 24 frame updates
        Assert.Equal(Start, dates[0]);
        Assert.Equal(At(44), dates[1]); // the first frame at or after 41.7 ms
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-083")]
    public void An_animation_timeline_holds_still_while_its_schedule_is_paused_and_updates_again_once_it_resumes()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        var scrolling = new CellSample<bool>(false); // ScrollActivity.shared.isScrolling
        runtime.OnMain(() =>
        {
            // App/Views/Chat/TimelineRows.swift:499  TimelineView(.animation(minimumInterval: 1.0 / 30.0, paused: ScrollActivity.shared.isScrolling))
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 30.0, Paused: scrolling.Value), dates.Add);
            timeline.Appear();
        });
        Frames(runtime, frames, count: 10); // 4 to 40 ms: one entry, at 33.3 ms
        Assert.Equal([Start, At(36)], dates);
        runtime.OnMain(() => scrolling.Value = true); // at 40 ms
        Frames(runtime, frames, count: 25); // 44 to 140 ms
        Assert.DoesNotContain(dates, date => date > At(40)); // no frame updates while paused
        Assert.Equal(0, frames.Pending); // and no frames asked for
        runtime.OnMain(() => scrolling.Value = false); // at 140 ms
        Assert.Equal(1, frames.Pending);
        Frames(runtime, frames, count: 25); // 144 to 240 ms
        Assert.Contains(dates, date => date > At(140));
        Assert.Equal(1, frames.Pending);
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-VIEW-3")]
    [Trait("Decision", "ED-083")]
    public void An_animation_timeline_that_disappears_updates_no_more_and_asks_for_no_more_frames()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        AnimationTimeline? timeline = null;
        runtime.OnMain(() =>
        {
            // App/Views/Settings/AboutUpdateViews.swift:450  TimelineView(.animation(minimumInterval: 1.0 / 30.0, paused: ...))
            timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 30.0, Paused: false), dates.Add);
            timeline.Appear();
        });
        Frames(runtime, frames, count: 10); // one entry, at 33.3 ms
        runtime.OnMain(timeline!.Disappear);
        Frames(runtime, frames, count: 25);
        Assert.Equal([Start, At(36)], dates);
        Assert.Equal(0, frames.Pending);
    }
}
