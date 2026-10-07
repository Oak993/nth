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
public sealed class LiveAccountColdStartIdentityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Routing_and_the_handshake_host_use_the_same_actual_source_while_a_cold_start_is_pending(bool explicitAccount)
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid original = default;
        Guid selected = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-original";
            original = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            var second = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-selected", second));
            selected = explicitAccount ? second.Id : original;
            if (explicitAccount) rig.App.Settings.Accounts.Reorder(ProviderKind.Deepseek, [selected, original]);
        });
        var start = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var candidate = new ThreadRuntimeScriptedSession { StartScript = () => start.Task };
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
            new(registry.PrepareSession(configuration, id, useAccountDefaults: false), candidate));
        var send = rig.StartTurn("Start with the selected account.");
        rig.H.Main.Loop.RunUntil(() => candidate.StartCalls == 1, AppModelHarness.Bound);
        try
        {
            rig.H.Main.OnMain(() =>
            {
                var other = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek).Single(row => row.Id != selected).Id;
                rig.App.Settings.Accounts.Reorder(ProviderKind.Deepseek, [other, selected]);
                var host = new ThreadAccountHost(rig.App, rig.Runtime, rig.App.Settings.Pills,
                    new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct)));
                Assert.Equal(selected.ToString("D"), host.ActiveAccountId);
                Assert.Equal(explicitAccount ? new UsageAccountKey(ProviderKind.Deepseek, selected.ToString("D"))
                    : UsageAccountKey.Default(ProviderKind.Deepseek), rig.Runtime.UsageAccount);
            });
        }
        finally
        {
            start.SetResult("selected-session");
            rig.Finish(send);
        }
        Assert.Equal(explicitAccount ? selected : (Guid?)null, rig.Thread.ProviderAccountID);
        Assert.Empty(http.Received);
    }
}
