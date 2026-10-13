using Marrow.Core.Web;
using Marrow.Testing;
using Marrow.UI.NthUi.Map;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// W10 (ED-NEW-web-w10-5): a street tile source over a scripted transport, a manual clock, a cache folder of its own and a post seam the test runs by
/// hand. A fetch runs on the pool; <see cref="WaitForChange"/> waits for the source to say a tile arrived and runs what it posted.
/// </summary>
internal sealed class MapTilesRig : IDisposable
{
    public const string Template = "https://tiles.example.org/{z}/{x}/{y}.png";

    /// <summary>A 1 x 1 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private readonly ManualResetEventSlim posted = new(false);
    private readonly List<Action> pending = [];
    private readonly object gate = new();

    public MapTilesRig(bool cache = false, string template = Template, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? script = null)
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
        CacheRoot = Path.Combine(data, "MapTiles", Guid.NewGuid().ToString("N"));
        Handler = new ScriptedHttpHandler { Script = script ?? ((_, _) => Task.FromResult(ScriptedHttpHandler.Ok("image/png", Png))) };
        Assert.True(MapTileTemplate.TryParse(template, out var parsed));
        Parsed = parsed;
        Time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
        UseCache = cache;
        Tiles = Make();
    }

    public MapTileTemplate Parsed { get; }

    public ScriptedHttpHandler Handler { get; }

    public ManualTimeProvider Time { get; }

    public string CacheRoot { get; }

    public bool UseCache { get; }

    public HttpMapTiles Tiles { get; private set; }

    public int Changes { get; private set; }

    /// <summary>Another source over the same transport and folder, as a new run of the app would make.</summary>
    public HttpMapTiles Make()
    {
        var tiles = new HttpMapTiles(Parsed, Post, Handler, UseCache ? CacheRoot : null, Time);
        tiles.TilesChanged += () => Changes++;
        return tiles;
    }

    public void Replace() => Tiles = Make();

    private void Post(Action action)
    {
        lock (gate) pending.Add(action);
        posted.Set();
    }

    /// <summary>Waits until the source has posted a change (a fetch or a read finished), then runs what was posted.</summary>
    public void WaitForChange()
    {
        Assert.True(posted.Wait(TimeSpan.FromSeconds(20)), "no tile arrived");
        RunPosted();
    }

    /// <summary>Waits until no tile is on its way (loaded or failed), then runs what the source posted.</summary>
    public void Settle()
    {
        Assert.True(Task.WaitAll([Tiles.WhenIdleAsync()], TimeSpan.FromSeconds(20)), "a tile was still on its way");
        RunPosted();
    }

    public void RunPosted()
    {
        Action[] work;
        lock (gate)
        {
            work = [.. pending];
            pending.Clear();
            posted.Reset();
        }
        foreach (var action in work) action();
    }

    public void Dispose()
    {
        Tiles.Dispose();
        try { Directory.Delete(CacheRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
