using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountPollingTests
{
    [Fact]
    public void Connecting_a_second_account_after_a_single_account_turn_starts_the_polling_clock_live()
    {
        var reads = new List<UsageAccountKey>();
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => { reads.Add(key); return Task.FromResult<PlanLimits?>(null); });
        using var rig = new ThreadRuntimeTurnRig();
        var events = new List<HandshakeEvent>();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.AccountRouting.Changed += (_, value) => events.Add(value);
        });
        rig.Send("Keep using this account.");
        rig.H.Main.OnMain(() => rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Contains(reads, key => key.IsDefault && key.Provider == ProviderKind.Claude);
        var firstReads = reads.Count;
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromMinutes(4)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.True(reads.Count > firstReads);
        Assert.Empty(events);
        Assert.False(rig.App.Settings.AccountHandshakeEnabled);
    }

    [Fact]
    public void Closing_the_last_runtime_stops_all_account_reads_and_clock_beats()
    {
        var reads = new List<UsageAccountKey>();
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => { reads.Add(key); return Task.FromResult<PlanLimits?>(null); });
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
        });
        rig.Send("Finish before closing.");
        rig.Complete();
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var before = reads.Count;
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromMinutes(10)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(before, reads.Count);
    }
}
