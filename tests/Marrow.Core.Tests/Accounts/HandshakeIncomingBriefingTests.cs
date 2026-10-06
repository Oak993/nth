using Marrow.Core.Accounts;
namespace Marrow.Core.Tests.Accounts;
[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeIncomingBriefingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_explicit_AI_summary_uses_the_incoming_light_briefing_when_the_outgoing_one_is_unavailable(bool hardStop)
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(bring: DecisionValues.AiSummary);
        rig.Host.IncomingBriefing = "ACTUAL-INCOMING-LIGHT-BRIEFING";
        rig.Host.BriefingFails = new IOException("outgoing account unavailable");
        rig.SetUsed("c1", hardStop ? 100 : 98);
        if (hardStop) await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        else await rig.Machine.TickAsync(rig.Ct);
        var package = Assert.Single(rig.Host.Packages);
        Assert.Equal(Bring.AiSummary, package.Bring);
        Assert.Equal("ACTUAL-INCOMING-LIGHT-BRIEFING", package.Briefing);
        Assert.Contains("light-briefing:c2", rig.Host.Log);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        if (hardStop) Assert.DoesNotContain("briefing", rig.Host.Log);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_or_failed_incoming_AI_briefing_refuses_the_switch_instead_of_substituting_Marrows_summary(bool fails)
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(bring: DecisionValues.AiSummary);
        rig.Host.IncomingBriefing = null;
        if (fails) rig.Host.IncomingBriefingFails = new IOException("light model unavailable");
        rig.SetUsed("c1", 100);
        await rig.Machine.LimitHitAsync("c1", null, rig.Ct);
        Assert.Empty(rig.Host.Packages);
        Assert.Equal("c1", rig.Host.ActiveAccountId);
        Assert.Equal(1, rig.Host.StandbysDisposed);
        Assert.Contains(rig.Events, value => value.Kind == HandshakeEventKind.Degraded);
    }
    [Fact]
    public async Task A_mechanical_summary_choice_and_an_unanswered_first_time_card_do_not_generate_a_light_summary()
    {
        await using var chosen = new HandshakeRig();
        chosen.SettleAll(); chosen.SetUsed("c1", 100);
        await chosen.Machine.LimitHitAsync("c1", null, chosen.Ct);
        Assert.DoesNotContain(chosen.Host.Log, value => value.StartsWith("light-briefing:", StringComparison.Ordinal));
        await using var unset = new HandshakeRig(); unset.SetUsed("c1", 100);
        await unset.Machine.LimitHitAsync("c1", null, unset.Ct);
        Assert.Empty(unset.Host.Log);
        Assert.Equal(HandshakePhase.Asking, unset.Machine.Phase);
    }
}
