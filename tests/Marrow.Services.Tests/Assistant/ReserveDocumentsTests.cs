using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.3, 11.7, 15.3; ED-NEW-dextra-as13-7): the standing rules and the two switches of Settings >
// Reserves, the turn costs the plan reads, and the two documents the store keeps for them (reserves.json, calibration.json):
// versioned, unknown fields kept, a newer file left alone, a bad line skipped, a crash never leaves a half file.
public sealed class ReserveDocumentsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private static ReserveRule Always(string names, double keep) =>
        new("", ReserveScope.Named(names), ReserveLifetime.Standing) { KeepPercent = keep, Basis = ReserveBasis.WholeWindow };

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void Standing_rules_and_both_switches_round_trip_through_the_store()
    {
        using (var store = AssistantStore.Open(temp.Roots))
        {
            var settings = new ReserveSettings();
            Assert.Null(settings.Add(Always("Claude 20x", 30)));
            Assert.Null(settings.Add(new ReserveRule("", ReserveScope.Assistant("channel-a"), ReserveLifetime.Standing) { KeepPercent = 85 }));
            Assert.Null(settings.Add(new ReserveRule("", ReserveScope.Named("OpenRouter key"), ReserveLifetime.Standing) { KeepDollars = 5m, Windows = ReserveWindows.Day }));
            settings.SetHoldReserves(true);
            settings.SetAlwaysAskBeforeStarting(true);
            store.SaveReserves(settings.ToJson()!);
        }
        using var reopened = AssistantStore.Open(temp.Roots);
        var loaded = new ReserveSettings();
        loaded.Load(reopened.Reserves());
        Assert.False(loaded.ReadOnly);
        Assert.True(loaded.HoldReserves);
        Assert.True(loaded.AlwaysAskBeforeStarting);
        Assert.Equal(["Always keep 30% on Claude 20x", "Always keep 85% on channel-a", "Keep $5 a day on OpenRouter key"], loaded.Rules.Select(ReserveWords.Title));
        Assert.Equal(ReserveScopeKind.Assistant, loaded.Rules[1].Scope.Kind);
        Assert.Equal("channel-a", loaded.Rules[1].Scope.AssistantId);
        Assert.Equal(5m, loaded.Rules[2].KeepDollars);
        Assert.Equal(ReserveWindows.Day, loaded.Rules[2].Windows);
        Assert.All(loaded.Rules, r => Assert.Equal(ReserveBasis.WholeWindow, r.Basis));
        Assert.Equal(3, loaded.Rules.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void Only_a_standing_rule_is_kept_a_second_with_the_same_id_gets_a_new_one_and_the_list_is_capped()
    {
        var settings = new ReserveSettings();
        Assert.NotNull(settings.Add(new ReserveRule("t", ReserveScope.Every, ReserveLifetime.Task) { KeepPercent = 30 }));
        Assert.Empty(settings.Rules);
        Assert.NotNull(settings.Add(new ReserveRule("n", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = double.NaN }));

        Assert.Null(settings.Add(new ReserveRule("same", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 10 }));
        Assert.Null(settings.Add(new ReserveRule("same", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 20 }));
        Assert.Equal(2, settings.Rules.Select(r => r.Id).Distinct().Count());
        Assert.True(settings.Remove(settings.Rules[0].Id));
        Assert.False(settings.Remove("nothing"));
        Assert.Single(settings.Rules);

        for (var i = 0; i < ReserveSettings.MaxRules; i++) settings.Add(Always("A", 10));
        Assert.Equal(ReserveSettings.MaxRules, settings.Rules.Count);
        Assert.Contains("At most", settings.Add(Always("A", 10)), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void A_change_is_announced_once_and_a_standing_rule_is_stored_whole_window()
    {
        var settings = new ReserveSettings();
        var changes = 0;
        settings.Changed += () => changes++;
        settings.Add(new ReserveRule("a", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30, Basis = ReserveBasis.WhatIsLeft });
        settings.SetHoldReserves(true);
        settings.SetAlwaysAskBeforeStarting(true);
        settings.Remove("a");
        Assert.Equal(4, changes);
        settings.Add(new ReserveRule("b", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30, Basis = ReserveBasis.WhatIsLeft });
        Assert.Equal(ReserveBasis.WholeWindow, settings.Rules[0].Basis);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void Fields_this_build_does_not_know_survive_a_load_and_a_save()
    {
        var settings = new ReserveSettings();
        settings.Add(Always("Claude 20x", 30));
        var document = settings.ToJson()!;
        Assert.Equal(ReserveSettings.FormatVersion, document["version"]!.GetValue<int>());
        document["theme"] = "violet";
        document["rules"]![0]!["colour"] = "teal";

        var later = new ReserveSettings();
        later.Load(document);
        later.SetHoldReserves(true);
        var saved = later.ToJson()!;
        Assert.Equal("violet", saved["theme"]!.GetValue<string>());
        Assert.Equal("teal", saved["rules"]![0]!["colour"]!.GetValue<string>());
        Assert.True(saved["holdReserves"]!.GetValue<bool>());
        Assert.Single(saved["rules"]!.AsArray());
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void A_file_saved_by_a_newer_version_is_left_exactly_as_it_is()
    {
        var newer = new JsonObject
        {
            ["version"] = ReserveSettings.FormatVersion + 1,
            ["holdReserves"] = true,
            ["rules"] = new JsonArray(new JsonObject { ["id"] = "x", ["somethingNew"] = 1 }),
        };
        var settings = new ReserveSettings();
        settings.Load(newer);
        Assert.True(settings.ReadOnly);
        Assert.Empty(settings.Rules);
        Assert.False(settings.HoldReserves);       // nothing of it is read
        Assert.Null(settings.ToJson());            // nothing is written
        Assert.Contains("newer version", settings.Add(Always("A", 10)), StringComparison.Ordinal);
        Assert.False(settings.SetHoldReserves(true));
        Assert.False(settings.SetAlwaysAskBeforeStarting(true));
        Assert.False(settings.Remove("x"));
        Assert.Equal(ReserveSettings.FormatVersion + 1, newer["version"]!.GetValue<int>());
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void A_line_that_is_not_a_rule_is_skipped_and_a_task_rule_in_the_file_is_not_loaded()
    {
        JsonObject Rule(string id, string lifetime = "Standing", string basis = "WholeWindow", JsonNode? keep = null, int windows = 3) => new()
        {
            ["id"] = id, ["lifetime"] = lifetime, ["basis"] = basis, ["keepPercent"] = keep ?? 30, ["windows"] = windows,
            ["scope"] = new JsonObject { ["kind"] = "EveryAccount" },
        };
        var bad = Rule("bad-scope");
        bad["scope"] = new JsonObject { ["kind"] = "Galaxy" };
        var document = new JsonObject
        {
            ["version"] = 1,
            ["rules"] = new JsonArray(
                Rule("ok"), Rule("task", lifetime: "Task"), Rule("b", basis: "Sideways"), Rule("w", windows: 99), Rule("n", keep: JsonValue.Create("lots")),
                bad, Rule("ok"), JsonValue.Create("rule"), new JsonObject()),
        };
        var settings = new ReserveSettings();
        settings.Load(document);
        Assert.Equal(["ok"], settings.Rules.Select(r => r.Id));
        settings.Load(new JsonObject { ["rules"] = "not a list" });
        Assert.Empty(settings.Rules);
        settings.Load(null);
        Assert.False(settings.HoldReserves);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-6")]
    public void Turn_costs_are_a_median_and_a_widened_p90_and_a_thin_history_takes_a_large_guess()
    {
        var clock = new ManualTimeProvider(T0);
        var turns = new TurnCostHistory(clock);
        Assert.Equal(ReserveDefaults.UncalibratedTurnTokens, turns.P90("opus"), 6);
        Assert.Equal(ReserveDefaults.UncalibratedTurnTokens / 2, turns.P50("opus"), 6);
        turns.Record("opus", 10_000, T0);
        turns.Record("opus", 20_000, T0);
        Assert.Equal(ReserveDefaults.UncalibratedTurnTokens, turns.P90("opus"), 6); // two turns are too few
        Assert.Equal(2, turns.Count("opus"));

        foreach (var tokens in new[] { 30_000, 40_000, 50_000, 60_000, 70_000, 80_000, 90_000, 100_000 }) turns.Record("opus", tokens, T0);
        // Ten turns, 10,000 to 100,000: the p50 is 55,000; the p90 is 91,000, widened by 1 + 1/sqrt(10).
        Assert.Equal(55_000, turns.P50("opus"), 6);
        Assert.Equal(91_000 * (1 + 1 / Math.Sqrt(10)), turns.P90("opus"), 6);

        // A model nobody ran takes the default; no model name is the pool of unnamed spend.
        Assert.Equal(ReserveDefaults.UncalibratedTurnTokens, turns.P90("sonnet"), 6);
        Assert.Equal(0, turns.Count(null));
        turns.Record(null, 1_000, T0);
        turns.Record("  ", 1_000, T0);
        Assert.Equal(2, turns.Count(null));
        turns.Record("opus", 0, T0);                                  // a turn that cost nothing says nothing
        turns.Record("opus", -5, T0);
        Assert.Equal(10, turns.Count("opus"));

        clock.Advance(TimeSpan.FromDays(15));
        Assert.Equal(0, turns.Count("opus"));
        Assert.Equal(ReserveDefaults.UncalibratedTurnTokens, turns.P90("opus"), 6);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void Turn_costs_round_trip_with_their_unknown_fields_and_skip_a_bad_line()
    {
        var clock = new ManualTimeProvider(T0);
        var turns = new TurnCostHistory(clock);
        for (var i = 1; i <= 4; i++) turns.Record("opus", i * 10_000, T0.AddMinutes(i));
        var stored = turns.ToJson();
        stored[0]!["note"] = "kept";
        stored.Add(new JsonObject { ["model"] = "opus", ["at"] = "yesterday", ["tokens"] = 5 });
        stored.Add(new JsonObject { ["model"] = "opus", ["at"] = "2026-10-12T09:00:00Z", ["tokens"] = -1 });
        stored.Add(JsonValue.Create(7));

        var loaded = new TurnCostHistory(clock);
        loaded.Load(stored);
        Assert.Equal(4, loaded.Count("opus"));
        Assert.Equal(turns.P90("opus"), loaded.P90("opus"), 6);
        Assert.Equal("kept", loaded.ToJson()[0]!["note"]!.GetValue<string>());
        loaded.Load(null);
        Assert.Equal(0, loaded.Count("opus"));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void The_calibration_document_holds_the_intervals_and_the_turns_and_keeps_both_through_a_reopen()
    {
        var clock = new ManualTimeProvider(T0);
        var calibrator = new UsageCalibrator(clock);
        var turns = new TurnCostHistory(clock);
        var percent = 10.0;
        calibrator.Observe("claude:1", new ReserveWindow("w", ReserveWindowKind.FiveHour, percent, T0, T0.AddHours(5)), false);
        for (var i = 1; i <= 5; i++)
        {
            calibrator.Spend("claude:1", "opus", 300_000);
            turns.Record("opus", 150_000, T0.AddMinutes(i));
            percent += 3;
            calibrator.Observe("claude:1", new ReserveWindow("w", ReserveWindowKind.FiveHour, percent, T0.AddMinutes(i), T0.AddHours(5)), false);
        }
        using (var store = AssistantStore.Open(temp.Roots))
            store.SaveCalibration(calibrator.ToJson(turns.ToJson())!);

        using var reopened = AssistantStore.Open(temp.Roots);
        var document = reopened.Calibration()!;
        var again = new UsageCalibrator(clock);
        var againTurns = new TurnCostHistory(clock);
        again.Load(document);
        againTurns.Load(document["turns"]);
        Assert.Equal(calibrator.RateFor("claude:1", "claude", "opus", ReserveWindowKind.FiveHour), again.RateFor("claude:1", "claude", "opus", ReserveWindowKind.FiveHour));
        Assert.True(again.RateFor("claude:1", "claude", "opus", ReserveWindowKind.FiveHour).Calibrated);
        Assert.Equal(5, againTurns.Count("opus"));
        Assert.True(File.Exists(Path.Combine(reopened.Root, AssistantStore.CalibrationFile)));
        Assert.Null(reopened.Reserves());
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void A_document_that_does_not_parse_reads_as_nothing_and_a_half_written_one_is_cleaned_at_open()
    {
        string root;
        using (var store = AssistantStore.Open(temp.Roots))
        {
            root = store.Root;
            store.SaveReserves(new JsonObject { ["version"] = 1, ["rules"] = new JsonArray() });
        }
        File.WriteAllText(Path.Combine(root, AssistantStore.ReservesFile), "{ \"version\": 1, \"rules\": [");   // cut off
        File.WriteAllText(Path.Combine(root, ".reserves.json.0123456789abcdef.tmp"), "{ half");
        using var reopened = AssistantStore.Open(temp.Roots);
        Assert.Null(reopened.Reserves());
        Assert.Equal(1, reopened.OpenReport.TempFilesRemoved);
        Assert.False(File.Exists(Path.Combine(root, ".reserves.json.0123456789abcdef.tmp")));
        var settings = new ReserveSettings();
        settings.Load(reopened.Reserves());
        Assert.Empty(settings.Rules);
    }
}
