using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

// OD-0116 (owner): moving to another key by itself is OFF by default; the first time it would happen the user is asked; the silent retry applies only once the user has chosen that.
public sealed class ApiKeyPoolMoveConsentTests
{
    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T139")]
    [Trait("Rule", "R-TEST-4")]
    public void By_default_a_402_does_not_move_the_thread_and_the_first_time_asks()
    {
        var rig = new ApiKeyRig(autoMove: false);
        var rotation = rig.Rotation();
        rotation.Current();
        Assert.Null(rotation.OnFailure(402, null));
        var e = Assert.Single(rig.Events);
        Assert.Equal((ApiKeyEventKind.MoveNeedsAnswer, "a", "b", 402), (e.Kind, e.FromKeyId, e.ToKeyId, e.Status));
        Assert.Contains("Key A", rotation.StopReason, StringComparison.Ordinal);
        Assert.Contains("Key B", rotation.StopReason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T140")]
    [Trait("Rule", "R-TEST-4")]
    public void The_thread_waits_on_its_key_until_answered_and_a_new_thread_is_not_held_back()
    {
        var rig = new ApiKeyRig(autoMove: false);
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.OnFailure(429, null);
        Assert.Null(rotation.Current());
        Assert.NotNull(rotation.StopReason);
        Assert.Equal("b", rig.Pick(rig.Rotation(ApiKeyRig.Other)));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T141")]
    [Trait("Rule", "R-TEST-4")]
    public void Yes_just_this_time_moves_once_and_the_next_forced_move_asks_again()
    {
        var rig = new ApiKeyRig(autoMove: false);
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.OnFailure(402, null);
        rotation.AllowMoveOnce();
        Assert.Equal("b", rotation.Current()!.Id);
        Assert.Contains(rig.Events, e => e.Kind == ApiKeyEventKind.Continued && e.ToName == "Key B");
        Assert.Null(rotation.OnFailure(402, null));
        Assert.Equal(2, rig.Events.Count(e => e.Kind == ApiKeyEventKind.MoveNeedsAnswer));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T142")]
    [Trait("Rule", "R-TEST-4")]
    public void Yes_given_before_the_refusal_comes_back_continues_the_same_request()
    {
        var rig = new ApiKeyRig(autoMove: false);
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.AllowMoveOnce();
        Assert.Equal("b", rotation.OnFailure(429, null)!.Id);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T143")]
    [Trait("Rule", "R-TEST-4")]
    public void A_remembered_yes_moves_silently_and_a_remembered_no_never_does()
    {
        var rig = new ApiKeyRig(autoMove: false);
        rig.Moves.Set(ApiKeyRig.Thread, KeyMoveSetting.Allow);
        var yes = rig.Rotation();
        yes.Current();
        Assert.Equal("b", yes.OnFailure(402, null)!.Id);

        rig.Moves.Set(ApiKeyRig.Other, KeyMoveSetting.Deny);
        var no = rig.Rotation(ApiKeyRig.Other);
        Assert.Equal("b", no.Current()!.Id);
        Assert.Null(no.OnFailure(402, null));
        var declined = Assert.Single(rig.Events, e => e.Kind == ApiKeyEventKind.MoveDeclined);
        Assert.Equal("b", declined.FromKeyId);
        Assert.Null(no.Current());
        no.AllowMoveOnce();
        Assert.Equal("c", no.Current()!.Id);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T144")]
    [Trait("Rule", "R-TEST-4")]
    public void The_answer_taking_effect_later_lets_the_waiting_thread_move_on_its_next_request()
    {
        var rig = new ApiKeyRig(autoMove: false);
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.OnFailure(402, null);
        Assert.Null(rotation.Current());
        rig.Moves.Set(ApiKeyRig.Thread, KeyMoveSetting.Allow);
        Assert.Equal("b", rotation.Current()!.Id);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T145")]
    [Trait("Rule", "R-TEST-4")]
    public void A_key_that_reaches_its_cap_is_a_forced_move_too()
    {
        var rig = new ApiKeyRig(autoMove: false);
        rig.Store.Update("a", k => k with { MonthlyCapUsd = 1 });
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.RecordSpend(2);
        Assert.Null(rotation.Current());
        Assert.Contains(rig.Events, e => e.Kind == ApiKeyEventKind.MoveNeedsAnswer && e.FromKeyId == "a" && e.ToKeyId == "b");
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T146")]
    [Trait("Rule", "R-TEST-4")]
    public void Starting_a_thread_on_the_first_usable_key_never_asks()
    {
        var rig = new ApiKeyRig(autoMove: false);
        rig.Store.Update("a", k => k with { Allowed = false });
        Assert.Equal("b", rig.Pick(rig.Rotation()));
        Assert.Empty(rig.Events);
    }
}
