using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Port.MultiAccount;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveApiKeyConsentRevocationTests
{
    [Fact]
    public async Task A_recovered_source_cannot_carry_a_one_time_API_move_into_a_later_request_that_must_wait()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid source = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            Assert.True(APIKeychain.SetKey("offline-target", rig.App.Settings.Accounts.Add(ProviderKind.Deepseek)));
            LiveApiKeyLaunch.Keys(rig).SetCap(source, 0);
        });
        var api = LiveApiKeyLaunch.Ensure(rig);
        var rotation = Assert.IsAssignableFrom<Marrow.Core.Accounts.Keys.IAsyncApiKeyRotation>(api.KeyRotation);
        Task<Marrow.Core.Accounts.Keys.KeyChoice?> selection = null!;
        rig.H.Main.OnMain(() => { selection = rotation.CurrentAsync(CancellationToken.None); });
        Assert.NotNull(rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request);
        Task answer = null!;
        rig.H.Main.OnMain(() =>
        {
            answer = rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id, [new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime);
            LiveApiKeyLaunch.Keys(rig).SetCap(source, null);
        });
        rig.H.Main.Loop.Run(() => answer, AppModelHarness.Bound);
        rig.H.Main.Loop.Run(() => selection, AppModelHarness.Bound);
        Assert.Equal(source.ToString("D"), (await selection)!.Id);
        rig.H.Main.Loop.Run(async () =>
        {
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait);
            Assert.Null(await rotation.OnFailureAsync(429, null, CancellationToken.None));
            Assert.Equal(source, rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
        }, AppModelHarness.Bound);
        Assert.Empty(http.Received);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }

    [Fact]
    public void A_newer_wait_choice_revokes_a_queued_one_time_API_move()
    {
        using var http = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(402, "{\"error\":{\"message\":\"no credits\"}}") };
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        var send = AwaitCard(rig);
        Task answer = null!;
        rig.H.Main.OnMain(() =>
        {
            answer = rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id, [new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime);
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait);
        });
        rig.H.Main.Loop.Run(() => answer, AppModelHarness.Bound);
        rig.Finish(send);
        Assert.Single(http.Received);
        Assert.Equal(DecisionValues.Wait, rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }

    [Theory]
    [InlineData(RememberScope.ThisThread)]
    [InlineData(RememberScope.AllThreads)]
    public void A_synchronous_remember_observer_can_cancel_the_card_without_enabling_handshaking_or_spending(RememberScope scope)
    {
        using var http = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(402, "{\"error\":{\"message\":\"no credits\"}}") };
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        var send = AwaitCard(rig);
        Action cancel = () => rig.Runtime.StopSession();
        rig.H.Main.OnMain(() => rig.App.Settings.AccountDecisions.Changed += cancel);
        var answer = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id, [new(DecisionKind.OnLimit, DecisionValues.Switch)], scope));
        var error = Record.Exception(() => rig.H.Main.Loop.Run(() => answer, AppModelHarness.Bound));
        Assert.True(error is null or OperationCanceledException, error?.ToString());
        rig.Finish(send);
        Assert.Single(http.Received);
        Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        Assert.Null(rig.App.AccountRouting.State(rig.InitialThread.Id));
        rig.H.Main.OnMain(() => { rig.App.Settings.AccountDecisions.Changed -= cancel; rig.Runtime.CancelPersistence(); });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }

    [Fact]
    public void Adopting_a_private_API_session_retires_its_private_rotation_and_keeps_the_live_account_binding()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        rig.H.Main.Loop.Run(async () =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var account = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Assert.True(APIKeychain.SetKey("offline-target", account));
            var source = rig.App.Thread(rig.InitialThread.Id)!;
            var configuration = new SessionConfiguration
            {
                Provider = account.Service, AccountId = account.Id, Model = source.Model,
                WorkingDirectory = SwiftURL.Create("marrow-native-path:" + rig.Directory.Replace('\\', '/'))!, Environment = ValueMap<string, string>.Empty,
                RuntimeMode = source.RuntimeMode, InteractionMode = source.InteractionMode,
                AntiSlopEnabled = rig.App.Settings.AntiSlopEnabled,
            };
            var launch = AccountSessionFactory.Create(rig.App.Providers, configuration, account.Id, freshContext: true);
            var api = Assert.IsAssignableFrom<OpenAICompatibleSession>(launch.Session);
            var manager = (AppApiKeyRouting)rig.App.GetType().GetProperty("ApiKeys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(rig.App)!;
            manager.BindPrivate(api, account.Id);
            var privateRotation = api.KeyRotation!;
            var sessionId = await api.Start();
            Assert.Equal(account.Id.ToString("D"), privateRotation.Current()!.Id);
            rig.Runtime.AdoptHandshakeSession(api, sessionId, launch.Configuration);
            Assert.NotSame(privateRotation, api.KeyRotation);
            Assert.Throws<ObjectDisposedException>(() => privateRotation.Current());
            Assert.Equal(account.Id.ToString("D"), api.KeyRotation!.Current()!.Id);
            Assert.Equal(account.Id, rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
        }, AppModelHarness.Bound);
        Assert.Empty(http.Received);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }

    private static Task AwaitCard(ThreadRuntimeTurnRig rig)
    {
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            Assert.True(APIKeychain.SetKey("offline-target", rig.App.Settings.Accounts.Add(ProviderKind.Deepseek)));
        });
        var send = rig.StartTurn("Keep the pending request owned by its original conversation.");
        rig.H.Main.Loop.RunUntil(() => rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request is not null || send.IsCompleted, AppModelHarness.Bound);
        Assert.NotNull(rig.App.AccountRouting.State(rig.InitialThread.Id)?.Request);
        Assert.False(send.IsCompleted);
        return send;
    }
}
