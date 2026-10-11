using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7 budget: frame requests a second for slow schedules on a 240 Hz display. Headless and deterministic
// (ManualFrameSource frames at 240 Hz in whole ticks, ManualTimeProvider), Debug build. A frame request is one
// RequestAnimationFrame plus one compositor commit in production (FrameClock.cs AvaloniaFrameSource, ED-237).
// Measured on the unchanged base (1fdf47b43): 240 a second for a 30 Hz display link and for a 24 Hz timeline.
public sealed class FrameClockPacingBudgetTests
{
    private const int Rate = 240;
    private const int WarmUpFrames = Rate;          // one simulated second
    private const int MeasuredFrames = Rate * 10;   // ten simulated seconds

    private static TimeSpan FrameTime(long frame) => TimeSpan.FromTicks(frame * TimeSpan.TicksPerSecond / Rate);

    private sealed class Counting(ManualFrameSource inner) : IFrameSource
    {
        public long Requests;

        public int Pending => inner.Pending;

        public void RequestFrame(Action<TimeSpan> frame)
        {
            Requests++;
            inner.RequestFrame(frame);
        }

        public void CancelFrame(Action<TimeSpan> frame) => inner.CancelFrame(frame);
    }

    private static (double RequestsPerSecond, double DrawsPerSecond) Measure(Action<TestRuntime> start, Func<long> draws)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        var counting = new Counting(frames);
        using var source = FrameClock.Source.Override(counting);
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
        var requestsBefore = counting.Requests;
        var drawsBefore = draws();
        Run(MeasuredFrames);
        var seconds = MeasuredFrames / (double)Rate;
        return ((counting.Requests - requestsBefore) / seconds, (draws() - drawsBefore) / seconds);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_30_Hz_display_link_on_a_240_Hz_display_asks_for_at_most_360_frames_a_second()
    {
        long ticks = 0;
        var (requests, draws) = Measure(runtime => runtime.OnMain(() =>
        {
            var link = FrameClock.DisplayLink(() => ticks++);
            link.PreferredFrameRateRange = new FrameRateRange(24, 60, 30); // StreamVeil.cs:233
            link.Add();
        }), () => ticks);
        Assert.InRange(draws, 29.5, 30.5);
        Assert.True(requests <= 360, $"{requests} frame requests a second (budget 360; 240 on the unchanged base)");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_24_Hz_timeline_on_a_240_Hz_display_asks_for_at_most_360_frames_a_second()
    {
        long updates = 0;
        var (requests, draws) = Measure(runtime => runtime.OnMain(() =>
        {
            // HydraProgressBar.cs:220  AnimationTimelineSchedule(1.0 / 24.0, ...)
            var timeline = new AnimationTimeline(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 24.0, Paused: false), _ => updates++);
            timeline.Appear();
        }), () => updates);
        Assert.InRange(draws, 23.5, 24.5);
        Assert.True(requests <= 360, $"{requests} frame requests a second (budget 360; 240 on the unchanged base)");
    }
}
