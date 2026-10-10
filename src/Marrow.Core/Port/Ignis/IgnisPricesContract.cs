// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-data). The all-model price catalogue beside the curated model points: a trimmed
// copy of LiteLLM's public price file, fetched from GitHub only (never from a model provider, never with a model call), at
// most once a day when updating automatically. The curated points stay authoritative; the catalogue covers every other
// model of a provider the points file maps (IgnisProviderRule.Catalogue) and refreshes curated prices once they are old.
namespace Marrow.Core.Port.Ignis;

/// <summary>
/// The result of checking the price catalogue: the day of the catalogue in use after the check ("yyyy-MM-dd", null when
/// none), whether new prices were installed, whether the check was skipped (checked within the last day), and an error.
/// </summary>
public sealed record IgnisPricesCheck(string? Day, bool Updated, bool Skipped, string? Error = null);

/// <summary>The price catalogue (implemented by the points store beside <see cref="IIgnisPoints"/>).</summary>
public interface IIgnisPriceCatalogue
{
    /// <summary>The day the catalogue in use was last confirmed current ("yyyy-MM-dd"); null when there is none.</summary>
    string? PricesDay { get; }

    /// <summary>When the catalogue was last checked successfully; null when never.</summary>
    DateTimeOffset? PricesCheckedAt { get; }

    /// <summary>What the last build of the catalogue set aside (a price far from the curated one, a model that failed a guardrail).</summary>
    IReadOnlyList<string> PricesWarnings { get; }

    /// <summary>
    /// Checks the catalogue and installs a changed one. Without <paramref name="force"/>, a catalogue checked within the
    /// last day is not asked again (<see cref="IgnisPricesCheck.Skipped"/>).
    /// </summary>
    Task<IgnisPricesCheck> CheckPricesAsync(bool force, CancellationToken cancellationToken);

    /// <summary>
    /// Asks whether the file on GitHub changed, without installing or storing anything (a conditional request with the held
    /// ETag): <see cref="IgnisPricesCheck.Updated"/> is true when it did (or none is held), and
    /// <see cref="IgnisPricesCheck.Day"/> is today then, else the day of the catalogue in use (ED-NEW-ignis-data-2).
    /// </summary>
    Task<IgnisPricesCheck> ProbePricesAsync(CancellationToken cancellationToken);
}
