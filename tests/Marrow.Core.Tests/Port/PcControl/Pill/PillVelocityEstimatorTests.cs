// PORT-ONLY: the pill's release velocity (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3, 10.11): the least-squares slope of the
// last 80 ms of samples (at least three), zero when the pointer stopped 50 ms before the release, clamped to 8,000 DIP/s.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-13")]
public sealed class PillVelocityEstimatorTests
{
    [Fact]
    public void A_steady_drag_gives_its_speed()
    {
        var estimator = new PillVelocityEstimator();
        for (var t = 0; t <= 64; t += 8) estimator.Add(1_000 + t, new PillVector(500 - (1.8 * t), 20 + (0.5 * t)));

        var velocity = estimator.Velocity(1_066);

        Assert.Equal(-1_800, velocity.X, 6);
        Assert.Equal(500, velocity.Y, 6);
    }

    [Fact]
    public void Only_the_last_80_ms_count()
    {
        var estimator = new PillVelocityEstimator();
        // A fast start, then 80 ms at 1 DIP/ms: only the slow part counts.
        for (var t = 0; t < 100; t += 10) estimator.Add(t, new PillVector(t * 10, 0));
        for (var t = 100; t <= 180; t += 10) estimator.Add(t, new PillVector(1_000 + (t - 100), 0));

        Assert.Equal(1_000, estimator.Velocity(180).X, 6);
    }

    [Fact]
    public void A_pointer_that_stopped_before_letting_go_throws_nothing()
    {
        var estimator = new PillVelocityEstimator();
        for (var t = 0; t <= 40; t += 8) estimator.Add(t, new PillVector(t * 3, 0));

        Assert.NotEqual(PillVector.Zero, estimator.Velocity(90));
        Assert.Equal(PillVector.Zero, estimator.Velocity(91));
    }

    [Fact]
    public void Fewer_than_three_samples_give_zero()
    {
        var estimator = new PillVelocityEstimator();
        estimator.Add(0, new PillVector(0, 0));
        estimator.Add(8, new PillVector(40, 0));

        Assert.Equal(PillVector.Zero, estimator.Velocity(10));
        estimator.Add(16, new PillVector(80, 0));
        Assert.Equal(5_000, estimator.Velocity(16).X, 6);
    }

    [Fact]
    public void Each_axis_is_clamped_to_8000_DIP_per_second()
    {
        var estimator = new PillVelocityEstimator();
        for (var t = 0; t <= 32; t += 8) estimator.Add(t, new PillVector(t * 20, -t * 30));

        Assert.Equal(new PillVector(8_000, -8_000), estimator.Velocity(32));
    }
}
