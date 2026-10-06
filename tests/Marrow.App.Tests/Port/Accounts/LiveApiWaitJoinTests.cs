using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveApiWaitJoinTests
{
    [Theory]
    [InlineData(402)]
    [InlineData(429)]
    public void A_real_API_wait_answer_applies_once_and_resumes_the_same_account_and_turn_after_its_own_recovery(int status)
    {
        using var http = new InProcessHttpHandler();
        var calls = 0;
        http.Answer = _ => ++calls == 1
            ? InProcessHttpHandler.Reply(status, "{\"error\":{\"message\":\"limited\"}}", "application/json", ("Retry-After", "180"))
            : InProcessHttpHandler.Reply(200, "data: {\"choices\":[{\"delta\":{\"content\":\"Recovered on the original account\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream");
        using var transport = URLSession.Transport.Override(http);
        var recovered = false;
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(key.IsDefault && recovered
            ? new() { Windows = ValueList<PlanLimits.Window>.Empty, AccountCredits = new() { Total = 10, Currency = "USD", IsUsable = true } } : null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid source = default;
        var asks = 0;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            Assert.True(APIKeychain.SetKey("offline-next", rig.App.Settings.Accounts.Add(ProviderKind.Deepseek)));
            rig.App.AccountRouting.Changed += (_, value) => { if (value.Request is not null) asks++; };
        });
        var send = rig.StartTurn("Wait on this account and continue this reply.");
        rig.H.Main.Loop.RunUntil(() => rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request is not null || send.IsCompleted, AppModelHarness.Bound);
        Assert.NotNull(rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
            [new(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.JustThisTime), AppModelHarness.Bound);
        rig.Finish(send);
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(HandshakePhase.Waiting, rig.App.AccountRouting.State(rig.InitialThread.Id)?.To);
        Assert.Equal(1, asks);
        Assert.Single(http.Received);
        Assert.Null(rig.App.Settings.AccountDecisions.GetThread(rig.InitialThread.Id, DecisionKind.OnLimit));
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        recovered = true;
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromMinutes(4)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.Loop.RunUntil(() => http.Received.Count == 2 && rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        Assert.Equal(source, rig.Thread.ProviderAccountID);
        Assert.All(http.Received, request => Assert.Equal("Bearer offline-source", request.Header("Authorization")));
        Assert.Single(rig.Runtime.Turns);
        Assert.Equal(TurnStatus.Completed, rig.Runtime.Turns[0].Status);
        Assert.Equal(1, asks);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }

    [Fact]
    public void A_real_API_refusal_with_no_same_service_key_left_asks_before_sending_the_conversation_elsewhere()
    {
        using var http = new InProcessHttpHandler();
        http.Answer = _ => InProcessHttpHandler.Reply(402, "{\"error\":{\"message\":\"no credits\"}}");
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid source = default, alternative = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            var otherKey = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-capped", otherKey));
            rig.App.Settings.AccountKeys.SetCap(otherKey.Id, 0);
            rig.App.Settings.MetaAPIKeyInput = "offline-other-service";
            alternative = rig.App.Settings.Accounts.ForService(ProviderKind.Meta)[0].Id;
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.Bring, DecisionValues.MarrowSummary);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.MidReply, DecisionValues.Finish);
        });
        rig.Send("Ask before sharing the recorded conversation elsewhere.");
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var request = rig.H.Main.OnMain(() => rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request);
        Assert.NotNull(request);
        Assert.Equal([DecisionKind.OtherService], request.Kinds);
        Assert.Equal(source.ToString("D"), request.OutgoingAccountId);
        Assert.Equal(alternative.ToString("D"), request.IncomingAccountId);
        Assert.Equal("Bearer offline-source", Assert.Single(http.Received).Header("Authorization"));
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OtherService));
        Assert.Equal(ProviderKind.Deepseek, rig.Thread.Provider);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
