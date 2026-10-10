using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Services.Port.Ignis;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Ignis;

// Ignis U1 (OD-0134, ED-NEW-ignis-engine): the "Save report" text for one chat, filled from its status and the real
// ledger, in the design's line layout (phoenix-designs.html Stage.report).
public sealed class IgnisReportTextTests
{
    private static readonly Guid Chat = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid Other = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    private static IgnisSavingsLedger Ledger(string folder)
    {
        var ledger = new IgnisSavingsLedger(folder, TimeZoneInfo.Utc);
        // Tuesday 13 October 2026, this chat.
        var today = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
        ledger.Record(new IgnisLedgerEntry(today, Chat, IgnisLedgerKind.Saved, IgnisSource.Reuse, 1.50m, 12));
        ledger.Record(new IgnisLedgerEntry(today, Chat, IgnisLedgerKind.Saved, IgnisSource.KeptWarm, 0.40m));
        ledger.Record(new IgnisLedgerEntry(today, Chat, IgnisLedgerKind.Saved, IgnisSource.TidyUp, 0.42m));
        ledger.Record(new IgnisLedgerEntry(today, Chat, IgnisLedgerKind.Saved, IgnisSource.SmartHelpers, 0.08m));
        ledger.Record(new IgnisLedgerEntry(today, Chat, IgnisLedgerKind.Lost, null, 0.21m));
        // Another chat today and earlier days: in "Last 7 days" and "All chats", not in this chat's lines.
        ledger.Record(new IgnisLedgerEntry(today, Other, IgnisLedgerKind.Saved, IgnisSource.Reuse, 1.00m));
        ledger.Record(new IgnisLedgerEntry(today.AddDays(-6), Other, IgnisLedgerKind.Saved, IgnisSource.Reuse, 3.00m));
        ledger.Record(new IgnisLedgerEntry(today.AddDays(-7), Other, IgnisLedgerKind.Saved, IgnisSource.Reuse, 1_200.00m));
        return ledger;
    }

    private static IgnisChatStatus Status(IgnisAccountKind account) => new()
    {
        ThreadID = Chat,
        Mode = IgnisMode.WarmAndTidy,
        Auto = true,
        ModelDisplayName = "Opus 5.5",
        Cache = IgnisCacheState.Warm,
        SavedBySource = IgnisChatStatus.NoSavings,
        Next = IgnisNext.WarmAndTidy,
        Account = account,
        ReuseRate = 0.94,
        ContextUsed = 380_000,
        ContextWindow = 1_000_000,
        WeekShare = 0.09,
        WeeklyLimitAt = new DateTimeOffset(2026, 10, 15, 18, 30, 0, TimeSpan.Zero),
        ChatCost = 4.12m,
    };

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_plan_report_has_the_designs_lines()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var text = IgnisReportText.Build(Status(IgnisAccountKind.Plan), Ledger(folder.Path), new DateTimeOffset(2026, 10, 13, 10, 6, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        Assert.Equal(
            "Ignis report · this chat · Tuesday 10:06\n" +
            "Model: Opus 5.5 · Mode: Warm + tidy up · Auto on\n" +
            "\n" +
            "Saved today: $2.40\n" +
            "  Reused quick memory  $1.50\n" +
            "  Kept warm            $0.40\n" +
            "  Tidy-ups             $0.42\n" +
            "  Smart helpers        $0.08\n" +
            "Lost when it went cold: $0.21 · Quick memory reused 94% of the time\n" +
            "Chat size: 38% full\n" +
            "\n" +
            "This chat used 9% of your week. At this pace you’ll reach the weekly limit Thursday evening.\n" +
            "Last 7 days: $6.40 saved · All chats: $1,206 saved",
            text);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_API_key_report_says_what_the_chat_cost_and_unreported_numbers_say_so()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var status = Status(IgnisAccountKind.ApiKey) with { CacheReported = false, ContextUsed = null };
        var lines = IgnisReportText.Build(status, Ledger(folder.Path), new DateTimeOffset(2026, 10, 13, 10, 6, 0, TimeSpan.Zero), TimeZoneInfo.Utc).Split('\n');
        Assert.Equal("Lost when it went cold: $0.21 · Quick memory reuse not reported", lines[8]);
        Assert.Equal("Chat size: not reported", lines[9]);
        Assert.Equal("API key: this chat cost $4.12 so far.", lines[11]);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_report_adds_longer_memory_paid_credits_helper_rebuilds_and_the_cache_lifetimes()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var ledger = Ledger(folder.Path);
        var today = new DateTimeOffset(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);
        ledger.Record(new IgnisLedgerEntry(today, Chat, IgnisLedgerKind.Saved, IgnisSource.LongerMemory, 0.60m));
        ledger.Record(new IgnisLedgerEntry(today, Chat, IgnisLedgerKind.Spent, IgnisSource.LongerMemory, 0.05m));
        ledger.Record(new IgnisLedgerEntry(today, Chat, IgnisLedgerKind.Missed, null, 0.31m));
        var status = Status(IgnisAccountKind.Plan) with
        {
            PaidCredits = true,
            MissedToday = ledger.ChatDay(Chat, new DateOnly(2026, 10, 13)).Missed,
            CacheLifetimes = new IgnisLifetimeChoice(
                IgnisLifetimeVerdict.Because(IgnisCacheBucket.Main, IgnisLifetimeReason.UserChoice, "promptCacheTtl"),
                new IgnisLifetimeVerdict(IgnisCacheBucket.Helpers, IgnisLifetimeReason.Longer, 40, 1.30m, 0.10m)),
        };

        var lines = IgnisReportText.Build(status, ledger, new DateTimeOffset(2026, 10, 13, 10, 6, 0, TimeSpan.Zero), TimeZoneInfo.Utc).Split('\n');

        Assert.Equal(
        [
            "Saved today: $3.00",
            "  Reused quick memory  $1.50",
            "  Kept warm            $0.40",
            "  Tidy-ups             $0.42",
            "  Smart helpers        $0.08",
            "  Longer quick memory  $0.60",
            "Lost when it went cold: $0.21 · Quick memory reused 94% of the time",
            "Chat size: 38% full",
            "Paid credits: 5-minute cache",
            "Helpers rebuilt their quick memory: $0.31",
            "Chat’s quick memory: your own setting (promptCacheTtl)",
            "Helpers’ quick memory: 1 hour, chosen by Ignis (saves about $1.20 over 40 breaks)",
            "",
        ], lines[3..16]);
    }
}
