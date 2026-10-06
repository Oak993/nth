using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Providers;
using System.Text;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountPreferenceRecoveryTests
{
    [Fact]
    public void Unreadable_preferences_do_not_break_an_ordinary_turn_or_grant_automatic_switching_permission()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        var saved = Encoding.UTF8.GetBytes("{unreadable");
        using var rig = new ThreadRuntimeTurnRig(configureWithHarness: (h, thread) =>
        {
            h.Defaults.Set(AccountDecisionStore.SettingsKey, saved);
            return thread;
        });
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
        });
        rig.Send("Keep ordinary chat usable.");
        Assert.Single(rig.Session.Inputs);
        Assert.Equal(saved, Assert.IsType<byte[]>(rig.H.Defaults.Object(AccountDecisionStore.SettingsKey)));
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
        Assert.Throws<InvalidOperationException>(() => rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch));
        Assert.Equal(saved, Assert.IsType<byte[]>(rig.H.Defaults.Object(AccountDecisionStore.SettingsKey)));
    }
}
