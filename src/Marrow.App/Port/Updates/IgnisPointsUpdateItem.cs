// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-settings; PLAN unit U5; price catalogue: ED-NEW-ignis-data). Ignis's model points
// as one row of "Update everything" (design Settings › Updates: "Ignis model points · Context, cache and rebuild points for
// each model", "2026.10.08 → 2026.10.10"). One action updates both the curated points (IIgnisPoints.CheckAsync, the GitHub
// release feed) and the all-model price catalogue (IIgnisPriceCatalogue.CheckPricesAsync, LiteLLM's file on GitHub); both are
// data files fetched only from GitHub, and nothing is ever sent to a model provider. Kind ModelPoints runs after the tools
// and before Marrow itself; the page lists it right after Marrow.
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Port.Updates;

/// <summary>
/// The installed version is the points file in use, with the price catalogue's day ("2026.10.10 · prices 2026-10-10"); the
/// latest is the feed's. ENGINEERING DECISIONS (ED-NEW-ignis-settings, ED-NEW-ignis-data): with "Update automatically" on, the
/// check installs a valid newer file and refreshes the price catalogue at once (data files, no restart), at most once a day
/// each, so the row reads "Up to date" after it; a feed not published yet names the installed version as the latest ("Up to
/// date", PLAN lead decision) while the catalogue is still refreshed; a feed or catalogue that cannot be read throws, so the
/// row keeps the error and says "Checks when updated". With it off, a check asks the small points feed only, and Update
/// refreshes both.
/// </summary>
public sealed class IgnisPointsUpdateItem(IIgnisPoints points, Func<bool> updateAutomatically) : IUpdateItem
{
    public const string ItemId = "ignis:points";

    /// <summary>How often an automatic check asks the feeds again.</summary>
    public static TimeSpan CheckEvery { get; } = TimeSpan.FromDays(1);

    private readonly IIgnisPriceCatalogue? prices = points as IIgnisPriceCatalogue;
    private IgnisPointsCheck? installedNow;
    private string? pricesError;

    public string Id => ItemId;

    public string Title => IgnisWords.UpdatesRowTitle;

    public UpdateItemKind Kind => UpdateItemKind.ModelPoints;

    public async Task<string?> InstalledVersion(CancellationToken cancellation)
    {
        installedNow = null;
        pricesError = null;
        if (updateAutomatically())
        {
            if (CheckedRecently(points.CheckedAt))
                installedNow = new IgnisPointsCheck(points.Current.Version, points.Current.Version, false);
            else
            {
                var check = await points.CheckAsync(install: true, cancellation);
                if (check.Error is null) installedNow = check;
            }
            if (prices is not null) pricesError = (await prices.CheckPricesAsync(force: false, cancellation)).Error;
        }
        return Text(points.Current.Version);
    }

    public async Task<string?> LatestVersion(CancellationToken cancellation)
    {
        var check = installedNow ?? await points.CheckAsync(install: false, cancellation);
        var error = check.Error ?? pricesError;
        (installedNow, pricesError) = (null, null);
        if (error is not null) throw new InvalidOperationException(error);
        return Text(check.Latest ?? points.Current.Version);
    }

    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        progress.Report(null);
        var check = await points.CheckAsync(install: true, cancellation);
        var priced = prices is null ? null : await prices.CheckPricesAsync(force: true, cancellation);
        var error = check.Error ?? priced?.Error;
        return error is not null
            ? new UpdateOutcome(false, null, false, false, error)
            : new UpdateOutcome(true, Text(points.Current.Version), check.Updated || priced?.Updated == true, false, null);
    }

    private string Text(string version) => IgnisWords.UpdatesRowVersion(version, prices?.PricesDay);

    private static bool CheckedRecently(DateTimeOffset? at) =>
        at is { } checkedAt && SwiftDate.Now >= checkedAt && SwiftDate.Now - checkedAt < CheckEvery;
}
