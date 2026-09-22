using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-TASK-6: each timing shape in the source has its own contract; tests drive time to both sides of the
// boundary, never with a real sleep. Source: draft B (proto-B ClockTests).
public sealed class TimingShapeTests
{
    [Fact]
    [Trait("Rule", "R-TASK-6")]
    [Trait("Decision", "ED-068")]
    public void Scroll_freeze_debounce_lifts_150_ms_after_the_last_tick()
    {
        using var runtime = new TestRuntime();
        var sidebar = new SidebarScrollModel();
        foreach (var at in new[] { 0, 100, 200 })
        {
            runtime.AdvanceMilliseconds(at - runtime.Time.Elapsed.TotalMilliseconds);
            runtime.Loop.Enqueue(sidebar.NoteListScroll);
            runtime.RunUntilIdle();
            Assert.True(sidebar.IsListScrolling);
        }
        runtime.AdvanceMilliseconds(149);
        Assert.True(sidebar.IsListScrolling);
        runtime.AdvanceMilliseconds(1);
        Assert.False(sidebar.IsListScrolling);
        Assert.Equal(1, sidebar.RaiseCount); // set once per scroll, not once per tick
    }

    [Fact]
    [Trait("Rule", "R-TASK-6")]
    public void Live_resize_sync_is_throttled_not_debounced()
    {
        using var runtime = new TestRuntime();
        var panel = new TerminalResizeModel();
        for (var i = 0; i < 4; i++)
        {
            runtime.Loop.Enqueue(panel.ResizeSubviews);
            runtime.RunUntilIdle();
            runtime.AdvanceMilliseconds(20);
        }
        // Calls at 0, 20, 40, 60 ms: the first armed a sync for 50 ms; the one at 60 armed the next.
        Assert.Equal(1, panel.Syncs);
        runtime.AdvanceMilliseconds(50);
        Assert.Equal(2, panel.Syncs);
    }

    [Fact]
    [Trait("Rule", "R-TIME-2")]
    public void Continuous_instants_subtract_to_durations()
    {
        using var runtime = new TestRuntime();
        var lastFlushAt = SwiftClock.Now - TimeSpan.FromSeconds(1); // ThreadRuntime.swift:425
        runtime.AdvanceMilliseconds(250);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), SwiftClock.Now - lastFlushAt);
        Assert.True(SwiftClock.Now - lastFlushAt >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    [Trait("Rule", "R-TIME-1")]
    public void Date_now_reads_the_same_clock_the_timers_run_on()
    {
        using var runtime = new TestRuntime();
        var start = SwiftDate.Now;
        runtime.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(90.0, SwiftDate.TimeIntervalSince(SwiftDate.Now, start));
    }
}
