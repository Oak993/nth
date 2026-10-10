using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

// OD-0116, OD-0122, ED-439, research section 3: OpenCode (and OpenCode Go) wait with the context stored by a no-reply message when the running server offers it, else a warm start.
[Trait("Rule", "R-TEST-4")]
public sealed class OpenCodeStandbyHostTests
{
    [Theory]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    public void A_server_that_offers_no_reply_stores_the_context_and_the_ACP_session_loads_it_at_the_switch(ProviderKind provider)
    {
        using var rig = new ThreadRuntimeTurnRig();
        var server = new ThreadRuntimeOpenCodeServer(true);
        var acp = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult("ses_standby") };
        SessionConfiguration? loaded = null;
        using var serverOverride = OpenCodeHistoryInjection.Server.Override((_, _) => Task.FromResult<IOpenCodeServer>(server));
        using var constructor = AccountSessionFactory.Constructor.Override(configuration => { loaded = configuration; return acp; });
        rig.H.Main.Loop.Run(async () =>
        {
            var account = rig.App.Settings.Accounts.ForService(provider)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(account);
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, _) => new(configuration with { Executable = SwiftURL.Create("marrow-native-path:C:/offline/" + provider.Id + "/opencode.exe")! }, new ThreadRuntimeScriptedSession()));
            var target = new SwitchTarget(new(account.ToString("D"), provider.Id, provider.DisplayName));
            Assert.False(await ((IDeferredDeliveryHost)host).ResolveDeliversDeferredAsync(target, CancellationToken.None));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Equal(StandbyKind.Injected, standby.Kind);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            Assert.Single(server.Stored);
            Assert.Equal(0, acp.StartCalls);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        Assert.Equal("ses_standby", loaded!.ResumeID);
        Assert.Equal(("ses_standby", provider), (rig.Thread.ProviderSessionID, rig.Thread.Provider));
        Assert.Equal(2, server.Stops); // the run-time check's short-lived server, then the standby's at the handover
        rig.Send("the next message");
        var turn = Assert.Single(acp.Inputs).Text;
        Assert.EndsWith("the next message", turn, StringComparison.Ordinal);
        Assert.DoesNotContain("[nth Handshake]", turn, StringComparison.Ordinal);
    }

    [Fact]
    public void A_server_without_no_reply_falls_back_to_a_warm_start()
    {
        using var rig = new ThreadRuntimeTurnRig();
        var server = new ThreadRuntimeOpenCodeServer(false);
        var warm = new ThreadRuntimeScriptedSession { StartScript = () => Task.FromResult("warm-opencode") };
        using var serverOverride = OpenCodeHistoryInjection.Server.Override((_, _) => Task.FromResult<IOpenCodeServer>(server));
        rig.H.Main.Loop.Run(async () =>
        {
            var account = rig.App.Settings.Accounts.ForService(ProviderKind.Opencode)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(account);
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, _) => new(configuration with { Executable = SwiftURL.Create("marrow-native-path:C:/offline/fallback/opencode.exe")! }, warm));
            var target = new SwitchTarget(new(account.ToString("D"), ProviderKind.Opencode.Id, "OpenCode"));
            Assert.True(await ((IDeferredDeliveryHost)host).ResolveDeliversDeferredAsync(target, CancellationToken.None));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            Assert.Equal(StandbyKind.Deferred, standby.Kind);
            Assert.Equal(1, warm.StartCalls);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), false, CancellationToken.None);
            await host.SwitchAsync(standby, target, false, CancellationToken.None);
        }, AppModelHarness.Bound);
        Assert.Empty(server.Stored);
        rig.Send("next");
        Assert.Contains("[nth Handshake]", Assert.Single(warm.Inputs).Text, StringComparison.Ordinal);
    }
}
