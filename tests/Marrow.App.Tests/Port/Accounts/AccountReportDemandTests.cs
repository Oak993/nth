using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class AccountReportDemandTests
{
    [Fact]
    public void Visible_account_views_share_live_reports_until_the_last_view_closes()
    {
        using var rig = new AccountReportDemandRig();
        var first = rig.Observe();
        var second = rig.Observe();
        rig.Drain();
        rig.Percent = 55;
        rig.Advance();
        Assert.Equal(55, rig.H.Main.OnMain(() => rig.App.AccountRouting.Snapshot(rig.Source)!.Limits!.Windows[0].Percent));
        Assert.Equal(55, rig.H.Main.OnMain(() => rig.App.AccountRouting.Snapshot(rig.Next)!.Limits!.Windows[0].Percent));
        rig.H.Main.OnMain(first.Dispose);
        rig.Percent = 75;
        rig.Advance();
        Assert.Equal(75, rig.H.Main.OnMain(() => rig.App.AccountRouting.Snapshot(rig.Next)!.Limits!.Windows[0].Percent));
        var callback = Assert.Single(rig.Clock.RepeatingCallbacks);
        rig.H.Main.OnMain(() => { second.Dispose(); second.Dispose(); });
        rig.Drain();
        var reads = rig.Reads.Count;
        rig.Percent = 95;
        rig.H.Main.OnMain(() => callback());
        rig.Advance();
        Assert.Equal(reads, rig.Reads.Count);
        Assert.False(rig.H.Main.OnMain(() => rig.App.Settings.AccountHandshakeEnabled));
    }

    [Fact]
    public void Reopening_a_report_view_resumes_polling_and_keeps_the_one_minute_provider_throttle()
    {
        using var rig = new AccountReportDemandRig();
        var first = rig.Observe();
        rig.Drain();
        var initial = rig.Reads.Count;
        rig.H.Main.OnMain(first.Dispose);
        var next = rig.Observe();
        rig.Drain();
        Assert.Equal(initial, rig.Reads.Count);
        rig.Percent = 65;
        // Check the actual minimum boundary. Four minutes can include two valid reads (at 1 and 4)
        // when the provider jitter is zero; that does not violate the one-minute throttle.
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(Marrow.Services.Port.Usage.AccountLimitsScheduler.MinimumInterval));
        rig.Drain();
        Assert.Equal(65, rig.H.Main.OnMain(() => rig.App.AccountRouting.Snapshot(rig.Next)!.Limits!.Windows[0].Percent));
        Assert.Equal(2, rig.Reads.Count(key => key == new UsageAccountKey(ProviderKind.Claude, rig.Next.ToString("D"))));
        rig.H.Main.OnMain(next.Dispose);
        rig.Drain();
    }
}
