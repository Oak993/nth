using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountRoutingTests
{
    [Fact]
    public void An_ordinary_single_accounts_unknown_limit_reads_its_own_shared_report_before_waiting()
    {
        var reads = new List<UsageAccountKey>();
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) =>
        {
            reads.Add(key);
            return Task.FromResult<PlanLimits?>(new()
            {
                Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = 100, ResetsAt = Marrow.Core.Port.SwiftDate.Now.AddSeconds(30), DurationMinutes = 300 }]),
            });
        });
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.AutoContinueAfterLimit = true;
        });
        rig.Send("Keep the ordinary single-account wait.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(null)));
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.Loop.RunUntilIdle();
        Assert.Equal(UsageAccountKey.Default(ProviderKind.Claude), Assert.Single(reads));
        Assert.True(rig.App.AutoContinue.ResumesAt.ContainsKey(rig.InitialThread.Id));
        Assert.Null(rig.App.AccountRouting.State(rig.InitialThread.Id));
        rig.H.Main.OnMain(() => rig.App.AutoContinue.Cancel(rig.InitialThread.Id));
    }

    [Fact]
    public void A_real_multi_account_limit_signal_has_one_owner_and_requests_the_first_time_card_before_any_switch()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        var events = new List<HandshakeEvent>();
        Guid source = default;
        rig.H.Main.OnMain(() =>
        {
            var first = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0];
            source = first.Id;
            rig.App.Settings.Accounts.MarkSignedIn(first.Id);
            var second = rig.App.Settings.Accounts.Add(ProviderKind.Claude);
            rig.App.Settings.Accounts.MarkSignedIn(second.Id);
            rig.App.AccountRouting.Changed += (_, value) => events.Add(value);
        });
        rig.Send("Preserve this exact interrupted reply.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddHours(1))));
        Assert.Null(rig.App.AutoContinue.LimitedAccount(rig.InitialThread.Id));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var card = Assert.Single(events, value => value.Kind == HandshakeEventKind.DecisionRequested).Request!;
        Assert.Equal(source.ToString("D"), card.OutgoingAccountId);
        Assert.True(card.FirstTime);
        Assert.Contains(DecisionKind.OnLimit, card.Kinds);
        Assert.DoesNotContain(events, value => value.To == HandshakePhase.Switch);
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        Assert.Equal(0, rig.Session.StopCalls);
    }

    [Fact]
    public void An_ordinary_single_account_limit_stays_with_the_existing_auto_continue_owner()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        var events = new List<HandshakeEvent>();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.AccountRouting.Changed += (_, value) => events.Add(value);
        });
        rig.Send("The ordinary path.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddHours(1))));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(UsageAccountKey.Default(ProviderKind.Claude), rig.App.AutoContinue.LimitedAccount(rig.InitialThread.Id));
        Assert.Empty(events);
        Assert.Equal(0, rig.Session.StopCalls);
    }
}
