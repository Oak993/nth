using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Caps", 15.3; ED-NEW-dextra-as15-3): what each assistant used today, per account and window, and the
// money spent on API keys: the figures a day's cap is checked against. The day is the local calendar day. The file carries a version, a newer
// one is left alone, fields this build does not know come back, a line that is not a row is skipped, and the file is bounded.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-3")]
public sealed class CostLedgerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static CostCap DayOf(string? assistant) => new("c", CostCapScope.AssistantDay) { Percent = 15, Dollars = 100m, AssistantId = assistant };

    private static CostCap Keys() => new("k", CostCapScope.ApiDay) { Dollars = 100m };

    private static CostLedger Filled(ManualTimeProvider clock)
    {
        var ledger = new CostLedger(clock);
        ledger.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 3);
        ledger.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.Weekly, 1);
        ledger.AddPoints("channel-a", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 2);
        ledger.AddDollars("dextra", "openrouter:key", "OpenRouter", 1.5m);
        ledger.AddDollars("channel-a", "openrouter:key", "OpenRouter", 0.5m);
        return ledger;
    }

    [Fact]
    public void Points_and_dollars_are_added_per_assistant_account_and_window_and_a_cap_reads_what_it_covers()
    {
        var ledger = Filled(new ManualTimeProvider(T0));

        var dextra = ledger.UseOf(DayOf("dextra"));
        Assert.Equal(3, dextra.Points[("claude:20x", ReserveWindowKind.FiveHour)]);
        Assert.Equal(1, dextra.Points[("claude:20x", ReserveWindowKind.Weekly)]);
        Assert.Equal(1.5m, dextra.Dollars);
        Assert.Equal("Claude 20x", dextra.NameOf("claude:20x"));

        var everyone = ledger.UseOf(DayOf(null));                                  // a cap that names no one, read over every assistant
        Assert.Equal(5, everyone.Points[("claude:20x", ReserveWindowKind.FiveHour)]);
        var onlyChannel = ledger.UseOf(DayOf(null), ledger.Today, "channel-a");    // ... or for one: each assistant on its own
        Assert.Equal(2, onlyChannel.Points[("claude:20x", ReserveWindowKind.FiveHour)]);
        Assert.Equal(0.5m, onlyChannel.Dollars);

        var keys = ledger.UseOf(Keys());                                           // the API keys' cap reads the money of every assistant
        Assert.Equal(2.0m, keys.Dollars);
        Assert.Empty(keys.Points);
    }

    [Fact]
    public void Amounts_that_are_nothing_are_not_added_and_dollars_do_not_drift()
    {
        var ledger = new CostLedger(new ManualTimeProvider(T0));
        var changed = 0;
        ledger.Changed += () => changed++;
        ledger.AddPoints("dextra", "a", "A", false, ReserveWindowKind.FiveHour, 0);
        ledger.AddPoints("dextra", "a", "A", false, ReserveWindowKind.FiveHour, -1);
        ledger.AddPoints("dextra", "a", "A", false, ReserveWindowKind.FiveHour, double.NaN);
        ledger.AddDollars("dextra", "k", "K", 0m);
        Assert.Equal(0, changed);
        for (var i = 0; i < 3; i++) ledger.AddDollars("dextra", "k", "K", 0.1m);
        Assert.Equal(3, changed);
        Assert.Equal(0.3m, ledger.UseOf(DayOf("dextra")).Dollars);
    }

    [Fact]
    public void The_day_is_the_local_calendar_day_and_yesterday_does_not_count_today()
    {
        var clock = new ManualTimeProvider(T0);
        var ledger = Filled(clock);
        var yesterday = ledger.Today;
        clock.Advance(TimeSpan.FromHours(15));                                     // 00:00 on the 14th, UTC being the test's local zone
        Assert.Equal(yesterday.AddDays(1), ledger.Today);
        Assert.Empty(ledger.UseOf(DayOf("dextra")).Points);
        Assert.Equal(0m, ledger.UseOf(DayOf("dextra")).Dollars);
        Assert.Equal(3, ledger.UseOf(DayOf("dextra"), yesterday).Points[("claude:20x", ReserveWindowKind.FiveHour)]);   // still on record
        ledger.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 4);
        Assert.Equal(4, ledger.UseOf(DayOf("dextra")).Points[("claude:20x", ReserveWindowKind.FiveHour)]);

        clock.Advance(TimeSpan.FromDays(2));                                       // the 16th: only the last three days are kept
        ledger.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 1);
        var days = ledger.ToJson()!["days"]!.AsArray().Select(d => d!["day"]!.GetValue<string>()).ToList();
        Assert.Equal(["2026-10-14", "2026-10-16"], days);
    }

    [Fact]
    public void The_document_round_trips_and_keeps_what_this_build_does_not_know()
    {
        var clock = new ManualTimeProvider(T0);
        var first = Filled(clock).ToJson()!;
        Assert.Equal(1, first["version"]!.GetValue<int>());
        first["future"] = "kept";
        first["days"]!.AsArray()[0]!["rows"]!.AsArray()[0]!["note"] = "also kept";

        var reopened = new CostLedger(clock);
        reopened.Load(first);
        Assert.Equal(3, reopened.UseOf(DayOf("dextra")).Points[("claude:20x", ReserveWindowKind.FiveHour)]);
        Assert.Equal(2.0m, reopened.UseOf(Keys()).Dollars);
        var again = reopened.ToJson()!;
        Assert.Equal("kept", again["future"]!.GetValue<string>());
        Assert.Equal("also kept", again["days"]!.AsArray()[0]!["rows"]!.AsArray()[0]!["note"]!.GetValue<string>());
        Assert.Equal(1, again["version"]!.GetValue<int>());
        Assert.Equal(5, again["days"]!.AsArray()[0]!["rows"]!.AsArray().Count);
    }

    [Fact]
    public void A_file_with_no_version_is_the_first_version_and_a_newer_one_is_read_for_nothing_and_never_written_over()
    {
        var clock = new ManualTimeProvider(T0);
        var plain = Filled(clock).ToJson()!;
        plain.Remove("version");
        var loaded = new CostLedger(clock);
        loaded.Load(plain);
        Assert.False(loaded.ReadOnly);
        Assert.Equal(3, loaded.UseOf(DayOf("dextra")).Points[("claude:20x", ReserveWindowKind.FiveHour)]);

        var newer = Filled(clock).ToJson()!;
        newer["version"] = CostLedger.FormatVersion + 1;
        var held = new CostLedger(clock);
        held.Load(newer);
        Assert.True(held.ReadOnly);
        Assert.Empty(held.UseOf(DayOf("dextra")).Points);
        var changed = 0;
        held.Changed += () => changed++;
        held.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 9);
        Assert.Equal(9, held.UseOf(DayOf("dextra")).Points[("claude:20x", ReserveWindowKind.FiveHour)]);   // what is spent now still counts (a cap must not read a day as empty)
        Assert.Equal(1, changed);
        Assert.Null(held.ToJson());                                                // and the store keeps the file as it is, which nothing here writes over
    }

    [Fact]
    public void A_line_that_is_not_a_row_is_skipped_and_never_repaired()
    {
        var clock = new ManualTimeProvider(T0);
        var good = new JsonObject { ["assistant"] = "dextra", ["account"] = "claude:20x", ["name"] = "Claude 20x", ["api"] = false, ["window"] = "FiveHour", ["amount"] = 2.5 };
        JsonObject Bad(string field, JsonNode? value)
        {
            var copy = (JsonObject)good.DeepClone();
            if (value is null) copy.Remove(field); else copy[field] = value;
            return copy;
        }
        var rows = new JsonArray(
            good.DeepClone(),
            Bad("assistant", null), Bad("account", ""), Bad("window", "Fortnight"), Bad("window", null), Bad("amount", -1), Bad("amount", 0), Bad("amount", "lots"),
            Bad("assistant", new string('x', 200)));
        var document = new JsonObject
        {
            ["version"] = 1,
            ["days"] = new JsonArray(
                new JsonObject { ["day"] = "2026-10-13", ["rows"] = rows },
                new JsonObject { ["day"] = "not a day", ["rows"] = rows.DeepClone() },
                new JsonObject { ["day"] = "2020-01-01", ["rows"] = rows.DeepClone() },       // long gone
                new JsonObject { ["day"] = "2026-10-13" },                                     // no rows
                JsonValue.Create("nonsense")),
        };
        var ledger = new CostLedger(clock);
        ledger.Load(document);
        var use = ledger.UseOf(DayOf("dextra"));
        Assert.Equal(2.5, use.Points[("claude:20x", ReserveWindowKind.FiveHour)]);
        Assert.Single(use.Points);
        Assert.Single(ledger.ToJson()!["days"]!.AsArray());
    }

    [Fact]
    public void A_day_holds_a_bounded_number_of_rows_and_a_row_that_exists_still_grows()
    {
        var ledger = new CostLedger(new ManualTimeProvider(T0));
        for (var i = 0; i < CostLedger.MaxRowsPerDay + 50; i++) ledger.AddPoints("dextra", "account-" + i, "Account " + i, false, ReserveWindowKind.FiveHour, 1);
        Assert.Equal(CostLedger.MaxRowsPerDay, ledger.ToJson()!["days"]![0]!["rows"]!.AsArray().Count);
        ledger.AddPoints("dextra", "account-0", "Account 0", false, ReserveWindowKind.FiveHour, 1);
        Assert.Equal(2, ledger.UseOf(DayOf("dextra")).Points[("account-0", ReserveWindowKind.FiveHour)]);
        Assert.False(ledger.UseOf(DayOf("dextra")).Points.ContainsKey(("account-" + (CostLedger.MaxRowsPerDay + 10), ReserveWindowKind.FiveHour)));
    }
}
