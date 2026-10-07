using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountAvailabilityTests
{
    [Fact]
    public void A_pill_limit_skips_the_next_step_when_its_actual_API_key_is_capped()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek);
        var incoming = new ThreadRuntimeScriptedSession();
        incoming.SendScript = input =>
        {
            incoming.Emit(new ProviderEvent.MessageCompleted("readback", "Ready."));
            incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            return Task.CompletedTask;
        };
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) => new(configuration, incoming));
        Guid available = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var capped = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            available = next.Id;
            Assert.True(APIKeychain.SetKey("offline-capped", capped));
            Assert.True(APIKeychain.SetKey("offline-available", next));
            LiveApiKeyLaunch.Keys(rig).SetCap(capped.Id, 0);
            var pill = rig.App.Settings.Pills.Create("Skip cap", [new(source.Id.ToString("D")), new(capped.Id.ToString("D")), new(next.Id.ToString("D"))]);
            rig.App.UpdateThread(rig.InitialThread.Id, thread => (thread with { ProviderAccountID = source.Id }).WithPillRelay(new(pill.Id, 0, source.Id.ToString("D"))));
        });
        rig.Install(rig.Session);
        rig.Send("Continue this saved pill order.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddHours(1))));
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(available, rig.Thread.ProviderAccountID);
        Assert.Equal(2, rig.Thread.PillStepIndex);
    }

    [Fact]
    public void A_destination_key_capped_during_final_readback_cannot_be_adopted_or_stop_the_original_session()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        var incoming = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var destination = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            rig.App.Settings.DeepseekAPIKeyInput = "offline-destination";
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, new(), summaries, (configuration, id) => new(configuration, incoming));
            var target = new SwitchTarget(new(destination.Id.ToString("D"), destination.Service.Id, "Destination"));
            incoming.SendScript = input =>
            {
                if (incoming.Inputs.Count == 2) LiveApiKeyLaunch.Keys(rig).SetCap(destination.Id, 0);
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
}
