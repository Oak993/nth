using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountAnswerIsolationTests
{
    [Fact]
    public void A_rejected_card_answer_does_not_block_the_next_valid_answer_or_runtime_cleanup()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
        });
        rig.Send("Keep the card usable.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(null)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var wrong = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
            [new(DecisionKind.EarlierAccount, DecisionValues.GoBack)], RememberScope.JustThisTime));
        Assert.Throws<ArgumentException>(() => rig.H.Main.Loop.Run(() => wrong, AppModelHarness.Bound));
        var valid = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
            [new(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.JustThisTime));
        rig.H.Main.Loop.Run(() => valid, AppModelHarness.Bound);
        Assert.Equal(HandshakePhase.Waiting, rig.App.AccountRouting.State(rig.InitialThread.Id)!.To);
        rig.H.Main.OnMain(() => rig.Runtime.CancelPersistence());
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Null(rig.App.AccountRouting.State(rig.InitialThread.Id));
        Assert.Equal(0, rig.Session.StopCalls);
    }
}
