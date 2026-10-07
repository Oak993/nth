using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAskEachTimeTests
{
    [Theory]
    [InlineData(RememberScope.JustThisTime)]
    [InlineData(RememberScope.ThisThread)]
    [InlineData(RememberScope.AllThreads)]
    public void The_API_first_time_Ask_choice_keeps_the_request_pending_without_spending_or_enabling_switching(RememberScope scope)
    {
        using var http = new InProcessHttpHandler();
        http.Answer = _ => InProcessHttpHandler.Reply(402, "{\"error\":{\"message\":\"no credits\"}}");
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            Assert.True(APIKeychain.SetKey("offline-target", rig.App.Settings.Accounts.Add(ProviderKind.Deepseek)));
        });
        var send = rig.StartTurn("Ask before any additional account spends.");
        try
        {
            rig.H.Main.Loop.RunUntil(() => rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request is not null || send.IsCompleted, AppModelHarness.Bound);
            var choice = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
                [new(DecisionKind.OnLimit, DecisionValues.Ask)], scope));
            rig.H.Main.Loop.Run(() => choice, AppModelHarness.Bound);
            Assert.False(rig.App.AccountRouting.State(rig.InitialThread.Id)!.Request!.FirstTime);
            Assert.False(send.IsCompleted);
            Assert.Single(http.Received);
            Assert.False(rig.App.Settings.AccountHandshakeEnabled);
            Assert.Equal(scope == RememberScope.ThisThread ? DecisionValues.Ask : null, rig.App.Settings.AccountDecisions.GetThread(rig.InitialThread.Id, DecisionKind.OnLimit));
            Assert.Equal(scope == RememberScope.AllThreads ? DecisionValues.Ask : null, rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.Runtime.StopSession());
            rig.Finish(send);
            rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
            rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        }
    }
}
