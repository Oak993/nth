using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// OD-0116, lead decision 2026-10-06: a destination without a verified text-only standby gets the context with its first real turn,
// after the source reply has ended, with no read-back and no resumed reply.
[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeDeferredDeliveryTests
{
    [Fact]
    public async Task A_deferred_destination_receives_the_package_without_a_read_back_or_gap_fill_and_never_resumes_a_reply()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.DeferredAccounts.Add("c2");
        rig.Host.ReplyInFlight = true;
        rig.Summaries.KeyFacts = ["file.cs"];
        rig.SetUsed("c1", 100, rig.Clock.GetUtcNow().AddHours(1), true);
        await rig.Machine.LimitHitAsync("c1", rig.Clock.GetUtcNow().AddHours(1), rig.Ct);
        Assert.Equal(["standby:c2", "receive", "switch:c2:noresume:safe"], rig.Host.Log);
        Assert.Empty(rig.Host.GapsSent);
        Assert.DoesNotContain(rig.Events, e => e.Kind == HandshakeEventKind.InSyncReady);
    }

    [Fact]
    public async Task A_near_limit_move_to_a_deferred_destination_waits_for_the_reply_to_end_even_when_finish_was_chosen()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(midReply: DecisionValues.Finish);
        rig.Host.DeferredAccounts.Add("c2");
        rig.Host.ReplyInFlight = true;
        rig.SetUsed("c1", 99, rig.Clock.GetUtcNow().AddHours(1));
        await rig.Machine.TickAsync(rig.Ct);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.DoesNotContain(rig.Host.Log, entry => entry.StartsWith("switch", StringComparison.Ordinal));
        rig.Host.ReplyInFlight = false;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        Assert.Contains("switch:c2:noresume:safe", rig.Host.Log);
    }

    [Fact]
    public async Task A_destination_that_is_not_deferred_keeps_its_read_back()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.SetUsed("c1", 100, rig.Clock.GetUtcNow().AddHours(1), true);
        await rig.Machine.LimitHitAsync("c1", rig.Clock.GetUtcNow().AddHours(1), rig.Ct);
        Assert.Contains("receive:readback", rig.Host.Log);
    }
}
