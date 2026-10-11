using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7 budget: frame requests a second for slow schedules on a 240 Hz display. Headless and deterministic
// (ManualFrameSource frames at 240 Hz in whole ticks, ManualTimeProvider), Debug build. A frame request is one
// RequestAnimationFrame plus one compositor commit in production (FrameClock.cs AvaloniaFrameSource, ED-237).
// Measured on the unchanged base (1fdf47b43): 240 a second for a 30 Hz display link and for a 24 Hz timeline (limit
// then 360). After ED-NEW-lean-7, with the source holding each request back until 1.5 refreshes before its draw is due:
// 60 and 48 (two frames per draw), limits 78 and 62 (1.3x).
public sealed class FrameClockPacingBudgetTests
{
    private const int Rate = 240;
    private const int WarmUpFrames = Rate;          // one simulated second
    private const int MeasuredFrames = Rate * 10;   // ten simulated seconds

    private static TimeSpan FrameTime(long frame) => TimeSpan.FromTicks(frame * TimeSpan.TicksPerSecond / Rate);

    private static (double RequestsPerSecond, double DrawsPerSecond) Measure(Action<TestRuntime> start, Func<long> draws)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource { WakePeriod = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / Rate) };
        using var source = FrameClock.Source.Override(frames);
        start(runtime);
        long frame = 0;
        void Run(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var gap = FrameTime(frame + 1) - FrameTime(frame);
                frame++;
                runtime.Advance(gap);
                runtime.OnMain(() => frames.Advance(gap));
            }
        }
        Run(WarmUpFrames);
        var requestsBefore = frames.Delivered;
        var drawsBefore = draws();
        Run(MeasuredFrames);
        var seconds = MeasuredFrames / (double)Rate;
        return ((frames.Delivered - requestsBefore) / seconds, (draws() - drawsBefore) / seconds);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_30_Hz_display_link_on_a_240_Hz_display_asks_for_at_most_78_frames_a_second()
    {
        long ticks = 0;
        var (requests, draws) = Measure(runtime => runtime.OnMain(() =>
        {
            var link = FrameClock.DisplayLink(() => ticks++);
            link.PreferredFrameRateRange = new FrameRateRange(24, 60, 30); // StreamVeil.cs:233
            link.Add();
        }), () => ticks);
        Assert.InRange(draws, 29.5, 30.5);
        Assert.True(requests <= 78, $"{requests} frame requests a second (budget 78; 240 on the unchanged base, 60 after ED-NEW-lean-7)");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_24_Hz_timeline_on_a_240_Hz_display_asks_for_at_most_62_frames_a_second()
    {
        long updates = 0;
        var (requests, draws) = Measure(runtime => runtime.OnMain(() =>
        {
            // HydraProgressBar.cs:220  AnimationTimelineSchedule(1.0 / 24.0, ...)
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 24.0, Paused: false), _ => updates++);
            timeline.Appear();
        }), () => updates);
        Assert.InRange(draws, 23.5, 24.5);
        Assert.True(requests <= 62, $"{requests} frame requests a second (budget 62; 240 on the unchanged base, 48 after ED-NEW-lean-7)");
    }
}
