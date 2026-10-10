using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Services.Port.Ignis;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Ignis;

// Ignis runtime (OD-0134, ED-NEW-ignis-runtime): the learned request rhythm on disk, the read-only look at the user's
// Claude settings, and the ledger's helper-rebuild and longer-memory accounting. Every file lives in a scratch folder.
public sealed class IgnisRuntimeStoreTests
{
    private static readonly Guid ChatA = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static IgnisRequestSample Sample(int i, double? gap = 600, string? model = "claude-opus-5-5", bool nth = false) =>
        new(T0.AddMinutes(i), model, gap, 100_000 + i, 2_000, i % 2, 3, nth);

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_gap_history_is_written_on_every_change_and_reads_back_the_same()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var store = new IgnisGapStore(folder.Path);
        Assert.Equal(Path.Combine(folder.Path, "ignis", "gaps-v1.json"), store.FilePath);
        Assert.False(File.Exists(store.FilePath));

        store.Add(ProviderKind.Claude, IgnisCacheBucket.Main, Sample(0, gap: null, model: null));
        store.Add(ProviderKind.Claude, IgnisCacheBucket.Helpers, Sample(1, gap: 312.5, nth: true));
        store.Add(ProviderKind.Codex, IgnisCacheBucket.Main, Sample(2));

        Assert.Equal(
            "{\"version\":1,\"lists\":[" +
            "{\"provider\":\"claude\",\"bucket\":\"main\",\"samples\":[{\"at\":\"2026-10-10T09:00:00.0000000Z\",\"prefix\":100000,\"w5m\":2000,\"w1h\":0,\"wu\":3,\"nth\":false}]}," +
            "{\"provider\":\"claude\",\"bucket\":\"helpers\",\"samples\":[{\"at\":\"2026-10-10T09:01:00.0000000Z\",\"model\":\"claude-opus-5-5\",\"gap\":312.5,\"prefix\":100001,\"w5m\":2000,\"w1h\":1,\"wu\":3,\"nth\":true}]}," +
            "{\"provider\":\"codex\",\"bucket\":\"main\",\"samples\":[{\"at\":\"2026-10-10T09:02:00.0000000Z\",\"model\":\"claude-opus-5-5\",\"gap\":600,\"prefix\":100002,\"w5m\":2000,\"w1h\":0,\"wu\":3,\"nth\":false}]}]}",
            Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath)));

        var reread = new IgnisGapStore(folder.Path);
        Assert.Equal(store.Encode(), reread.Encode());
        Assert.Equal([Sample(1, gap: 312.5, nth: true)], reread.Samples(ProviderKind.Claude, IgnisCacheBucket.Helpers));
        Assert.Empty(reread.Samples(ProviderKind.Pi, IgnisCacheBucket.Main));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Each_list_keeps_the_last_two_hundred_requests()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var store = new IgnisGapStore(folder.Path);
        for (var i = 0; i < 205; i++) store.Add(ProviderKind.Claude, IgnisCacheBucket.Helpers, Sample(i));
        store.Add(ProviderKind.Claude, IgnisCacheBucket.Main, Sample(0));

        var helpers = store.Samples(ProviderKind.Claude, IgnisCacheBucket.Helpers);
        Assert.Equal(IgnisGapMemory.Window, helpers.Count);
        Assert.Equal(200, IgnisGapMemory.Window);
        Assert.Equal(Sample(5), helpers[0]);
        Assert.Equal(Sample(204), helpers[^1]);
        Assert.Single(store.Samples(ProviderKind.Claude, IgnisCacheBucket.Main));
        Assert.Equal(200, new IgnisGapStore(folder.Path).Samples(ProviderKind.Claude, IgnisCacheBucket.Helpers).Count);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_corrupt_gap_file_is_moved_aside_and_learning_starts_again()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "ignis"));
        var path = Path.Combine(folder.Path, "ignis", "gaps-v1.json");
        var garbage = Encoding.UTF8.GetBytes("{\"version\":1,\"lists\":[ truncated");
        File.WriteAllBytes(path, garbage);

        var store = new IgnisGapStore(folder.Path);

        Assert.Empty(store.Samples(ProviderKind.Claude, IgnisCacheBucket.Main));
        Assert.False(File.Exists(path));
        var aside = Assert.Single(Directory.GetFiles(Path.Combine(folder.Path, "ignis"), "gaps-v1.corrupt-*.json"));
        Assert.Equal(garbage, File.ReadAllBytes(aside));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Unreadable_entries_are_skipped_and_the_rest_kept()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "ignis"));
        File.WriteAllText(Path.Combine(folder.Path, "ignis", "gaps-v1.json"),
            "{\"version\":1,\"lists\":[" +
            "{\"provider\":\"nobody\",\"bucket\":\"main\",\"samples\":[{\"at\":\"2026-10-10T09:00:00.0000000Z\",\"prefix\":1}]}," +
            "{\"provider\":\"claude\",\"bucket\":\"sideways\",\"samples\":[{\"at\":\"2026-10-10T09:00:00.0000000Z\",\"prefix\":1}]}," +
            "{\"provider\":\"claude\",\"bucket\":\"main\",\"samples\":[{\"at\":\"yesterday\"},{\"at\":\"2026-10-10T09:00:00.0000000Z\",\"gap\":-4,\"prefix\":-7,\"w5m\":10}]}]}");

        var store = new IgnisGapStore(folder.Path);

        Assert.Equal([new IgnisRequestSample(T0, null, null, 0, 10, 0, 0, false)], store.Samples(ProviderKind.Claude, IgnisCacheBucket.Main));
        Assert.Empty(Directory.GetFiles(Path.Combine(folder.Path, "ignis"), "gaps-v1.corrupt-*.json"));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_claude_settings_a_launch_reads_are_found_and_only_read()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var home = Path.Combine(folder.Path, "home");
        var project = Path.Combine(folder.Path, "project");
        var managed = Path.Combine(folder.Path, "managed", "managed-settings.json");
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        Directory.CreateDirectory(Path.Combine(project, ".claude"));
        var user = Path.Combine(home, ".claude", "settings.json");
        var local = Path.Combine(project, ".claude", "settings.local.json");
        File.WriteAllText(user, "{ \"subagentPromptCacheTtl\": \"5m\" }");
        File.WriteAllText(local, "{ \"env\": { \"FORCE_PROMPT_CACHING_5M\": \"1\" } }");
        var (userBytes, userTime, localBytes, localTime) = (File.ReadAllBytes(user), File.GetLastWriteTimeUtc(user), File.ReadAllBytes(local), File.GetLastWriteTimeUtc(local));
        var noEnvironment = new Dictionary<string, string>();

        Assert.Equal(
            [user, Path.Combine(project, ".claude", "settings.json"), local, managed],
            IgnisClaudeSettings.Paths(noEnvironment, project, home, managed));
        Assert.Equal(new HashSet<string> { "subagentPromptCacheTtl", "FORCE_PROMPT_CACHING_5M" }, IgnisClaudeSettings.UserTtlChoices(noEnvironment, project, home, managed));
        Assert.Equal(userBytes, File.ReadAllBytes(user));
        Assert.Equal(localBytes, File.ReadAllBytes(local));
        Assert.Equal((userTime, localTime), (File.GetLastWriteTimeUtc(user), File.GetLastWriteTimeUtc(local)));
        Assert.False(File.Exists(managed));
        Assert.False(File.Exists(Path.Combine(project, ".claude", "settings.json")));

        // CLAUDE_CONFIG_DIR (any letter case) moves the user file; HOME wins over the given home.
        var config = Path.Combine(folder.Path, "config");
        Assert.Equal(Path.Combine(config, "settings.json"), IgnisClaudeSettings.Paths(new Dictionary<string, string> { ["claude_config_dir"] = config }, null, home, managed)[0]);
        Assert.Equal(Path.Combine(folder.Path, "h2", ".claude", "settings.json"), IgnisClaudeSettings.Paths(new Dictionary<string, string> { ["HOME"] = Path.Combine(folder.Path, "h2") }, null, home, managed)[0]);
        // No project: the user and managed files only.
        Assert.Equal(new HashSet<string> { "subagentPromptCacheTtl" }, IgnisClaudeSettings.UserTtlChoices(noEnvironment, null, home, managed));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Helper_rebuilds_and_longer_memory_are_kept_apart_from_the_other_sources()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var ledger = new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc);
        ledger.Record(new IgnisLedgerEntry(T0, ChatA, IgnisLedgerKind.Saved, IgnisSource.KeptWarm, 0.40m, 1));
        ledger.Record(new IgnisLedgerEntry(T0, ChatA, IgnisLedgerKind.Saved, IgnisSource.LongerMemory, 0.384m, 1));
        ledger.Record(new IgnisLedgerEntry(T0, ChatA, IgnisLedgerKind.Spent, IgnisSource.LongerMemory, 0.012m));
        ledger.Record(new IgnisLedgerEntry(T0, ChatA, IgnisLedgerKind.Spent, IgnisSource.KeptWarm, 0.02m, 1));
        ledger.Record(new IgnisLedgerEntry(T0, ChatA, IgnisLedgerKind.Lost, null, 0.21m));
        // A rebuild's source is dropped like a loss's.
        ledger.Record(new IgnisLedgerEntry(T0, ChatA, IgnisLedgerKind.Missed, IgnisSource.KeptWarm, 0.048m, 1));
        ledger.Record(new IgnisLedgerEntry(T0.AddMinutes(1), ChatA, IgnisLedgerKind.Missed, null, 0.05m, 1));

        var day = ledger.ChatDay(ChatA, new DateOnly(2026, 10, 10));
        Assert.Equal(0.384m, day.LongerMemory);
        Assert.Equal(0.784m, day.Saved); // 0.40 kept warm + 0.384 longer memory
        Assert.Equal(0.384m, day.SavedFrom(IgnisSource.LongerMemory));
        Assert.Equal(0.032m, day.Spent); // 0.012 longer-memory writes + 0.02 ping
        Assert.Equal(0.012m, day.LongerMemorySpent);
        Assert.Equal(1, day.Pings);
        Assert.Equal(0.21m, day.Lost);
        Assert.Equal(0.098m, day.Missed);
        Assert.Equal(0.784m, ledger.AllTimeSaved);

        var text = Encoding.UTF8.GetString(File.ReadAllBytes(ledger.FilePath));
        Assert.Contains("\"kind\":\"missed\",\"amount\":0.098,\"count\":2", text, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"saved\",\"source\":\"longerMemory\",\"amount\":0.384,\"count\":1", text, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"spent\",\"source\":\"longerMemory\",\"amount\":0.012", text, StringComparison.Ordinal);
        var reread = new IgnisSavingsLedger(folder.Path, TimeZoneInfo.Utc);
        Assert.Equal(ledger.Encode(), reread.Encode());
        Assert.Equal(day, reread.ChatDay(ChatA, new DateOnly(2026, 10, 10)));
    }
}
