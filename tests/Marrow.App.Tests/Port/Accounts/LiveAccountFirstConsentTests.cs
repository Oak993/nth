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
public sealed class LiveAccountFirstConsentTests
{
    [Fact]
    public void A_slow_first_time_answer_does_not_overwrite_the_users_later_handshake_setting()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek);
        const string request = "Preserve the latest user preference.";
        Guid target = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-target", next));
        });
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var incoming = new ThreadRuntimeScriptedSession();
        incoming.SendScript = async input =>
        {
            if (incoming.Inputs.Count == 1) await read.Task;
            incoming.Emit(new ProviderEvent.MessageCompleted("read", request));
            incoming.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        };
        var briefing = new ThreadRuntimeScriptedSession();
        briefing.SendScript = input =>
        {
            briefing.Emit(new ProviderEvent.MessageCompleted("brief", request));
            briefing.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            return Task.CompletedTask;
        };
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
            new(registry.PrepareSession(configuration, id, useAccountDefaults: false), id == target ? incoming : briefing));
        rig.Send(request);
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddHours(1))));
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var answer = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
            [new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime));
        try
        {
            rig.H.Main.Loop.RunUntil(() => incoming.Inputs.Count == 1, AppModelHarness.Bound);
            rig.H.Main.OnMain(() =>
            {
                rig.App.Settings.AccountHandshakeEnabled = true;
                rig.App.Settings.AccountHandshakeEnabled = false;
            });
        }
        finally
        {
            read.SetResult();
            rig.H.Main.Loop.Run(() => answer, AppModelHarness.Bound);
            rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        }
        Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        Assert.False(Assert.IsType<bool>(rig.H.Defaults.Object("multiAccountHandshake")));
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        Assert.Empty(http.Received);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_first_time_answer_enables_handshaking_only_when_the_user_chooses_to_keep_going(bool keepGoing)
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
        });
        Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        Assert.Null(rig.H.Defaults.Object("multiAccountHandshake"));
        rig.Send("Ask before enabling the automatic preparation.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddHours(1))));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var answer = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
            [new(DecisionKind.OnLimit, keepGoing ? DecisionValues.Switch : DecisionValues.Wait)], RememberScope.JustThisTime));
        rig.H.Main.Loop.Run(() => answer, AppModelHarness.Bound);
        Assert.Equal(keepGoing, rig.App.Settings.AccountHandshakeEnabled);
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OtherService));
        Assert.Equal(0, rig.Session.StopCalls);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
