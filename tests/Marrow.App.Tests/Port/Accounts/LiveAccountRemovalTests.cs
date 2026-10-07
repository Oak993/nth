using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountRemovalTests
{
    [Fact]
    public void Removing_a_bound_source_account_cancels_its_route_without_breaking_the_app_polling_queue()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        Guid source = default;
        rig.H.Main.OnMain(() =>
        {
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(source);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { ProviderAccountID = source });
        });
        rig.Install(rig.Session);
        rig.Send("Preserve the unavailable account identity.");
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.App.Settings.Accounts.Remove(source));
        rig.H.Main.Loop.RunUntilIdle();
        var error = Record.Exception(() => rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound));
        Assert.Null(error);
        Assert.Null(rig.App.AccountRouting.State(rig.InitialThread.Id));
        Assert.Equal(source, rig.Thread.ProviderAccountID);
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromMinutes(4)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(0, rig.Session.StopCalls);
    }
}
