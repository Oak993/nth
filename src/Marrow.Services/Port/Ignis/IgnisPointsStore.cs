// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1; price catalogue: ED-NEW-ignis-data). The model points in
// use (IIgnisPoints): the bundled copy (IgnisPointsData), or a newer valid copy fetched from the GitHub feed and kept at
// Storage.Root/ignis/model-points.json. A stored copy that is invalid, or not newer than the bundled one (a Marrow update
// brought newer points), is not used. A fetched file is installed only when it is valid and newer than the points in
// use; an older or equal one keeps them. Beside them, the all-model price catalogue (IIgnisPriceCatalogue,
// IgnisPriceCatalogue) is kept at Storage.Root/ignis/prices.json: Find asks the curated points first (with the
// catalogue's prices once a curated entry is old) and then the catalogue. Checking never sends anything to a model provider.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Services.Port.Ignis;

public sealed class IgnisPointsStore : IIgnisPoints, IIgnisPriceCatalogue
{
    private readonly Lock gate = new();
    private readonly SemaphoreSlim checking = new(1, 1);
    private readonly SemaphoreSlim checkingPrices = new(1, 1);
    private readonly Uri feed;
    private readonly Uri catalogueAddress;
    private IgnisPointsFile current;
    private IgnisPointsOrigin origin;
    private DateTimeOffset? checkedAt;
    private IgnisCatalogue? catalogue;

    /// <param name="dataRoot">The app's data root (Storage.Root); the stored copy is dataRoot/ignis/model-points.json.</param>
    /// <param name="feed">The feed address; null: <see cref="IgnisPointsFeed.Url"/>.</param>
    /// <param name="bundledJson">The bundled points; null: <see cref="IgnisPointsData.Json"/>.</param>
    /// <param name="catalogue">The price catalogue's address; null: <see cref="IgnisPriceCatalogue.Url"/>.</param>
    public IgnisPointsStore(string dataRoot, Uri? feed = null, string? bundledJson = null, Uri? catalogue = null)
    {
        StoredPath = Path.Combine(dataRoot, "ignis", "model-points.json");
        PricesPath = Path.Combine(dataRoot, "ignis", "prices.json");
        this.feed = feed ?? new Uri(IgnisPointsFeed.Url);
        catalogueAddress = catalogue ?? new Uri(IgnisPriceCatalogue.Url);
        var bundled = IgnisPointsParser.Parse(bundledJson ?? IgnisPointsData.Json);
        Bundled = bundled.File ?? throw new InvalidOperationException("The bundled Ignis model points are invalid: " + bundled.Error);
        (current, origin) = (Bundled, IgnisPointsOrigin.Bundled);
        if (ReadStored() is { } stored && Newer(stored, Bundled)) (current, origin) = (stored, IgnisPointsOrigin.Stored);
        this.catalogue = IgnisPriceCatalogue.Read(ReadText(PricesPath));
    }

    public event Action? Changed;

    public string StoredPath { get; }

    /// <summary>Where the trimmed price catalogue is kept (dataRoot/ignis/prices.json).</summary>
    public string PricesPath { get; }

    public IgnisPointsFile Bundled { get; }

    public IgnisPointsFile Current { get { lock (gate) return current; } }

    public IgnisPointsOrigin Origin { get { lock (gate) return origin; } }

    public DateTimeOffset? CheckedAt { get { lock (gate) return checkedAt; } }

    /// <summary>The price catalogue in use; null when none was fetched yet.</summary>
    public IgnisCatalogue? Catalogue { get { lock (gate) return catalogue; } }

    public string? PricesDay => Catalogue?.Day;

    public DateTimeOffset? PricesCheckedAt => Catalogue?.CheckedAt;

    public IReadOnlyList<string> PricesWarnings => Catalogue?.Warnings ?? [];

    /// <summary>
    /// The curated model (with the catalogue's prices once its entry is older than 30 days and the prices agree within
    /// 4x), else the catalogue's model under its provider's rule; null when neither knows it.
    /// </summary>
    public IgnisModelPoints? Find(ProviderKind provider, string? model)
    {
        var points = Current;
        var prices = Catalogue;
        if (IgnisPointsParser.Find(points, provider, model) is { } curated)
            return IgnisPriceCatalogue.Merge(curated, prices, DateOnly.FromDateTime(SwiftDate.Now.UtcDateTime));
        return IgnisPriceCatalogue.Find(prices, Rules(points), provider, model);
    }

    public async Task<IgnisPointsCheck> CheckAsync(bool install, CancellationToken cancellationToken)
    {
        await checking.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fetched = await IgnisPointsFeed.Fetch(feed, cancellationToken).ConfigureAwait(false);
            if (fetched.NotPublished)
            {
                Checked();
                return new IgnisPointsCheck(Current.Version, null, false);
            }
            if (fetched.Error is { } error) return new IgnisPointsCheck(Current.Version, null, false, error);
            var parsed = IgnisPointsParser.Parse(fetched.Body);
            if (parsed.File is not { } file) return new IgnisPointsCheck(Current.Version, null, false, "The feed's model points are invalid: " + parsed.Error);
            Checked();
            if (!install || !Newer(file, Current)) return new IgnisPointsCheck(Current.Version, file.Version, false);

            Directory.CreateDirectory(Path.GetDirectoryName(StoredPath)!);
            FileOps.WriteAtomic(StoredPath, Encoding.UTF8.GetBytes(fetched.Body!));
            lock (gate) (current, origin) = (file, IgnisPointsOrigin.Stored);
            Changed?.Invoke();
            return new IgnisPointsCheck(file.Version, file.Version, true);
        }
        finally
        {
            checking.Release();
        }
    }

    public async Task<IgnisPricesCheck> CheckPricesAsync(bool force, CancellationToken cancellationToken)
    {
        await checkingPrices.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var held = Catalogue;
            var now = SwiftDate.Now;
            if (!force && held is not null && now - held.CheckedAt < IgnisPriceCatalogue.CheckEvery && now >= held.CheckedAt)
                return new IgnisPricesCheck(held.Day, false, true);
            var fetched = await IgnisPriceCatalogue.Fetch(catalogueAddress, held?.ETag, cancellationToken).ConfigureAwait(false);
            if (fetched.NotPublished) return new IgnisPricesCheck(held?.Day, false, false, "The price catalogue was not found.");
            if (fetched.Error is { } error) return new IgnisPricesCheck(held?.Day, false, false, error);
            if (fetched.NotModified && held is not null)
            {
                var confirmed = held.Confirmed(now, fetched.ETag);
                Keep(confirmed);
                return new IgnisPricesCheck(confirmed.Day, false, false);
            }
            var built = IgnisPriceCatalogue.Build(fetched.Body, Rules(Current), now, fetched.ETag);
            if (built.Catalogue is not { } fresh) return new IgnisPricesCheck(held?.Day, false, false, built.Error);
            Keep(fresh);
            Changed?.Invoke();
            return new IgnisPricesCheck(fresh.Day, true, false);
        }
        finally
        {
            checkingPrices.Release();
        }
    }

    public async Task<IgnisPricesCheck> ProbePricesAsync(CancellationToken cancellationToken)
    {
        await checkingPrices.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var held = Catalogue;
            var now = SwiftDate.Now;
            var fetched = await IgnisPriceCatalogue.Fetch(catalogueAddress, held?.ETag, cancellationToken).ConfigureAwait(false);
            if (fetched.NotPublished) return new IgnisPricesCheck(held?.Day, false, false, "The price catalogue was not found.");
            if (fetched.Error is { } error) return new IgnisPricesCheck(held?.Day, false, false, error);
            if (fetched.NotModified && held is not null) return new IgnisPricesCheck(held.Day, false, false);
            var built = IgnisPriceCatalogue.Build(fetched.Body, Rules(Current), now, fetched.ETag);
            return built.Catalogue is { } fresh
                ? new IgnisPricesCheck(fresh.Day, true, false)
                : new IgnisPricesCheck(held?.Day, false, false, built.Error);
        }
        finally
        {
            checkingPrices.Release();
        }
    }

    /// <summary>Whether <paramref name="candidate"/>'s version is later than <paramref name="than"/>'s ("yyyy.MM.dd", ordinal).</summary>
    public static bool Newer(IgnisPointsFile candidate, IgnisPointsFile than) => string.CompareOrdinal(candidate.Version, than.Version) > 0;

    /// <summary>The file whose provider rules apply: the points in use, or the bundled ones when those name none (a v1 file).</summary>
    private IgnisPointsFile Rules(IgnisPointsFile points) => points.Providers.Count > 0 ? points : Bundled;

    /// <summary>Uses <paramref name="kept"/> and stores it; a copy that cannot be written is still used until nth quits.</summary>
    private void Keep(IgnisCatalogue kept)
    {
        lock (gate) catalogue = kept;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PricesPath)!);
            FileOps.WriteAtomic(PricesPath, Encoding.UTF8.GetBytes(IgnisPriceCatalogue.Write(kept)));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Checked()
    {
        lock (gate) checkedAt = SwiftDate.Now;
    }

    private IgnisPointsFile? ReadStored() => ReadText(StoredPath) is { } text ? IgnisPointsParser.Parse(text).File : null;

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? Encoding.UTF8.GetString(File.ReadAllBytes(path)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
