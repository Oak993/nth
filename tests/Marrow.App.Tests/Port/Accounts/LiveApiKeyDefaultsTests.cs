using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveApiKeyDefaultsTests
{
    [Fact]
    public void An_API_key_change_applies_destination_defaults_and_preserves_composer_choices_until_that_change()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek,
            configure: thread => thread with { Model = "offline-composer", Effort = "low", FastMode = false }, install: false);
        Guid target = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-next", next));
            rig.App.Settings.Accounts.SetDefaults(target, new() { Model = "offline-destination", Effort = "max", FastMode = true });
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        });
        var api = LiveApiKeyLaunch.Ensure(rig);
        rig.H.Main.OnMain(() => Assert.NotNull(api.KeyRotation!.Current()));
        Assert.Equal("offline-composer", rig.Thread.Model);
        Assert.Equal("low", rig.Thread.Effort);
        Assert.False(rig.Thread.FastMode);
        rig.H.Main.OnMain(() => Assert.Equal(target.ToString("D"), api.KeyRotation!.OnFailure(402, null)!.Id));
        Assert.Equal("offline-destination", rig.Thread.Model);
        Assert.Equal("max", rig.Thread.Effort);
        Assert.True(rig.Thread.FastMode);
        Assert.Equal(target, rig.Thread.ProviderAccountID);
        Assert.Empty(http.Received);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
