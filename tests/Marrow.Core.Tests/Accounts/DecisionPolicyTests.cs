using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "Remember scope", "Rule for every choice": Settings starts unset; unset asks the first time; thread choice, then Settings, then ask; one Remember line per card.
public sealed class DecisionPolicyTests
{
    private static readonly Guid Thread = new("00000000-0000-0000-0000-0000000000a5");
    private static readonly Guid Other = new("00000000-0000-0000-0000-0000000000b6");

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T17")]
    [Trait("Rule", "R-TEST-4")]
    public void Nothing_chosen_asks_and_is_the_first_time()
    {
        var policy = new DecisionPolicy(new InMemoryDecisionStore());
        var r = policy.Resolve(Thread, DecisionKind.OnLimit);
        Assert.True(r.MustAsk);
        Assert.True(r.FirstTime);
        Assert.Equal(ResolutionSource.None, r.Source);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T18")]
    [Trait("Rule", "R-TEST-4")]
    public void Ask_me_in_Settings_asks_each_time_but_is_not_the_first_time()
    {
        var store = new InMemoryDecisionStore();
        store.SetSetting(DecisionKind.OnLimit, DecisionValues.Ask);
        var r = new DecisionPolicy(store).Resolve(Thread, DecisionKind.OnLimit);
        Assert.True(r.MustAsk);
        Assert.False(r.FirstTime);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T19")]
    [Trait("Rule", "R-TEST-4")]
    public void A_Settings_value_never_asks()
    {
        var store = new InMemoryDecisionStore();
        store.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        var r = new DecisionPolicy(store).Resolve(Thread, DecisionKind.OnLimit);
        Assert.Equal(DecisionValues.Switch, r.Value);
        Assert.Equal(ResolutionSource.Settings, r.Source);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T20")]
    [Trait("Rule", "R-TEST-4")]
    public void The_thread_choice_beats_the_pills_value_which_beats_Settings()
    {
        var store = new InMemoryDecisionStore();
        var policy = new DecisionPolicy(store);
        store.SetSetting(DecisionKind.EarlierAccount, DecisionValues.Stay);
        Assert.Equal(ResolutionSource.Settings, policy.Resolve(Thread, DecisionKind.EarlierAccount).Source);
        var viaPill = policy.Resolve(Thread, DecisionKind.EarlierAccount, DecisionValues.GoBack);
        Assert.Equal((DecisionValues.GoBack, ResolutionSource.Pill), (viaPill.Value, viaPill.Source));
        store.SetThread(Thread, DecisionKind.EarlierAccount, DecisionValues.Ask);
        Assert.True(policy.Resolve(Thread, DecisionKind.EarlierAccount, DecisionValues.GoBack).MustAsk);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T21")]
    [Trait("Rule", "R-TEST-4")]
    public void Just_this_time_stores_nothing()
    {
        var store = new InMemoryDecisionStore();
        new DecisionPolicy(store).Record(Thread, [new Choice(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime);
        Assert.Null(store.GetSetting(DecisionKind.OnLimit));
        Assert.Null(store.GetThread(Thread, DecisionKind.OnLimit));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T22")]
    [Trait("Rule", "R-TEST-4")]
    public void This_thread_is_kept_on_that_thread_only()
    {
        var store = new InMemoryDecisionStore();
        var policy = new DecisionPolicy(store);
        policy.Record(Thread, [new Choice(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.ThisThread);
        Assert.Equal(DecisionValues.Wait, policy.Resolve(Thread, DecisionKind.OnLimit).Value);
        Assert.True(policy.Resolve(Other, DecisionKind.OnLimit).MustAsk);
        Assert.Null(store.GetSetting(DecisionKind.OnLimit));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T23")]
    [Trait("Rule", "R-TEST-4")]
    public void All_threads_writes_the_Settings_value_and_one_card_applies_one_scope_to_every_choice()
    {
        var store = new InMemoryDecisionStore();
        var policy = new DecisionPolicy(store);
        policy.Record(Thread, [new Choice(DecisionKind.OnLimit, DecisionValues.Switch), new Choice(DecisionKind.Bring, DecisionValues.Full)], RememberScope.AllThreads);
        Assert.Equal(DecisionValues.Switch, store.GetSetting(DecisionKind.OnLimit));
        Assert.Equal(DecisionValues.Full, store.GetSetting(DecisionKind.Bring));
        Assert.Equal(DecisionValues.Full, policy.Resolve(Other, DecisionKind.Bring).Value);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T24")]
    [Trait("Rule", "R-TEST-4")]
    public void A_value_the_kind_does_not_offer_is_refused()
    {
        var policy = new DecisionPolicy(new InMemoryDecisionStore());
        Assert.Throws<ArgumentException>(() => policy.Record(Thread, [new Choice(DecisionKind.MidReply, DecisionValues.Ask)], RememberScope.AllThreads));
        Assert.Throws<ArgumentException>(() => policy.Record(Thread, [new Choice(DecisionKind.Bring, DecisionValues.Switch)], RememberScope.ThisThread));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T25")]
    [Trait("Rule", "R-TEST-4")]
    public void Use_Settings_clears_the_thread_choices()
    {
        var store = new InMemoryDecisionStore();
        var policy = new DecisionPolicy(store);
        store.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        policy.Record(Thread, [new Choice(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.ThisThread);
        store.ClearThread(Thread);
        Assert.Equal(DecisionValues.Switch, policy.Resolve(Thread, DecisionKind.OnLimit).Value);
    }
}
