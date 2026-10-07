using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

[Trait("Rule", "R-TEST-4")]
public sealed class ApiKeyBudgetTests
{
    [Fact]
    public void Parallel_heads_reserve_one_shared_remaining_cap_and_settle_only_their_own_key()
    {
        var rig = new ApiKeyRig(keys: 1);
        rig.Store.Update("a", key => key with { MonthlyCapUsd = 1 });
        var first = rig.Rotation(); var second = rig.Rotation(ApiKeyRig.Other);
        Assert.Equal("a", rig.Pick(first)); Assert.Equal("a", rig.Pick(second));
        var reserved = first.Budget!.Reserve("a", 0.7)!;
        Assert.Null(second.Budget!.Reserve("a", 0.4));
        reserved.Complete(0.2);
        reserved.Complete(0.9); // duplicate completion must not charge twice
        Assert.Equal(0.2, rig.Spend.SpendSince("a", DateTimeOffset.MinValue));
        Assert.NotNull(second.Budget.Reserve("a", 0.8));
        Assert.Null(first.Budget.Reserve("a", 0.01));
    }

    [Fact]
    public void Unknown_price_or_nonfinite_bound_cannot_authorize_a_capped_request()
    {
        var rig = new ApiKeyRig(keys: 1);
        rig.Store.Update("a", key => key with { MonthlyCapUsd = 1 });
        var rotation = rig.Rotation(); rig.Pick(rotation);
        Assert.True(rotation.Budget!.RequiresPrice("a"));
        Assert.Null(rotation.Budget.Reserve("a", null));
        Assert.Null(rotation.Budget.Reserve("a", double.NaN));
        Assert.Null(rotation.Budget.Reserve("a", -1));
        Assert.Equal(0, rig.Spend.SpendSince("a", DateTimeOffset.MinValue));
    }

    [Fact]
    public void Interrupted_or_unreported_usage_retains_its_hold_across_new_pool_instances()
    {
        var rig = new ApiKeyRig(keys: 1);
        rig.Store.Update("a", key => key with { MonthlyCapUsd = 1 });
        var rotation = rig.Rotation(); rig.Pick(rotation);
        rotation.Budget!.Reserve("a", 1)!.Complete(null);
        var restart = new ApiKeyPool(ApiKeyRig.Service, rig.Store, rig.Credits, rig.Spend, rig.Clock);
        Assert.True(restart.State().AllOut);
        Assert.Equal(1, rig.Spend.SpendSince("a", DateTimeOffset.MinValue));
    }

    [Fact]
    public void A_fitting_reservation_does_not_exhaust_itself_but_a_lowered_cap_revokes_dispatch()
    {
        var rig = new ApiKeyRig(keys: 1);
        rig.Store.Update("a", key => key with { MonthlyCapUsd = 1 });
        var rotation = rig.Rotation(); rig.Pick(rotation);
        var reservation = rotation.Budget!.Reserve("a", 1)!;
        Assert.True(reservation.CanDispatch);
        Assert.True(rig.Pool.IsAvailable("a"));
        rig.Store.Update("a", key => key with { MonthlyCapUsd = 0.5 });
        Assert.False(reservation.CanDispatch);
        reservation.CancelBeforeDispatch();
        Assert.Equal(0, rig.Spend.SpendSince("a", DateTimeOffset.MinValue));
    }

    [Fact]
    public void Adding_a_cap_after_an_unpriced_request_cannot_turn_unknown_prior_spend_into_free_budget()
    {
        var rig = new ApiKeyRig(keys: 1);
        var rotation = rig.Rotation(); rig.Pick(rotation);
        rotation.Budget!.Reserve("a", null)!.Complete(null);
        rig.Store.Update("a", key => key with { MonthlyCapUsd = 1 });
        Assert.True(rig.Pool.State().AllOut);
        Assert.Null(rig.Pool.State().ReturnsAt);
        Assert.Null(rig.Pick(rotation));
    }
}
