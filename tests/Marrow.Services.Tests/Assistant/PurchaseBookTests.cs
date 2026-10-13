using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "The switch", "Limits", "History"; lean-tests 10 "Persisted formats" and "Retryable
// side effects"; ED-NEW-dextra-as20-9): purchases.json starts off with the default limits; a history row is the shop, the
// amount, the date and the mission with the limits' bookkeeping, and never card data; the intent is written before an order
// is placed, and a crash between the order and its outcome leaves an "unknown" row that counts and is never placed again;
// unknown fields survive; a newer nth's file and one that does not parse are left untouched with purchases off.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-9")]
public sealed class PurchaseBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static PurchaseRequest Request(decimal total = 46.80m) =>
        new("Shop Example", "https://shop.example", "https://shop.example/checkout", new ValueList<PurchaseItem>([new("Tamper, 58 mm", total)]), 0m, 0m, total, "USD", "Home");

    private static (PurchaseBook Book, Func<JsonObject?> Saved) Over(JsonObject? start = null, bool unreadable = false)
    {
        JsonObject? saved = start;
        var book = new PurchaseBook(() => start, document => saved = document, unreadable);
        return (book, () => saved);
    }

    [Fact]
    public void A_new_book_has_purchases_off_and_the_default_limits_and_writes_nothing_until_a_change()
    {
        var (book, saved) = Over();
        Assert.Equal(PurchasePolicy.Default, book.Policy);
        Assert.False(book.Policy.On);
        Assert.Null(saved());
        Assert.Empty(book.History);
    }

    [Fact]
    public void A_history_row_holds_the_shop_amount_date_and_mission_and_the_limits_bookkeeping_only()
    {
        var (book, saved) = Over();
        var mission = Guid.NewGuid();
        var row = book.Begin(Request(), mission, "dextra", Now);
        book.Settle(row.Id, PurchaseRowState.Paid);
        var stored = saved()!["history"]!.AsArray().Single()!.AsObject();
        Assert.Equal(new[] { "amount", "assistant", "at", "currency", "id", "mission", "shop", "state" }, stored.Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal(("shop.example", 46.80m, mission.ToString("D"), "paid"),
            (stored["shop"]!.GetValue<string>(), stored["amount"]!.GetValue<decimal>(), stored["mission"]!.GetValue<string>(), stored["state"]!.GetValue<string>()));
        Assert.Equal(1, saved()!["version"]!.GetValue<int>());
    }

    [Fact]
    public void A_crash_between_the_order_and_its_outcome_leaves_an_unknown_row_that_counts_and_is_kept()
    {
        var (book, saved) = Over();
        book.Begin(Request(), null, "dextra", Now); // the intent; the process ends before Settle
        var (again, savedAgain) = Over(saved());
        var row = Assert.Single(again.History);
        Assert.Equal(PurchaseRowState.Unknown, row.State);
        Assert.Equal("unknown", savedAgain()!["history"]![0]!["state"]!.GetValue<string>());
        Assert.Equal(46.80m, again.Spend(Now, Utc).Today);
    }

    [Fact]
    public void An_intent_that_cannot_be_stored_throws_and_leaves_no_row()
    {
        var book = new PurchaseBook(() => null, _ => throw new IOException("disk full"));
        Assert.Throws<IOException>(() => book.Begin(Request(), null, "dextra", Now));
        Assert.Empty(book.History);
    }

    [Fact]
    public void Spend_counts_today_and_this_month_by_assistant_and_leaves_out_what_was_not_paid()
    {
        var (book, _) = Over();
        book.Settle(book.Begin(Request(10m), null, "dextra", Now.AddDays(-20)).Id, PurchaseRowState.Paid); // September: not this month
        book.Settle(book.Begin(Request(20m), null, "store-2", Now.AddDays(-3)).Id, PurchaseRowState.Paid);
        book.Settle(book.Begin(Request(30m), null, "dextra", Now.AddHours(-1)).Id, PurchaseRowState.Paid);
        book.Settle(book.Begin(Request(40m), null, "dextra", Now).Id, PurchaseRowState.NotPaid);
        var spend = book.Spend(Now, Utc);
        Assert.Equal((30m, 50m), (spend.Today, spend.ThisMonth));
        Assert.Equal((30m, 20m), (spend.Of("dextra"), spend.Of("store-2")));
    }

    [Fact]
    public void The_policy_round_trips_and_fields_it_does_not_know_survive_a_load_and_save()
    {
        var start = JsonNode.Parse("""
            {"version":1,"on":false,"currency":"EUR","later":{"kept":true},
             "limits":{"purchase":25,"day":100,"month":200,"asturs":{"store-2":15},"weekly":70},
             "history":[{"id":"6d4f8e3a-5a84-4b39-9b3a-3c3c8f1f6a10","shop":"shop.example","amount":12.5,"currency":"EUR","at":"2026-10-12T10:00:00.0000000+00:00","mission":null,"assistant":"dextra","state":"paid","note":"kept"}]}
            """)!.AsObject();
        var (book, saved) = Over(start);
        Assert.Equal(new PurchasePolicy(false, "EUR", new PurchaseLimits { PerPurchase = 25m, PerDay = 100m, PerMonth = 200m, PerAstur = new ValueMap<string, decimal>([new("store-2", 15m)]) }), book.Policy);
        book.SetPolicy(book.Policy with { On = true });
        var after = saved()!;
        Assert.True(after["on"]!.GetValue<bool>());
        Assert.True(after["later"]!["kept"]!.GetValue<bool>());
        Assert.Equal(70, after["limits"]!["weekly"]!.GetValue<int>());
        Assert.Equal("kept", after["history"]![0]!["note"]!.GetValue<string>());
    }

    [Fact]
    public void A_newer_nths_file_and_one_that_does_not_parse_keep_purchases_off_and_are_never_written()
    {
        var newer = JsonNode.Parse("""{"version":2,"on":true,"history":[{"id":"6d4f8e3a-5a84-4b39-9b3a-3c3c8f1f6a10","shop":"s","amount":1,"currency":"USD","at":"2026-10-12T10:00:00Z","assistant":"dextra","state":"paying"}]}""")!.AsObject();
        var (book, saved) = Over(newer);
        Assert.True(book.ReadOnly);
        Assert.False(book.Policy.On);
        Assert.Equal(PurchaseRowState.Paying, Assert.Single(book.History).State);
        Assert.Throws<PurchaseBookReadOnlyException>(() => book.SetPolicy(book.Policy with { On = true }));
        Assert.Throws<PurchaseBookReadOnlyException>(() => book.Begin(Request(), null, "dextra", Now));
        Assert.False(book.TurnOff());
        Assert.Same(newer, saved());

        var (broken, brokenSaved) = Over(null, unreadable: true);
        Assert.True(broken.ReadOnly && broken.Unreadable);
        Assert.Throws<PurchaseBookReadOnlyException>(() => broken.SetPolicy(PurchasePolicy.Default with { On = true }));
        Assert.Null(brokenSaved());
    }

    [Fact]
    public void Turning_off_never_fails_the_owner_even_when_the_store_cannot_be_written()
    {
        var start = JsonNode.Parse("""{"version":1,"on":true}""")!.AsObject();
        var book = new PurchaseBook(() => start, _ => throw new UnauthorizedAccessException());
        Assert.True(book.Policy.On);
        Assert.False(book.TurnOff());
        Assert.False(book.Policy.On);
        Assert.Equal(nameof(UnauthorizedAccessException), book.Fault);
    }

    [Fact]
    public void A_limit_nth_does_not_take_is_refused_and_the_store_file_lives_beside_reserves()
    {
        var (book, saved) = Over();
        Assert.Throws<ArgumentException>(() => book.SetPolicy(PurchasePolicy.Default with { Limits = PurchaseLimits.Default with { PerDay = -1m } }));
        Assert.Null(saved());
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var real = PurchaseBook.Over(store);
        real.SetPolicy(PurchasePolicy.Default with { On = true });
        Assert.True(File.Exists(Path.Combine(store.Root, AssistantStore.PurchasesFile)));
        Assert.True(PurchaseBook.Over(store).Policy.On);
        File.WriteAllText(Path.Combine(store.Root, AssistantStore.PurchasesFile), "{ not json");
        Assert.True(PurchaseBook.Over(store).Unreadable);
    }
}
