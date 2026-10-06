using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveAccountWaitTests
{
    [Fact]
    public void A_user_chosen_wait_resumes_the_original_reply_at_its_reported_reset_without_a_second_user_bubble()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            rig.App.Settings.AutoContinueAfterLimit = false;
        });
        rig.Send("Carry on from this exact request.");
        var turn = Assert.Single(rig.Runtime.Turns).Id;
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddSeconds(30))));
        rig.Complete(TurnStatus.Failed);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        var answer = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
            [new(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.JustThisTime));
        rig.H.Main.Loop.Run(() => answer, AppModelHarness.Bound);
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromSeconds(31)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        rig.H.Main.Loop.RunUntilIdle();
        Assert.Equal(2, rig.Session.Inputs.Count);
        Assert.Equal(turn, Assert.Single(rig.Runtime.Turns).Id);
        Assert.Equal(TurnStatus.Running, rig.Runtime.Turns[0].Status);
        Assert.Single(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.User);
        Assert.Equal(0, rig.Session.StopCalls);
        Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
    }

    [Fact]
    public void Repeated_waits_on_the_same_session_keep_one_original_bubble_without_repeating_any_prefix()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            rig.App.Settings.AutoContinueAfterLimit = false;
        });
        rig.Send("Finish all three segments.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.MessageCompleted("original", "First. ")));
        for (var wait = 0; wait < 2; wait++)
        {
            rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.UsageLimit(rig.H.Main.Time.GetUtcNow().AddSeconds(30))));
            rig.Complete(TurnStatus.Failed);
            rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
            var answer = rig.H.Main.OnMain(() => rig.App.AccountRouting.AnswerAsync(rig.InitialThread.Id,
                [new(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.JustThisTime));
            rig.H.Main.Loop.Run(() => answer, AppModelHarness.Bound);
            rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromSeconds(31)));
            rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
            rig.H.Main.Loop.RunUntilIdle();
            rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.MessageCompleted("segment-" + wait, wait == 0 ? "Second. " : "Third.")));
        }
        rig.Complete();
        var answerItem = Assert.Single(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Assistant);
        Assert.Equal("original", answerItem.Id);
        Assert.Equal("First. Second. Third.", Assert.IsType<TimelineItem.Content.Assistant>(answerItem.Item.TimelineItemContent).Value.Text);
        Assert.Single(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.User);
        Assert.Single(rig.Runtime.Turns);
    }
}
