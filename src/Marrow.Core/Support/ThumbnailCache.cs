using System.Globalization;
using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/ThumbnailCache.swift (B2 1fbf81e), :1-83.
public sealed class ThumbnailCache : Actor
{
    // :10
    public static Ambient<ThumbnailCache> Shared { get; } = new(new ThumbnailCache());

    // :12-14
    public sealed record Thumbnail(DecodedBitmap Image);

    // :16-20
    private readonly RecentCache<string, Thumbnail> thumbnails = new(120, 32 * 1_024 * 1_024);
    private readonly Dictionary<string, TaskHandle<Thumbnail?>> inFlight = new(SwiftStringComparer.Instance);
    private long decodes;

    public ThumbnailCache(IExecutor? target = null) : base(target) { }

    // :20 test-readable actor state.
    public Task<long> Decodes() => Isolated(() => decodes);

    // :22-42
    public Task<Thumbnail?> ThumbnailFor(string path, double pointSize, bool fillingSquare = false) => Isolated(async () =>
    {
        if (SwiftTask.IsCancelled) return null;
        var pixels = Math.Max(1L, SwiftNumber.TruncatingToInt(Math.Ceiling(pointSize * 2)));
        var key = path + "#" + pixels.ToString(CultureInfo.InvariantCulture) + "#" + (fillingSquare ? "true" : "false");
        if (thumbnails.TryGetValue(key, out var cached)) return cached;
        if (!inFlight.TryGetValue(key, out var task))
        {
            decodes++;
            task = TaskHandle.Detached(() => Task.FromResult(Decode(path, pixels, fillingSquare)));
            inFlight[key] = task;
        }
        var thumbnail = await task.Value;
        if (inFlight.TryGetValue(key, out var pending) && ReferenceEquals(pending, task))
        {
            inFlight.Remove(key);
            if (thumbnail is not null)
                thumbnails.Insert(thumbnail, key, checked((long)thumbnail.Image.BytesPerRow * thumbnail.Image.Height));
        }
        return SwiftTask.IsCancelled ? null : thumbnail;
    });

    // :44-62
    private static Thumbnail? Decode(string path, long pixels, bool fillingSquare)
    {
        var image = WicImageDecoder.Decode(path, pixels, fillingSquare);
        return image is null ? null : new Thumbnail(image);
    }
}

// :65-83
public static class ThumbnailMemory
{
    private static readonly RecentCache<string, DecodedBitmap> images = new(120, 16 * 1_024 * 1_024);

    // :72-74
    private static string Key(string path, double pointSize) => path + "#" +
        SwiftNumber.TruncatingToInt(Math.Ceiling(pointSize * 2)).ToString(CultureInfo.InvariantCulture);

    // :76-78
    public static DecodedBitmap? ImageFor(string path, double pointSize)
    {
        MainActor.AssumeIsolated();
        return images.TryGetValue(Key(path, pointSize), out var image) ? image : null;
    }

    // :80-82
    public static void Store(DecodedBitmap image, string path, double pointSize)
    {
        MainActor.AssumeIsolated();
        images.Insert(image, Key(path, pointSize), checked((long)image.BytesPerRow * image.Height));
    }
}
