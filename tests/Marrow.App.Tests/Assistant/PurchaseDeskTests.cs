using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "The switch", "The card", "Limits", "Windows Hello"; ED-NEW-dextra-as20-10): the desk's
// settings over fakes. The switch ships off and turns on only with a fill path, a card and Windows Hello; saving, replacing and
// removing the card and changing a limit each ask Windows Hello, and turning off never does; a limit nth does not take is refused
// without asking; three failed checks in a row turn purchases off until the owner turns them on again.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-10")]
public sealed class PurchaseDeskTests
{
    [Fact]
    public async Task The_switch_ships_off_and_turns_on_only_with_a_fill_path_a_card_and_windows_hello()
    {
        using (var handOver = new PurchaseRig(canPay: false))
        {
            Assert.False(handOver.Desk.State.Policy.On);
            Assert.Equal(PurchaseWords.NeedsFill, await handOver.Desk.TurnOn());
            Assert.Equal(0, handOver.Hello.Asked);
        }

        using var rig = new PurchaseRig();
        Assert.False(rig.Desk.State.Policy.On);
        Assert.Equal(PurchaseWords.NeedsCard, await rig.Desk.TurnOn());
        Assert.Equal(0, rig.Hello.Asked);                                         // no card: Windows Hello is not even asked

        Assert.Equal(CardVaultOutcome.Done, (await rig.AddCard()).Outcome);
        rig.Hello.Then(HelloResult.Cancelled);
        Assert.Equal(PurchaseWords.NotVerified(1, PurchaseWords.NothingChanged), await rig.Desk.TurnOn());
        Assert.False(rig.Desk.State.Policy.On);

        Assert.Null(await rig.Desk.TurnOn());
        Assert.True(rig.Desk.State.Policy.On);
        Assert.Equal(["save the card", "turn purchases on", "turn purchases on"], rig.Hello.Reasons);
        Assert.True(rig.Saved!["on"]!.GetValue<bool>());
        Assert.Contains(rig.Audit, e => e.Tool == PurchaseDesk.Tool && e.Result == "purchases turned on after Windows Hello");
    }

    [Fact]
    public async Task Saving_replacing_and_removing_the_card_and_changing_a_limit_each_ask_windows_hello_and_turning_off_never_does()
    {
        using var rig = await new PurchaseRig().On();
        Assert.Equal(CardVaultOutcome.Done, (await rig.AddCard("4242424242424242")).Outcome);
        Assert.Equal("4242", rig.Desk.State.LastFour);

        rig.Hello.Then(HelloResult.Failed);
        Assert.Equal(PurchaseWords.NotVerified(1, PurchaseWords.NothingChanged), await rig.Desk.SetLimit(PurchaseLimitKind.PerDay, 100m));
        Assert.Equal(150m, rig.Desk.State.Policy.Limits.PerDay);                      // the old limit holds until Windows Hello says yes
        Assert.Null(await rig.Desk.SetLimit(PurchaseLimitKind.PerDay, 100m));
        Assert.Equal(100m, rig.Desk.State.Policy.Limits.PerDay);
        Assert.Null(await rig.Desk.SetLimit(PurchaseLimitKind.PerAstur, 20m, "store-2"));
        Assert.Equal(20m, rig.Desk.State.Policy.Limits.AsturLimit("store-2"));
        var asked = rig.Hello.Asked;
        Assert.Null(await rig.Desk.SetLimit(PurchaseLimitKind.PerDay, 100m));          // the same limit again changes nothing and asks nothing
        Assert.Equal(asked, rig.Hello.Asked);

        Assert.Equal(PurchaseWords.NowOff, rig.Desk.TurnOff());
        Assert.False(rig.Desk.State.Policy.On);
        Assert.Equal(asked, rig.Hello.Asked);                                          // turning off never asks

        Assert.Equal(CardVaultOutcome.Done, (await rig.Desk.RemoveCard()).Outcome);
        Assert.Equal(CardVaultState.NoCard, rig.Desk.State.Card);
        Assert.Equal(["save the card", "turn purchases on", "replace the card", "change a purchase limit", "change a purchase limit", "change a purchase limit", "remove the card"],
            rig.Hello.Reasons);
        Assert.Equal(PurchaseWords.CardRemoved, rig.Desk.State.Status);
    }

    [Fact]
    public async Task A_limit_nth_does_not_take_is_refused_without_asking()
    {
        using var rig = await new PurchaseRig().On();
        var asked = rig.Hello.Asked;
        Assert.Equal("A limit cannot be below 0.", await rig.Desk.SetLimit(PurchaseLimitKind.PerPurchase, -1m));
        Assert.Equal("A limit can be at most 100000.", await rig.Desk.SetLimit(PurchaseLimitKind.PerMonth, 100_001m));
        Assert.Equal("A limit is in whole cents.", await rig.Desk.SetLimit(PurchaseLimitKind.PerDay, 1.005m));
        Assert.Equal(asked, rig.Hello.Asked);
        Assert.Equal(PurchaseLimits.Default, rig.Desk.State.Policy.Limits);
    }

    [Fact]
    public async Task Three_failed_checks_in_a_row_turn_purchases_off_until_the_owner_turns_them_on_again()
    {
        using var rig = await new PurchaseRig().On();
        rig.SameOrigin();
        var waiting = rig.Desk.Request(rig.Call(PurchaseRig.Arguments()));
        var card = Guid.Parse(waiting.DecisionId!);

        rig.Hello.Then(HelloResult.Failed, HelloResult.Cancelled);
        Assert.Equal(PurchaseWords.NotVerified(1, PurchaseWords.NothingChanged), await rig.Desk.SetLimit(PurchaseLimitKind.PerDay, 90m));
        Assert.Equal(PurchaseWords.NotVerified(2, PurchaseWords.NothingFilled), await rig.Desk.Pay(card));
        rig.Hello.Then(HelloResult.Failed);
        Assert.Equal(PurchaseWords.LockedOut, await rig.Desk.SetLimit(PurchaseLimitKind.PerDay, 90m));

        Assert.False(rig.Desk.State.Policy.On);
        Assert.False(rig.Saved!["on"]!.GetValue<bool>());
        Assert.Equal(PurchaseWords.LockedOut, rig.Desk.State.Status);
        var withdrawn = rig.Queue.Find(card)!;
        Assert.Equal((DecisionState.Withdrawn, PurchaseWords.LockedOut), (withdrawn.State, withdrawn.Note));
        var handed = Assert.Single(rig.Queue.Waiting);
        Assert.Equal((PurchaseWords.HandOverRule, PurchaseWords.LockedOut), (handed.Rule, handed.Why));
        Assert.Contains(rig.Audit, e => e.Result == "purchases turned off: three failed Windows Hello checks");

        Assert.Null(await rig.Desk.TurnOn());                                          // on again only by the owner, with Windows Hello
        Assert.True(rig.Desk.State.Policy.On);
        Assert.Equal(0, rig.Desk.Guard.Failures);
    }

    [Fact]
    public async Task A_check_that_cannot_ask_or_is_busy_counts_as_no_failure_and_says_so()
    {
        using var rig = await new PurchaseRig().On();
        rig.Hello.Then(HelloResult.Unavailable);
        Assert.Equal(PurchaseWords.HelloUnavailable, await rig.Desk.SetLimit(PurchaseLimitKind.PerDay, 90m));
        Assert.Equal(0, rig.Desk.Guard.Failures);
        rig.Hello.Hold();
        var first = rig.Desk.SetLimit(PurchaseLimitKind.PerDay, 90m);
        Assert.Equal(PurchaseWords.HelloBusy, await rig.Desk.SetLimit(PurchaseLimitKind.PerMonth, 200m));
        Assert.Equal("change a purchase limit", rig.Desk.State.Asking);                 // the section says what Windows is asking
        rig.Hello.Release(HelloResult.Verified);
        Assert.Null(await first);
        Assert.Null(rig.Desk.State.Asking);
        Assert.Equal((90m, 300m), (rig.Desk.State.Policy.Limits.PerDay, rig.Desk.State.Policy.Limits.PerMonth));
    }

    [Fact]
    public async Task Turning_off_never_fails_the_owner_and_says_when_it_could_not_be_saved()
    {
        var failing = false;
        using var rig = new PurchaseRig(save: _ =>
        {
            if (failing) throw new UnauthorizedAccessException();
        });
        await rig.On();
        failing = true;
        Assert.Equal(PurchaseWords.OffNotSaved, rig.Desk.TurnOff());
        Assert.False(rig.Desk.State.Policy.On);
        Assert.Contains(rig.Audit, e => e.Result == "purchases turned off; not saved: UnauthorizedAccessException");
    }
}
