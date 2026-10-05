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
public sealed class LiveAccountExitedWaitTests
{
    [Fact]
    public void A_chosen_wait_reopens_an_exited_provider_on_the_same_account_and_continues_the_original_turn()
    {
        using var http = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(http);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek,
            configure: thread => thread with { ProviderSessionID = "original-session" });
        Guid original = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            original = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            Assert.True(APIKeychain.SetKey("offline-second", rig.App.Settings.Accounts.Add(ProviderKind.Deepseek)));
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Wait);
            rig.App.Settings.AutoContinueAfterLimit = false;
        });
        var restarted = new ThreadRuntimeScriptedSession();
        var launches = new List<SessionConfiguration>();
        using var factory = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            Assert.Equal(original, id);
            Assert.False(fresh);
            launches.Add(configuration);
            return new(registry.PrepareSession(configuration, id, useAccountDefaults: false), restarted);
        });
        rig.Send("Preserve this request and its prefix.");
        var turn = Assert.Single(rig.Runtime.Turns).Id;
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.MessageCompleted("original", "Prefix. "));
            rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddSeconds(30)));
        });
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.OnMain(() =>
        {
            rig.Session.IsRunning = false;
            rig.Session.Emit(new ProviderEvent.Exited(null));
        });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromSeconds(31)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.Loop.RunUntilIdle();
        Assert.Equal("original-session", Assert.Single(launches).ResumeID);
        Assert.Equal(1, restarted.StartCalls);
        Assert.Single(restarted.Inputs);
        Assert.Equal(turn, Assert.Single(rig.Runtime.Turns).Id);
        Assert.Single(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.User);
        Assert.Null(rig.Thread.ProviderAccountID);
        Assert.Empty(http.Received);
    }
}
