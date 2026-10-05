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
public sealed class LiveAccountPreemptionTests
{
    [Fact]
    public void A_hard_limit_cancels_an_unfinished_briefing_and_asks_without_waiting_for_its_model_reply()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(new()
        {
            Windows = new([new() { Id = "reported", Title = "Reported", Percent = key.IsDefault ? 98 : 10 }]),
        }));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek);
        var briefing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new List<ThreadRuntimeScriptedSession>();
        var events = new List<HandshakeEvent>();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            Assert.True(APIKeychain.SetKey("offline-target", rig.App.Settings.Accounts.Add(ProviderKind.Deepseek)));
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.Bring, DecisionValues.MarrowSummary);
            rig.App.Settings.AccountHandshakeEnabled = true;
            rig.App.AccountRouting.Changed += (_, value) => events.Add(value);
        });
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            var candidate = new ThreadRuntimeScriptedSession();
            candidate.SendScript = async input =>
            {
                await briefing.Task;
                candidate.Emit(new ProviderEvent.MessageCompleted("brief", "The unfinished original task."));
                candidate.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            };
            sessions.Add(candidate);
            return new(registry.PrepareSession(configuration, id, useAccountDefaults: false), candidate);
        });
        rig.Send("The unfinished original task.");
        rig.H.Main.Loop.RunUntil(() => sessions.Count == 2 && sessions[1].Inputs.Count == 1, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Ask);
            rig.Session.Emit(new ProviderEvent.UsageLimit(null));
        });
        try
        {
            rig.H.Main.Loop.RunUntil(() => events.Any(value => value.Kind == HandshakeEventKind.DecisionRequested), TimeSpan.FromSeconds(2));
            Assert.False(briefing.Task.IsCompleted);
            Assert.Equal(0, rig.Session.StopCalls);
            Assert.DoesNotContain(events, value => value.To == HandshakePhase.Switch);
        }
        finally
        {
            briefing.SetResult();
            rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        }
        Assert.Empty(http.Received);
    }
}
