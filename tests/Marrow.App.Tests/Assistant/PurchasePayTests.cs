using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "The confirmation card", "Limits", "History", "Never allowed"; lean-tests 10 "Retryable side
// effects"; ED-NEW-dextra-as20-11): prepare_purchase to the money, over fakes. A pay card is bound to its shop, items, total,
// card and what is left of the limits, and expires in 10 minutes; a changed amount raises a new card; Windows Hello comes after
// Pay and before the fill, and a cancelled check fills nothing; the intent is written before the order and a crash after it
// leaves an "unknown" row that is never paid again; limits per purchase, day, month and Astur hand the payment over, and an
// Astur at $0 always does; subscriptions, transfers and money to people are refused; the model reads only {card}.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-11")]
public sealed class PurchasePayTests
{
    [Fact]
    public async Task A_purchase_within_the_limits_raises_a_pay_card_bound_to_its_inputs_and_the_model_reads_only_the_placeholder()
    {
        using var rig = await new PurchaseRig().On();
        var result = rig.Desk.Request(rig.Call(PurchaseRig.Arguments()));
        Assert.False(result.IsError);
        var card = rig.Queue.Find(Guid.Parse(result.DecisionId!))!;
        Assert.Equal((DecisionKind.Purchase, DecisionWeight.Irreversible, ActionClass.Money), (card.Kind, card.Weight, card.Class));
        Assert.Equal("Buy from shop.localhost?", card.Title);
        Assert.Equal(PurchaseWords.Rule, card.Rule);
        Assert.Equal(PurchaseWords.Bound, card.Why);
        Assert.Equal([
            ("Shop", "Shop Example · shop.localhost"), ("Items", "Tamper, 58 mm  $46.80"), ("Shipping", "$0.00"), ("Tax", "$0.00"), ("Total", "$46.80"),
            ("Ship to", "Home"), ("Checkout", PurchaseRig.Shop + "/checkout"), ("Card", "•••• 5556"),
            ("Left after", "$103.20 of $150.00 today · $253.20 of $300.00 this month")], card.Inputs.Select(i => (i.Name, i.Value)));
        Assert.Equal([DecisionAnswerKind.DoIt, DecisionAnswerKind.Dont], card.Answers);
        Assert.Equal(("Pay $46.80", "Don't buy"), (DecisionWords.Label(DecisionAnswerKind.DoIt, card), DecisionWords.Label(DecisionAnswerKind.Dont, card)));
        Assert.Equal(PurchaseWords.ToAgentWaiting(card.Id), result.Text);
        Assert.Contains(PurchaseWords.Placeholder, result.Text, StringComparison.Ordinal);

        // The same request again is the same card; the queue never settles Pay by itself.
        Assert.Equal(card.Id.ToString("D"), rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId);
        Assert.Equal(PurchaseWords.PayOnCard, rig.Queue.Answer(card.Id, DecisionAnswer.DoIt));
        Assert.Equal(DecisionState.Open, rig.Queue.Find(card.Id)!.State);
    }

    [Fact]
    public async Task A_changed_amount_raises_a_new_card_and_the_old_one_is_withdrawn()
    {
        using var rig = await new PurchaseRig().On();
        var first = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments(46.80m))).DecisionId!);
        var second = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments(48.10m))).DecisionId!);
        Assert.NotEqual(first, second);
        Assert.Equal((DecisionState.Withdrawn, PurchaseWords.Changed), (rig.Queue.Find(first)!.State, rig.Queue.Find(first)!.Note));
        Assert.Equal("Pay $48.10", DecisionWords.Label(DecisionAnswerKind.DoIt, rig.Queue.Find(second)!));
        Assert.Equal(PurchaseWords.NoSuchCard, await rig.Desk.Pay(first));
    }

    [Fact]
    public async Task Windows_hello_comes_after_pay_and_before_the_fill_and_a_cancelled_check_fills_nothing()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        var asked = rig.Hello.Asked;
        Assert.Equal(asked, rig.Hello.Asked);                                           // raising the card asks nothing

        rig.Hello.Hold();
        var paying = rig.Desk.Pay(card);
        Assert.False(paying.IsCompleted);
        Assert.Equal(asked + 1, rig.Hello.Asked);
        Assert.Equal("pay $46.80 to shop.localhost", rig.Hello.Reasons[^1]);
        Assert.Equal(["snapshot", "hold"], page.Events);                                // the boundary read and the agents held: nothing is filled before Windows Hello
        Assert.Equal(PurchaseWords.Asking("pay $46.80 to shop.localhost"), rig.Queue.Find(card)!.Note);
        Assert.Equal(PurchaseWords.Underway, rig.Queue.Answer(card, DecisionAnswer.Dont()));   // nothing answers a card while Windows asks
        Assert.Equal(PurchaseWords.Underway, await rig.Desk.Pay(card));
        rig.Hello.Release(HelloResult.Cancelled);
        Assert.Equal(PurchaseWords.NotVerified(1, PurchaseWords.NothingFilled), await paying);
        Assert.Empty(page.Filled);
        Assert.Equal(["snapshot", "hold", "release"], page.Events);
        Assert.Equal((DecisionState.Open, PurchaseWords.NotVerified(1, PurchaseWords.NothingFilled)), (rig.Queue.Find(card)!.State, rig.Queue.Find(card)!.Note));
        Assert.Empty(rig.Desk.State.History);
    }

    [Fact]
    public async Task Pay_fills_the_shops_own_fields_with_the_agents_held_writes_the_intent_before_the_order_and_records_how_it_ended()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        string? atOrder = null;
        var loggedFirst = false;
        page.Submitting = () =>
        {
            atOrder = rig.Saved!["history"]![0]!["state"]!.GetValue<string>();
            loggedFirst = rig.Audit.Any(e => e.Result!.StartsWith("paying: the owner's Pay, after Windows Hello", StringComparison.Ordinal));
        };
        Assert.Null(await rig.Desk.Pay(card));

        Assert.Equal(["snapshot", "hold", "hold", "snapshot", "fill number (held)", "fill exp (held)", "fill csc (held)", "release", "submit (held)",
            "empty number (held)", "empty exp (held)", "empty csc (held)", "release"], page.Events);   // paid, and the card is off the page before the agents go
        Assert.Equal((PurchaseRig.Number, "12/30", PurchaseRig.Code), (page.Took["number"], page.Took["exp"], page.Took["csc"]));
        Assert.All(page.Filled.Values, v => Assert.Equal("", v));
        Assert.False(page.AgentsHeld);
        Assert.All(page.Handed, m => Assert.All(m.Span.ToArray(), c => Assert.Equal('\0', c)));   // what was handed to the page is zero now
        Assert.Equal("paying", atOrder);                                                // the intent was on disk when the order was placed
        Assert.True(loggedFirst);                                                       // and the owner's Pay was logged before it
        var paid = rig.Queue.Find(card)!;
        Assert.Equal((DecisionState.Answered, DecisionAnswerKind.DoIt, "Paid $46.80 at shop.localhost after Windows Hello"), (paid.State, paid.Answer!.Kind, paid.Note));
        var row = Assert.Single(rig.Desk.State.History);
        Assert.Equal(("shop.localhost", 46.80m, rig.Mission, PurchaseRowState.Paid), (row.Shop, row.Amount, row.Mission, row.State));
        Assert.Equal((rig.Thread, PurchaseWords.ToAgentPaid(new PurchaseRequest("Shop Example", PurchaseRig.Shop, PurchaseRig.Shop + "/checkout", new([new("Tamper, 58 mm", 46.80m)]), 0m, 0m, 46.80m, "USD", "Home"))),
            rig.Delivered[^1]);
        Assert.Equal(PurchaseWords.NoSuchCard, await rig.Desk.Pay(card));             // a paid card is never paid again
    }

    [Fact]
    public async Task A_crash_between_the_order_and_its_outcome_leaves_an_unknown_row_that_counts_and_the_card_is_never_paid_again()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        System.Text.Json.Nodes.JsonObject? atOrder = null;
        page.Submitting = () => atOrder = (System.Text.Json.Nodes.JsonObject)rig.Saved!.DeepClone();
        page.SubmitThrows = new IOException("the shop's page went away");
        Assert.Null(await rig.Desk.Pay(card));

        var closed = rig.Queue.Find(card)!;
        Assert.Equal((DecisionState.Answered, PurchaseWords.CheckWithShop), (closed.State, closed.Note));
        Assert.Equal(PurchaseRowState.Unknown, Assert.Single(rig.Desk.State.History).State);
        Assert.All(page.Filled.Values, v => Assert.Equal("", v));                       // the card was taken off the page
        Assert.Contains(rig.Delivered, d => d.Text == PurchaseWords.ToAgentUnknown(new PurchaseRequest("Shop Example", PurchaseRig.Shop, PurchaseRig.Shop + "/checkout", new([new("Tamper, 58 mm", 46.80m)]), 0m, 0m, 46.80m, "USD", "Home")));
        Assert.Equal(PurchaseWords.NoSuchCard, await rig.Desk.Pay(card));

        // The process ending at the order: the next start reads the intent as unknown, and it counts against the limits.
        var again = new PurchaseBook(() => atOrder, _ => { });
        Assert.Equal(PurchaseRowState.Unknown, Assert.Single(again.History).State);
        Assert.Equal(46.80m, again.Spend(PurchaseRig.Start, TimeZoneInfo.Utc).Today);
    }

    [Fact]
    public async Task A_log_line_or_an_intent_that_cannot_be_written_pays_nothing_and_takes_the_card_off_the_page()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        rig.FailAudit = e => e.Result!.StartsWith("paying", StringComparison.Ordinal);
        Assert.Equal(PurchaseWords.LogNotWritten, await rig.Desk.Pay(card));
        Assert.DoesNotContain(page.Events, e => e.StartsWith("submit", StringComparison.Ordinal));
        Assert.All(page.Filled.Values, v => Assert.Equal("", v));
        Assert.Empty(rig.Desk.State.History);
        Assert.Equal(DecisionState.Open, rig.Queue.Find(card)!.State);
    }

    [Fact]
    public async Task Limits_per_purchase_day_month_and_astur_hand_the_payment_over_and_an_astur_at_0_always_does()
    {
        using var rig = await new PurchaseRig().On();
        string Why(string arguments, AssistantChat? chat = null) => rig.Queue.Find(Guid.Parse(rig.Desk.Request(rig.Call(arguments, chat)).DecisionId!))!.Why;

        Assert.Equal("Over your $50.00 limit per purchase, so the payment is yours to make.", Why(PurchaseRig.Arguments(60m)));
        rig.Spent(120m, PurchaseRig.Start.AddHours(-1));
        Assert.Equal("Over your $150.00 limit per day, so the payment is yours to make.", Why(PurchaseRig.Arguments(40m)));
        rig.Spent(160m, PurchaseRig.Start.AddDays(-5));
        Assert.Equal("Over your $300.00 limit per month, so the payment is yours to make.", Why(PurchaseRig.Arguments(25m)));

        using var astur = await new PurchaseRig().On();
        var store = astur.Chat("store-2", Guid.NewGuid());
        var handed = astur.Queue.Find(Guid.Parse(astur.Desk.Request(astur.Call(PurchaseRig.Arguments(10m), store)).DecisionId!))!;
        Assert.Equal("Store has a $0.00 limit, so the payment is yours to make.", handed.Why);
        Assert.Equal([DecisionAnswerKind.Pick], handed.Answers);
        Assert.Equal([PurchaseWords.IllPay, PurchaseWords.DontBuy], handed.Options);
        Assert.Null(await astur.Desk.SetLimit(PurchaseLimitKind.PerAstur, 20m, "store-2"));
        Assert.Equal("Over Store's $20.00 limit per month, so the payment is yours to make.",
            astur.Queue.Find(Guid.Parse(astur.Desk.Request(astur.Call(PurchaseRig.Arguments(30m), store)).DecisionId!))!.Why);
        Assert.Equal(DecisionWeight.Irreversible, astur.Queue.Find(Guid.Parse(astur.Desk.Request(astur.Call(PurchaseRig.Arguments(15m), store)).DecisionId!))!.Weight);
    }

    [Fact]
    public async Task What_is_left_of_the_limits_changing_after_the_card_was_raised_asks_again_with_a_new_card()
    {
        using var rig = await new PurchaseRig().On();
        rig.SameOrigin();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments(20m))).DecisionId!);
        rig.Spent(10m, PurchaseRig.Start);
        var asked = rig.Hello.Asked;
        Assert.Null(await rig.Desk.Pay(card));
        Assert.Equal(asked, rig.Hello.Asked);                                           // nothing was asked or filled for a card that changed
        Assert.Equal((DecisionState.Withdrawn, PurchaseWords.LimitsChanged), (rig.Queue.Find(card)!.State, rig.Queue.Find(card)!.Note));
        var fresh = Assert.Single(rig.Queue.Waiting);
        Assert.Equal("$120.00 of $150.00 today · $270.00 of $300.00 this month", fresh.Inputs.Single(i => i.Name == "Left after").Value);
        Assert.Equal(PurchaseWords.ToAgentWaiting(fresh.Id), rig.Delivered[^1].Text);
    }

    [Fact]
    public async Task A_pay_card_expires_after_10_minutes_and_is_never_paid_after_that()
    {
        using var rig = await new PurchaseRig().On();
        rig.SameOrigin();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        rig.Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(DecisionState.Open, rig.Queue.Find(card)!.State);
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal((DecisionState.Withdrawn, PurchaseWords.Expired), (rig.Queue.Find(card)!.State, rig.Queue.Find(card)!.Note));
        Assert.Contains(rig.Delivered, d => d.Text.Contains("expired after 10 minutes", StringComparison.Ordinal));
        Assert.Equal(PurchaseWords.NoSuchCard, await rig.Desk.Pay(card));
        Assert.Empty(rig.Desk.State.History);
    }

    [Fact]
    public async Task Subscriptions_transfers_and_money_to_people_are_refused_and_raise_no_card()
    {
        using var rig = await new PurchaseRig().On();
        foreach (var arguments in new[]
        {
            PurchaseRig.Arguments(kind: "subscription"), PurchaseRig.Arguments(kind: "transfer"), PurchaseRig.Arguments(kind: "to_person"),
            PurchaseRig.Arguments(item: "Coffee club monthly plan"), PurchaseRig.Arguments(item: "Gift to Sam"),
        })
        {
            var refused = rig.Desk.Request(rig.Call(arguments));
            Assert.True(refused.IsError);
            Assert.Equal(PurchaseRequests.NeverAllowed, refused.Text);
        }
        Assert.Empty(rig.Queue.Items);
        Assert.Equal(5, rig.Audit.Count(e => e.Result == "refused: " + PurchaseRequests.NeverAllowed));
    }

    [Fact]
    public async Task Without_a_fill_path_purchases_off_no_card_or_another_currency_the_payment_is_handed_to_the_owner()
    {
        using (var handOver = new PurchaseRig(canPay: false))
        {
            var card = handOver.Queue.Find(Guid.Parse(handOver.Desk.Request(handOver.Call(PurchaseRig.Arguments())).DecisionId!))!;
            Assert.Equal((PurchaseWords.HandOverWhy, PurchaseWords.HandOverRule, DecisionWeight.Reversible), (card.Why, card.Rule, card.Weight));
            Assert.DoesNotContain(card.Inputs, i => i.Name is "Card" or "Left after");
            Assert.Null(handOver.Queue.Answer(card.Id, DecisionAnswer.Pick(PurchaseWords.IllPay)));
            Assert.Contains(handOver.Delivered, d => d.Text.StartsWith("The owner is paying at shop.localhost himself.", StringComparison.Ordinal));
        }
        using var rig = new PurchaseRig();
        Assert.Equal(PurchaseWords.HandedOff, rig.Queue.Find(Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!))!.Why);
        await rig.AddCard();
        Assert.Null(await rig.Desk.TurnOn());
        Assert.Equal(PurchaseWords.OtherCurrency("EUR", "USD"),
            rig.Queue.Find(Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments(currency: "EUR"))).DecisionId!))!.Why);
        var dont = rig.Queue.Waiting.Single();
        Assert.Null(rig.Queue.Answer(dont.Id, DecisionAnswer.Pick(PurchaseWords.DontBuy)));
        Assert.Contains(rig.Delivered, d => d.Text.StartsWith("The owner chose not to buy at shop.localhost.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Turning_purchases_off_or_removing_the_card_hands_a_waiting_pay_card_over_and_one_stopped_while_windows_asked_fills_nothing()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        rig.Hello.Hold();
        var paying = rig.Desk.Pay(card);
        Assert.Equal(PurchaseWords.NowOff, rig.Desk.TurnOff());                        // turning off while Windows asks
        Assert.Equal(DecisionState.Open, rig.Queue.Find(card)!.State);                 // no hand-over yet: nth might still be paying
        Assert.Equal([card], rig.Queue.Waiting.Select(w => w.Id));
        rig.Hello.Release(HelloResult.Verified);
        Assert.Null(await paying);                                                      // settled: handed over once it had stopped
        Assert.Empty(page.Filled);
        Assert.Equal((DecisionState.Withdrawn, PurchaseWords.TurnedOff), (rig.Queue.Find(card)!.State, rig.Queue.Find(card)!.Note));
        Assert.Equal(PurchaseWords.TurnedOff, Assert.Single(rig.Queue.Waiting).Why);

        Assert.Null(await rig.Desk.TurnOn());
        var next = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments(12m))).DecisionId!);
        Assert.Equal(DecisionWeight.Irreversible, rig.Queue.Find(next)!.Weight);
        Assert.Equal(CardVaultOutcome.Done, (await rig.Desk.RemoveCard()).Outcome);
        Assert.False(rig.Desk.State.Policy.On);
        Assert.Equal((DecisionState.Withdrawn, PurchaseWords.HandedNoCard), (rig.Queue.Find(next)!.State, rig.Queue.Find(next)!.Note));
    }
}
