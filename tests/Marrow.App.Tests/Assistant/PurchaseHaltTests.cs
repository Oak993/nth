using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-20's security review (DESIGN.md rev 5, 12.6 "What Stop all does", 12.7 "Limits", "The model never sees the card", "The
// fill is bound to the merchant"; ED-NEW-dextra-as20-14 to -18), over fakes. A payment under way is stopped by Stop all, the switch,
// a removed card or a lockout before it places anything, and only once it has stopped is it handed to the owner, so he is never
// told to pay while nth may still be paying; one already being placed goes on and is never handed over. The limits are read again
// with the intent row under one lock, so two cards paid together never pass a limit between them. Paid or not, the card comes off
// the page before the agents go; a field that will not empty closes the page, and when that fails the agents stay held. A page
// whose total cannot be read, or is in another currency, fills nothing. The card's Checkout line shows no reordering character.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-16")]
public sealed class PurchaseHaltTests
{
    private static Guid Card(PurchaseRig rig, string arguments, AssistantChat? chat = null) =>
        Guid.Parse(rig.Desk.Request(rig.Call(arguments, chat)).DecisionId!);

    [Fact]
    public async Task Turning_purchases_off_during_the_fill_places_nothing_and_hands_over_only_once_the_payment_stopped()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var card = Card(rig, PurchaseRig.Arguments());
        (DecisionState State, int Waiting)? atTurnOff = null;
        page.Filling = field =>
        {
            if (field != "number") return;
            Assert.Equal(PurchaseWords.NowOff, rig.Desk.TurnOff());
            atTurnOff = (rig.Queue.Find(card)!.State, rig.Queue.Waiting.Count);
        };
        Assert.Null(await rig.Desk.Pay(card));

        Assert.Equal((DecisionState.Open, 1), atTurnOff);                               // while it was filling: still the pay card, no hand-over
        Assert.DoesNotContain(page.Events, e => e.StartsWith("submit", StringComparison.Ordinal));
        Assert.DoesNotContain("fill exp (held)", page.Events);                          // nothing more typed after the switch went off
        Assert.All(page.Filled.Values, v => Assert.Equal("", v));
        Assert.False(page.AgentsHeld);
        Assert.Empty(rig.Desk.State.History);
        Assert.Equal((DecisionState.Withdrawn, PurchaseWords.TurnedOff), (rig.Queue.Find(card)!.State, rig.Queue.Find(card)!.Note));
        var handed = Assert.Single(rig.Queue.Waiting);                                  // exactly one hand-over: the owner pays once
        Assert.Equal((PurchaseWords.TurnedOff, PurchaseWords.HandOverRule), (handed.Why, handed.Rule));
        Assert.Single(rig.Delivered, d => d.Text == PurchaseWords.ToAgentHandOver(handed.Id, PurchaseWords.TurnedOff));
    }

    [Fact]
    public async Task Removing_the_card_during_the_fill_or_a_lockout_at_the_payments_own_check_places_nothing_and_hands_over_once()
    {
        using (var rig = await new PurchaseRig().On())
        {
            var page = rig.SameOrigin();
            var card = Card(rig, PurchaseRig.Arguments());
            Task<CardVaultResult>? removal = null;
            page.Filling = field =>
            {
                if (field == "exp") removal = rig.Desk.RemoveCard();
            };
            Assert.Null(await rig.Desk.Pay(card));
            Assert.Equal(CardVaultOutcome.Done, (await removal!).Outcome);
            Assert.DoesNotContain(page.Events, e => e.StartsWith("submit", StringComparison.Ordinal));
            Assert.All(page.Filled.Values, v => Assert.Equal("", v));
            Assert.Empty(rig.Desk.State.History);
            Assert.Equal((DecisionState.Withdrawn, PurchaseWords.HandedNoCard), (rig.Queue.Find(card)!.State, rig.Queue.Find(card)!.Note));
            Assert.Equal(PurchaseWords.HandedNoCard, Assert.Single(rig.Queue.Waiting).Why);
        }

        using var locked = await new PurchaseRig().On();
        locked.SameOrigin();
        var paid = Card(locked, PurchaseRig.Arguments());
        locked.Hello.Then(HelloResult.Failed, HelloResult.Failed, HelloResult.Failed);
        Assert.Equal(PurchaseWords.NotVerified(1, PurchaseWords.NothingChanged), await locked.Desk.SetLimit(PurchaseLimitKind.PerDay, 140m));
        Assert.Equal(PurchaseWords.NotVerified(2, PurchaseWords.NothingChanged), await locked.Desk.SetLimit(PurchaseLimitKind.PerDay, 140m));
        Assert.Null(await locked.Desk.Pay(paid));                                       // the third failure is the payment's own check
        Assert.False(locked.Desk.State.Policy.On);
        Assert.Equal((DecisionState.Withdrawn, PurchaseWords.LockedOut), (locked.Queue.Find(paid)!.State, locked.Queue.Find(paid)!.Note));
        Assert.Equal(PurchaseWords.LockedOut, Assert.Single(locked.Queue.Waiting).Why);
    }

    [Fact]
    public async Task Stop_all_stops_a_payment_mid_fill_takes_the_card_off_the_page_and_keeps_its_card_open_for_a_fresh_pay()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var card = Card(rig, PurchaseRig.Arguments());
        page.Filling = field =>
        {
            if (field == "number") rig.Desk.Stop();
        };
        Assert.Equal(PurchaseWords.StoppedEmptied, await rig.Desk.Pay(card));
        Assert.Equal(["fill number (held)", "empty number (held)", "release", "release"], page.Events.SkipWhile(e => !e.StartsWith("fill", StringComparison.Ordinal)));
        Assert.Equal("", page.Filled["number"]);
        Assert.Empty(rig.Desk.State.History);
        Assert.Equal((DecisionState.Open, PurchaseWords.StoppedEmptied), (rig.Queue.Find(card)!.State, rig.Queue.Find(card)!.Note));
        Assert.DoesNotContain(rig.Delivered, d => d.Text.Contains("yours to make", StringComparison.Ordinal));

        page.Filling = null;
        Assert.Null(await rig.Desk.Pay(card));                                          // the stop was for that run only
        Assert.Equal(PurchaseRowState.Paid, Assert.Single(rig.Desk.State.History).State);
    }

    [Fact]
    public async Task A_payment_already_being_placed_goes_on_when_purchases_are_turned_off_or_stop_is_pressed_and_is_never_handed_over()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var card = Card(rig, PurchaseRig.Arguments());
        string? said = null;
        page.Submitting = () =>
        {
            rig.Desk.Stop();
            said = rig.Desk.TurnOff();
        };
        Assert.Null(await rig.Desk.Pay(card));
        Assert.Equal(PurchaseWords.OffWhilePlacing, said);
        Assert.Equal(PurchaseRowState.Paid, Assert.Single(rig.Desk.State.History).State);
        Assert.Equal(DecisionState.Answered, rig.Queue.Find(card)!.State);
        Assert.Empty(rig.Queue.Waiting);                                                // no hand-over card: the owner is never told to pay it again
        Assert.All(page.Filled.Values, v => Assert.Equal("", v));
    }

    [Fact]
    public async Task Two_cards_paid_together_never_pass_a_limit_between_them()
    {
        using var rig = await new PurchaseRig().On();
        rig.Spent(120m, PurchaseRig.Start.AddHours(-1));                               // $150 a day, $120 spent: room for one $25 purchase
        var other = Guid.NewGuid();
        var first = rig.SameOrigin(25m);
        var second = rig.SameOrigin(25m, other);
        var a = Card(rig, PurchaseRig.Arguments(25m));
        var b = Card(rig, PurchaseRig.Arguments(25m), rig.Chat(thread: other));
        Assert.Equal(2, rig.Queue.Waiting.Count(w => w.Weight == DecisionWeight.Irreversible));   // each passed the limits alone
        Task<string?>? payingB = null;
        first.Filling = field =>
        {
            if (field == "number") payingB = rig.Desk.Pay(b);                           // B is paid while A types the card
        };
        Assert.Null(await rig.Desk.Pay(a));
        Assert.Null(await payingB!);

        var row = Assert.Single(rig.Desk.State.History, r => r.Amount == 25m);
        Assert.Equal(PurchaseRowState.Paid, row.State);
        Assert.Equal(145m, rig.Book.Spend(rig.Clock.GetUtcNow(), TimeZoneInfo.Utc).Today);
        Assert.Contains("submit (held)", second.Events);
        Assert.DoesNotContain(first.Events, e => e.StartsWith("submit", StringComparison.Ordinal));
        Assert.All(first.Filled.Values, v => Assert.Equal("", v));
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(a)!.State);
        Assert.Equal("Over your $150.00 limit per day, so the payment is yours to make.", Assert.Single(rig.Queue.Waiting).Why);
    }

    [Fact]
    public async Task A_field_that_will_not_empty_after_the_order_closes_the_page_and_when_it_will_not_close_the_agents_stay_held()
    {
        using (var rig = await new PurchaseRig().On())
        {
            var page = rig.SameOrigin();
            page.StickyFields.Add("number");                                             // a masked input that puts the number back
            Assert.Null(await rig.Desk.Pay(Card(rig, PurchaseRig.Arguments())));
            Assert.True(page.Discarded);
            Assert.False(page.AgentsHeld);
            Assert.Equal(PurchaseRowState.Paid, Assert.Single(rig.Desk.State.History).State);
            var discard = page.Events.IndexOf("discard (held)");
            Assert.True(discard > page.Events.IndexOf("submit (held)"));
            Assert.Equal("release", page.Events[^1]);
        }

        using var stuck = await new PurchaseRig().On();
        var held = stuck.SameOrigin();
        held.StickyFields.Add("number");
        held.DiscardFails = true;
        Assert.Null(await stuck.Desk.Pay(Card(stuck, PurchaseRig.Arguments())));
        Assert.True(held.AgentsHeld);                                                   // never let go: no agent reads the number left in the field
        Assert.Equal("discard (held)", held.Events[^1]);
        Assert.Contains(PurchaseWords.KeptHeld, stuck.Faults);
        Assert.DoesNotContain(stuck.EveryText(), t => t.Contains(PurchaseRig.Number, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_checkout_whose_total_cannot_be_read_or_is_in_another_currency_is_handed_over_before_windows_hello()
    {
        using var rig = await new PurchaseRig().On();
        foreach (var (shown, currency, words) in new (decimal?, string?, string)[]
        {
            (null, "USD", "nth could not read the total on shop.localhost's checkout."),
            (46.80m, null, "nth could not read the total on shop.localhost's checkout."),
            (46.80m, "GBP", "The checkout on shop.localhost charges in GBP, not USD."),
        })
        {
            var page = rig.SameOrigin();
            page.Page = page.Page with { ShownTotal = shown, ShownCurrency = currency };
            var card = Card(rig, PurchaseRig.Arguments());
            var asked = rig.Hello.Asked;
            Assert.Null(await rig.Desk.Pay(card));
            Assert.Equal(asked, rig.Hello.Asked);
            Assert.Empty(page.Filled);
            Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(card)!.State);
            var handed = Assert.Single(rig.Queue.Waiting);
            Assert.StartsWith(words, handed.Why, StringComparison.Ordinal);
            Assert.Equal(PurchaseWords.HandOverRule, handed.Rule);
            Assert.Null(rig.Queue.Answer(handed.Id, DecisionAnswer.Pick(PurchaseWords.DontBuy)));
        }
        Assert.Empty(rig.Desk.State.History);
    }

    [Fact]
    public async Task The_cards_checkout_line_is_the_cleaned_address_with_no_reordering_character()
    {
        using var rig = await new PurchaseRig().On();
        var arguments = $$"""{"shop":"Shop Example","checkout_url":"{{PurchaseRig.Shop}}/‮moc.nozama//:sptth","items":["Tamper, 58 mm: 46.80"],"total":46.80,"currency":"USD","ship_to":"Home","kind":"purchase"}""";
        var card = rig.Queue.Find(Card(rig, arguments))!;
        var line = card.Inputs.Single(i => i.Name == "Checkout").Value;
        Assert.StartsWith(PurchaseRig.Shop + "/", line, StringComparison.Ordinal);
        Assert.All(line, c => Assert.True(c is > ' ' and < (char)0x7F, $"U+{(int)c:X4} in the Checkout line"));
    }
}
