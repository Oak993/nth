// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-settings; PLAN unit U5). Ignis's model points as one row of "Update everything"
// (design Settings › Updates: "Ignis model points · Context, cache and rebuild points for each model", "2026.10.08 → 2026.10.10").
// The points are a data file fetched only from the GitHub release feed (IIgnisPoints.CheckAsync); nothing is ever sent to a
// model provider. Kind ModelPoints runs after the tools and before Marrow itself; the page lists it right after Marrow.
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Port.Updates;

/// <summary>
/// The installed version is the points file in use; the latest is the feed's. ENGINEERING DECISIONS (ED-NEW-ignis-settings):
/// with "Update automatically" on, the check installs a valid newer file at once (a data file, no restart), so the row reads
/// "Up to date" after it; a feed not published yet names the installed version as the latest ("Up to date", PLAN lead decision);
/// a feed that cannot be read throws, so the row keeps the error and says "Checks when updated".
/// </summary>
public sealed class IgnisPointsUpdateItem(IIgnisPoints points, Func<bool> updateAutomatically) : IUpdateItem
{
    public const string ItemId = "ignis:points";

    private IgnisPointsCheck? installedNow;

    public string Id => ItemId;

    public string Title => IgnisWords.UpdatesRowTitle;

    public UpdateItemKind Kind => UpdateItemKind.ModelPoints;

    public async Task<string?> InstalledVersion(CancellationToken cancellation)
    {
        installedNow = null;
        if (updateAutomatically())
        {
            var check = await points.CheckAsync(install: true, cancellation);
            if (check.Error is null) installedNow = check;
        }
        return points.Current.Version;
    }

    public async Task<string?> LatestVersion(CancellationToken cancellation)
    {
        var check = installedNow ?? await points.CheckAsync(install: false, cancellation);
        installedNow = null;
        if (check.Error is { } error) throw new InvalidOperationException(error);
        return check.Latest ?? points.Current.Version;
    }

    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        progress.Report(null);
        var check = await points.CheckAsync(install: true, cancellation);
        return check.Error is { } error
            ? new UpdateOutcome(false, null, false, false, error)
            : new UpdateOutcome(true, points.Current.Version, check.Updated, false, null);
    }
}
