using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

[Trait("Decision", "ED-248")]
public sealed class AmbientMotionClockTests
{
    [Fact]
    public void Ambient_requests_are_at_most_60_Hz_even_on_a_165_Hz_display()
    {
        var clock = new AmbientMotionClock();
        var count = 0;
        for (var i = 0; i < 1650; i++)
        {
            var now = i / 165.0;
            if (clock.RequestDelay(now, 0, true) > 0) continue;
            clock.Requested(now); count++;
        }
        Assert.InRange(count, 540, 600);
        clock.Requested(10);
        Assert.Equal(0, clock.RequestDelay(10.001, 10, true, interaction: true));
    }

    [Fact]
    public void Twenty_seconds_without_input_eases_to_a_stop_over_point_six_seconds()
    {
        var clock = new AmbientMotionClock();
        clock.Sample(0, 0, true);
        clock.Sample(20, 0, true); Assert.Equal(1, clock.Speed);
        clock.Sample(20.3, 0, true); Assert.Equal(.5, clock.Speed, 8);
        clock.Sample(20.6, 0, true); Assert.Equal(0, clock.Speed); Assert.False(clock.Moving);
        Assert.True(double.IsPositiveInfinity(clock.RequestDelay(21, 0, true)));
        var stopped = clock.Time;
        clock.Sample(100, 0, true); Assert.Equal(stopped, clock.Time);
    }

    [Fact]
    public void Input_resumes_the_existing_phase_with_a_smooth_ease_in()
    {
        var clock = new AmbientMotionClock();
        clock.Sample(0, 0, true); clock.Sample(20.6, 0, true);
        var before = clock.Time;
        clock.Sample(30, 30, true); Assert.Equal(before, clock.Time); Assert.Equal(0, clock.Speed);
        clock.Sample(30.3, 30, true); Assert.Equal(.5, clock.Speed, 8);
        clock.Sample(30.6, 30, true); Assert.Equal(1, clock.Speed); Assert.True(clock.Moving);
    }

    [Fact]
    public void Continuous_pointer_input_does_not_restart_the_resume_ramp()
    {
        var clock = new AmbientMotionClock();
        clock.Sample(0, 0, true); clock.Sample(21, 0, true);
        clock.Sample(30, 30, true);
        for (var i = 1; i <= 100; i++) clock.Sample(30 + i * .01, 30 + i * .01, true);
        Assert.Equal(1, clock.Speed);
        Assert.True(clock.Time > .5, "moving the pointer must let the decoration advance after resuming");
    }

    [Fact]
    public void An_unavailable_window_stops_even_interaction_requests_and_never_replays_the_gap()
    {
        var clock = new AmbientMotionClock();
        clock.Sample(0, 0, true); clock.Sample(1, 1, true);
        var before = clock.Time;
        clock.Sample(2, 2, false); clock.Sample(100, 100, false);
        Assert.Equal(before, clock.Time); Assert.False(clock.Moving);
        Assert.True(double.IsPositiveInfinity(clock.RequestDelay(100, 100, false, interaction: true)));
        clock.Sample(101, 101, true); Assert.Equal(before, clock.Time); Assert.Equal(0, clock.Speed);
    }
}
