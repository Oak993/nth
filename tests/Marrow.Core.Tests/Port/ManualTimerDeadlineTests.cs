using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

public sealed class ManualTimerDeadlineTests
{
    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public void Equal_timer_counts_do_not_hide_the_transition_from_deadline_to_kill()
    {
        var clock = new ManualTimeProvider();
        using var deadline = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(120), Timeout.InfiniteTimeSpan);
        Assert.Equal(1, clock.ActiveTimers);
        Assert.Equal([TimeSpan.FromSeconds(120)], clock.TimerDeadlines);
        clock.Advance(TimeSpan.FromSeconds(120));
        using var kill = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        Assert.Equal(1, clock.ActiveTimers);
        Assert.Equal([TimeSpan.FromSeconds(121)], clock.TimerDeadlines);
        kill.Dispose();
        Assert.Empty(clock.TimerDeadlines);
    }
}
