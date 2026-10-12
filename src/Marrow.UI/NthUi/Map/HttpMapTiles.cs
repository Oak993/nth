using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.Core.Port;
using Marrow.Core.Web;

// PORT-ONLY: web surfaces W10, the street tiles a reader asked for (E:/Droppy/work/design/web-surfaces/DESIGN.md 7.2 "Street tiles only if the user
// adds a tile source in Settings; off by default because it sends locations to a third party", 9 "Map street tiles source (none)"; ED-NEW-web-w10-5).
// No Swift counterpart. The source Settings binds when the reader adds a tile address (MapTileTemplate: https, {z} {x} {y}, a site on the
// internet). The map asks for the tiles in view; each is fetched once, kept in memory (256, least recently used first out) and on disk (64 MB,
// oldest first out, a tile older than 30 days is asked for again), and the map is told when one arrives. A request is https to the one host in the
// address, with no cookies, no credentials, no referrer and no redirects, at most four at a time, 5 seconds each; a tile over 512 KB, a
// reply that is not an image or one that does not decode is dropped and not asked for again for a minute. The disk key is the address's own hash, so a
// changed address never reads another source's tiles. Nothing here runs unless a reader added a source.

namespace Marrow.UI.NthUi.Map;

public sealed class HttpMapTiles : INthUiMapTiles, IDisposable
{
    public const int MaxTileBytes = 512 * 1024;
    public const int MemoryTiles = 256;
    public const int MaxInFlight = 4;
    public const long DiskCapBytes = 64L * 1024 * 1024;
    public static readonly TimeSpan Stale = TimeSpan.FromDays(30);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private static readonly Logger Log = new(Logger.Subsystem, "map");

    private readonly MapTileTemplate template;
    private readonly Action<Action> post;
    private readonly HttpClient client;
    private readonly string? cacheFolder;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim gate = new(MaxInFlight);
    private readonly CancellationTokenSource stop = new();
    private readonly object memoryLock = new();
    private readonly Dictionary<(int Z, int X, int Y), LinkedListNode<(int Z, int X, int Y, Bitmap Image)>> memory = [];
    private readonly LinkedList<(int Z, int X, int Y, Bitmap Image)> order = new();
    private readonly ConcurrentDictionary<(int Z, int X, int Y), DateTimeOffset?> requested = new();
    private int writesSinceTrim;
    private int changePosted;

    /// <param name="template">The checked tile address.</param>
    /// <param name="post">Runs an action on the UI thread (the change event goes out there).</param>
    /// <param name="handler">The transport; tests hand a fake.</param>
    /// <param name="cacheFolder">The folder tiles are kept in; null keeps none on disk (tests).</param>
    public HttpMapTiles(MapTileTemplate template, Action<Action> post, HttpMessageHandler? handler = null, string? cacheFolder = null, TimeProvider? time = null)
    {
        this.template = template;
        this.post = post;
        this.time = time ?? TimeProvider.System;
        this.cacheFolder = cacheFolder is null ? null : Path.Combine(cacheFolder, Key(template));
        client = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false }, disposeHandler: true) { Timeout = RequestTimeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("nth", "1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg", 0.9));
    }

    public int MaxZoom => 19;

    public string Attribution => template.Attribution;

    public event Action? TilesChanged;

    /// <summary>The tiles kept in memory now.</summary>
    public int MemoryCount
    {
        get
        {
            lock (memoryLock) return memory.Count;
        }
    }

    /// <summary>The tile if it is here; otherwise null, and it is fetched (or read from disk) in the background.</summary>
    public IImage? Tile(int zoom, int x, int y)
    {
        if (zoom is < 0 or > 19) return null;
        var n = 1 << zoom;
        if (x < 0 || x >= n || y < 0 || y >= n) return null;
        var key = (zoom, x, y);
        lock (memoryLock)
        {
            if (memory.TryGetValue(key, out var node))
            {
                order.Remove(node);
                order.AddFirst(node);
                return node.Value.Image;
            }
        }
        if (stop.IsCancellationRequested) return null;
        var now = time.GetUtcNow();
        if (requested.TryGetValue(key, out var failedAt))
        {
            if (failedAt is null || now - failedAt < RetryAfterFailure) return null; // on its way, or failed lately
        }
        requested[key] = null;
        _ = Task.Run(() => LoadAsync(key), stop.Token);
        return null;
    }

    /// <summary>Removes this source's tiles from disk (the reader took the source away or changed its address).</summary>
    public void DeleteCache()
    {
        if (cacheFolder is null) return;
        try
        {
            if (Directory.Exists(cacheFolder)) Directory.Delete(cacheFolder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error($"map: the tile cache could not be removed ({error.GetType().Name})");
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        client.Dispose();
        lock (memoryLock)
        {
            foreach (var entry in order) entry.Image.Dispose();
            order.Clear();
            memory.Clear();
        }
    }

    private async Task LoadAsync((int Z, int X, int Y) key)
    {
        try
        {
            await gate.WaitAsync(stop.Token).ConfigureAwait(false);
            try
            {
                var cached = ReadDisk(key) is { } onDisk ? Decode(onDisk) : null;
                var image = cached ?? await FetchAsync(key).ConfigureAwait(false);
                if (image is null)
                {
                    requested[key] = time.GetUtcNow();
                    return;
                }
                Remember(key, image);
                requested.TryRemove(key, out _);
                RaiseChanged();
            }
            finally
            {
                gate.Release();
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
        {
            requested.TryRemove(key, out _); // the source was let go while this tile was on its way
        }
    }

    private async Task<Bitmap?> FetchAsync((int Z, int X, int Y) key)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, template.UrlFor(key.Z, key.X, key.Y));
            request.Headers.Referrer = null;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return null;
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            if (response.Content.Headers.ContentLength is { } length && length > MaxTileBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(stop.Token).ConfigureAwait(false);
            var buffer = new byte[MaxTileBytes + 1];
            var read = 0;
            while (read < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(read), stop.Token).ConfigureAwait(false);
                if (count == 0) break;
                read += count;
            }
            if (read > MaxTileBytes) return null;
            var bytes = buffer.AsSpan(0, read).ToArray();
            if (Decode(bytes) is not { } image) return null; // only a picture is kept
            WriteDisk(key, bytes);
            return image;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        {
            Log.Error($"map: a tile could not be fetched ({error.GetType().Name})");
            return null;
        }
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            Log.Error($"map: a tile did not decode ({error.GetType().Name})");
            return null;
        }
    }

    private void Remember((int Z, int X, int Y) key, Bitmap image)
    {
        lock (memoryLock)
        {
            if (memory.Remove(key, out var old))
            {
                order.Remove(old);
                old.Value.Image.Dispose();
            }
            memory[key] = order.AddFirst((key.Z, key.X, key.Y, image));
            while (memory.Count > MemoryTiles && order.Last is { } last)
            {
                order.RemoveLast();
                memory.Remove((last.Value.Z, last.Value.X, last.Value.Y));
                last.Value.Image.Dispose();
            }
        }
    }

    private void RaiseChanged()
    {
        if (Interlocked.Exchange(ref changePosted, 1) == 1) return; // one pending tell covers every tile that arrives before it runs
        post(() =>
        {
            Interlocked.Exchange(ref changePosted, 0);
            if (!stop.IsCancellationRequested) TilesChanged?.Invoke();
        });
    }

    private string? TilePath((int Z, int X, int Y) key) =>
        cacheFolder is null ? null : Path.Combine(cacheFolder, key.Z.ToString(System.Globalization.CultureInfo.InvariantCulture), key.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            key.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".tile");

    private byte[]? ReadDisk((int Z, int X, int Y) key)
    {
        if (TilePath(key) is not { } path) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 or > MaxTileBytes || time.GetUtcNow() - info.LastWriteTimeUtc > Stale) return null;
            return File.ReadAllBytes(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteDisk((int Z, int X, int Y) key, byte[] bytes)
    {
        if (TilePath(key) is not { } path) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
            if (Interlocked.Increment(ref writesSinceTrim) % 50 == 0) Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error($"map: a tile could not be kept ({error.GetType().Name})");
        }
    }

    // The disk cap: the oldest files go until the folder fits.
    private void Trim()
    {
        if (cacheFolder is null || !Directory.Exists(cacheFolder)) return;
        try
        {
            var files = new DirectoryInfo(cacheFolder).EnumerateFiles("*.tile", SearchOption.AllDirectories).OrderBy(file => file.LastWriteTimeUtc).ToList();
            var total = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (total <= DiskCapBytes) break;
                total -= file.Length;
                file.Delete();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error($"map: the tile cache could not be trimmed ({error.GetType().Name})");
        }
    }

    // The folder a source's tiles live in: the hash of its whole address, so no other address ever reads them.
    private static string Key(MapTileTemplate template) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(template.Text)), 0, 8).ToLowerInvariant();
}
