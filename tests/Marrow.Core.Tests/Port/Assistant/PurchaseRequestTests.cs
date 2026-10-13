using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using static Marrow.Core.Tests.Fixtures.CheckoutPages;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "Every purchase shows the confirmation card", "Never allowed"; 15.5 and the tool's
// schema; ED-NEW-dextra-as20-1, -3): prepare_purchase's arguments become the card's shop, origin, items, shipping, tax, total
// and address; a total that does not add up, an address that is not the shop's own https site, and every subscription,
// transfer or payment to a person are refused before any card is raised; the tool is an agents' tool that only raises a card.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-3")]
public sealed class PurchaseRequestTests
{
    private static JSONValue With(JSONValue arguments, string name, JSONValue value) =>
        JSONValue.FromDictionaryLiteral([.. arguments.JSONValueObject!.Where(p => p.Key != name).Select(p => (p.Key, p.Value)), (name, value)]);

    [Fact]
    public void The_fixtures_arguments_read_as_the_canvas_card()
    {
        var request = PurchaseRequests.Parse(Arguments(), out var refusal, allowLoopbackHttp: true);
        Assert.Equal("", refusal);
        Assert.NotNull(request);
        Assert.Equal(("Shop Example", Shop, 46.80m, "USD", "Home (your saved address)"), (request.Shop, request.Origin, request.Total, request.Currency, request.ShipTo));
        Assert.Equal(new[] { new PurchaseItem("Tamper, 58 mm", 24.00m), new PurchaseItem("Milk jug, 350 ml", 14.50m) }, request.Items);
        Assert.Equal((4.90m, 3.40m, 38.50m), (request.Shipping, request.Tax, request.Subtotal));
        Assert.Equal("Buy from shop.localhost?", PurchaseWords.Title(request));
        Assert.Equal("Pay $46.80", PurchaseWords.Pay(request));
        Assert.Equal("pay $46.80 to shop.localhost", PurchaseWords.PayReason(request));
    }

    [Theory]
    [InlineData("subscription")]
    [InlineData("transfer")]
    [InlineData("to_person")]
    public void Subscriptions_transfers_and_money_to_people_are_refused(string kind)
    {
        Assert.Null(PurchaseRequests.Parse(Arguments(kind: kind), out var refusal, allowLoopbackHttp: true));
        Assert.Equal(PurchaseRequests.NeverAllowed, refusal);
    }

    [Theory]
    [InlineData("Coffee club membership: 46.80")]
    [InlineData("Beans, auto-renew every 4 weeks: 46.80")]
    [InlineData("Streaming plan, per month: 46.80")]
    [InlineData("Send money to Sam: 46.80")]
    public void A_purchase_whose_words_say_it_repeats_or_pays_a_person_is_refused_like_its_kind(string item)
    {
        var arguments = With(With(With(Arguments(), "items", JSONValue.FromArrayLiteral(item)), "shipping", 0.0), "tax", 0.0);
        Assert.Null(PurchaseRequests.Parse(arguments, out var refusal, allowLoopbackHttp: true));
        Assert.Equal(PurchaseRequests.NeverAllowed, refusal);
    }

    [Fact]
    public void A_total_that_does_not_add_up_is_refused()
    {
        Assert.Null(PurchaseRequests.Parse(Arguments(total: 40.00), out var refusal, allowLoopbackHttp: true));
        Assert.Contains("do not add up", refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://shop.example/checkout")]
    [InlineData("https://shop.example@evil.example/pay")]
    [InlineData("shop.example/checkout")]
    [InlineData("file:///C:/Users/x/pay.html")]
    public void A_checkout_that_is_not_the_shops_own_https_site_is_refused(string address)
    {
        Assert.Null(PurchaseRequests.Parse(Arguments(checkout: address), out var refusal));
        Assert.Contains("full https address", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fixtures_plain_http_shop_is_refused_outside_the_fixtures()
    {
        Assert.Null(PurchaseRequests.Parse(Arguments(), out _));
        Assert.NotNull(PurchaseRequests.Parse(Arguments(checkout: "https://shop.example/checkout"), out _));
    }

    [Fact]
    public void The_checkout_address_is_kept_as_its_origin_and_escaped_path_so_the_card_cannot_read_as_another_site()
    {
        // Dextra AS-20 (ED-NEW-dextra-as20-18): a right-to-left override after evil.example's path would make the line read as ending in amazon.com.
        var request = PurchaseRequests.Parse(Arguments(checkout: "https://evil.example/\u202Emoc.nozama//:sptth#top"), out var refusal);
        Assert.True(request is not null, refusal);
        Assert.Equal("https://evil.example", request!.Origin);
        Assert.StartsWith("https://evil.example/", request.CheckoutUrl, StringComparison.Ordinal);
        Assert.All(request.CheckoutUrl, c => Assert.True(c is > ' ' and < (char)0x7F, $"U+{(int)c:X4}"));
        Assert.DoesNotContain('#', request.CheckoutUrl);
        Assert.Equal("https://shop.example/checkout", PurchaseRequests.Parse(Arguments(checkout: "  HTTPS://Shop.Example:443/checkout "), out _)!.CheckoutUrl);
    }

    [Theory]
    [InlineData("Milk jug: 14.50", "Milk jug", 14.50)]
    [InlineData("Lens 50mm f/1.8: $129.99", "Lens 50mm f/1.8", 129.99)]
    [InlineData("Cable, 2 m:   9", "Cable, 2 m", 9.00)]
    public void An_item_is_its_name_and_the_price_after_the_last_colon(string line, string name, double price) =>
        Assert.Equal(new PurchaseItem(name, (decimal)price), PurchaseRequests.ParseItem(line));

    [Theory]
    [InlineData("No price here")]
    [InlineData(": 4.00")]
    [InlineData("Half a cent: 0.005")]
    [InlineData("Negative: -3")]
    public void A_line_without_a_name_or_a_price_in_cents_is_no_item(string line) => Assert.Null(PurchaseRequests.ParseItem(line));

    [Fact]
    public void Untrusted_text_loses_control_and_format_characters_and_is_cut()
    {
        Assert.Equal("Shop Example", PurchaseRequests.Clean("Shop\u202E\u0000 \r\n Example\u200B", 50));
        Assert.Equal(new string('a', 120), PurchaseRequests.Clean(new string('a', 500), 120));
        Assert.Equal("", PurchaseRequests.Clean(null, 10));
    }

    [Fact]
    public void Amounts_read_in_the_cards_currency()
    {
        Assert.Equal(("$46.80", "€12.00", "£3.50", "46.80 CHF"),
            (PurchaseMoney.Format(46.8m, "USD"), PurchaseMoney.Format(12m, "EUR"), PurchaseMoney.Format(3.5m, "GBP"), PurchaseMoney.Format(46.8m, "CHF")));
    }

    [Fact]
    public void The_tool_is_offered_to_agents_runs_before_auto_answering_and_only_raises_a_card()
    {
        var tool = AssistantToolCatalog.Find("prepare_purchase")!;
        Assert.True(tool.ForAgents);
        Assert.True(tool.RunsBeforeAutoAnswer);
        Assert.False(tool.ReadOnly || tool.Destructive);
        Assert.Contains(PurchaseWords.Placeholder, tool.Description, StringComparison.Ordinal);
        Assert.Null(tool.Validate(Arguments()));
        var placed = ActionClassifier.Classify(new ActionRequest.AssistantTool("prepare_purchase"), new ClassifierContext());
        Assert.Equal((ActionClass.Agents, AssistantToolGuard.None), (placed.Class, placed.Guard));
        // a reader only reads: it may not prepare a purchase
        Assert.Equal(Verdict.Deny, PolicyEngine.Decide(placed, InjectionSelfCheck.Context(TaintContext.Clean, AgentRole.Reader, DateTimeOffset.UnixEpoch)).Verdict);
    }

    [Fact]
    public void Paying_is_money_never_covered_by_a_grant_and_off_unless_the_owner_turned_purchases_on()
    {
        var pay = new Classification(ActionClass.Money, "pays at the shop") { Money = MoneyKind.Purchase };
        var on = InjectionSelfCheck.Context(TaintContext.Clean, AgentRole.Actor, DateTimeOffset.UnixEpoch);
        Assert.Equal((Verdict.Ask, PolicyRule.AlwaysConfirmed), (PolicyEngine.Decide(pay, on).Verdict, PolicyEngine.Decide(pay, on).Rule));
        Assert.Equal(PolicyRule.PurchasesOff, PolicyEngine.Decide(pay, on with { PurchasesOn = false }).Rule);
        Assert.Equal(PolicyRule.MoneyNeverAllowed, PolicyEngine.Decide(pay with { Money = MoneyKind.Subscription }, on).Rule);
        Assert.Equal(DecisionWeight.Irreversible, DecisionRules.Weigh(ActionClass.Money));
    }

    [Fact]
    public void A_purchase_card_is_pay_or_dont_and_a_handed_over_one_is_its_two_options()
    {
        var pay = new DecisionItem(Guid.NewGuid(), DecisionKind.Purchase, DecisionWeight.Irreversible, "Buy from shop.example?") { Class = ActionClass.Money };
        Assert.Equal(new[] { DecisionAnswerKind.DoIt, DecisionAnswerKind.Dont }, pay.Answers);
        var handOver = pay with { Weight = DecisionWeight.Reversible, Options = new ValueList<string>([PurchaseWords.IllPay, PurchaseWords.DontBuy]) };
        Assert.Equal(new[] { DecisionAnswerKind.Pick }, handOver.Answers);
        Assert.Null(DecisionRules.Refusal(handOver, DecisionAnswer.Pick(PurchaseWords.IllPay)));
        Assert.NotNull(DecisionRules.Refusal(handOver, DecisionAnswer.AllowOnce));
    }
}
