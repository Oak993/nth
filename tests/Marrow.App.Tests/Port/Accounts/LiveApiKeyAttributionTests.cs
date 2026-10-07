using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveApiKeyAttributionTests
{
    [Fact]
    public void A_limit_after_same_service_key_continuation_belongs_to_the_key_that_actually_served_the_request()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid target = default;
        var events = new List<HandshakeEvent>();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-next", next));
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
            rig.App.AccountRouting.Changed += (_, value) => events.Add(value);
        });
        var api = LiveApiKeyLaunch.Ensure(rig);
        rig.H.Main.OnMain(() =>
        {
            Assert.NotNull(api.KeyRotation!.Current());
            Assert.Equal(target.ToString("D"), api.KeyRotation.OnFailure(429, TimeSpan.FromMinutes(1))!.Id);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, null);
            api.OnEvent!(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddHours(1)));
        });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.NotEmpty(events);
        Assert.All(events.Where(value => value.Reason == HandshakeReason.HardLimit && value.OutgoingAccountId is not null), value => Assert.Equal(target.ToString("D"), value.OutgoingAccountId));
        Assert.Equal(target, rig.Thread.ProviderAccountID);
        Assert.Equal(target, api.Configuration.AccountId);
        Assert.Equal(rig.App.Providers.Environment(target), api.Configuration.Environment);
        Assert.Empty(http.Received);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
