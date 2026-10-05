using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "Handshake timing", "Handshake defaults", "Handshake point everywhere": per step, then per account, then general; Smart is measured, else $20 plans 95%, 5x/10x/20x plans 98%.
public sealed class HandshakeTimingTests
{
    private static readonly Guid Thread = HandshakeRig.Thread;

    private static double Start(AccountInfo account, PillStep? step = null, HandshakeOptions? options = null, InMemoryAccountLimits? limits = null) =>
        HandshakeTiming.StartPercent(account, step, options ?? new HandshakeOptions(), limits ?? new InMemoryAccountLimits(), Thread);

    [Theory]
    [InlineData(PlanTier.Standard, 95)]
    [InlineData(PlanTier.Large, 98)]
    [InlineData(PlanTier.Unknown, 95)]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T26")]
    [Trait("Rule", "R-TEST-4")]
    public void Smart_without_data_uses_the_plan_aware_default(PlanTier plan, double expected) =>
        Assert.Equal(expected, Start(new AccountInfo("a", "s", "A", Plan: plan)));

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T27")]
    [Trait("Rule", "R-TEST-4")]
    public void Smart_with_data_starts_when_what_is_left_covers_the_handshake_one_step_and_a_margin()
    {
        var limits = new InMemoryAccountLimits();
        limits.SetRate("a", 2);
        limits.SetHandshakeCost("a", 3);
        Assert.Equal(94, Start(new AccountInfo("a", "s", "A", Plan: PlanTier.Large), limits: limits));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T28")]
    [Trait("Rule", "R-TEST-4")]
    public void Smart_stays_between_80_and_99()
    {
        var limits = new InMemoryAccountLimits();
        limits.SetRate("a", 0);
        limits.SetHandshakeCost("a", 0);
        Assert.Equal(99, Start(new AccountInfo("a", "s", "A"), limits: limits));
        limits.SetRate("a", 40);
        Assert.Equal(80, Start(new AccountInfo("a", "s", "A"), limits: limits));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T29")]
    [Trait("Rule", "R-TEST-4")]
    public void A_step_point_beats_an_account_point_which_beats_the_general_default()
    {
        var general = new HandshakeOptions(General: HandshakePoint.At(90));
        var account = new AccountInfo("a", "s", "A", HandshakeAt: HandshakePoint.At(97));
        Assert.Equal(90, Start(new AccountInfo("a", "s", "A"), options: general));
        Assert.Equal(97, Start(account, options: general));
        Assert.Equal(85, Start(account, new PillStep("a", HandshakeAt: HandshakePoint.At(85)), general));
        Assert.Equal(95, Start(account, new PillStep("a", HandshakeAt: HandshakePoint.Smart), general));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T30")]
    [Trait("Rule", "R-TEST-4")]
    public void A_set_point_is_80_to_99_or_Smart()
    {
        Assert.True(HandshakePoint.Smart.IsSmart);
        Assert.Equal(80, HandshakePoint.At(80).Percent);
        Assert.Equal(99, HandshakePoint.At(99).Percent);
        Assert.Throws<ArgumentOutOfRangeException>(() => HandshakePoint.At(79));
        Assert.Throws<ArgumentOutOfRangeException>(() => HandshakePoint.At(100));
    }
}
