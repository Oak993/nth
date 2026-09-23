using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// F.FrameClock: frame-driven work runs from Avalonia's frame clock behind a seam a test drives by hand, and the
// system timer resolution is never changed (ED-083, ED-098 (1) as amended by ED-117 (1), ED-117; IMPLEMENTING.md
// R-VIEW-10, R-TASK-3, R-GCD-3, 4.3).
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
//           30 ms): resumes at the first frame at which `duration` since the call is complete, never on time
//           alone; false, at once, when the current Swift task is cancelled (before or during), as
//           SwiftTask.TrySleep, and an already cancelled task asks for no frame
//       DisplayLink DisplayLink(Action tick)                     CADisplayLink from displayLink(target:selector:)
//       T TimelineView<T>(this T control, Func<AnimationTimelineSchedule> schedule, Action<DateTimeOffset> update)
//           where T : Control                                    an AnimationTimeline that appears on attach and
//                                                                disappears on detach
//   DisplayLink { FrameRateRange? PreferredFrameRateRange; void Add(); void Invalidate(); }
//       ticks on main, outside any task. The first tick comes at the first frame after Add. Without a preferred
//       rate it then ticks at every frame; with preferred rate p it ticks at a frame when 1/p since its last tick
//       is complete. Minimum and Maximum are stored and not read. Invalidate stops it, also inside its own tick.
//   FrameRateRange(float Minimum, float Maximum, float? Preferred)             CAFrameRateRange
//   AnimationTimelineSchedule(double? MinimumInterval = null, bool Paused = false)
//       TimelineView(.animation(minimumInterval:paused:))
//   AnimationTimeline(Func<AnimationTimelineSchedule> schedule, Action<DateTimeOffset> update)
//       { DateTimeOffset? Date; void Appear(); void Disappear(); void Redraw(); }
//       Appear draws at once with SwiftDate.Now, paused or not. While appeared and not paused it asks for every
//       frame, and at a frame it updates when MinimumInterval is null or the span MinimumInterval since its last
//       draw is complete, passing SwiftDate.Now at that frame; intervals missed without a frame collapse into one
//       update. The schedule is re-read after any observable state it read changes (as Tracked); a schedule
//       change never draws by itself. Paused or gone, it asks for no frames, and a frame asked for before does
//       nothing when it arrives. On resume it asks for a frame at once, reusing a stale request still outstanding.
//       Date is the last draw's date (null before Appear); Redraw runs update at once with that date and changes
//       neither the last-draw instant nor the requests.
// Complete (ED-117 (2)): a span S from instant s is complete at a frame when (SwiftClock.Now - s) + 1 ms >= S,
// compared in ticks; a Double interval becomes a span through SwiftTask.Seconds (1/60 is 166667 ticks, 1/30 333333,
// 1/24 416667, 1/20 500000). Frames from the frame source are the only wake-ups (ED-117 (1)); spans are measured
// on SwiftClock and dates are SwiftDate.Now at the frame, because the Swift callers compare them with stored Date
// stamps: EffortSlider.swift:1042 and :1051 (startedAt = Date.now; timeline.date.timeIntervalSince(startedAt)) and
// StreamVeil.swift:188-189 (now = .now; now >= deadline). The TimeSpan a frame callback receives decides nothing.
// At most one frame request is outstanding per timeline, link or sleep. Nothing here uses Avalonia's internal
// animation clock or a timer. Most tests move both clocks together; the frame-time-only tests hold SwiftClock still.
// Expected values that rest on ED-117 (2) to (8) are the port's parked choice for SwiftUI and Core Animation
// behaviour nobody has verified (O-34); those tests carry the Oracle trait and no Req trait (R-TEST-6).
public sealed class FrameClockTests
{
    // ManualTimeProvider's default start (tests/Marrow.Testing/ManualTimeProvider.cs).
    private static readonly DateTimeOffset Start = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int milliseconds) => Start + TimeSpan.FromMilliseconds(milliseconds);

    private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    // Frames `every` apart: move the Swift clock, then deliver one frame on main with the frame source's own time
    // moved by the same amount.
    private static void Frames(TestRuntime runtime, ManualFrameSource frames, int count, TimeSpan every)
    {
        for (var i = 0; i < count; i++)
        {
            runtime.Advance(every);
            runtime.OnMain(() => frames.Advance(every));
        }
    }

    private static void Frames(TestRuntime runtime, ManualFrameSource frames, int count, int everyMilliseconds = 4) =>
        Frames(runtime, frames, count, Ms(everyMilliseconds));

    // Frames 16, 17 and 17 ms apart, repeated: the frame times of a 60 Hz display at whole milliseconds.
    private static void SixtyHertzFrames(TestRuntime runtime, ManualFrameSource frames, int count)
    {
        int[] gaps = [16, 17, 17];
        for (var i = 0; i < count; i++) Frames(runtime, frames, count: 1, everyMilliseconds: gaps[i % 3]);
    }

    // Frames that move only the frame source's time: SwiftClock and SwiftDate stand still.
    private static void FrameTimeOnly(TestRuntime runtime, ManualFrameSource frames, int count, int everyMilliseconds = 4)
    {
        for (var i = 0; i < count; i++) runtime.OnMain(() => frames.Advance(Ms(everyMilliseconds)));
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

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void A_frame_tick_sleep_ends_at_a_frame_once_its_duration_is_complete_and_never_on_time_alone()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var log = new List<string>();
        runtime.OnMain(() => MainActor.Spawn(async () =>
        {
            // App/Views/Chat/ThreadTimeline.swift:1168  try? await Task.sleep(for: .milliseconds(17))
            var slept = await FrameClock.TrySleep(TimeSpan.FromMilliseconds(17));
            log.Add($"slept={slept} main={MainActor.IsIsolated} at={(SwiftDate.Now - Start).TotalMilliseconds}");
        }));
        Assert.Equal(1, frames.Pending); // it waits on a frame
        Assert.Equal(0, runtime.Time.ActiveTimers); // and on no timer
        Frames(runtime, frames, count: 1, everyMilliseconds: 15); // a frame at +15: 15 + 1 < 17
        Assert.Empty(log);
        Assert.Equal(1, frames.Pending);
        runtime.AdvanceMilliseconds(2); // +17 on the clock alone: no frame, no wake-up
        Assert.Empty(log);
        Assert.Equal(1, frames.Pending);
        runtime.OnMain(() => frames.Advance(Ms(2))); // the next frame, at +17
        Assert.Equal(["slept=True main=True at=17"], log);
        Assert.Equal(0, frames.Pending); // nothing asks for frames once the sleep is over
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void A_frame_tick_sleep_of_17_ms_ends_at_a_frame_16_ms_after_the_call_where_elapsed_and_1_ms_reach_17()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var log = new List<string>();
        runtime.OnMain(() => MainActor.Spawn(async () =>
        {
            // App/Views/Chat/ThreadTimeline.swift:1211  try? await Task.sleep(for: .milliseconds(17))
            var slept = await FrameClock.TrySleep(TimeSpan.FromMilliseconds(17));
            log.Add($"slept={slept} at={(SwiftDate.Now - Start).TotalMilliseconds}");
        }));
        Frames(runtime, frames, count: 1, everyMilliseconds: 15); // +15: not complete
        Assert.Empty(log);
        Frames(runtime, frames, count: 1, everyMilliseconds: 1); // +16: 16 + 1 >= 17
        Assert.Equal(["slept=True at=16"], log);
        Assert.Equal(0, frames.Pending);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void Frame_tick_sleeps_of_17_ms_called_at_a_frame_take_one_frame_each_at_60_Hz()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var period = TimeSpan.FromTicks(166667); // SwiftTask.Seconds(1.0 / 60.0)
        var beats = new List<DateTimeOffset>();
        Frames(runtime, frames, count: 1, every: period); // the calls start at a frame
        runtime.OnMain(() => MainActor.Spawn(async () =>
        {
            // App/Views/Chat/ThreadTimeline.swift:1223  a 17 ms beat, repeated
            for (var beat = 0; beat < 6; beat++)
            {
                if (!await FrameClock.TrySleep(TimeSpan.FromMilliseconds(17))) return;
                beats.Add(SwiftDate.Now);
            }
        }));
        Assert.Equal(1, frames.Pending);
        Frames(runtime, frames, count: 6, every: period); // 166667 ticks + 1 ms >= 17 ms at every frame
        Assert.Equal(Enumerable.Range(2, 6).Select(frame => Start + TimeSpan.FromTicks(166667L * frame)), beats);
        Assert.Equal(0, frames.Pending);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void A_frame_tick_sleep_never_resumes_while_only_frame_time_moves()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var log = new List<string>();
        runtime.OnMain(() => MainActor.Spawn(async () =>
        {
            // App/Views/Composer/HydraFusedPairIcon.swift:40
            var slept = await FrameClock.TrySleep(TimeSpan.FromMilliseconds(17));
            log.Add($"slept={slept}");
        }));
        FrameTimeOnly(runtime, frames, count: 30); // 120 ms of frame time, SwiftClock still at the call
        Assert.Empty(log);
        Assert.Equal(1, frames.Pending); // one request outstanding, never more
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
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
    [Trait("Decision", "ED-117")]
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

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void A_display_link_ticks_on_main_outside_any_task_at_every_frame_from_the_first_frame_after_add_until_invalidate()
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
        Assert.Equal(1, frames.Pending);
        runtime.OnMain(link.Invalidate);
        Frames(runtime, frames, count: 2);
        Assert.Equal(3, ticks.Count);
        Assert.Equal(0, frames.Pending);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
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

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void A_display_link_preferring_30_frames_a_second_ticks_at_the_first_frame_and_then_every_other_60_Hz_frame()
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
        // Frames at 16, 33, 50, 66, 83, 100, 116 and 133 ms. After the tick at 16, 1/30 s (333333 ticks) is complete
        // once 32.33 ms have passed: 50 (34 ms), 83 (33 ms), 116 (33 ms); 33, 66, 100 and 133 are 17 or 16 ms on.
        SixtyHertzFrames(runtime, frames, count: 8);
        Assert.Equal([At(16), At(50), At(83), At(116)], ticks);
        Assert.Equal(1, frames.Pending);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void A_display_link_preferring_30_frames_a_second_ticks_29_times_every_36_ms_from_4_ms_over_4_ms_frames_to_1020_ms()
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
        // The first frame after Add (4 ms), then 32 + 1 < 33.33 and 36 + 1 >= 33.33: every 9th frame.
        Frames(runtime, frames, count: 255);
        Assert.Equal(Enumerable.Range(0, 29).Select(k => At(4 + 36 * k)), ticks);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void A_display_link_preferring_30_frames_a_second_ticks_once_only_while_only_frame_time_moves()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var ticks = new List<DateTimeOffset>();
        runtime.OnMain(() =>
        {
            var link = FrameClock.DisplayLink(() => ticks.Add(SwiftDate.Now));
            // UI/Markdown/StreamVeil.swift:178
            link.PreferredFrameRateRange = new FrameRateRange(24, 60, 30);
            link.Add();
        });
        FrameTimeOnly(runtime, frames, count: 30);
        Assert.Equal([Start], ticks); // the first frame after Add, then 1/30 s never completes on SwiftClock
        Assert.Equal(1, frames.Pending);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void A_display_link_without_a_preferred_rate_ticks_at_every_frame_with_the_same_date_while_only_frame_time_moves()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var ticks = new List<DateTimeOffset>();
        runtime.OnMain(() =>
        {
            // UI/Markdown/StreamVeil.swift:359-360
            var link = FrameClock.DisplayLink(() => ticks.Add(SwiftDate.Now));
            link.Add();
        });
        FrameTimeOnly(runtime, frames, count: 5);
        Assert.Equal([Start, Start, Start, Start, Start], ticks);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
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
            Assert.Null(timeline.Date);
            timeline.Appear();
            Assert.Equal([At(5)], dates); // drawn at once, with the date it appeared
            Assert.Equal(At(5), timeline.Date);
        });
        Assert.Equal(0, frames.Pending);
        Frames(runtime, frames, count: 10);
        Assert.Equal([At(5)], dates);
        Assert.Equal(0, frames.Pending);
        runtime.OnMain(timeline!.Disappear);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_at_one_60th_of_a_second_updates_at_every_60_Hz_frame_with_that_frames_date()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        runtime.OnMain(() =>
        {
            // App/Views/Settings/AboutUpdateViews.swift:364  TimelineView(.animation(minimumInterval: 1.0 / 60.0, paused: ...))
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 60.0, Paused: false), dates.Add);
            timeline.Appear();
        });
        SixtyHertzFrames(runtime, frames, count: 9); // 16 + 1 and 17 + 1 >= 16.667 ms
        Assert.Equal([Start, At(16), At(33), At(50), At(66), At(83), At(100), At(116), At(133), At(150)], dates);
        Assert.Equal(1, frames.Pending);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_at_one_30th_of_a_second_updates_at_every_4th_frame_of_83333_ticks_and_at_no_other()
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
        // 3 frames: 249999 + 10000 < 333333; 4 frames: 333332 + 10000 >= 333333.
        Frames(runtime, frames, count: 24, every: TimeSpan.FromTicks(83333));
        Assert.Equal(
            new[] { Start }.Concat(Enumerable.Range(1, 6).Select(k => Start + TimeSpan.FromTicks(83333L * 4 * k))),
            dates);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_at_one_20th_of_a_second_updates_once_elapsed_and_1_ms_reach_the_interval_with_that_frames_date()
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
        // 7 ms apart, to 203 ms. 49 is the boundary: 49 + 1 equals 50 ms exactly; 42 + 1 does not reach it.
        Frames(runtime, frames, count: 29, everyMilliseconds: 7);
        Assert.Equal([Start, At(49), At(98), At(147), At(196)], dates);
        Assert.Equal(1, frames.Pending); // it keeps asking while it runs
        Assert.Equal(0, runtime.Time.ActiveTimers);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_waits_for_a_frame_even_when_its_interval_has_passed_on_the_clock_and_collapses_missed_intervals()
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
        runtime.AdvanceMilliseconds(100); // three intervals' worth of time, and no frame
        Assert.Equal([Start], dates);
        Assert.Equal(0, runtime.Time.ActiveTimers);
        runtime.OnMain(() => frames.Advance(Ms(100)));
        Assert.Equal([Start, At(100)], dates); // one update at the frame, not one per missed interval
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_at_one_24th_of_a_second_updates_every_44_ms_over_4_ms_frames_24_dates_to_1020_ms()
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
        // 1/24 s is 416667 ticks: 40 + 1 ms falls short, 44 + 1 ms reaches it.
        Frames(runtime, frames, count: 255);
        Assert.Equal(new[] { Start }.Concat(Enumerable.Range(1, 23).Select(k => At(44 * k))), dates);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_at_one_30th_of_a_second_gets_no_update_after_appear_while_only_frame_time_moves()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        runtime.OnMain(() =>
        {
            // App/Views/Settings/AboutUpdateViews.swift:450  TimelineView(.animation(minimumInterval: 1.0 / 30.0, paused: ...))
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 30.0, Paused: false), dates.Add);
            timeline.Appear();
        });
        FrameTimeOnly(runtime, frames, count: 30);
        Assert.Equal([Start], dates);
        Assert.Equal(1, frames.Pending);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_without_a_minimum_interval_updates_at_every_frame_with_the_same_date_while_only_frame_time_moves()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        runtime.OnMain(() =>
        {
            // TimelineView(.animation) with minimumInterval nil (AnimationTimelineSchedule's default)
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: null, Paused: false), dates.Add);
            timeline.Appear();
        });
        FrameTimeOnly(runtime, frames, count: 4);
        Assert.Equal([Start, Start, Start, Start, Start], dates);
        Assert.Equal(1, frames.Pending);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_holds_still_while_paused_and_resumes_measured_from_its_last_draw_before_the_pause()
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
        Frames(runtime, frames, count: 10); // 4 to 40 ms
        Assert.Equal([Start, At(36)], dates);
        runtime.OnMain(() => scrolling.Value = true); // at 40 ms
        Assert.Equal([Start, At(36)], dates); // the pause does not draw
        Assert.Equal(1, frames.Pending); // the stale request asked for at 40 ms
        Frames(runtime, frames, count: 1); // 44 ms: the stale request does nothing and asks for no other
        Assert.Equal(0, frames.Pending);
        for (var frame = 0; frame < 24; frame++) // 48 to 140 ms
        {
            Frames(runtime, frames, count: 1);
            Assert.Equal(0, frames.Pending);
        }
        Assert.Equal([Start, At(36)], dates);
        runtime.OnMain(() => scrolling.Value = false); // at 140 ms
        Assert.Equal([Start, At(36)], dates); // the resume does not draw
        Assert.Equal(1, frames.Pending); // it asks for a frame at once
        for (var frame = 0; frame < 25; frame++) // 144 to 240 ms
        {
            Frames(runtime, frames, count: 1);
            Assert.Equal(1, frames.Pending);
        }
        Assert.Equal([Start, At(36), At(144), At(180), At(216)], dates);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_paused_and_resumed_with_no_frame_between_reuses_its_request_and_updates_at_most_once_a_frame()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        var scrolling = new CellSample<bool>(false);
        runtime.OnMain(() =>
        {
            // App/Views/Chat/TimelineRows.swift:499
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 30.0, Paused: scrolling.Value), dates.Add);
            timeline.Appear();
        });
        Frames(runtime, frames, count: 10); // 4 to 40 ms
        runtime.OnMain(() => scrolling.Value = true); // at 40 ms
        runtime.OnMain(() => scrolling.Value = false); // at 40 ms, no frame between
        Assert.Equal(1, frames.Pending);
        Assert.Equal([Start, At(36)], dates);
        for (var frame = 0; frame < 10; frame++) // 44 to 80 ms
        {
            var before = dates.Count;
            Frames(runtime, frames, count: 1);
            Assert.InRange(dates.Count - before, 0, 1);
            Assert.Equal(1, frames.Pending);
        }
        Assert.Equal([Start, At(36), At(72)], dates);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void An_animation_timeline_redrawn_while_paused_passes_its_last_draws_date_and_keeps_its_next_due_time()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        var scrolling = new CellSample<bool>(false);
        AnimationTimeline? timeline = null;
        runtime.OnMain(() =>
        {
            // App/Views/Chat/HydraProgressBar.swift:132-133 and :142  drawn once per change, paused while scrolling
            timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 30.0, Paused: scrolling.Value), dates.Add);
            timeline.Appear();
        });
        Frames(runtime, frames, count: 10); // 4 to 40 ms: the update at 36
        runtime.OnMain(() => scrolling.Value = true); // at 40 ms
        Frames(runtime, frames, count: 5); // 44 to 60 ms
        Assert.Equal(0, frames.Pending);
        runtime.OnMain(() =>
        {
            Assert.Equal(At(36), timeline!.Date);
            timeline.Redraw(); // at 60 ms
        });
        Assert.Equal([Start, At(36), At(36)], dates); // the last draw's date, never SwiftDate.Now
        Assert.Equal(0, frames.Pending); // no request
        runtime.OnMain(() => scrolling.Value = false); // at 60 ms
        Frames(runtime, frames, count: 10); // 64 to 100 ms: due from 36, so 72 (68 + 1 < 69.33)
        Assert.Equal([Start, At(36), At(36), At(72)], dates);
        runtime.OnMain(() => Assert.Equal(At(72), timeline!.Date));
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void A_new_minimum_interval_applies_from_the_next_frame_measured_from_the_last_draw_and_does_not_draw_by_itself()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var dates = new List<DateTimeOffset>();
        var supercharged = new CellSample<bool>(true);
        runtime.OnMain(() =>
        {
            // App/Views/Composer/EffortSlider.swift:1050  minimumInterval: 1.0 / (kind == .supercharged && pair == nil ? 30 : 60)
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / (supercharged.Value ? 30 : 60), Paused: false), dates.Add);
            timeline.Appear();
        });
        Frames(runtime, frames, count: 10); // 4 to 40 ms: the update at 36
        Assert.Equal([Start, At(36)], dates);
        runtime.OnMain(() => supercharged.Value = false); // at 40 ms: 1/60 from now on
        Assert.Equal([Start, At(36)], dates); // the change does not draw
        Assert.Equal(1, frames.Pending);
        // From 36: 44 and 48 fall short of 16.667 ms, 52 reaches it (16 + 1), then every 16 ms.
        Frames(runtime, frames, count: 15); // 44 to 100 ms
        Assert.Equal([Start, At(36), At(52), At(68), At(84), At(100)], dates);
    }

    // The value is the port's parked choice for unverified SwiftUI and Core Animation behaviour (O-34).
    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-VIEW-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
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
        Frames(runtime, frames, count: 10); // the update at 36
        runtime.OnMain(timeline!.Disappear);
        Frames(runtime, frames, count: 25);
        Assert.Equal([Start, At(36)], dates);
        Assert.Equal(0, frames.Pending);
    }
}
