using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountSlowQuotaTests
{
    [Fact]
    public void A_reported_hard_limit_asks_before_an_unfinished_background_quota_read_returns()
    {
        var report = new TaskCompletionSource<PlanLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => report.Task);
        using var rig = new ThreadRuntimeTurnRig();
        var events = new List<HandshakeEvent>();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            rig.App.AccountRouting.Changed += (_, value) => events.Add(value);
        });
        rig.Send("Do not hide the card behind a usage request.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(null)));
        rig.H.Main.Loop.RunUntilIdle();
        try
        {
            Assert.Single(events, value => value.Kind == HandshakeEventKind.DecisionRequested);
            Assert.False(report.Task.IsCompleted);
            Assert.Null(rig.App.AutoContinue.LimitedAccount(rig.InitialThread.Id));
        }
        finally
        {
            report.SetResult(null);
            rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        }
    }
}
