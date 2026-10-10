using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.App.Tests.Port.Ignis;

// Ignis price catalogue (ED-NEW-ignis-data): model points with a price catalogue for the Updates row tests. It holds a
// scripted points feed and a scripted catalogue on the TestRuntime clock, and never reaches a network or a model provider.
internal sealed class FakePricedPoints(string version) : IIgnisPoints, IIgnisPriceCatalogue
{
    public event Action? Changed;
    public IgnisPointsFile Current { get; private set; } = new(IgnisPointsFile.CurrentSchemaVersion, version, ValueList<IgnisModelPoints>.Empty);
    public IgnisPointsOrigin Origin => IgnisPointsOrigin.Bundled;
    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>The feed's version; null: not published yet.</summary>
    public string? Feed { get; set; }

    /// <summary>When set, a points check fails with this error.</summary>
    public string? FeedError { get; set; }

    /// <summary>When set, a catalogue check fails with this error.</summary>
    public string? PricesError { get; set; }

    /// <summary>Whether the next catalogue fetch brings changed prices.</summary>
    public bool PricesChanged { get; set; } = true;

    /// <summary>Every points check (install?).</summary>
    public List<bool> Checks { get; } = [];

    /// <summary>Every catalogue check (force?).</summary>
    public List<bool> PriceChecks { get; } = [];

    /// <summary>How many times the row asked whether the catalogue changed (a dry run: nothing installed or stored).</summary>
    public int Probes { get; private set; }

    /// <summary>The catalogue checks that reached the (scripted) network.</summary>
    public int PriceFetches { get; private set; }

    public string? PricesDay => PricesCheckedAt?.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    public DateTimeOffset? PricesCheckedAt { get; private set; }
    public IReadOnlyList<string> PricesWarnings => [];

    public IgnisModelPoints? Find(ProviderKind provider, string? model) => null;

    public Task<IgnisPointsCheck> CheckAsync(bool install, CancellationToken cancellationToken)
    {
        Checks.Add(install);
        if (FeedError is { } error) return Task.FromResult(new IgnisPointsCheck(Current.Version, null, false, error));
        CheckedAt = SwiftDate.Now;
        var updated = false;
        if (install && Feed is { } feed && string.CompareOrdinal(feed, Current.Version) > 0)
        {
            Current = Current with { Version = feed };
            updated = true;
            Changed?.Invoke();
        }
        return Task.FromResult(new IgnisPointsCheck(Current.Version, Feed, updated));
    }

    /// <summary>The store's dry run: changed (200) when none is held or <see cref="PricesChanged"/>, then today; else 304 and the held day. Nothing is stored.</summary>
    public Task<IgnisPricesCheck> ProbePricesAsync(CancellationToken cancellationToken)
    {
        Probes++;
        if (PricesError is { } error) return Task.FromResult(new IgnisPricesCheck(PricesDay, false, false, error));
        var changed = PricesCheckedAt is null || PricesChanged;
        var today = SwiftDate.Now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return Task.FromResult(new IgnisPricesCheck(changed ? today : PricesDay, changed, false));
    }

    /// <summary>The store's rule: without force, a catalogue checked within the last day is not asked again.</summary>
    public Task<IgnisPricesCheck> CheckPricesAsync(bool force, CancellationToken cancellationToken)
    {
        PriceChecks.Add(force);
        var now = SwiftDate.Now;
        if (!force && PricesCheckedAt is { } at && now - at < TimeSpan.FromDays(1)) return Task.FromResult(new IgnisPricesCheck(PricesDay, false, true));
        PriceFetches++;
        if (PricesError is { } error) return Task.FromResult(new IgnisPricesCheck(PricesDay, false, false, error));
        PricesCheckedAt = now;
        var updated = PricesChanged;
        PricesChanged = false;
        return Task.FromResult(new IgnisPricesCheck(PricesDay, updated, false));
    }
}
