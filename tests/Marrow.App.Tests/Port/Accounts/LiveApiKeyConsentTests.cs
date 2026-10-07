using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveApiKeyConsentTests
{
    [Theory]
    [InlineData(402, RememberScope.JustThisTime)]
    [InlineData(429, RememberScope.JustThisTime)]
    [InlineData(429, RememberScope.ThisThread)]
    [InlineData(429, RememberScope.AllThreads)]
    public void The_live_API_card_awaits_consent_and_continues_the_identical_request_on_the_actual_key(int status, RememberScope scope)
    {
        using var http = new InProcessHttpHandler();
        var calls = 0;
        http.Answer = _ => ++calls == 1
            ? InProcessHttpHandler.Reply(status, "{\"error\":{\"message\":\"nope\"}}", "application/json", ("Retry-After", "0"))
            : InProcessHttpHandler.Reply(200, "data: {\"choices\":[{\"delta\":{\"content\":\"Retained answer\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream");
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid source = default, destination = default;
        var events = new List<HandshakeEvent>();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            destination = next.Id;
            Assert.True(APIKeychain.SetKey("offline-destination", next));
            rig.App.AccountRouting.Changed += (_, value) => events.Add(value);
        });
        Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        var send = rig.StartTurn("Keep this original turn and complete request.");
        rig.H.Main.Loop.RunUntil(() => rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request is not null || send.IsCompleted, AppModelHarness.Bound);
        var request = Assert.Single(events, value => value.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal(source.ToString("D"), request.OutgoingAccountId);
        Assert.Equal(destination.ToString("D"), request.IncomingAccountId);
        Assert.True(request.FirstTime);
        Assert.Equal([DecisionKind.OnLimit], request.Kinds);
        Assert.False(send.IsCompleted);
        Assert.Single(http.Received);
        Assert.Single(rig.Runtime.Turns);
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        var invalid = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
            [new(DecisionKind.OtherService, DecisionValues.Switch)], scope));
        Assert.Throws<ArgumentException>(() => rig.H.Main.Loop.Run(() => invalid, AppModelHarness.Bound));
        Assert.False(send.IsCompleted);
        var answer = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
            [new(DecisionKind.OnLimit, DecisionValues.Switch)], scope));
        rig.H.Main.Loop.Run(() => answer, AppModelHarness.Bound);
        rig.Finish(send);
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        Assert.Equal(2, http.Received.Count);
        Assert.Equal("Bearer offline-source", http.Received[0].Header("Authorization"));
        Assert.Equal("Bearer offline-destination", http.Received[1].Header("Authorization"));
        Assert.Equal(http.Received[0].BodyText, http.Received[1].BodyText);
        Assert.Equal(destination, rig.Thread.ProviderAccountID);
        Assert.Equal(new UsageAccountKey(ProviderKind.Deepseek, destination.ToString("D")), rig.Runtime.UsageAccount);
        Assert.Single(rig.Runtime.Turns);
        Assert.Equal(TurnStatus.Completed, rig.Runtime.Turns[0].Status);
        Assert.True(rig.App.Settings.AccountHandshakeEnabled);
        Assert.Equal(scope == RememberScope.AllThreads ? DecisionValues.Switch : null, rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        Assert.Equal(scope == RememberScope.ThisThread ? DecisionValues.Switch : null, rig.App.Settings.AccountDecisions.GetThread(rig.InitialThread.Id, DecisionKind.OnLimit));
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OtherService));
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }

    [Fact]
    public void Stopping_a_live_API_card_invalidates_its_answer_and_never_spends_on_the_next_key()
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
        var send = rig.StartTurn("Cancel before a second key spends.");
        rig.H.Main.Loop.RunUntil(() => rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request is not null || send.IsCompleted, AppModelHarness.Bound);
        Assert.NotNull(rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request);
        Assert.False(send.IsCompleted);
        rig.H.Main.OnMain(() => rig.Runtime.StopSession());
        rig.Finish(send);
        var late = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
            [new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.AllThreads));
        Assert.Throws<InvalidOperationException>(() => rig.H.Main.Loop.Run(() => late, AppModelHarness.Bound));
        Assert.Single(http.Received);
        Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
