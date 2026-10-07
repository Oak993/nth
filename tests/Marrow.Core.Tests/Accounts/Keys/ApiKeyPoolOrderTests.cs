using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

// NOTES "API providers": several keys, nickname, Allowed switch, order (in order / most credits first / spread evenly); "Enable/disable per account".
public sealed class ApiKeyPoolOrderTests
{
    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T101")]
    [Trait("Rule", "R-TEST-4")]
    public void In_order_the_first_allowed_key_is_used_with_its_nickname_and_secret()
    {
        var rig = new ApiKeyRig();
        var choice = rig.Rotation().Current()!;
        Assert.Equal(("a", "Key A", "sk-a"), (choice.Id, choice.Nickname, choice.Secret));
        Assert.Equal("a", rig.Pick(rig.Rotation()));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T102")]
    [Trait("Rule", "R-TEST-4")]
    public void A_key_that_is_not_allowed_is_never_used_and_stays_stored()
    {
        var rig = new ApiKeyRig();
        rig.Store.Update("a", k => k with { Allowed = false });
        Assert.Equal("b", rig.Pick(rig.Rotation()));
        Assert.Equal("sk-a", rig.Store.Secret("a"));
        rig.Store.Update("a", k => k with { Allowed = true });
        Assert.Equal("a", rig.Pick(rig.Rotation()));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T103")]
    [Trait("Rule", "R-TEST-4")]
    public void A_key_with_no_stored_secret_is_skipped()
    {
        var rig = new ApiKeyRig();
        rig.Store.SetSecret("a", null);
        rig.Store.SetSecret("b", "  ");
        Assert.Equal("c", rig.Pick(rig.Rotation()));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T104")]
    [Trait("Rule", "R-TEST-4")]
    public void With_no_allowed_key_there_is_no_choice_and_the_pool_says_all_keys_out()
    {
        var rig = new ApiKeyRig(2);
        rig.Store.Update("a", k => k with { Allowed = false });
        rig.Store.Update("b", k => k with { Allowed = false });
        Assert.Null(rig.Rotation().Current());
        Assert.True(rig.Pool.State().AllOut);
        Assert.Equal(ApiKeyEventKind.AllKeysOut, Assert.Single(rig.Events).Kind);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T105")]
    [Trait("Rule", "R-TEST-4")]
    public void Most_credits_first_picks_the_key_with_the_most_credits_and_unknown_credits_come_last()
    {
        var rig = new ApiKeyRig();
        rig.Store.SetOrder(ApiKeyRig.Service, KeyOrderMode.MostCreditsFirst);
        rig.Credits.Set("a", 3);
        rig.Credits.Set("c", 9);
        Assert.Equal("c", rig.Pick(rig.Rotation()));
        rig.Credits.Set("c", null);
        Assert.Equal("a", rig.Pick(rig.Rotation()));
        rig.Credits.Set("a", null);
        Assert.Equal("a", rig.Pick(rig.Rotation()));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T106")]
    [Trait("Rule", "R-TEST-4")]
    public void Spread_evenly_gives_each_key_a_turn_before_any_gets_a_second()
    {
        var rig = new ApiKeyRig();
        rig.Store.SetOrder(ApiKeyRig.Service, KeyOrderMode.SpreadEvenly);
        var picks = Enumerable.Range(0, 6).Select(_ => rig.Pick(rig.Rotation(Guid.NewGuid()))).ToList();
        Assert.Equal(["a", "b", "c", "a", "b", "c"], picks);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T107")]
    [Trait("Rule", "R-TEST-4")]
    public void Spread_evenly_skips_a_key_that_is_off_and_does_not_owe_it_turns_later()
    {
        var rig = new ApiKeyRig();
        rig.Store.SetOrder(ApiKeyRig.Service, KeyOrderMode.SpreadEvenly);
        rig.Store.Update("b", k => k with { Allowed = false });
        Assert.Equal(["a", "c", "a", "c"], Enumerable.Range(0, 4).Select(_ => rig.Pick(rig.Rotation())).ToList());
        rig.Store.Update("b", k => k with { Allowed = true });
        Assert.Equal("b", rig.Pick(rig.Rotation()));
    }
}
