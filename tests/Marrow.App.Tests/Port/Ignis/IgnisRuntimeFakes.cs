using Marrow.App.Port.Ignis.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.App.Tests.Port.Ignis;

// Offline fakes for the Ignis runtime and controller tests (unit U3): no model provider is ever reached; the engine, the
// points and the ledger stand in for unit U1's.

internal static class IgnisSamples
{
    public const string Opus = "claude-opus-test";
    public const string Sonnet = "claude-sonnet-test";
    public const string Haiku = "claude-haiku-test";

    public static IgnisModelPoints Model(string id, string display, long window, bool keepWarm = true, int ttlMain = 3600, int ttlSub = 300) =>
        new("claude", new ValueList<string>([id]), ValueList<string>.Empty, display, window,
            new IgnisPrices(5m, 25m, 0.5m, 6.25m, 10m, ValueList<IgnisPriceStep>.Empty),
            new IgnisCachePoints(ttlMain, ttlSub, keepWarm), null, null, null, "https://example.invalid/pricing", "2026-10-10");

    public static IgnisTurnUsage Usage(long read, long write1h = 0, long write5m = 0, long uncached = 100, string? model = Opus, string? agent = null, long? context = null, bool reported = true) =>
        new(ProviderKind.Claude, model, agent, uncached, read, write5m, write1h, 0, 50, context ?? (uncached + read + write1h + write5m), 200_000, reported);
}

internal sealed class FakeIgnisPoints : IIgnisPoints
{
    public List<IgnisModelPoints> Models { get; } =
    [
        IgnisSamples.Model(IgnisSamples.Opus, "Opus Test", 200_000),
        IgnisSamples.Model(IgnisSamples.Sonnet, "Sonnet Test", 1_000_000),
        IgnisSamples.Model(IgnisSamples.Haiku, "Haiku Test", 200_000, keepWarm: false),
    ];

    public event Action? Changed;
    public IgnisPointsFile Current => new(1, "2026.10.10", new ValueList<IgnisModelPoints>(Models));
    public IgnisPointsOrigin Origin => IgnisPointsOrigin.Bundled;
    public DateTimeOffset? CheckedAt => null;
    public IgnisModelPoints? Find(ProviderKind provider, string? model) =>
        Models.FirstOrDefault(points => points.Ids.Any(id => string.Equals(id, model, StringComparison.OrdinalIgnoreCase)));
    public Task<IgnisPointsCheck> CheckAsync(bool install, CancellationToken cancellationToken) => throw new InvalidOperationException("no feed in tests");
    public void Raise() => Changed?.Invoke();
}

internal sealed class FakeSavingsLedger : ISavingsLedger
{
    public List<IgnisLedgerEntry> Entries { get; } = [];
    public List<IgnisEvent> Recorded { get; } = [];
    public event Action? Changed;

    public void Record(IgnisLedgerEntry entry) { Entries.Add(entry); Changed?.Invoke(); }
    public void Record(IgnisEvent @event) { Recorded.Add(@event); Changed?.Invoke(); }

    public IgnisDayTotals Day(DateOnly day) => Totals(day, Entries);
    public IgnisDayTotals ChatDay(Guid thread, DateOnly day) => Totals(day, Entries.Where(entry => entry.ThreadID == thread));
    public IReadOnlyList<IgnisDayTotals> Days(DateOnly from, DateOnly to)
    {
        var days = new List<IgnisDayTotals>();
        for (var day = from; day <= to; day = day.AddDays(1)) days.Add(Day(day));
        return days;
    }
    public decimal AllTimeSaved => Entries.Where(entry => entry.Kind == IgnisLedgerKind.Saved).Sum(entry => entry.Amount);
    public DateOnly? Since => Entries.Count == 0 ? null : Entries.Min(entry => LocalDay(entry.At));
    public IReadOnlyList<IgnisEvent> Events(Guid thread, DateOnly day) => Recorded.Where(e => e.ThreadID == thread && LocalDay(e.At) == day).Reverse().ToArray();
    public IgnisEvent? Event(string id) => Recorded.FirstOrDefault(e => e.Id == id);

    private static DateOnly LocalDay(DateTimeOffset at) => DateOnly.FromDateTime(at.ToLocalTime().DateTime);

    private static IgnisDayTotals Totals(DateOnly day, IEnumerable<IgnisLedgerEntry> entries)
    {
        var list = entries.Where(entry => LocalDay(entry.At) == day).ToArray();
        decimal Saved(IgnisSource source) => list.Where(e => e.Kind == IgnisLedgerKind.Saved && e.Source == source).Sum(e => e.Amount);
        int Count(IgnisLedgerKind kind, IgnisSource source) => list.Where(e => e.Kind == kind && e.Source == source).Sum(e => e.Count);
        return new IgnisDayTotals(day, Saved(IgnisSource.Reuse), Saved(IgnisSource.KeptWarm), Saved(IgnisSource.TidyUp), Saved(IgnisSource.SmartHelpers),
            list.Where(e => e.Kind == IgnisLedgerKind.Lost).Sum(e => e.Amount), list.Where(e => e.Kind == IgnisLedgerKind.Spent).Sum(e => e.Amount),
            Count(IgnisLedgerKind.Saved, IgnisSource.Reuse), Count(IgnisLedgerKind.Spent, IgnisSource.KeptWarm), Count(IgnisLedgerKind.Saved, IgnisSource.TidyUp),
            Count(IgnisLedgerKind.Saved, IgnisSource.SmartHelpers))
        {
            // ED-NEW-ignis-runtime: the fifth source, its extra writes and the helpers' missed rebuilds.
            LongerMemory = Saved(IgnisSource.LongerMemory),
            LongerMemorySpent = list.Where(e => e.Kind == IgnisLedgerKind.Spent && e.Source == IgnisSource.LongerMemory).Sum(e => e.Amount),
            Missed = list.Where(e => e.Kind == IgnisLedgerKind.Missed).Sum(e => e.Amount),
            // ED-NEW-ignisadvisor-1: advice the chat's model asked for, a cost of its own.
            Advice = list.Where(e => e.Kind == IgnisLedgerKind.Advice).Sum(e => e.Amount),
            AdviceCalls = list.Where(e => e.Kind == IgnisLedgerKind.Advice).Sum(e => e.Count),
        };
    }
}

/// <summary>Fixed, readable maths: the real maths is unit U1's and tested there.</summary>
internal sealed class FakeIgnisEngine : IIgnisEngine
{
    public bool KeepWarm { get; set; } = true;
    public List<IgnisKeepWarmQuestion> Questions { get; } = [];
    public Dictionary<string, long> TidyPoints { get; } = new(StringComparer.Ordinal)
    {
        [IgnisSamples.Opus] = 150_000,
        [IgnisSamples.Sonnet] = 190_000,
        [IgnisSamples.Haiku] = 150_000,
    };

    public long? TidyPoint(IgnisModelPoints points) => TidyPoints.TryGetValue(points.Ids[0], out var at) ? at : null;
    public bool ShouldKeepWarm(IgnisKeepWarmQuestion question) { Questions.Add(question); return KeepWarm; }
    public decimal ReuseSaved(IgnisModelPoints points, IgnisTurnUsage usage) => usage.Reported ? usage.CacheRead / 1000m * 0.01m : 0m;
    public decimal RebuildPremium(IgnisModelPoints points, long contextTokens) => contextTokens / 1000m * 0.02m;
    public decimal PingCost(IgnisModelPoints points, long contextTokens) => contextTokens / 1000m * 0.001m;
    public decimal TidySaved(IgnisModelPoints points, long beforeTokens, long afterTokens) => (beforeTokens - afterTokens) / 1000m * 0.005m;
    public decimal LongerMemoryCost(IgnisModelPoints points, long oneHourWrites, long promptTokens) => oneHourWrites / 1000m * 0.003m;
}

internal sealed class FakeIgnisEnvironment : IIgnisEnvironment
{
    public bool IsLocked { get; set; }
    public HashSet<Guid> Archived { get; } = [];
    public double? PlanPercent { get; set; }
    public bool Draft { get; set; }
    public bool OnScreen { get; set; }
    public bool IsArchived(Guid thread) => Archived.Contains(thread);
    public bool IsOnScreen(Guid thread) => OnScreen;
    public bool HasDraft(Guid thread) => Draft;
    public double? FullestPlanWindow(ProviderKind provider) => PlanPercent;
    public IgnisAccountKind Account(ProviderKind provider) => provider.IsAPIKeyBased ? IgnisAccountKind.ApiKey : IgnisAccountKind.Plan;
    public string? Version { get; set; } = "2.1.300";
    public HashSet<string> ClaudeSettings { get; } = [];
    public int SettingsReads { get; private set; }
    public string? CliVersion(ProviderKind provider) => provider == ProviderKind.Claude ? Version : null;
    public IReadOnlySet<string> ClaudeCacheSettings(IReadOnlyDictionary<string, string> environment, string? workingDirectory) { SettingsReads++; return ClaudeSettings; }
}

/// <summary>A chat runtime that records what Ignis asked of it.</summary>
internal sealed class FakeIgnisPort : IIgnisRuntimePort, IIgnisRuntimeTimeline
{
    public FakeIgnisPort() { }

    /// <summary>A runtime for a given chat (a sidebar row's).</summary>
    public FakeIgnisPort(Guid thread) => ThreadID = thread;

    public Guid ThreadID { get; } = Guid.NewGuid();
    public ProviderKind Provider { get; set; } = ProviderKind.Claude;
    public string? Model { get; set; } = IgnisSamples.Opus;
    public bool IsBetweenTasks { get; set; } = true;
    public DateTimeOffset? LastRequestAt { get; set; }
    public DateTimeOffset? TurnStartedAt { get; set; }
    public bool CanKeepWarmSilently { get; set; }
    public bool IsTidying { get; set; }
    public int Pings { get; private set; }
    public int SilentKeepWarms { get; private set; }
    public List<int> Tidies { get; } = [];
    public List<IgnisEvent> Lines { get; } = [];

    public bool SendHiddenPing() { if (!IsBetweenTasks) return false; Pings++; return true; }
    public bool KeepWarmSilently() { if (!IsBetweenTasks || !CanKeepWarmSilently) return false; SilentKeepWarms++; return true; }
    public bool Tidy(int keepTurns) { if (!IsBetweenTasks) return false; Tidies.Add(keepTurns); IsTidying = true; return true; }
    public void AppendEvent(IgnisEvent @event) => Lines.Add(@event);
}

/// <summary>A controller that only records what the runtime forwarded (for the ThreadRuntime hook tests).</summary>
internal sealed class RecordingIgnisController : IIgnisController
{
    public List<(Guid Thread, IgnisTurnUsage Usage)> Observed { get; } = [];
    public List<Guid> Finished { get; } = [];
    public List<(Guid Thread, DateTimeOffset? ResetsAt)> Limits { get; } = [];
    public List<IIgnisRuntimePort> Attached { get; } = [];

    public event Action<Guid>? Changed { add { } remove { } }
    public IgnisSettings Settings => throw new NotSupportedException();
    public IIgnisPoints Points => throw new NotSupportedException();
    public ISavingsLedger Ledger => throw new NotSupportedException();
    public IgnisChatStatus Status(Guid thread) => IgnisChatStatus.Unknown(thread, IgnisMode.WarmAndTidy, true);
    public void SetMode(Guid thread, IgnisMode mode) { }
    public void SetAuto(Guid thread, bool auto) { }
    public void KeepWarmNow(Guid thread) { }
    public void TidyUpNow(Guid thread) { }
    public string Report(Guid thread) => "";
    public void Attach(IIgnisRuntimePort port) => Attached.Add(port);
    public void Detach(Guid thread) { }
    public void Observe(Guid thread, IgnisTurnUsage usage) => Observed.Add((thread, usage));
    public void TurnFinished(Guid thread) => Finished.Add(thread);
    public void UsageLimitHit(Guid thread, DateTimeOffset? resetsAt) => Limits.Add((thread, resetsAt));
    public List<(Guid Thread, bool OnCredits)> Credits { get; } = [];
    public void CreditsHint(Guid thread, bool onCredits) => Credits.Add((thread, onCredits));
}
