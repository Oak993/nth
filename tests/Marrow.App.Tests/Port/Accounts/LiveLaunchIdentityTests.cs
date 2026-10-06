using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveLaunchIdentityTests
{
    [Fact]
    public void A_new_thread_starting_on_a_reordered_account_freezes_that_actual_launch_identity_for_limits()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid selected = default;
        var events = new List<HandshakeEvent>();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-legacy-key";
            var original = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var second = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-selected-key", second));
            selected = second.Id;
            rig.App.Settings.Accounts.Reorder(ProviderKind.Deepseek, [second.Id, original.Id]);
            rig.App.AccountRouting.Changed += (_, value) => events.Add(value);
        });
        var candidate = new ThreadRuntimeScriptedSession();
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
            new(registry.PrepareSession(configuration, id, useAccountDefaults: false), candidate));
        rig.Send("Start on row one.");
        Assert.Equal(selected, rig.Thread.ProviderAccountID);
        Assert.Equal(new UsageAccountKey(ProviderKind.Deepseek, selected.ToString("D")), rig.Runtime.UsageAccount);
        rig.H.Main.OnMain(() => candidate.Emit(new ProviderEvent.UsageLimit(null)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(selected.ToString("D"), Assert.Single(events, value => value.Kind == HandshakeEventKind.DecisionRequested).OutgoingAccountId);
        Assert.Empty(http.Received);
    }
}
