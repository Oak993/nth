using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7: a request held back to an instant (IWakeFrameSource) on the manual frame source, and the slow display
// link's and timeline's changes of rate while their request sleeps: they draw exactly where a source that holds nothing
// back makes them draw.
public sealed class FrameClockWakeTests
{
    private static readonly TimeSpan Refresh240 = TimeSpan.FromTicks(41667);

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_manual_frame_source_without_a_wake_period_answers_a_held_request_at_the_next_frame()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        var log = new List<double>();
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(50), time => log.Add(time.TotalMilliseconds));
        frames.Advance(TimeSpan.FromMilliseconds(4));
        Assert.Equal([4.0], log);
        Assert.Equal(1, frames.Delivered);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_manual_frame_source_with_a_wake_period_answers_a_held_request_at_the_first_frame_the_clock_has_reached_it()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource { WakePeriod = Refresh240 };
        var log = new List<double>();
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(10), time => log.Add(time.TotalMilliseconds));
        Assert.Equal(1, frames.Pending);
        for (var i = 0; i < 2; i++)
        {
            runtime.Advance(TimeSpan.FromMilliseconds(4));
            frames.Advance(TimeSpan.FromMilliseconds(4));
        }
        Assert.Empty(log); // 8 ms: not yet
        Assert.Equal(0, frames.Delivered);
        runtime.Advance(TimeSpan.FromMilliseconds(4));
        frames.Advance(TimeSpan.FromMilliseconds(4));
        Assert.Equal([12.0], log);
        Assert.Equal(0, frames.Pending);
        Assert.Equal(1, frames.Delivered);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_held_request_withdrawn_before_its_instant_never_runs()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource { WakePeriod = Refresh240 };
        var ran = 0;
        Action<TimeSpan> frame = _ => ran++;
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(10), frame);
        frames.CancelFrame(frame);
        Assert.Equal(0, frames.Pending);
        runtime.Advance(TimeSpan.FromMilliseconds(20));
        frames.Advance(TimeSpan.FromMilliseconds(20));
        Assert.Equal(0, ran);
    }

    // The rate of a display link changed while its request sleeps (as StreamVeil could) and a timeline re-timed, paused and
    // resumed many times at awkward instants: the same draws as on a source that holds nothing back.
    [Theory]
    [InlineData(240)]
    [InlineData(144)]
    [InlineData(60)]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void Rates_changed_while_a_request_sleeps_draw_where_a_source_that_holds_nothing_back_draws(int display)
    {
        Assert.Equal(LinkRetimed(display, wake: false), LinkRetimed(display, wake: true));
        Assert.Equal(TimelineChurned(display, wake: false), TimelineChurned(display, wake: true));
    }

    private static List<long> LinkRetimed(int display, bool wake)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource { WakePeriod = wake ? TimeSpan.FromTicks(TimeSpan.TicksPerSecond / display) : null };
        using var source = FrameClock.Source.Override(frames);
        var start = SwiftDate.Now;
        var draws = new List<long>();
        DisplayLink? link = null;
        runtime.OnMain(() =>
        {
            link = FrameClock.DisplayLink(() => draws.Add((SwiftDate.Now - start).Ticks));
            link.PreferredFrameRateRange = new FrameRateRange(24, 60, 20);
            link.Add();
        });
        float[] rates = [20, 60, 24, 30, 20];
        for (var k = 1; k <= display * 3; k++)
        {
            var gap = TimeSpan.FromTicks(k * TimeSpan.TicksPerSecond / display - (k - 1) * TimeSpan.TicksPerSecond / display);
            runtime.Advance(gap);
            runtime.OnMain(() => frames.Advance(gap));
            if (k % 37 == 0) runtime.OnMain(() => link!.PreferredFrameRateRange = new FrameRateRange(24, 60, rates[k / 37 % rates.Length]));
        }
        return draws;
    }

    private static List<long> TimelineChurned(int display, bool wake)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource { WakePeriod = wake ? TimeSpan.FromTicks(TimeSpan.TicksPerSecond / display) : null };
        using var source = FrameClock.Source.Override(frames);
        var start = SwiftDate.Now;
        var draws = new List<long>();
        var rate = new Fixtures.CellSample<int>(24);
        var paused = new Fixtures.CellSample<bool>(false);
        runtime.OnMain(() => new AnimationTimeline(
            () => new AnimationTimelineSchedule(MinimumInterval: 1.0 / rate.Value, Paused: paused.Value),
            date => draws.Add((date - start).Ticks)).Appear());
        int[] rates = [24, 20, 60, 30, 24];
        for (var k = 1; k <= display * 3; k++)
        {
            var gap = TimeSpan.FromTicks(k * TimeSpan.TicksPerSecond / display - (k - 1) * TimeSpan.TicksPerSecond / display);
            runtime.Advance(gap);
            runtime.OnMain(() => frames.Advance(gap));
            if (k % 29 == 0) runtime.OnMain(() => rate.Value = rates[k / 29 % rates.Length]);
            if (k % 41 == 0) runtime.OnMain(() => paused.Value = !paused.Value);
        }
        return draws;
    }
}
