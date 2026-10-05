using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountBootstrapTests
{
    [Fact]
    public void Bootstrap_reads_each_connected_account_before_any_thread_runtime_is_opened()
    {
        var reads = new List<UsageAccountKey>();
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) =>
        {
            reads.Add(key);
            return Task.FromResult<PlanLimits?>(new()
            {
                Windows = new([new() { Id = "five", Title = "5-hour limit", DurationMinutes = 300, Percent = key.IsDefault ? 20 : 70 }]),
            });
        });
        using var h = new AppModelHarness();
        var app = h.MakeModel();
        Guid source = default, next = default;
        h.Main.OnMain(() =>
        {
            source = app.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id;
            app.Settings.Accounts.MarkSignedIn(source);
            next = app.Settings.Accounts.Add(ProviderKind.Claude).Id;
            app.Settings.Accounts.MarkSignedIn(next);
        });
        h.Bootstrap(app);
        h.Main.Loop.Run(() => app.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Contains(UsageAccountKey.Default(ProviderKind.Claude), reads);
        Assert.Contains(new UsageAccountKey(ProviderKind.Claude, next.ToString("D")), reads);
        Assert.Equal(20, h.Main.OnMain(() => app.AccountRouting.Snapshot(source)!.Limits!.Windows[0].Percent));
        Assert.Equal(70, h.Main.OnMain(() => app.AccountRouting.Snapshot(next)!.Limits!.Windows[0].Percent));
        Assert.False(h.Main.OnMain(() => app.Settings.AccountHandshakeEnabled));
    }

    [Fact]
    public void Timer_reports_notify_the_live_app_view_without_a_thread_or_settings_mutation()
    {
        var percent = 20d;
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(new()
        {
            Windows = new([new() { Id = "five", Title = "5-hour limit", DurationMinutes = 300, Percent = percent }]),
        }));
        using var rig = new ThreadRuntimeTurnRig();
        Guid source = default;
        rig.H.Main.OnMain(() =>
        {
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(source);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
        });
        rig.Send("Watch the live report.");
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var notifications = 0;
        rig.H.Main.OnMain(() => rig.App.PropertyChanged += (_, value) =>
        {
            if (value.PropertyName == "AccountReportsRevision")
            {
                MainActor.AssumeIsolated();
                notifications++;
            }
        });
        percent = 55;
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromMinutes(4)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(55, rig.H.Main.OnMain(() => rig.App.AccountRouting.Snapshot(source)!.Limits!.Windows[0].Percent));
        Assert.True(notifications > 0);
        rig.Complete();
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
