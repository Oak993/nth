using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

// OD-0116 (lead decision 2026-10-06): a key the user turns off, or whose secret goes away, while a thread is on it is a forced move like out and cap:
// nothing moves until the user has agreed; the first time the thread asks.
[Trait("Rule", "R-TEST-4")]
public sealed class ApiKeyPoolTurnedOffConsentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_live_threads_key_that_goes_out_of_use_asks_before_the_thread_moves(bool secretRemoved)
    {
        var rig = new ApiKeyRig(autoMove: false);
        var rotation = rig.Rotation();
        Assert.Equal("a", rotation.Current()!.Id);
        if (secretRemoved) rig.Store.SetSecret("a", null);
        else rig.Store.Update("a", k => k with { Allowed = false });
        Assert.Null(rotation.Current());
        var asked = Assert.Single(rig.Events);
        Assert.Equal((ApiKeyEventKind.MoveNeedsAnswer, "a", "b"), (asked.Kind, asked.FromKeyId, asked.ToKeyId));
        Assert.NotNull(rotation.StopReason);
        Assert.Equal("b", rig.Pick(rig.Rotation(ApiKeyRig.Other)));
    }

    [Fact]
    public void A_no_to_moving_keeps_a_turned_off_key_from_being_replaced()
    {
        var rig = new ApiKeyRig(autoMove: false);
        rig.Moves.Set(ApiKeyRig.Thread, KeyMoveSetting.Deny);
        var rotation = rig.Rotation();
        rotation.Current();
        rig.Store.Update("a", k => k with { Allowed = false });
        Assert.Null(rotation.Current());
        Assert.Equal(ApiKeyEventKind.MoveDeclined, Assert.Single(rig.Events).Kind);
    }

    [Fact]
    public void After_a_yes_the_thread_moves_off_a_turned_off_key_once_or_always()
    {
        var once = new ApiKeyRig(autoMove: false);
        var rotation = once.Rotation();
        rotation.Current();
        once.Store.Update("a", k => k with { Allowed = false });
        Assert.Null(rotation.Current());
        rotation.AllowMoveOnce();
        Assert.Equal("b", rotation.Current()!.Id);

        var always = new ApiKeyRig();
        var remembered = always.Rotation();
        remembered.Current();
        always.Store.Update("a", k => k with { Allowed = false });
        Assert.Equal("b", remembered.Current()!.Id);
    }
}
