using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.App.Tests.Port.Ignis;

// Ignis U5 (OD-0134, ED-NEW-ignis-settings): fakes of the U0 contract for Settings › Ignis and the Updates row. The engine (U1) is built
// elsewhere; these hold fixed days and a scripted feed, and never reach a network or a model provider.
internal sealed class FakeSettingsLedger(IEnumerable<IgnisDayTotals> days) : ISavingsLedger
{
    private readonly Dictionary<DateOnly, IgnisDayTotals> byDay = days.ToDictionary(day => day.Day);
    public event Action? Changed;
    public void Raise() => Changed?.Invoke();
    public void Record(IgnisLedgerEntry entry) { }
    public void Record(IgnisEvent @event) { }
    public IgnisDayTotals Day(DateOnly day) => byDay.TryGetValue(day, out var totals) ? totals : IgnisDayTotals.Empty(day);
    public IgnisDayTotals ChatDay(Guid thread, DateOnly day) => Day(day);
    public IReadOnlyList<IgnisDayTotals> Days(DateOnly from, DateOnly to)
    {
        var list = new List<IgnisDayTotals>();
        for (var day = from; day <= to; day = day.AddDays(1)) list.Add(Day(day));
        return list;
    }
    public decimal AllTimeSaved => byDay.Values.Sum(day => day.Saved);
    public DateOnly? Since => byDay.Count == 0 ? null : byDay.Keys.Min();
    public IReadOnlyList<IgnisEvent> Events(Guid thread, DateOnly day) => [];
    public IgnisEvent? Event(string id) => null;
}

internal sealed class FakeSettingsPoints(string version, params IgnisModelPoints[] models) : IIgnisPoints
{
    public event Action? Changed;
    public IgnisPointsFile Current { get; private set; } = new(IgnisPointsFile.CurrentSchemaVersion, version, new ValueList<IgnisModelPoints>(models));
    public IgnisPointsOrigin Origin => IgnisPointsOrigin.Bundled;
    public DateTimeOffset? CheckedAt => null;

    /// <summary>The feed's version; null: not published yet.</summary>
    public string? Feed { get; set; }

    /// <summary>When set, a check fails with this error.</summary>
    public string? FeedError { get; set; }

    public List<bool> Checks { get; } = [];

    public IgnisModelPoints? Find(ProviderKind provider, string? model) =>
        Current.Models.FirstOrDefault(entry => entry.Provider == SwiftEnum<ProviderKind>.RawValue(provider)
            && model is not null && (entry.Ids.Contains(model, StringComparer.OrdinalIgnoreCase) || entry.Aliases.Contains(model, StringComparer.OrdinalIgnoreCase)));

    public Task<IgnisPointsCheck> CheckAsync(bool install, CancellationToken cancellationToken)
    {
        Checks.Add(install);
        if (FeedError is { } error) return Task.FromResult(new IgnisPointsCheck(Current.Version, null, false, error));
        var updated = false;
        if (install && Feed is { } feed && string.CompareOrdinal(feed, Current.Version) > 0)
        {
            Current = Current with { Version = feed };
            updated = true;
            Changed?.Invoke();
        }
        return Task.FromResult(new IgnisPointsCheck(Current.Version, Feed, updated));
    }

    public static IgnisModelPoints Model(string provider, string id, string name, long window, long? stepAt = null, long? tidyAt = null, string? tidyReason = null, string? helper = null) =>
        new(provider, new ValueList<string>([id]), new ValueList<string>([]), name, window,
            new IgnisPrices(1m, 5m, 0.1m, 1.25m, 2m, stepAt is { } step ? new ValueList<IgnisPriceStep>([new IgnisPriceStep(step, 2m, 10m, 0.2m, 2.5m, 4m)]) : new ValueList<IgnisPriceStep>([])),
            new IgnisCachePoints(3600, 300, true), tidyAt, tidyReason, helper, "https://example.invalid/pricing", "2026-10-08");
}
