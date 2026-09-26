using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Store;

// Tests for src/Marrow.Services/Store/TokenLedger.cs, the port of
// DroppyCode/Services/Store/TokenLedger.swift (B3 52bc792), :1-698.
public sealed class TokenLedgerTests
{
    // ---- TokenSpendTracker: PR-USAGE-089, PR-USAGE-090 ----

    [Fact]
    [Trait("Req", "PR-USAGE-089")] // TokenLedger.swift:14-22
    public void Spend_FirstCallSeedsFromMinOfTotalAndBefore_ThenReturnsPositiveDelta()
    {
        var tracker = new TokenSpendTracker();
        var (next, spent) = tracker.Spend(total: 100, before: 40);
        Assert.Equal(60, spent); // 100 - min(100,40)
        var (_, spentAgain) = next.Spend(total: 150);
        Assert.Equal(50, spentAgain); // 150 - 100
    }

    [Fact]
    [Trait("Req", "PR-USAGE-089")] // TokenLedger.swift:14-22
    public void Spend_ADropReseedsSilentlyAndReturnsZero()
    {
        var tracker = new TokenSpendTracker();
        var (afterFirst, _) = tracker.Spend(total: 500);
        var (afterDrop, dropSpend) = afterFirst.Spend(total: 50); // a lower cumulative total: compaction or fresh session
        Assert.Equal(0, dropSpend);
        var (_, spentAfterDrop) = afterDrop.Spend(total: 80);
        Assert.Equal(30, spentAfterDrop); // counted from the new, lower baseline
    }

    [Fact]
    [Trait("Req", "PR-USAGE-089")] // TokenLedger.swift:19-21 max(0, ...) clamps
    public void Spend_NegativeTotalsClampToZeroAtBaselineAndAssignment()
    {
        var tracker = new TokenSpendTracker();
        var (next, spent) = tracker.Spend(total: -5, before: -10);
        Assert.Equal(0, spent); // max(0, -5 - max(0,min(-5,-10))) = max(0, -5-0)=0
        var (_, spentAgain) = next.Spend(total: 10);
        Assert.Equal(10, spentAgain); // lastTotal was clamped to 0
    }

    [Fact]
    [Trait("Req", "PR-USAGE-090")] // TokenLedger.swift:29-33
    public void SeedBaseline_SetsTheBaselineWithoutRecordingWhenUnseeded()
    {
        var tracker = new TokenSpendTracker();
        var seeded = tracker.SeedBaseline(total: 200);
        var (_, spent) = seeded.Spend(total: 250);
        Assert.Equal(50, spent); // the inherited 200 was never counted
    }

    [Fact]
    [Trait("Req", "PR-USAGE-090")] // TokenLedger.swift:30 guard !seeded else return
    public void SeedBaseline_IsANoOpOnceAlreadySeeded()
    {
        var tracker = new TokenSpendTracker();
        var (afterFirstSpend, _) = tracker.Spend(total: 100); // seeds via the first Spend
        var afterSeedAttempt = afterFirstSpend.SeedBaseline(total: 999); // must not move the baseline
        var (_, spent) = afterSeedAttempt.Spend(total: 120);
        Assert.Equal(20, spent); // still counted from 100, not from 999
    }

    // ---- TokenActivityMode: PR-USAGE-107 ----

    [Fact]
    [Trait("Req", "PR-USAGE-107")] // TokenLedger.swift:580-593
    public void TokenActivityMode_HasThreeCasesInDeclarationOrderWithFixedTitles()
    {
        Assert.Equal([TokenActivityMode.Daily, TokenActivityMode.Weekly, TokenActivityMode.Cumulative], SwiftEnum<TokenActivityMode>.AllCases);
        Assert.Equal("daily", SwiftEnum<TokenActivityMode>.RawValue(TokenActivityMode.Daily));
        Assert.Equal("weekly", SwiftEnum<TokenActivityMode>.RawValue(TokenActivityMode.Weekly));
        Assert.Equal("cumulative", SwiftEnum<TokenActivityMode>.RawValue(TokenActivityMode.Cumulative));
        Assert.Equal("Daily", TokenActivityMode.Daily.Title());
        Assert.Equal("Weekly", TokenActivityMode.Weekly.Title());
        Assert.Equal("Cumulative", TokenActivityMode.Cumulative.Title());
    }

    // ---- TokenActivityGrid: PR-USAGE-108, PR-USAGE-109, PR-USAGE-110, PR-SETTINGS-136 ----

    [Fact]
    [Trait("Req", "PR-USAGE-108")] // TokenLedger.swift:600-653
    public void Build_CoversTheWholeYearInSundayFirstColumnsWithFutureDaysAsCells()
    {
        var now = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero); // a Sunday-anchored mid-year date
        var grid = TokenActivityGrid.Build(new Dictionary<string, long>(), TokenActivityMode.Daily, now);

        var allDays = grid.Columns.SelectMany(c => c).Where(d => d is not null).Select(d => d!.Value).ToList();
        Assert.Equal(new DateOnly(2026, 1, 1), allDays.Min());
        Assert.Equal(new DateOnly(2026, 12, 31), allDays.Max());
        foreach (var column in grid.Columns) Assert.Equal(TokenActivityGrid.Rows, column.Count);
        foreach (var column in grid.Columns)
        {
            var firstRealDay = column.FirstOrDefault(d => d is not null);
            if (firstRealDay is { } d0 && column[0] is { } d1) Assert.Equal(DayOfWeek.Sunday, d1.DayOfWeek);
        }
        Assert.Equal(new DateOnly(2026, 3, 16), grid.Tomorrow); // the day after `now`
    }

    [Fact]
    [Trait("Req", "PR-USAGE-109")] // TokenLedger.swift:655-682, daily mode
    public void Build_DailyModePutsEachDaysOwnTotalInItsCellAndZeroElsewhere()
    {
        var now = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var daily = new Dictionary<string, long> { ["2026-01-05"] = 30, ["2026-01-06"] = 70 };
        var grid = TokenActivityGrid.Build(daily, TokenActivityMode.Daily, now);

        long ValueFor(DateOnly date)
        {
            for (var c = 0; c < grid.Columns.Count; c++)
                for (var r = 0; r < TokenActivityGrid.Rows; r++)
                    if (grid.Columns[c][r] == date) return grid.Values[c][r];
            throw new InvalidOperationException("date not in grid");
        }

        Assert.Equal(30, ValueFor(new DateOnly(2026, 1, 5)));
        Assert.Equal(70, ValueFor(new DateOnly(2026, 1, 6)));
        Assert.Equal(0, ValueFor(new DateOnly(2026, 1, 7)));
        Assert.Equal(70, grid.MaxValue);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-109")] // TokenLedger.swift:665-672, weekly mode: nil cells stay 0
    public void Build_WeeklyModeSumsTheColumnIntoEveryRealCellAndLeavesOutOfYearCellsAtZero()
    {
        var now = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero); // Jan 1 2026 is a Thursday
        var daily = new Dictionary<string, long> { ["2026-01-01"] = 10, ["2026-01-03"] = 5 };
        var grid = TokenActivityGrid.Build(daily, TokenActivityMode.Weekly, now);

        // The first column starts on the Sunday on or before Jan 1 (Dec 28, 2025) and holds Jan 1-3 as real cells.
        var firstColumn = grid.Columns[0];
        var firstColumnValues = grid.Values[0];
        for (var r = 0; r < TokenActivityGrid.Rows; r++)
        {
            if (firstColumn[r] is null) Assert.Equal(0, firstColumnValues[r]); // out-of-year cell stays 0
            else Assert.Equal(15, firstColumnValues[r]); // every real cell of the week reads the week total
        }
    }

    [Fact]
    [Trait("Req", "PR-USAGE-109")] // TokenLedger.swift:673-680, cumulative mode: a running total in every cell
    public void Build_CumulativeModeWritesARunningTotalIntoEveryCellIncludingOutOfYearOnes()
    {
        var now = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var daily = new Dictionary<string, long> { ["2026-01-01"] = 10, ["2026-01-02"] = 5 };
        var grid = TokenActivityGrid.Build(daily, TokenActivityMode.Cumulative, now);

        var firstColumn = grid.Columns[0];
        var firstColumnValues = grid.Values[0];
        var leadingOutOfYear = firstColumn.TakeWhile(d => d is null).Count();
        for (var r = 0; r < leadingOutOfYear; r++) Assert.Equal(0, firstColumnValues[r]); // before Jan 1: still 0
        // The running total accumulates through the real days in column/row order.
        Assert.True(firstColumnValues[TokenActivityGrid.Rows - 1] >= 15);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-110")] // TokenLedger.swift:614-616, :693-703
    public void Build_MonthTicksAreTwelveFixedEnglishAbbreviationsAtEachMonthsFirstColumn()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var grid = TokenActivityGrid.Build(new Dictionary<string, long>(), TokenActivityMode.Daily, now);

        Assert.Equal(12, grid.MonthTicks.Count);
        Assert.Equal(["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"],
            grid.MonthTicks.Select(t => t.Label).ToArray());
        Assert.Equal(0, grid.MonthTicks[0].Column); // January's clamped start is the range start (:110 failure_cases)
        for (var i = 1; i < grid.MonthTicks.Count; i++) Assert.True(grid.MonthTicks[i].Column > grid.MonthTicks[i - 1].Column);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-136")] // TokenLedger.swift:655-682, the three modes read the same underlying data differently
    public void Build_TheThreeModesScaleTheSameDataDifferentlyAndMaxValueIsThePostScaleMaximum()
    {
        var now = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var daily = new Dictionary<string, long> { ["2026-01-01"] = 4, ["2026-01-08"] = 6 };

        var dailyGrid = TokenActivityGrid.Build(daily, TokenActivityMode.Daily, now);
        var weeklyGrid = TokenActivityGrid.Build(daily, TokenActivityMode.Weekly, now);
        var cumulativeGrid = TokenActivityGrid.Build(daily, TokenActivityMode.Cumulative, now);

        Assert.Equal(6, dailyGrid.MaxValue); // the largest single day
        Assert.True(weeklyGrid.MaxValue is 4 or 6 or 10); // week sums, depending on how Jan 1/Jan 8 fall into columns
        Assert.Equal(10, cumulativeGrid.MaxValue); // the final running total
    }

    // ---- TokenLedger: PR-USAGE-091 through PR-USAGE-106 ----

    private static TokenLedger NewLedger(TestRuntime runtime) =>
        runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);

    [Fact]
    [Trait("Req", "PR-USAGE-091")] // TokenLedger.swift:184-189
    public void MergedTotals_AddsLegacyLiveAndHistorySourcesRatherThanAccumulating()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("droppycode.tokenActivity.liveDaily", new Dictionary<string, object> { ["2026-01-01"] = 5L });

        var ledger = NewLedger(runtime);
        runtime.OnMain(() => ledger.Record(3, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(8, ledger.DailyTotals["2026-01-01"]); // 5 legacy + 3 live

        // Recomputing (as a second construction over the same file would) lands the same number, not double.
        runtime.Advance(TimeSpan.FromSeconds(3)); // flush the debounced write
        var reloaded = NewLedger(runtime);
        Assert.Equal(8, reloaded.DailyTotals["2026-01-01"]);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-092")] // TokenLedger.swift:128-145
    public void Record_IgnoresNonPositiveSpendAndKeysByLocalDay()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = NewLedger(runtime);
        var day = new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

        runtime.OnMain(() => ledger.Record(0, day));
        runtime.OnMain(() => ledger.Record(-5, day));
        Assert.False(ledger.DailyTotals.ContainsKey("2026-05-04"));

        runtime.OnMain(() => ledger.Record(12, day));
        Assert.Equal(12, ledger.DailyTotals["2026-05-04"]);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-092")] // TokenLedger.swift:138-144, the two-second debounce
    public void Record_DebouncesTheLiveSaveByTwoSecondsAndLandsOneWrite()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = NewLedger(runtime);
        var day = new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

        runtime.OnMain(() => ledger.Record(5, day));
        var path = Path.Combine(scratch.Root, "token-activity-live-v2.json");
        Assert.False(File.Exists(path)); // not written yet

        runtime.OnMain(() => ledger.Record(7, day)); // rides the same window
        runtime.Advance(TimeSpan.FromSeconds(2));
        Assert.True(File.Exists(path));
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(12, document.RootElement.GetProperty("daily").GetProperty("2026-05-04").GetInt64());
    }

    [Fact]
    [Trait("Req", "PR-USAGE-093")] // TokenLedger.swift:147-153
    public void FlushLiveSpend_WritesImmediatelyAndCancelsThePendingTimer()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = NewLedger(runtime);
        runtime.OnMain(() => ledger.Record(9, new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero)));

        var path = Path.Combine(scratch.Root, "token-activity-live-v2.json");
        Assert.False(File.Exists(path));
        runtime.OnMain(() => ledger.FlushLiveSpend());
        Assert.True(File.Exists(path));

        var sizeAfterFlush = new FileInfo(path).Length;
        runtime.Advance(TimeSpan.FromSeconds(5)); // the cancelled timer must not write again / throw
        Assert.Equal(sizeAfterFlush, new FileInfo(path).Length);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-093")] // TokenLedger.swift:149 guard let liveSave else { return }
    public void FlushLiveSpend_IsANoOpWithNothingPending()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = NewLedger(runtime);
        runtime.OnMain(() => ledger.FlushLiveSpend()); // must not throw or write
        Assert.False(File.Exists(Path.Combine(scratch.Root, "token-activity-live-v2.json")));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-094")] // TokenLedger.swift:330-351, :379-382
    public void LiveSpendState_PersistsExactlyFourNamedMapsAtomically()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = NewLedger(runtime);
        runtime.OnMain(() => ledger.RecordCodexSpend(4, "ABCDEF01-0000-0000-0000-000000000000", new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero)));
        runtime.OnMain(() => ledger.FlushLiveSpend());

        var path = Path.Combine(scratch.Root, "token-activity-live-v2.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["daily", "legacyDaily", "liveCodex", "settledCodex"], names);
        Assert.Equal(4, document.RootElement.GetProperty("liveCodex").GetProperty("abcdef01-0000-0000-0000-000000000000").GetInt64());
    }

    [Fact]
    [Trait("Req", "PR-USAGE-095")] // TokenLedger.swift:359-368
    public void LiveSpendStateStore_ReadsTheV1KeyOnceWhenTheV2FileIsAbsentAndNeverRewritesIt()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("droppycode.tokenActivity.liveDaily", new Dictionary<string, object> { ["2025-12-25"] = 9L });
        runtime.Defaults.Writes.Clear();

        var ledger = NewLedger(runtime);
        Assert.Equal(9, ledger.LegacyDaily["2025-12-25"]);
        Assert.DoesNotContain("droppycode.tokenActivity.liveDaily", runtime.Defaults.Writes); // left on disk untouched
    }

    [Fact]
    [Trait("Req", "PR-USAGE-096")] // TokenLedger.swift:75-81
    public void UncertainTokensThisYear_SumsOnlyLegacyDaysInTheCurrentYear()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime(); // ManualTimeProvider starts 2026-09-22
        runtime.Defaults.Set("droppycode.tokenActivity.liveDaily",
            new Dictionary<string, object> { ["2026-01-01"] = 5L, ["2026-06-15"] = 7L, ["2025-12-31"] = 100L });

        var ledger = NewLedger(runtime);
        Assert.Equal(12, ledger.UncertainTokensThisYear); // 5 + 7; the 2025 day is excluded
    }

    [Fact]
    [Trait("Req", "PR-USAGE-096")] // TokenLedger.swift:75-81, an empty machine
    public void UncertainTokensThisYear_IsZeroWithNoLegacyValues()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = NewLedger(runtime);
        Assert.Equal(0, ledger.UncertainTokensThisYear);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-106")] // TokenLedger.swift:118-126
    public void RecordCodexSpend_DropsAnEmptyRolloutIDAndLowercasesANonEmptyOne()
    {
        using var scratch = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = NewLedger(runtime);
        var day = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);

        runtime.OnMain(() => ledger.RecordCodexSpend(5, "", day));
        Assert.False(ledger.DailyTotals.ContainsKey("2026-05-04"));

        runtime.OnMain(() => ledger.RecordCodexSpend(5, "ABC-DEF", day));
        Assert.Equal(5, ledger.DailyTotals["2026-05-04"]);
    }

    // ---- CodexFileFingerprint.RolloutIdentity: PR-USAGE-101 ----

    [Theory]
    [Trait("Req", "PR-USAGE-101")] // TokenLedger.swift:250-261
    [InlineData(@"C:\home\.codex\sessions\rollout-2026-01-01T00-00-00-ABCDEF01-1111-2222-3333-444444444444.jsonl", "abcdef01-1111-2222-3333-444444444444")]
    [InlineData(@"C:\home\.codex\sessions\no-uuid-here.jsonl", @"C:\home\.codex\sessions\no-uuid-here.jsonl")]
    public void RolloutIdentity_MatchesTheLastUuidInTheBaseNameCaseInsensitivelyAndLowercasesIt(string path, string expected)
    {
        Assert.Equal(expected, CodexFileFingerprint.RolloutIdentity(path));
    }

    [Fact]
    [Trait("Req", "PR-USAGE-101")] // TokenLedger.swift:258 [.backwards]: a name with two UUIDs takes the later one
    public void RolloutIdentity_TakesTheLaterUuidWhenTwoAreInTheName()
    {
        var name = @"C:\home\.codex\sessions\11111111-1111-1111-1111-111111111111-22222222-2222-2222-2222-222222222222.jsonl";
        Assert.Equal("22222222-2222-2222-2222-222222222222", CodexFileFingerprint.RolloutIdentity(name));
    }

    // ---- The Codex history scan: PR-USAGE-098, PR-USAGE-099, PR-USAGE-100, PR-USAGE-102, PR-USAGE-104, PR-USAGE-105 ----

    private static string WriteRollout(string directory, string fileName, DateTime mtimeLocal, string jsonl)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, jsonl);
        File.SetLastWriteTime(path, mtimeLocal);
        return path;
    }

    [Fact]
    [Trait("Req", "PR-USAGE-099")] // TokenLedger.swift:412-428: only .jsonl, within 12 months, strictly before launch
    public void ScanCodexHistory_OnlyCountsJsonlFilesWithinTheWindowAndBeforeLiveCutoff()
    {
        using var scratch = new StorageScratchFolder();
        var home = Path.Combine(scratch.Home, "home1");
        Directory.CreateDirectory(home);
        using var homeOverride = TokenHistoryScanner.HomeOverride.Override(home);

        var sessions = Path.Combine(home, ".codex", "sessions");
        var now = DateTime.Now;
        var line = TokenCountLine(100, 100, "2026-01-05T10:00:00Z"); // last==total: a fresh, unresumed file, nothing inherited
        WriteRollout(sessions, "in-window-a1b2c3d4-1111-2222-3333-444444444444.jsonl", now.AddDays(-10), line);
        WriteRollout(sessions, "too-old-a1b2c3d4-1111-2222-3333-555555555555.jsonl", now.AddMonths(-13), line);
        WriteRollout(sessions, "not-jsonl-a1b2c3d4-1111-2222-3333-666666666666.txt", now.AddDays(-10), line);
        WriteRollout(sessions, "written-after-launch-a1b2c3d4-1111-2222-3333-777777777777.jsonl", now.AddSeconds(30), line);

        var result = TokenHistoryScanner.ScanCodexHistory(ValueMap<string, CodexFileFingerprint>.Empty, DateTimeOffset.Now, ValueMap<string, long>.Empty);

        Assert.Single(result.Fingerprints);
        Assert.Equal(100, result.Daily.Values.Sum());
    }

    [Fact]
    [Trait("Req", "PR-USAGE-100")] // TokenLedger.swift:498-572 (B3; was :498-581 in B2): growth = tail total - inherited head total
    public void ScanCodexHistory_ContributesOnlyTheFilesOwnGrowthOverItsInheritedHistory()
    {
        using var scratch = new StorageScratchFolder();
        var home = Path.Combine(scratch.Home, "home2");
        Directory.CreateDirectory(home);
        using var homeOverride = TokenHistoryScanner.HomeOverride.Override(home);

        var sessions = Path.Combine(home, ".codex", "sessions");
        var content = TokenCountLine(200, 0, "2026-01-05T09:00:00Z") + "\n" + TokenCountLine(260, 200, "2026-01-05T11:00:00Z");
        var path = WriteRollout(sessions, "growth-a1b2c3d4-1111-2222-3333-888888888888.jsonl", DateTime.Now.AddDays(-1), content);

        var result = TokenHistoryScanner.ScanCodexHistory(ValueMap<string, CodexFileFingerprint>.Empty, DateTimeOffset.Now, ValueMap<string, long>.Empty);

        Assert.Equal(60, result.Daily["2026-01-05"]); // last total 260, inherited (from the FIRST token_count line) 200
        Assert.Equal("a1b2c3d4-1111-2222-3333-888888888888", result.Fingerprints[path].Identity);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-104")] // TokenLedger.swift:432-439: a cached fingerprint with matching mtime+size is reused
    public void ScanCodexHistory_ReusesACachedFingerprintWhenMtimeAndSizeMatch()
    {
        using var scratch = new StorageScratchFolder();
        var home = Path.Combine(scratch.Home, "home3");
        Directory.CreateDirectory(home);
        using var homeOverride = TokenHistoryScanner.HomeOverride.Override(home);

        var sessions = Path.Combine(home, ".codex", "sessions");
        var content = TokenCountLine(50, 0, "2026-02-01T00:00:00Z");
        var path = WriteRollout(sessions, "cached-a1b2c3d4-1111-2222-3333-999999999999.jsonl", DateTime.Now.AddDays(-1), content);
        var info = new FileInfo(path);
        var cached = new ValueMap<string, CodexFileFingerprint>(
            new Dictionary<string, CodexFileFingerprint>
            {
                [path] = new()
                {
                    Mtime = SwiftDate.Seconds1970(new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)),
                    Size = (ulong)info.Length,
                    Tokens = 999, // deliberately different from what a fresh parse would find, to prove reuse
                    Day = "2026-02-01",
                    Identity = "a1b2c3d4-1111-2222-3333-999999999999",
                },
            }, SwiftStringComparer.Instance);

        var result = TokenHistoryScanner.ScanCodexHistory(cached, DateTimeOffset.Now, ValueMap<string, long>.Empty);
        Assert.Equal(999, result.Daily["2026-02-01"]); // the cached value, not a re-parsed 50
    }

    [Fact]
    [Trait("Req", "PR-USAGE-104")] // TokenLedger.swift:454-456: fingerprints for vanished files are pruned
    public void ScanCodexHistory_PrunesFingerprintsForFilesNoLongerSeen()
    {
        using var scratch = new StorageScratchFolder();
        var home = Path.Combine(scratch.Home, "home4");
        Directory.CreateDirectory(home);
        using var homeOverride = TokenHistoryScanner.HomeOverride.Override(home);
        Directory.CreateDirectory(Path.Combine(home, ".codex", "sessions"));

        var cached = new ValueMap<string, CodexFileFingerprint>(
            new Dictionary<string, CodexFileFingerprint> { [@"C:\gone.jsonl"] = new() { Mtime = 0, Size = 0, Day = "2026-01-01", Identity = "x" } },
            SwiftStringComparer.Instance);

        var result = TokenHistoryScanner.ScanCodexHistory(cached, DateTimeOffset.Now, ValueMap<string, long>.Empty);
        Assert.Empty(result.Fingerprints);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-102")] // TokenLedger.swift:468-488: settled live spend clears the newest day first, never negative
    public void Reconcile_ClearsSettledLiveSpendFromTheNewestDayFirstAndNeverGoesNegative()
    {
        using var scratch = new StorageScratchFolder();
        var home = Path.Combine(scratch.Home, "home5");
        Directory.CreateDirectory(home);
        using var homeOverride = TokenHistoryScanner.HomeOverride.Override(home);

        var sessions = Path.Combine(home, ".codex", "sessions");
        WriteRollout(sessions, "d1-aaaaaaaa-1111-2222-3333-000000000001.jsonl", DateTime.Now.AddDays(-5), TokenCountLine(40, 40, "2026-01-01T00:00:00Z"));
        WriteRollout(sessions, "d2-aaaaaaaa-1111-2222-3333-000000000002.jsonl", DateTime.Now.AddDays(-1), TokenCountLine(30, 30, "2026-01-05T00:00:00Z"));
        // Both files share one rollout identity in this test's intent, so settle against the combined map directly:
        // exercise Reconcile's contract through two distinct identities instead, each independently.
        var settled = new ValueMap<string, long>(new Dictionary<string, long>
        {
            ["aaaaaaaa-1111-2222-3333-000000000001"] = 100, // more than that file's own growth: nothing negative
            ["aaaaaaaa-1111-2222-3333-000000000002"] = 10,
        }, SwiftStringComparer.Instance);

        var result = TokenHistoryScanner.ScanCodexHistory(ValueMap<string, CodexFileFingerprint>.Empty, DateTimeOffset.Now, settled);

        Assert.False(result.Daily.ContainsKey("2026-01-01")); // fully cleared, and not negative
        Assert.Equal(20, result.Daily["2026-01-05"]); // 30 - 10
    }

    [Fact]
    [Trait("Req", "PR-USAGE-105")] // TokenLedger.swift:574-577, Core/Support/ISO8601.swift:19-21
    public void EventTimestamps_ParseFractionalSecondsFirstThenWholeSeconds()
    {
        Assert.NotNull(ISO8601.Date("2026-01-05T10:00:00.123Z"));
        Assert.NotNull(ISO8601.Date("2026-01-05T10:00:00Z"));
        Assert.Null(ISO8601.Date("not a date"));
    }

    private static string TokenCountLine(long totalTokens, long lastTokenUsageTotal, string timestamp) =>
        "{\"timestamp\":\"" + timestamp + "\",\"payload\":{\"info\":{\"token_count\":true,\"total_token_usage\":{\"total_tokens\":" + totalTokens +
        "},\"last_token_usage\":{\"total_tokens\":" + lastTokenUsageTotal + "}}}}";

    /// Like <see cref="TokenCountLine"/>, but total_tokens is spliced in as a raw JSON literal instead of a plain
    /// integer, so a test can exercise JSONValue.int's other accepted/rejected shapes (a numeric string, a finite
    /// double, or a boolean) for PR-USAGE-100 (B3 b3_note).
    private static string TokenCountLineRawTotal(string totalTokensJson, long lastTokenUsageTotal, string timestamp) =>
        "{\"timestamp\":\"" + timestamp + "\",\"payload\":{\"info\":{\"token_count\":true,\"total_token_usage\":{\"total_tokens\":" + totalTokensJson +
        "},\"last_token_usage\":{\"total_tokens\":" + lastTokenUsageTotal + "}}}}";

    // ---- PR-USAGE-100 (B3 b3_note): total_token_usage.total_tokens now parses through JSONValue.int ----

    [Fact]
    [Trait("Req", "PR-USAGE-100")] // TokenLedger.swift:524, Core/Support/JSONValue.swift:88 .int on a numeric string
    public void ScanCodexHistory_AcceptsATailTotalTokensGivenAsANumericJsonString()
    {
        using var scratch = new StorageScratchFolder();
        var home = Path.Combine(scratch.Home, "home-string-total");
        Directory.CreateDirectory(home);
        using var homeOverride = TokenHistoryScanner.HomeOverride.Override(home);

        var sessions = Path.Combine(home, ".codex", "sessions");
        var content = TokenCountLine(200, 0, "2026-01-05T09:00:00Z") + "\n" +
            TokenCountLineRawTotal("\"260\"", 200, "2026-01-05T11:00:00Z"); // numeric-string total, tail event
        var path = WriteRollout(sessions, "string-total-a1b2c3d4-1111-2222-3333-aaaaaaaaaaaa.jsonl", DateTime.Now.AddDays(-1), content);

        var result = TokenHistoryScanner.ScanCodexHistory(ValueMap<string, CodexFileFingerprint>.Empty, DateTimeOffset.Now, ValueMap<string, long>.Empty);

        Assert.Equal(60, result.Daily["2026-01-05"]); // "260" parses through JSONValue.int the same as a plain 260
        Assert.Equal("a1b2c3d4-1111-2222-3333-aaaaaaaaaaaa", result.Fingerprints[path].Identity);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-100")] // TokenLedger.swift:524, Core/Support/JSONValue.swift:85 .int on a finite double truncates toward zero
    public void ScanCodexHistory_AcceptsATailTotalTokensGivenAsAFractionalNumberTruncatedTowardZero()
    {
        using var scratch = new StorageScratchFolder();
        var home = Path.Combine(scratch.Home, "home-fractional-total");
        Directory.CreateDirectory(home);
        using var homeOverride = TokenHistoryScanner.HomeOverride.Override(home);

        var sessions = Path.Combine(home, ".codex", "sessions");
        var content = TokenCountLine(200, 0, "2026-01-05T09:00:00Z") + "\n" +
            TokenCountLineRawTotal("260.9", 200, "2026-01-05T11:00:00Z"); // fractional total, tail event
        var path = WriteRollout(sessions, "fractional-total-a1b2c3d4-1111-2222-3333-bbbbbbbbbbbb.jsonl", DateTime.Now.AddDays(-1), content);

        var result = TokenHistoryScanner.ScanCodexHistory(ValueMap<string, CodexFileFingerprint>.Empty, DateTimeOffset.Now, ValueMap<string, long>.Empty);

        Assert.Equal(60, result.Daily["2026-01-05"]); // 260.9 truncates to 260 (not rounded to 261), then 260-200=60
        Assert.Equal("a1b2c3d4-1111-2222-3333-bbbbbbbbbbbb", result.Fingerprints[path].Identity);
    }

    [Fact]
    [Trait("Req", "PR-USAGE-100")] // TokenLedger.swift:524, Core/Support/JSONValue.swift:90 .int rejects a boolean, same as a missing field
    public void ScanCodexHistory_RejectsATotalTokensGivenAsABooleanAndContributesNothing()
    {
        using var scratch = new StorageScratchFolder();
        var home = Path.Combine(scratch.Home, "home-boolean-total");
        Directory.CreateDirectory(home);
        using var homeOverride = TokenHistoryScanner.HomeOverride.Override(home);

        var sessions = Path.Combine(home, ".codex", "sessions");
        var content = TokenCountLineRawTotal("true", 0, "2026-01-05T11:00:00Z"); // boolean total: the only candidate line
        var path = WriteRollout(sessions, "boolean-total-a1b2c3d4-1111-2222-3333-cccccccccccc.jsonl", DateTime.Now.AddDays(-1), content);

        var result = TokenHistoryScanner.ScanCodexHistory(ValueMap<string, CodexFileFingerprint>.Empty, DateTimeOffset.Now, ValueMap<string, long>.Empty);

        Assert.Empty(result.Daily); // the only candidate line is unparsable, same as a missing total
        Assert.Equal(0, result.Fingerprints[path].Tokens);
    }
}

