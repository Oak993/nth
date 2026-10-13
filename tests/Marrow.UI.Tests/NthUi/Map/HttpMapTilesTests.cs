using System.Net;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Testing;
using Marrow.UI.NthUi.Map;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi.Map;

// PORT-ONLY: web surfaces W10, "Map street tiles source (none)" (E:/Droppy/work/design/web-surfaces/DESIGN.md 7.2, 9; ED-NEW-web-w10-5). A tile is fetched once, kept in memory and on disk with caps, asked
// for with nothing that identifies the reader, bounded in size and time, and a source that is let go keeps nothing and asks for nothing more. No network: a scripted transport.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-5")]
public sealed class HttpMapTilesTests(HeadlessSession headless)
{
    [Fact]
    public void A_tile_is_fetched_once_and_then_comes_from_memory_and_the_map_is_told() => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig();
        Assert.Null(rig.Tiles.Tile(3, 2, 1));
        Assert.Null(rig.Tiles.Tile(3, 2, 1)); // still on its way: not asked twice
        rig.Settle();
        Assert.Equal(1, rig.Changes);
        var image = rig.Tiles.Tile(3, 2, 1);
        Assert.NotNull(image);
        Assert.Same(image, rig.Tiles.Tile(3, 2, 1));
        var request = Assert.Single(rig.Handler.Requests);
        Assert.Equal("https://tiles.example.org/3/2/1.png", request.Url.AbsoluteUri);
        Assert.Equal("GET", request.Method);
        Assert.False(request.Headers.ContainsKey("Cookie"));
        Assert.False(request.Headers.ContainsKey("Referer"));
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.StartsWith("nth/", request.Headers["User-Agent"], StringComparison.Ordinal);
        Assert.Equal(19, rig.Tiles.MaxZoom);
        Assert.Equal("Map tiles from tiles.example.org", rig.Tiles.Attribution);
    });

    [Fact]
    public void Tiles_that_arrive_together_tell_the_map_once() => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig();
        for (var x = 0; x < 6; x++) Assert.Null(rig.Tiles.Tile(4, x, 3));
        rig.Settle();
        Assert.Equal(6, rig.Handler.Requests.Count);
        Assert.True(rig.Changes is >= 1 and <= 6);
        for (var x = 0; x < 6; x++) Assert.NotNull(rig.Tiles.Tile(4, x, 3));
        Assert.Equal(6, rig.Handler.Requests.Count);
    });

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(20, 0, 0)]
    [InlineData(2, 4, 0)]
    [InlineData(2, 0, 4)]
    [InlineData(2, -1, 0)]
    [InlineData(0, 0, 1)]
    public void A_tile_that_does_not_exist_is_not_asked_for(int zoom, int x, int y) => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig();
        Assert.Null(rig.Tiles.Tile(zoom, x, y));
        rig.Settle();
        Assert.Empty(rig.Handler.Requests);
    });

    [Fact]
    public void Tiles_wrap_nowhere_the_map_asks_for_the_numbers_inside_the_world() => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig();
        Assert.Null(rig.Tiles.Tile(2, 3, 3));
        rig.Settle();
        Assert.Equal("https://tiles.example.org/2/3/3.png", Assert.Single(rig.Handler.Requests).Url.AbsoluteUri);
    });

    [Fact]
    public void A_tile_on_disk_is_read_again_by_a_new_run_without_a_request_and_a_changed_address_never_reads_it() => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig(cache: true);
        Assert.Null(rig.Tiles.Tile(5, 7, 9));
        rig.Settle();
        Assert.Single(rig.Handler.Requests);
        var files = Directory.GetFiles(rig.CacheRoot, "*.tile", SearchOption.AllDirectories);
        Assert.Single(files);
        Assert.EndsWith(Path.Combine("5", "7", "9.tile"), files[0], StringComparison.Ordinal);

        rig.Tiles.Dispose();
        rig.Replace();
        Assert.Null(rig.Tiles.Tile(5, 7, 9));
        rig.Settle();
        Assert.NotNull(rig.Tiles.Tile(5, 7, 9));
        Assert.Single(rig.Handler.Requests); // from disk

        // another address, another folder: nothing of the first source is read
        using var other = new MapTilesRig(cache: true, template: "https://tiles.example.org/other/{z}/{x}/{y}.png");
        Assert.NotEqual(rig.CacheRoot, other.CacheRoot);
        Assert.Null(other.Tiles.Tile(5, 7, 9));
        other.Settle();
        Assert.Single(other.Handler.Requests);
    });

    [Fact]
    public void Two_sources_with_different_addresses_in_one_folder_keep_apart() => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig(cache: true);
        Assert.Null(rig.Tiles.Tile(1, 1, 1));
        rig.Settle();
        var second = new HttpMapTiles(ParseTemplate("https://tiles.example.org/b/{z}/{x}/{y}.png"), static action => action(), rig.Handler, rig.CacheRoot, rig.Time);
        try
        {
            Assert.Null(second.Tile(1, 1, 1));
            Assert.True(Task.WaitAll([second.WhenIdleAsync()], TimeSpan.FromSeconds(20)));
            Assert.Equal(2, rig.Handler.Requests.Count);
            Assert.Equal(2, Directory.GetFiles(rig.CacheRoot, "*.tile", SearchOption.AllDirectories).Length);
        }
        finally
        {
            second.Dispose();
        }
    });

    private static MapTileTemplate ParseTemplate(string text)
    {
        Assert.True(MapTileTemplate.TryParse(text, out var template));
        return template;
    }

    [Fact]
    public void A_tile_on_disk_older_than_30_days_is_asked_for_again() => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig(cache: true);
        Assert.Null(rig.Tiles.Tile(2, 1, 1));
        rig.Settle();
        var file = Directory.GetFiles(rig.CacheRoot, "*.tile", SearchOption.AllDirectories).Single();
        File.SetLastWriteTimeUtc(file, rig.Time.GetUtcNow().UtcDateTime.AddDays(-29));
        rig.Tiles.Dispose();
        rig.Replace();
        Assert.Null(rig.Tiles.Tile(2, 1, 1));
        rig.Settle();
        Assert.Single(rig.Handler.Requests);

        File.SetLastWriteTimeUtc(file, rig.Time.GetUtcNow().UtcDateTime.AddDays(-31));
        rig.Tiles.Dispose();
        rig.Replace();
        Assert.Null(rig.Tiles.Tile(2, 1, 1));
        rig.Settle();
        Assert.Equal(2, rig.Handler.Requests.Count);
    });

    [Theory]
    [InlineData("text/html", "<html>not a tile</html>")]
    [InlineData("application/json", "{}")]
    [InlineData("image/png", "this is not a png")]
    public void A_reply_that_is_not_a_picture_is_dropped_and_not_kept(string contentType, string body) => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig(cache: true, script: (_, _) => Task.FromResult(ScriptedHttpHandler.Ok(contentType, body)));
        Assert.Null(rig.Tiles.Tile(3, 1, 1));
        rig.Settle();
        Assert.Null(rig.Tiles.Tile(3, 1, 1));
        Assert.Equal(0, rig.Changes);
        Assert.Single(rig.Handler.Requests);
        Assert.False(Directory.Exists(rig.CacheRoot) && Directory.GetFiles(rig.CacheRoot, "*", SearchOption.AllDirectories).Length > 0);
    });

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void A_status_other_than_200_is_dropped_and_a_redirect_is_not_followed(HttpStatusCode status) => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig(script: (_, _) =>
        {
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.Redirect) response.Headers.Location = new Uri("https://evil.example.org/tile.png");
            return Task.FromResult(response);
        });
        Assert.Null(rig.Tiles.Tile(3, 1, 1));
        rig.Settle();
        Assert.Null(rig.Tiles.Tile(3, 1, 1));
        Assert.Single(rig.Handler.Requests);
        Assert.Equal("tiles.example.org", rig.Handler.Requests[0].Url.Host);
    });

    [Fact]
    public void A_tile_over_512_KB_is_dropped_whether_or_not_it_says_how_big_it_is() => headless.OnUiThread(() =>
    {
        var big = new byte[HttpMapTiles.MaxTileBytes + 1];
        Array.Copy(MapTilesRig.Png, big, MapTilesRig.Png.Length);
        using var sized = new MapTilesRig(script: (_, _) => Task.FromResult(ScriptedHttpHandler.Ok("image/png", big)));
        Assert.Null(sized.Tiles.Tile(3, 1, 1));
        sized.Settle();
        Assert.Null(sized.Tiles.Tile(3, 1, 1));
        Assert.Equal(0, sized.Changes);
    });

    [Fact]
    public void A_failed_tile_is_asked_for_again_only_after_a_minute() => headless.OnUiThread(() =>
    {
        var answer = 404;
        using var rig = new MapTilesRig(script: (_, _) => Task.FromResult(answer == 200 ? ScriptedHttpHandler.Ok("image/png", MapTilesRig.Png) : new HttpResponseMessage((HttpStatusCode)answer)));
        Assert.Null(rig.Tiles.Tile(3, 1, 1));
        rig.Settle();
        rig.Time.Advance(TimeSpan.FromSeconds(59));
        Assert.Null(rig.Tiles.Tile(3, 1, 1));
        rig.Settle();
        Assert.Single(rig.Handler.Requests);
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        answer = 200;
        Assert.Null(rig.Tiles.Tile(3, 1, 1));
        rig.Settle();
        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.NotNull(rig.Tiles.Tile(3, 1, 1));
    });

    [Fact]
    public void No_more_than_four_requests_are_in_flight_at_once() => headless.OnUiThread(() =>
    {
        var inFlight = 0;
        var peak = 0;
        var release = new TaskCompletionSource();
        using var rig = new MapTilesRig(script: async (_, _) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            lock (release) peak = Math.Max(peak, now);
            await release.Task;
            Interlocked.Decrement(ref inFlight);
            return ScriptedHttpHandler.Ok("image/png", MapTilesRig.Png);
        });
        for (var x = 0; x < 12; x++) Assert.Null(rig.Tiles.Tile(4, x, 0));
        // the first four are held in the transport; the other eight wait for a place
        SpinWait.SpinUntil(() => Volatile.Read(ref inFlight) >= HttpMapTiles.MaxInFlight, TimeSpan.FromSeconds(20));
        Assert.Equal(HttpMapTiles.MaxInFlight, Volatile.Read(ref inFlight));
        release.SetResult();
        rig.Settle();
        Assert.Equal(12, rig.Handler.Requests.Count);
        Assert.Equal(HttpMapTiles.MaxInFlight, peak);
    });

    [Fact]
    public void Memory_holds_256_tiles_and_the_least_recently_used_go_first() => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig();
        for (var x = 0; x < HttpMapTiles.MemoryTiles + 20; x++) Assert.Null(rig.Tiles.Tile(9, x, 1));
        rig.Settle();
        Assert.Equal(HttpMapTiles.MemoryTiles, rig.Tiles.MemoryCount);
        var asked = rig.Handler.Requests.Count;
        Assert.Equal(HttpMapTiles.MemoryTiles + 20, asked);
        Assert.NotNull(rig.Tiles.Tile(9, HttpMapTiles.MemoryTiles + 19, 1)); // the newest is there, and asking for it again costs no request
        Assert.Equal(asked, rig.Handler.Requests.Count);
    });

    [Fact]
    public void A_source_that_is_let_go_asks_for_nothing_more_and_a_late_reply_changes_nothing() => headless.OnUiThread(() =>
    {
        var release = new TaskCompletionSource();
        using var rig = new MapTilesRig(script: async (_, token) =>
        {
            await release.Task;
            token.ThrowIfCancellationRequested();
            return ScriptedHttpHandler.Ok("image/png", MapTilesRig.Png);
        });
        Assert.Null(rig.Tiles.Tile(3, 1, 1));
        SpinWait.SpinUntil(() => rig.Handler.Requests.Count == 1, TimeSpan.FromSeconds(20));
        rig.Tiles.Dispose();
        release.SetResult();
        rig.Settle();
        Assert.Equal(0, rig.Changes);
        Assert.Null(rig.Tiles.Tile(3, 2, 2));
        Assert.Single(rig.Handler.Requests);
        Assert.Equal(0, rig.Tiles.MemoryCount);
    });

    [Fact]
    public void Taking_the_source_away_deletes_its_tiles_from_disk() => headless.OnUiThread(() =>
    {
        using var rig = new MapTilesRig(cache: true);
        Assert.Null(rig.Tiles.Tile(2, 1, 1));
        rig.Settle();
        Assert.Single(Directory.GetFiles(rig.CacheRoot, "*.tile", SearchOption.AllDirectories));
        rig.Tiles.DeleteCache();
        Assert.Empty(Directory.GetFiles(rig.CacheRoot, "*", SearchOption.AllDirectories));
    });

    [Fact]
    public void A_failure_is_logged_by_name_and_never_with_the_address() => headless.OnUiThread(() =>
    {
        var lines = new List<string>();
        using var sink = Logger.Sink.Override((message, _) => lines.Add(message));
        using var rig = new MapTilesRig(template: "https://tiles.example.org/{z}/{x}/{y}.png?key=sentinel-key-93b1", script: (_, _) => throw new HttpRequestException("refused sentinel-key-93b1"));
        Assert.Null(rig.Tiles.Tile(3, 1, 1));
        rig.Settle();
        var line = Assert.Single(lines);
        Assert.Equal("map: a tile could not be fetched (HttpRequestException)", line);
        Assert.DoesNotContain("sentinel-key-93b1", line, StringComparison.Ordinal);
    });
}
