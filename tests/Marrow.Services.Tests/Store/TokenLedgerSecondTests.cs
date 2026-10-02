using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Store;

// Tests for src/Marrow.Services/Store/TokenLedger.cs, the port of DroppyCode/Services/Store/TokenLedger.swift (B3 52bc792):
// the load-time roll-forward of live Codex spend (:359-377), the once-per-process history scan (:155-182) and the
// fingerprint store's migration ladder (:269-319). Where a record cites a different line than the pinned Swift has, the
// Swift lines are the ones cited here.
public sealed class TokenLedgerSecondTests
{
    private const string IdA = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string IdB = "bbbbbbbb-1111-2222-3333-444444444444";
    private const string IdC = "cccccccc-1111-2222-3333-444444444444";
    private const string IdD = "dddddddd-1111-2222-3333-444444444444";
    private static readonly DateTimeOffset NoonMay1 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- PR-USAGE-097: live Codex spend of an earlier process is settled on load ----

    [Fact]
    [Trait("Req", "PR-USAGE-097")] // TokenLedger.swift:369-376: liveCodex is added into settledCodex by identity, then emptied, on load
    public void Live_codex_spend_recorded_by_an_earlier_process_is_added_into_settled_provenance_on_load()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        TokenLedgerFiles.Write(TokenLedgerFiles.LiveState(scratch),
            "{\"daily\":{\"2026-05-04\":7},\"settledCodex\":{\"" + IdA + "\":5,\"" + IdB + "\":2},\"liveCodex\":{\"" + IdA + "\":3,\"" + IdC + "\":4},\"legacyDaily\":{}}");

        var ledger = TokenLedgerFiles.NewLedger(runtime);
        // The next write, which is when the rolled-forward state reaches the disk (record persistence).
        runtime.OnMain(() => ledger.RecordCodexSpend(6, IdD, NoonMay1));
        runtime.OnMain(() => ledger.FlushLiveSpend());

        var path = TokenLedgerFiles.LiveState(scratch);
        var settled = TokenLedgerFiles.ReadMap(path, "settledCodex");
        Assert.Equal(3, settled.Count);
        Assert.Equal(8, settled[IdA]); // 5 settled + 3 live, the same identity
        Assert.Equal(2, settled[IdB]); // untouched
        Assert.Equal(4, settled[IdC]); // a live-only identity enters settled
        // The roll-forward happened on load: this process's own spend stays live and out of the settled map.
        var live = TokenLedgerFiles.ReadMap(path, "liveCodex");
        Assert.Equal(new Dictionary<string, long> { [IdD] = 6 }, live);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-097")] // TokenLedger.swift:369-376: after a run with no Codex spend the roll-forward is a no-op
    public void With_no_live_codex_spend_the_roll_forward_leaves_settled_provenance_as_it_was()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        TokenLedgerFiles.Write(TokenLedgerFiles.LiveState(scratch),
            "{\"daily\":{},\"settledCodex\":{\"" + IdA + "\":5},\"liveCodex\":{},\"legacyDaily\":{}}");

        var ledger = TokenLedgerFiles.NewLedger(runtime);
        runtime.OnMain(() => ledger.Record(1, NoonMay1));
        runtime.OnMain(() => ledger.FlushLiveSpend());

        var path = TokenLedgerFiles.LiveState(scratch);
        Assert.Equal(new Dictionary<string, long> { [IdA] = 5 }, TokenLedgerFiles.ReadMap(path, "settledCodex"));
        Assert.Empty(TokenLedgerFiles.ReadMap(path, "liveCodex"));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-097")] // TokenLedger.swift:369-376, :359-367: a launch after a run that used Codex settles that run's spend
    public void A_launch_after_a_run_that_used_codex_moves_that_runs_spend_into_settled_provenance()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var first = TokenLedgerFiles.NewLedger(runtime);
        runtime.OnMain(() => first.RecordCodexSpend(9, IdA.ToUpperInvariant(), NoonMay1)); // recorded under the lowercased identity (:125)
        runtime.OnMain(() => first.FlushLiveSpend());
        var path = TokenLedgerFiles.LiveState(scratch);
        Assert.Equal(new Dictionary<string, long> { [IdA] = 9 }, TokenLedgerFiles.ReadMap(path, "liveCodex"));

        var second = TokenLedgerFiles.NewLedger(runtime); // the next launch
        runtime.OnMain(() => second.Record(1, NoonMay1));
        runtime.OnMain(() => second.FlushLiveSpend());

        Assert.Equal(new Dictionary<string, long> { [IdA] = 9 }, TokenLedgerFiles.ReadMap(path, "settledCodex"));
        Assert.Empty(TokenLedgerFiles.ReadMap(path, "liveCodex"));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-097")] // TokenLedger.swift:369-376, :165-174: the roll-forward is in place before any scan runs
    public void Spend_rolled_forward_on_load_is_subtracted_from_that_rollouts_growth_by_the_first_scan()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var home = TokenHistoryScanner.HomeOverride.Override(scratch.Home);
        TokenLedgerFiles.Write(TokenLedgerFiles.LiveState(scratch),
            "{\"daily\":{},\"settledCodex\":{},\"liveCodex\":{\"" + IdA + "\":100},\"legacyDaily\":{}}");
        var before = runtime.Time.GetUtcNow() - TimeSpan.FromDays(1);
        TokenLedgerFiles.WriteRollout(scratch, "rollout-2026-05-01T12-00-00-" + IdA + ".jsonl", before, TokenLedgerFiles.TokenCountLine(100, 100, "2026-05-01T12:00:00Z"));
        TokenLedgerFiles.WriteRollout(scratch, "rollout-2026-05-02T12-00-00-" + IdB + ".jsonl", before, TokenLedgerFiles.TokenCountLine(40, 40, "2026-05-02T12:00:00Z"));

        var ledger = TokenLedgerFiles.NewLedger(runtime);
        runtime.Loop.Run(() => ledger.RefreshIfNeeded());

        Assert.False(ledger.DailyTotals.ContainsKey("2026-05-01")); // all 100 tokens were already counted live by the earlier run
        Assert.Equal(40, ledger.DailyTotals["2026-05-02"]); // a rollout with no live spend counts in full
    }

    // ---- PR-USAGE-098: the history scan runs at most once per process ----

    [Fact]
    [Trait("Req", "PR-USAGE-098")] // TokenLedger.swift:155-182, :95-97: the cutoff is the launch moment, and totals merge all three sources
    public void The_first_refresh_scans_rollouts_older_than_the_launch_moment_and_merges_history_live_and_legacy_totals()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var home = TokenHistoryScanner.HomeOverride.Override(scratch.Home);
        runtime.Defaults.Set(TokenLedgerFiles.LegacyLiveDailyKey, new Dictionary<string, object> { ["2026-05-01"] = 5L });
        var ledger = TokenLedgerFiles.NewLedger(runtime); // launch: 2026-09-22 12:00
        var launch = runtime.Time.GetUtcNow();
        runtime.OnMain(() => ledger.Record(7, NoonMay1));
        runtime.Advance(TimeSpan.FromHours(1)); // the scan happens an hour after the launch
        TokenLedgerFiles.WriteRollout(scratch, "rollout-a-" + IdA + ".jsonl", launch - TimeSpan.FromDays(1), TokenLedgerFiles.TokenCountLine(100, 100, "2026-05-01T12:00:00Z"));
        // Written after launch but before the scan: live recording owns it, so a cutoff taken at scan time would count it.
        TokenLedgerFiles.WriteRollout(scratch, "rollout-b-" + IdB + ".jsonl", launch + TimeSpan.FromMinutes(30), TokenLedgerFiles.TokenCountLine(40, 40, "2026-05-02T12:00:00Z"));

        runtime.Loop.Run(() => ledger.RefreshIfNeeded());

        Assert.Equal(112, ledger.DailyTotals["2026-05-01"]); // 5 legacy + 7 live + 100 history
        Assert.False(ledger.DailyTotals.ContainsKey("2026-05-02"));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-098")] // TokenLedger.swift:155-160: every later call in the same process returns immediately
    public void Later_refreshes_in_the_same_process_scan_nothing_again()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var home = TokenHistoryScanner.HomeOverride.Override(scratch.Home);
        var ledger = TokenLedgerFiles.NewLedger(runtime);
        var old = runtime.Time.GetUtcNow() - TimeSpan.FromDays(2);
        TokenLedgerFiles.WriteRollout(scratch, "rollout-a-" + IdA + ".jsonl", old, TokenLedgerFiles.TokenCountLine(100, 100, "2026-05-01T12:00:00Z"));
        runtime.Loop.Run(() => ledger.RefreshIfNeeded());
        Assert.Equal(100, ledger.DailyTotals["2026-05-01"]);
        var saved = File.ReadAllBytes(TokenLedgerFiles.FingerprintsV3(scratch));

        TokenLedgerFiles.WriteRollout(scratch, "rollout-b-" + IdB + ".jsonl", old, TokenLedgerFiles.TokenCountLine(40, 40, "2026-05-02T12:00:00Z"));
        runtime.Loop.Run(() => ledger.RefreshIfNeeded());

        Assert.False(ledger.DailyTotals.ContainsKey("2026-05-02")); // the new rollout was not read
        Assert.Equal(saved, File.ReadAllBytes(TokenLedgerFiles.FingerprintsV3(scratch))); // and no second fingerprint save
    }

    [Fact]
    [Trait("Req", "PR-USAGE-098")] // TokenLedger.swift:157-159, :165-177: the flag is set before the await, and the scan runs off the main actor
    public void A_second_call_made_while_the_first_scan_is_in_flight_returns_at_once_and_the_scan_leaves_the_main_actor()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var home = TokenHistoryScanner.HomeOverride.Override(scratch.Home);
        var ledger = TokenLedgerFiles.NewLedger(runtime);
        TokenLedgerFiles.WriteRollout(scratch, "rollout-a-" + IdA + ".jsonl", runtime.Time.GetUtcNow() - TimeSpan.FromDays(2), TokenLedgerFiles.TokenCountLine(100, 100, "2026-05-01T12:00:00Z"));
        Task first = Task.CompletedTask;
        var firstDoneOnMain = true;
        var secondDoneOnMain = false;

        runtime.OnMain(() =>
        {
            first = ledger.RefreshIfNeeded();
            firstDoneOnMain = first.IsCompleted; // a scan run inline on main would have finished by now
            secondDoneOnMain = ledger.RefreshIfNeeded().IsCompleted; // returns at once: didStartLoad is already set
        });

        Assert.False(firstDoneOnMain);
        Assert.True(secondDoneOnMain);
        runtime.Loop.RunUntil(() => first.IsCompleted);
        Assert.Equal(100, ledger.DailyTotals["2026-05-01"]);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-098")] // TokenLedger.swift:175-178: the scan saves its fingerprints, one per counted rollout
    public void The_scan_saves_one_fingerprint_for_each_rollout_it_counted()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var home = TokenHistoryScanner.HomeOverride.Override(scratch.Home);
        var ledger = TokenLedgerFiles.NewLedger(runtime);
        var launch = runtime.Time.GetUtcNow();
        var path = TokenLedgerFiles.WriteRollout(scratch, "rollout-a-" + IdA + ".jsonl", launch - TimeSpan.FromDays(2), TokenLedgerFiles.TokenCountLine(100, 100, "2026-05-01T12:00:00Z"));
        TokenLedgerFiles.WriteRollout(scratch, "rollout-b-" + IdB + ".jsonl", launch + TimeSpan.FromMinutes(5), TokenLedgerFiles.TokenCountLine(40, 40, "2026-05-02T12:00:00Z"));

        runtime.Loop.Run(() => ledger.RefreshIfNeeded());

        using var document = JsonDocument.Parse(File.ReadAllBytes(TokenLedgerFiles.FingerprintsV3(scratch)));
        var entry = Assert.Single(document.RootElement.EnumerateObject());
        Assert.Equal(path, entry.Name);
        Assert.Equal(100, entry.Value.GetProperty("tokens").GetInt64());
        Assert.Equal("2026-05-01", entry.Value.GetProperty("day").GetString());
        Assert.Equal(IdA, entry.Value.GetProperty("identity").GetString());
    }

    [Fact]
    [Trait("Req", "PR-USAGE-098")] // TokenLedger.swift:99-103, :155: nothing but refreshIfNeeded scans, so an app that never opens Settings never does
    public void Building_the_ledger_scans_nothing()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var home = TokenHistoryScanner.HomeOverride.Override(scratch.Home);
        TokenLedgerFiles.WriteRollout(scratch, "rollout-a-" + IdA + ".jsonl", runtime.Time.GetUtcNow() - TimeSpan.FromDays(2), TokenLedgerFiles.TokenCountLine(100, 100, "2026-05-01T12:00:00Z"));

        var ledger = TokenLedgerFiles.NewLedger(runtime);
        runtime.OnMain(() => ledger.Record(3, NoonMay1));
        runtime.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(3, ledger.DailyTotals["2026-05-01"]);
        Assert.False(File.Exists(TokenLedgerFiles.FingerprintsV3(scratch)));
    }

    // ---- PR-USAGE-103: the fingerprint file and its migration ladder ----

    private static string FingerprintJson(string path, string? identity, long tokens = 11) =>
        JsonSerializer.Serialize(path) + ":{\"mtime\":1700000000.5,\"size\":2048,\"tokens\":" + tokens + ",\"day\":\"2026-03-04\"" +
        (identity is null ? "" : ",\"identity\":" + JsonSerializer.Serialize(identity)) + "}";

    [Fact]
    [Trait("Req", "PR-USAGE-103")] // TokenLedger.swift:281-288: a v1 file is deleted on sight, even with a v3 file present, and never migrated
    public void A_v1_file_is_deleted_without_being_migrated_even_when_the_v3_file_is_present()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        TokenLedgerFiles.Write(TokenLedgerFiles.FingerprintsV1(scratch), "{" + FingerprintJson("C:\\old\\one.jsonl", IdA) + "}");
        TokenLedgerFiles.Write(TokenLedgerFiles.FingerprintsV3(scratch), "{" + FingerprintJson("C:\\new\\three.jsonl", IdB, 77) + "}");

        var loaded = CodexFingerprintStore.Load();

        Assert.False(File.Exists(TokenLedgerFiles.FingerprintsV1(scratch)));
        var only = Assert.Single(loaded);
        Assert.Equal("C:\\new\\three.jsonl", only.Key);
        Assert.Equal(77, only.Value.Tokens);
        Assert.Equal(IdB, only.Value.Identity);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-103")] // TokenLedger.swift:281-284, :305-307: a v1 file alone is removed and nothing is carried over from it
    public void A_v1_file_alone_is_removed_and_yields_an_empty_map_with_no_v3_file_written()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        TokenLedgerFiles.Write(TokenLedgerFiles.FingerprintsV1(scratch), "{" + FingerprintJson("C:\\old\\one.jsonl", IdA) + "}");

        Assert.Empty(CodexFingerprintStore.Load());

        Assert.False(File.Exists(TokenLedgerFiles.FingerprintsV1(scratch)));
        Assert.False(File.Exists(TokenLedgerFiles.FingerprintsV3(scratch)));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-103")] // TokenLedger.swift:241-248, :285-288: the v3 file is used as it decodes, with tokens, day and identity defaulted
    public void A_v3_file_is_used_when_it_decodes_and_fills_missing_tokens_day_and_identity_with_defaults()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        TokenLedgerFiles.Write(TokenLedgerFiles.FingerprintsV3(scratch), "{\"C:\\\\p\\\\a.jsonl\":{\"mtime\":5,\"size\":9}}");
        TokenLedgerFiles.Write(TokenLedgerFiles.FingerprintsV2(scratch), "{" + FingerprintJson("C:\\v2\\b.jsonl", IdA) + "}");

        var loaded = CodexFingerprintStore.Load();

        var only = Assert.Single(loaded);
        Assert.Equal("C:\\p\\a.jsonl", only.Key);
        Assert.Equal(5, only.Value.Mtime);
        Assert.Equal(9UL, only.Value.Size);
        Assert.Equal(0, only.Value.Tokens);
        Assert.Equal("", only.Value.Day);
        Assert.Equal("", only.Value.Identity);
        Assert.True(File.Exists(TokenLedgerFiles.FingerprintsV2(scratch))); // a usable v3 leaves v2 alone
    }

    [Fact]
    [Trait("Req", "PR-USAGE-103")] // TokenLedger.swift:285-298: a v3 file that does not decode is passed over for the v2 file
    public void A_v3_file_that_does_not_decode_falls_through_to_the_v2_file()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        TokenLedgerFiles.Write(TokenLedgerFiles.FingerprintsV3(scratch), "{\"C:\\\\p\\\\a.jsonl\":{\"mtime\":5}}"); // size missing: a decode error
        TokenLedgerFiles.Write(TokenLedgerFiles.FingerprintsV2(scratch), "{" + FingerprintJson("C:\\v2\\b.jsonl", IdA) + "}");

        var loaded = CodexFingerprintStore.Load();

        Assert.Equal(["C:\\v2\\b.jsonl"], loaded.Keys.ToArray());
    }

    [Fact]
    [Trait("Req", "PR-USAGE-103")] // TokenLedger.swift:289-304: v2 is migrated forward with the identity filled from the path, deleted, and saved as v3
    public void A_v2_file_is_migrated_forward_filling_empty_identities_from_the_path_then_deleted_and_saved_as_v3()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var pathWithId = "C:\\sessions\\rollout-2026-03-04T10-00-00-" + IdA.ToUpperInvariant() + ".jsonl";
        var pathWithoutId = "C:\\sessions\\no-session-id.jsonl";
        var kept = "C:\\sessions\\kept.jsonl";
        TokenLedgerFiles.Write(TokenLedgerFiles.FingerprintsV2(scratch),
            "{" + FingerprintJson(pathWithId, null) + "," + FingerprintJson(pathWithoutId, "") + "," + FingerprintJson(kept, IdB) + "}");

        var loaded = CodexFingerprintStore.Load();

        Assert.Equal(IdA, loaded[pathWithId].Identity); // the last UUID-shaped run of the name, lowercased
        Assert.Equal(pathWithoutId, loaded[pathWithoutId].Identity); // no session id: the path itself
        Assert.Equal(IdB, loaded[kept].Identity); // an identity already there is kept
        Assert.Equal(11, loaded[kept].Tokens);
        Assert.False(File.Exists(TokenLedgerFiles.FingerprintsV2(scratch)));
        using var saved = JsonDocument.Parse(File.ReadAllBytes(TokenLedgerFiles.FingerprintsV3(scratch)));
        Assert.Equal(3, saved.RootElement.EnumerateObject().Count());
        Assert.Equal(IdA, saved.RootElement.GetProperty(pathWithId).GetProperty("identity").GetString());
        Assert.Equal("2026-03-04", saved.RootElement.GetProperty(kept).GetProperty("day").GetString());
        Assert.Equal(11, saved.RootElement.GetProperty(kept).GetProperty("tokens").GetInt64());
    }

    [Fact]
    [Trait("Req", "PR-USAGE-103")] // TokenLedger.swift:305-313: the preference dictionary is read, removed, converted entry by entry and saved as v3
    public void The_legacy_preference_dictionary_is_removed_converted_and_saved_as_v3_dropping_records_it_cannot_read()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var good = "C:\\sessions\\rollout-" + IdA + ".jsonl";
        runtime.Defaults.Set(TokenLedgerFiles.LegacyCodexFilesKey, new Dictionary<string, object>
        {
            [good] = new Dictionary<string, object> { ["mtime"] = 1700000000.5, ["size"] = 4096L, ["tokens"] = 12L, ["day"] = "2026-03-04" },
            ["C:\\intsize.jsonl"] = new Dictionary<string, object> { ["mtime"] = 10.0, ["size"] = 7, ["day"] = "2026-03-05", ["identity"] = IdB },
            ["C:\\nomtime.jsonl"] = new Dictionary<string, object> { ["size"] = 1L, ["day"] = "2026-03-04" },
            ["C:\\noday.jsonl"] = new Dictionary<string, object> { ["mtime"] = 1.0, ["size"] = 1L },
            ["C:\\textsize.jsonl"] = new Dictionary<string, object> { ["mtime"] = 1.0, ["size"] = "big", ["day"] = "2026-03-04" },
        });

        var loaded = CodexFingerprintStore.Load();

        Assert.Null(runtime.Defaults.Object(TokenLedgerFiles.LegacyCodexFilesKey)); // drained from the preferences
        Assert.Equal(2, loaded.Count);
        Assert.Equal(IdA, loaded[good].Identity); // empty identity: from the path
        Assert.Equal(4096UL, loaded[good].Size);
        Assert.Equal(12, loaded[good].Tokens);
        Assert.Equal(1700000000.5, loaded[good].Mtime);
        Assert.Equal(7UL, loaded["C:\\intsize.jsonl"].Size); // a size that is an Int, not an NSNumber, still counts
        Assert.Equal(0, loaded["C:\\intsize.jsonl"].Tokens); // tokens absent: 0
        Assert.Equal(IdB, loaded["C:\\intsize.jsonl"].Identity);
        using var saved = JsonDocument.Parse(File.ReadAllBytes(TokenLedgerFiles.FingerprintsV3(scratch)));
        Assert.Equal(2, saved.RootElement.EnumerateObject().Count());
        Assert.Equal(12, saved.RootElement.GetProperty(good).GetProperty("tokens").GetInt64());
    }

    [Fact]
    [Trait("Req", "PR-USAGE-103")] // TokenLedger.swift:305-306: the preference value is read as a dictionary of dictionaries or not at all
    public void A_legacy_preference_value_that_is_not_a_dictionary_of_dictionaries_is_left_alone_and_nothing_is_saved()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        runtime.Defaults.Set(TokenLedgerFiles.LegacyCodexFilesKey, new Dictionary<string, object>
        {
            ["C:\\a.jsonl"] = new Dictionary<string, object> { ["mtime"] = 1.0, ["size"] = 1L, ["day"] = "2026-03-04" },
            ["C:\\b.jsonl"] = "not a record",
        });

        Assert.Empty(CodexFingerprintStore.Load());

        Assert.NotNull(runtime.Defaults.Object(TokenLedgerFiles.LegacyCodexFilesKey)); // the cast failed, so the key was never removed
        Assert.False(File.Exists(TokenLedgerFiles.FingerprintsV3(scratch)));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-103")] // TokenLedger.swift:316-319, :302-303, :312-313: a failed save is silent and the loaded map is still returned
    public void A_save_that_fails_is_silent_and_the_migrated_map_is_still_returned()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        _ = Storage.Root; // the root is made on first use
        Directory.Delete(scratch.Root, recursive: true); // now the write has no parent folder to land in
        runtime.Defaults.Set(TokenLedgerFiles.LegacyCodexFilesKey, new Dictionary<string, object>
        {
            ["C:\\a.jsonl"] = new Dictionary<string, object> { ["mtime"] = 1.0, ["size"] = 1L, ["day"] = "2026-03-04", ["identity"] = IdA },
        });

        var loaded = CodexFingerprintStore.Load();

        Assert.Equal(IdA, Assert.Single(loaded).Value.Identity);
        Assert.False(Directory.Exists(scratch.Root));
    }
}
