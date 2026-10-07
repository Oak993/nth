using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakePolicyJoinTests
{
    [Fact]
    public void The_destination_configuration_carries_its_actual_Hydra_policy()
    {
        using var rig = new ThreadRuntimeTurnRig(hydraOn: true, settings: app => app.Settings.HydraEnabled = true);
        rig.H.Main.OnMain(() =>
        {
            var row = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            rig.App.Settings.Accounts.SetDefaults(row.Id, new() { Model = "destination-model", Effort = "high" });
            var target = new SwitchTarget(new(row.Id.ToString("D"), row.Service.Id, "Destination"));
            var destination = rig.App.Thread(rig.InitialThread.Id)! with { Provider = row.Service, ProviderAccountID = row.Id, Model = "destination-model", Effort = "high" };
            var expected = rig.App.HydraLaunch(destination);
            Assert.NotNull(expected);
            var configuration = ThreadRuntimeReflection.Call<SessionConfiguration>(rig.Runtime, "CreateHandshakeConfiguration", target)!;
            Assert.Equal(expected, configuration.Hydra);
        });
    }

    [Fact]
    public void A_Hydra_policy_change_during_final_readback_invalidates_the_prepared_session()
    {
        using var rig = new ThreadRuntimeTurnRig(hydraOn: true, settings: app =>
        {
            app.Settings.HydraEnabled = true;
            app.Settings.DeepseekAPIKeyInput = "offline-destination";
        });
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) => new(configuration, incoming));
            var target = new SwitchTarget(new(destination.Id.ToString("D"), destination.Service.Id, "Destination"));
            incoming.SendScript = input =>
            {
                if (incoming.Inputs.Count == 2) rig.App.Settings.HydraMaxHeads = (rig.App.Settings.HydraMaxHeads ?? 0) + 1;
                incoming.Emit(new ProviderEvent.MessageCompleted("readback", "Ready."));
                incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            await using var standby = await host.StartStandbyAsync(target, CancellationToken.None);
            await standby.ReceiveAsync(new(Bring.MarrowSummary, await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None), null), true, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.SwitchAsync(standby, target, false, CancellationToken.None));
            Assert.Equal(ProviderKind.Claude, rig.App.Thread(rig.InitialThread.Id)!.Provider);
            Assert.Equal(0, rig.Session.StopCalls);
        }, AppModelHarness.Bound);
        Assert.Equal(1, incoming.StopCalls);
    }

    [Fact]
    public void A_private_outgoing_briefing_keeps_the_composers_actual_Hydra_policy()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, hydraOn: true,
            configure: thread => thread with { Model = "offline-composer", Effort = "low" },
            settings: app =>
            {
                app.Settings.HydraEnabled = true;
                app.Settings.DeepseekAPIKeyInput = "offline-source";
                app.Settings.Accounts.SetDefaults(app.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id,
                    new() { Model = "offline-default", Effort = "high" });
            });
        SessionConfiguration? launched = null;
        var incoming = new ThreadRuntimeScriptedSession();
        incoming.SendScript = _ =>
        {
            incoming.Emit(new ProviderEvent.MessageCompleted("brief", "Recorded decisions."));
            incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            return Task.CompletedTask;
        };
        rig.H.Main.Loop.Run(async () =>
        {
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries,
                (configuration, id) => { launched = configuration; return new(configuration, incoming); });
            Assert.Equal("Recorded decisions.", await host.RequestBriefingAsync(new("Recorded facts.", null, []), CancellationToken.None));
            Assert.NotNull(launched);
            Assert.Equal("offline-composer", launched.Model);
            Assert.Equal("low", launched.Effort);
            Assert.Equal(rig.App.HydraLaunch(rig.App.Thread(rig.InitialThread.Id)!), launched.Hydra);
        }, AppModelHarness.Bound);
        Assert.Equal(1, incoming.StopCalls);
    }
}
