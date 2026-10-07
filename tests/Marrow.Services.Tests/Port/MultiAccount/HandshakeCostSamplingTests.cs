using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Port.Usage;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeCostSamplingTests
{
    [Fact]
    public async Task A_private_transfer_waits_for_all_overlapping_work_and_a_read_started_after_completion()
    {
        using var rig = new HandshakeCostSamplingRig();
        await rig.Scheduler.SignedInAsync(rig.Limits.Key(rig.Account));
        using var first = rig.Begin();
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        using var second = rig.Begin();
        var oldRead = rig.Clock.GetUtcNow();
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        HandshakeCostSamplingRig.Complete(first);
        rig.Observe(new(rig.Limits.Key(rig.Account), rig.MakeReport(22), rig.Clock.GetUtcNow()) { ReadStartedAt = rig.Clock.GetUtcNow() });
        Assert.Null(rig.Cost);
        HandshakeCostSamplingRig.Complete(second);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        rig.Observe(new(rig.Limits.Key(rig.Account), rig.MakeReport(23), rig.Clock.GetUtcNow()) { ReadStartedAt = oldRead });
        Assert.Null(rig.Cost);
        rig.Observe(new(rig.Limits.Key(rig.Account), rig.MakeReport(24), rig.Clock.GetUtcNow()));
        Assert.Equal(4, rig.Cost);
        // A later unrelated account read does not keep accumulating work into this finished Handshake.
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        rig.Observe(new(rig.Limits.Key(rig.Account), rig.MakeReport(80), rig.Clock.GetUtcNow()));
        Assert.Equal(4, rig.Cost);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("reset")]
    [InlineData("plan")]
    [InlineData("duration")]
    [InlineData("decrease")]
    [InlineData("unknown")]
    [InlineData("foreign")]
    public async Task An_unfinished_or_incompatible_observation_cannot_manufacture_a_private_cost(string change)
    {
        using var rig = new HandshakeCostSamplingRig();
        await rig.Scheduler.SignedInAsync(rig.Limits.Key(rig.Account));
        using var observation = rig.Begin();
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        if (change == "cancel") observation.Dispose();
        else HandshakeCostSamplingRig.Complete(observation);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        var report = rig.MakeReport(change == "decrease" ? 19 : 24);
        report = report with { PlanName = change == "plan" ? "Max" : report.PlanName,
            Windows = new([report.Windows[0] with { ResetsAt = change == "reset" ? rig.Reset.AddHours(4) : rig.Reset,
                DurationMinutes = change == "duration" ? 600 : 300 }]) };
        var key = change == "foreign" ? new(rig.Limits.Key(rig.Account).Provider, Guid.NewGuid().ToString("D")) : rig.Limits.Key(rig.Account);
        rig.Observe(new(key, change == "unknown" ? null : report, rig.Clock.GetUtcNow()));
        Assert.Null(rig.Cost);
    }
}
