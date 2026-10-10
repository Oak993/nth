// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1). The model points in use (IIgnisPoints): the bundled
// copy (IgnisPointsData), or a newer valid copy fetched from the GitHub feed and kept at
// Storage.Root/ignis/model-points.json. A stored copy that is invalid, or not newer than the bundled one (a Marrow update
// brought newer points), is not used. A fetched file is installed only when it is valid and newer than the points in
// use; an older or equal one keeps them. Checking never sends anything to a model provider.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Services.Port.Ignis;

public sealed class IgnisPointsStore : IIgnisPoints
{
    private readonly Lock gate = new();
    private readonly SemaphoreSlim checking = new(1, 1);
    private readonly Uri feed;
    private IgnisPointsFile current;
    private IgnisPointsOrigin origin;
    private DateTimeOffset? checkedAt;

    /// <param name="dataRoot">The app's data root (Storage.Root); the stored copy is dataRoot/ignis/model-points.json.</param>
    /// <param name="feed">The feed address; null: <see cref="IgnisPointsFeed.Url"/>.</param>
    /// <param name="bundledJson">The bundled points; null: <see cref="IgnisPointsData.Json"/>.</param>
    public IgnisPointsStore(string dataRoot, Uri? feed = null, string? bundledJson = null)
    {
        StoredPath = Path.Combine(dataRoot, "ignis", "model-points.json");
        this.feed = feed ?? new Uri(IgnisPointsFeed.Url);
        var bundled = IgnisPointsParser.Parse(bundledJson ?? IgnisPointsData.Json);
        Bundled = bundled.File ?? throw new InvalidOperationException("The bundled Ignis model points are invalid: " + bundled.Error);
        (current, origin) = (Bundled, IgnisPointsOrigin.Bundled);
        if (ReadStored() is { } stored && Newer(stored, Bundled)) (current, origin) = (stored, IgnisPointsOrigin.Stored);
    }

    public event Action? Changed;

    public string StoredPath { get; }

    public IgnisPointsFile Bundled { get; }

    public IgnisPointsFile Current { get { lock (gate) return current; } }

    public IgnisPointsOrigin Origin { get { lock (gate) return origin; } }

    public DateTimeOffset? CheckedAt { get { lock (gate) return checkedAt; } }

    public IgnisModelPoints? Find(ProviderKind provider, string? model) => IgnisPointsParser.Find(Current, provider, model);

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

    /// <summary>Whether <paramref name="candidate"/>'s version is later than <paramref name="than"/>'s ("yyyy.MM.dd", ordinal).</summary>
    public static bool Newer(IgnisPointsFile candidate, IgnisPointsFile than) => string.CompareOrdinal(candidate.Version, than.Version) > 0;

    private void Checked()
    {
        lock (gate) checkedAt = SwiftDate.Now;
    }

    private IgnisPointsFile? ReadStored()
    {
        try
        {
            if (!File.Exists(StoredPath)) return null;
            return IgnisPointsParser.Parse(Encoding.UTF8.GetString(File.ReadAllBytes(StoredPath))).File;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
