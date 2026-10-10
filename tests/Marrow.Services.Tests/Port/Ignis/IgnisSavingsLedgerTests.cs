using System.Text;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Port.Ignis;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Ignis;

// Ignis U1 (OD-0134, ED-NEW-ignis-engine): the savings ledger buckets money by local day, chat, kind and source, keeps
// its events, survives a restart byte for byte, and recovers from a corrupt file without deleting it.
public sealed class IgnisSavingsLedgerTests
{
    private static readonly TimeZoneInfo PlusThree = TimeZoneInfo.CreateCustomTimeZone("Ignis+3", TimeSpan.FromHours(3), "Ignis+3", "Ignis+3");
    private static readonly Guid ChatA = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid ChatB = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    private static DateTimeOffset Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Entries_are_bucketed_by_local_day_chat_and_source()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var ledger = new IgnisSavingsLedger(folder.Path, PlusThree);
        var changed = 0;
        ledger.Changed += () => changed++;

        // 22:30 UTC on the 10th is 01:30 on the 11th at UTC+3.
        ledger.Record(new IgnisLedgerEntry(Utc(10, 22, 30), ChatA, IgnisLedgerKind.Saved, IgnisSource.Reuse, 0.30m, 3));
        ledger.Record(new IgnisLedgerEntry(Utc(10, 20, 0), ChatA, IgnisLedgerKind.Saved, IgnisSource.Reuse, 0.10m, 1));
        ledger.Record(new IgnisLedgerEntry(Utc(11, 8, 0), ChatA, IgnisLedgerKind.Saved, IgnisSource.KeptWarm, 0.40m, 1));
        ledger.Record(new IgnisLedgerEntry(Utc(11, 8, 5), ChatA, IgnisLedgerKind.Saved, IgnisSource.TidyUp, 0.42m, 1));
        ledger.Record(new IgnisLedgerEntry(Utc(11, 8, 6), ChatB, IgnisLedgerKind.Saved, IgnisSource.SmartHelpers, 0.08m, 2));
        ledger.Record(new IgnisLedgerEntry(Utc(11, 8, 7), ChatA, IgnisLedgerKind.Lost, null, 0.21m));
        ledger.Record(new IgnisLedgerEntry(Utc(11, 9, 0), ChatA, IgnisLedgerKind.Spent, IgnisSource.KeptWarm, 0.02m));
        ledger.Record(new IgnisLedgerEntry(Utc(11, 10, 0), ChatA, IgnisLedgerKind.Spent, IgnisSource.KeptWarm, 0.02m));
        Assert.Equal(8, changed);

        var day11 = new DateOnly(2026, 10, 11);
        Assert.Equal(new IgnisDayTotals(day11, 0.30m, 0.40m, 0.42m, 0.08m, 0.21m, 0.04m, 3, 2, 1, 2), ledger.Day(day11));
        Assert.Equal(1.20m, ledger.Day(day11).Saved);
        Assert.Equal(new IgnisDayTotals(day11, 0.30m, 0.40m, 0.42m, 0m, 0.21m, 0.04m, 3, 2, 1, 0), ledger.ChatDay(ChatA, day11));
        Assert.Equal(new IgnisDayTotals(day11, 0m, 0m, 0m, 0.08m, 0m, 0m, 0, 0, 0, 2), ledger.ChatDay(ChatB, day11));
        Assert.Equal(0.10m, ledger.Day(new DateOnly(2026, 10, 10)).Reuse);

        var days = ledger.Days(new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 12));
        Assert.Equal([new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 10), day11, new DateOnly(2026, 10, 12)], days.Select(d => d.Day).ToArray());
        Assert.Equal(IgnisDayTotals.Empty(new DateOnly(2026, 10, 9)), days[0]);
        Assert.Equal(1.30m, ledger.AllTimeSaved);
        Assert.Equal(new DateOnly(2026, 10, 10), ledger.Since);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_file_is_written_on_every_change_and_reads_back_the_same()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var ledger = new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc);
        Assert.Null(ledger.Since);
        Assert.False(File.Exists(ledger.FilePath));

        ledger.Record(new IgnisLedgerEntry(Utc(10, 9, 0), ChatA, IgnisLedgerKind.Saved, IgnisSource.Reuse, 0.42m, 2));
        ledger.Record(new IgnisEvent("e1", ChatA, IgnisEventKind.TidiedUp, Utc(10, 9, 41), 0.42m, IgnisAmountKind.Saved, AtTokens: 95_000));

        Assert.Equal(Path.Combine(folder.Path, "ignis", "ledger-v1.json"), ledger.FilePath);
        Assert.Equal(
            "{\"version\":1,\"buckets\":[{\"day\":\"2026-10-10\",\"thread\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"kind\":\"saved\",\"source\":\"reuse\",\"amount\":0.42,\"count\":2}]," +
            "\"events\":[{\"id\":\"e1\",\"thread\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"kind\":\"tidiedUp\",\"at\":\"2026-10-10T09:41:00.0000000Z\",\"amount\":0.42,\"amountKind\":\"saved\",\"atTokens\":95000}]}",
            Encoding.UTF8.GetString(File.ReadAllBytes(ledger.FilePath)));

        var reread = new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc);
        Assert.Equal(ledger.Encode(), reread.Encode());
        Assert.Equal(0.42m, reread.AllTimeSaved);
        Assert.Equal(ledger.Event("e1"), reread.Event("e1"));
        Assert.Null(reread.RecoveredFrom);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Events_list_newest_first_per_chat_and_day_replace_by_id_and_age_out()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var now = runtime.Time.GetUtcNow();
        var ledger = new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc);
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        ledger.Record(new IgnisEvent("old", ChatA, IgnisEventKind.KeptWarm, now - TimeSpan.FromDays(61), 0.02m));
        ledger.Record(new IgnisEvent("a1", ChatA, IgnisEventKind.KeptWarm, now - TimeSpan.FromMinutes(30), 0.02m));
        ledger.Record(new IgnisEvent("a2", ChatA, IgnisEventKind.WentCold, now - TimeSpan.FromMinutes(5), 0.21m, IgnisAmountKind.Lost));
        ledger.Record(new IgnisEvent("b1", ChatB, IgnisEventKind.HelperRan, now - TimeSpan.FromMinutes(10), 0.08m, IgnisAmountKind.Saved, Model: "Haiku 5.5"));
        ledger.Record(new IgnisEvent("a1", ChatA, IgnisEventKind.KeptWarm, now - TimeSpan.FromMinutes(30), 0.03m));

        Assert.Equal(["a2", "a1"], ledger.Events(ChatA, today).Select(e => e.Id).ToArray());
        Assert.Equal(0.03m, ledger.Event("a1")!.Amount);
        Assert.Equal("Haiku 5.5", Assert.Single(ledger.Events(ChatB, today)).Model);
        Assert.Null(ledger.Event("old"));
        Assert.Null(ledger.Event("missing"));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_corrupt_file_is_moved_aside_and_the_ledger_starts_again()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "ignis"));
        var path = Path.Combine(folder.Path, "ignis", "ledger-v1.json");
        var garbage = Encoding.UTF8.GetBytes("{\"version\":1,\"buckets\":[ truncated");
        File.WriteAllBytes(path, garbage);

        var ledger = new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc);

        Assert.Equal(0m, ledger.AllTimeSaved);
        Assert.NotNull(ledger.RecoveredFrom);
        Assert.Equal(garbage, File.ReadAllBytes(ledger.RecoveredFrom!));
        Assert.StartsWith("ledger-v1.corrupt-", Path.GetFileName(ledger.RecoveredFrom), StringComparison.Ordinal);
        Assert.False(File.Exists(path));
        ledger.Record(new IgnisLedgerEntry(Utc(10, 9, 0), ChatA, IgnisLedgerKind.Saved, IgnisSource.Reuse, 0.10m));
        Assert.Equal(0.10m, new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc).AllTimeSaved);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void An_entry_with_an_unknown_value_is_skipped_and_the_rest_kept()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "ignis"));
        File.WriteAllText(Path.Combine(folder.Path, "ignis", "ledger-v1.json"),
            "{\"version\":1,\"buckets\":[" +
            "{\"day\":\"2026-10-10\",\"thread\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"kind\":\"saved\",\"source\":\"reuse\",\"amount\":0.5,\"count\":1}," +
            "{\"day\":\"2026-10-10\",\"thread\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"kind\":\"saved\",\"source\":\"magic\",\"amount\":9,\"count\":1}," +
            "{\"day\":\"10/10/2026\",\"thread\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"kind\":\"saved\",\"source\":\"reuse\",\"amount\":9,\"count\":1}]," +
            "\"events\":[{\"id\":\"x\",\"thread\":\"nope\",\"kind\":\"keptWarm\",\"at\":\"2026-10-10T09:00:00.0000000Z\"}]}");

        var ledger = new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc);

        Assert.Null(ledger.RecoveredFrom);
        Assert.Equal(0.5m, ledger.AllTimeSaved);
        Assert.Null(ledger.Event("x"));
    }
}
