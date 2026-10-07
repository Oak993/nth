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
public sealed class LiveAccountFinalConsentTests
{
    [Fact]
    public void A_choice_revoked_during_final_readback_is_checked_again_before_the_atomic_account_commit()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek);
        const string request = "Preserve every instruction and final permission.";
        Guid target = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-target", next));
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.Bring, DecisionValues.MarrowSummary);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.MidReply, DecisionValues.Finish);
        });
        var finalRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var incoming = new ThreadRuntimeScriptedSession();
        incoming.SendScript = async input =>
        {
            if (incoming.Inputs.Count == 2) await finalRead.Task;
            incoming.Emit(new ProviderEvent.MessageCompleted("read", request));
            incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        };
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
            new(registry.PrepareSession(configuration, id, useAccountDefaults: false), incoming));
        rig.Send(request);
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddHours(1))));
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.Loop.RunUntil(() => incoming.Inputs.Count == 2, AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait));
        finalRead.SetResult();
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.NotEqual(target, rig.Thread.ProviderAccountID);
        Assert.Equal(0, rig.Session.StopCalls);
        Assert.True(incoming.StopCalls > 0);
        Assert.Empty(http.Received);
    }
}
