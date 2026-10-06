using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveApiHydraRebindTests
{
    [Fact]
    public void A_live_key_switch_takes_the_destination_Hydra_policy_with_its_defaults()
    {
        using var h = new LiveApiHydraRig();
        h.Rig.H.Main.OnMain(() => Assert.Equal(h.Rig.App.HydraLaunch(h.Rig.App.Thread(h.Rig.InitialThread.Id)!), h.Api.Configuration.Hydra));
    }

    [Fact]
    public void The_next_message_keeps_the_actual_API_session_after_a_Hydra_default_change()
    {
        using var h = new LiveApiHydraRig();
        Assert.Same(h.Api, LiveApiKeyLaunch.Ensure(h.Rig));
        Assert.True(h.Api.IsRunning);
    }
}
