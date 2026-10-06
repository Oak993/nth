using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class PrivateContextTransferConsentTests
{
    [Theory]
    [InlineData("permission")]
    [InlineData("defaults")]
    [InlineData("pin")]
    public void The_real_host_rechecks_context_permission_before_a_prepared_account_receives_the_archive(string change)
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.MetaAPIKeyInput = "offline-private-destination");
        var privateSession = new ThreadRuntimeScriptedSession();
        privateSession.SendScript = _ =>
        {
            privateSession.Emit(new ProviderEvent.MessageCompleted("read", "Ready."));
            privateSession.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            return Task.CompletedTask;
        };
        rig.H.Main.Loop.Run(async () =>
        {
            var allowed = true;
            var account = rig.App.Settings.Accounts.ForService(ProviderKind.Meta)[0];
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, rig.App.Settings.Pills, summaries, (configuration, _) => new(configuration, privateSession))
                { CanCommit = () => allowed };
            var target = new SwitchTarget(new(account.Id.ToString("D"), account.Service.Id, "Next"));
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            var summary = await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None);
            if (change == "permission") allowed = false;
            if (change == "defaults") rig.App.Settings.Accounts.SetDefaults(account.Id, new() { Model = "changed-destination" });
            if (change == "pin") rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderAccountPinID = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id });
            await Assert.ThrowsAsync<InvalidOperationException>(() => standby.ReceiveAsync(new(Bring.MarrowSummary, summary, null), true, CancellationToken.None));
            Assert.Empty(privateSession.Inputs);
            Assert.Equal(0, rig.Session.StopCalls);
            Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        }, AppModelHarness.Bound);
        Assert.Equal(1, privateSession.StopCalls);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
