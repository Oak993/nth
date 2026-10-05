using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

[Trait("Rule", "R-TEST-4")]
public sealed class ApiKeyPoolHealthyConsentTests
{
    [Theory]
    [InlineData(KeyOrderMode.SpreadEvenly, KeyMoveSetting.Unset)]
    [InlineData(KeyOrderMode.SpreadEvenly, KeyMoveSetting.Deny)]
    [InlineData(KeyOrderMode.MostCreditsFirst, KeyMoveSetting.Unset)]
    [InlineData(KeyOrderMode.MostCreditsFirst, KeyMoveSetting.Deny)]
    public void Re_ranking_does_not_move_an_existing_healthy_thread_before_automatic_switching_is_chosen(KeyOrderMode order, KeyMoveSetting policy)
    {
        var rig = new ApiKeyRig(autoMove: false);
        rig.Store.SetOrder(ApiKeyRig.Service, order);
        rig.Moves.Set(ApiKeyRig.Thread, policy);
        rig.Credits.Set("a", 9);
        rig.Credits.Set("b", 5);
        var rotation = rig.Rotation();
        Assert.Equal("a", rotation.Current()!.Id);
        rig.Credits.Set("a", 0);
        rig.Credits.Set("b", 10);
        Assert.Equal("a", rotation.Current()!.Id);
        Assert.Empty(rig.Events);
        Assert.Equal("b", rig.Rotation(ApiKeyRig.Other).Current()!.Id);
    }
}
