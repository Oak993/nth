using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountReportingBoundaryTests
{
    [Fact]
    public void An_old_timer_callback_entering_after_the_last_runtime_closes_cannot_restart_polling()
    {
        var reads = new List<UsageAccountKey>();
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => { reads.Add(key); return Task.FromResult<PlanLimits?>(null); });
        using var rig = new ThreadRuntimeTurnRig();
        var clock = new RoutingCallbackClock(rig.H.Main.Time);
        using var scope = SwiftClock.Provider.Override(clock);
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
        });
        rig.Send("Close before an old callback enters.");
        rig.Complete();
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var callback = Assert.Single(clock.RepeatingCallbacks);
        var before = reads.Count;
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            rig.H.Main.Time.Advance(TimeSpan.FromMinutes(10));
            callback();
        });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(before, reads.Count);
    }

    [Fact]
    public void A_plan_change_already_queued_when_the_last_runtime_closes_cannot_restart_polling()
    {
        var reads = new List<UsageAccountKey>();
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => { reads.Add(key); return Task.FromResult<PlanLimits?>(null); });
        using var rig = new ThreadRuntimeTurnRig();
        Guid source = default;
        rig.H.Main.OnMain(() =>
        {
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(source);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
        });
        rig.Send("Close before queued bookkeeping runs.");
        rig.Complete();
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var before = reads.Count;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.SetPlan(source, "Max 20x");
            rig.Runtime.CancelPersistence();
        });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromMinutes(10)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(before, reads.Count);
    }

    [Fact]
    public void A_deferred_plan_poll_cannot_start_after_the_last_runtime_closes()
    {
        var reads = new List<UsageAccountKey>();
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => { reads.Add(key); return Task.FromResult<PlanLimits?>(null); });
        using var rig = new ThreadRuntimeTurnRig();
        Guid source = default;
        rig.H.Main.OnMain(() =>
        {
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(source);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
        });
        rig.Send("Finish before the pending refresh.");
        rig.Complete();
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.App.Settings.Accounts.SetPlan(source, "Max 20x"));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var before = reads.Count;
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromMinutes(10)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(before, reads.Count);
    }

    [Fact]
    public void A_positive_balance_read_started_before_a_real_refusal_cannot_revive_that_key_by_completing_later()
    {
        var pending = new TaskCompletionSource<PlanLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var began = false;
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) =>
        {
            if (key.IsDefault) { began = true; return pending.Task; }
            return Task.FromResult<PlanLimits?>(new() { Windows = ValueList<PlanLimits.Window>.Empty, AccountCredits = new() { Total = 9, Currency = "USD", IsUsable = true } });
        });
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        Guid target = default;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var next = rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-next", next));
            rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        });
        var api = LiveApiKeyLaunch.Ensure(rig);
        rig.H.Main.Loop.RunUntil(() => began, AppModelHarness.Bound);
        rig.H.Main.OnMain(() =>
        {
            Assert.NotNull(api.KeyRotation!.Current());
            rig.H.Main.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(target.ToString("D"), api.KeyRotation.OnFailure(402, null)!.Id);
            rig.H.Main.Time.Advance(TimeSpan.FromSeconds(1));
            pending.SetResult(new() { Windows = ValueList<PlanLimits.Window>.Empty, AccountCredits = new() { Total = 20, Currency = "USD", IsUsable = true } });
        });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() => Assert.Equal(target.ToString("D"), api.KeyRotation!.Current()!.Id));
    }
}
