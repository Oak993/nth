using Marrow.App.Tests.Fixtures;
using Marrow.App.Port.Web;
using Marrow.Core.Web;
using Marrow.Testing;
using Marrow.UI.NthUi.Map;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W10, "Map street tiles source (none)" in the app (E:/Droppy/work/design/web-surfaces/DESIGN.md 7.2, 9; ED-NEW-web-w10-5). The address in the settings becomes the maps' tile source when the app attaches its settings and follows every change; none is the
// offline basemap alone; the source that is replaced or taken away is let go and its tiles are deleted from disk.
[Collection("AppSettings")]
[Trait("Decision", "ED-NEW-web-w10-5")]
public sealed class WebW10MapTilesBindingTests
{
    private const string First = "https://tiles.example.org/{z}/{x}/{y}.png";
    private const string Second = "https://other.example.org/{z}/{x}/{y}.png";

    private static WebSettings Fresh() => new(new InMemorySettingsStore());

    private static void OnMain(Action body)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(body);
    }

    [Fact]
    public void Attaching_with_an_address_binds_a_source_for_it_and_none_means_the_offline_map_alone() => OnMain(() =>
    {
        var made = new List<MapTileTemplate>();
        var before = WebSettingsBinding.MakeTiles;
        WebSettingsBinding.MakeTiles = template => { made.Add(template); return new HttpMapTiles(template, action => action(), new ScriptedHttpHandler()); };
        try
        {
            var none = Fresh();
            using (WebSettingsBinding.Attach(none))
            {
                Assert.False(NthUiMapTiles.IsBound);
                Assert.Empty(made);
            }

            var with = Fresh();
            with.MapTileSource = First;
            using (WebSettingsBinding.Attach(with))
            {
                Assert.True(NthUiMapTiles.IsBound);
                Assert.Equal(First, Assert.Single(made).Text);
                Assert.Equal("Map tiles from tiles.example.org", NthUiMapTiles.Source.Current.Attribution);
            }
            Assert.False(NthUiMapTiles.IsBound); // detaching puts the offline map back
        }
        finally
        {
            WebSettingsBinding.MakeTiles = before;
        }
    });

    [Fact]
    public void A_change_of_the_address_replaces_the_source_and_taking_it_away_removes_it_and_the_maps_are_told_each_time() => OnMain(() =>
    {
        var made = new List<MapTileTemplate>();
        var before = WebSettingsBinding.MakeTiles;
        WebSettingsBinding.MakeTiles = template => { made.Add(template); return new HttpMapTiles(template, action => action(), new ScriptedHttpHandler()); };
        var told = 0;
        void Tell() => told++;
        NthUiMapTiles.Changed += Tell;
        try
        {
            var web = Fresh();
            using var attach = WebSettingsBinding.Attach(web);
            Assert.False(NthUiMapTiles.IsBound);
            var start = told;

            web.MapTileSource = First;
            Assert.True(NthUiMapTiles.IsBound);
            Assert.Equal([First], made.Select(template => template.Text));
            Assert.True(told > start);

            var second = told;
            web.MapTileSource = Second;
            Assert.Equal([First, Second], made.Select(template => template.Text));
            Assert.Equal("Map tiles from other.example.org", NthUiMapTiles.Source.Current.Attribution);
            Assert.True(told > second);

            web.OpenPreviews = false; // an unrelated setting changes nothing
            Assert.Equal(2, made.Count);

            var third = told;
            web.MapTileSource = "";
            Assert.False(NthUiMapTiles.IsBound);
            Assert.True(told > third);
        }
        finally
        {
            NthUiMapTiles.Changed -= Tell;
            WebSettingsBinding.MakeTiles = before;
        }
    });

    private static string KeyFolder(string cache, string address) =>
        Path.Combine(cache, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(address)), 0, 8).ToLowerInvariant());

    [Fact]
    public void The_source_taken_away_or_replaced_has_its_tiles_deleted_from_disk() => OnMain(() =>
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
        var cache = Path.Combine(data, "MapTilesBinding", Guid.NewGuid().ToString("N"));
        var before = WebSettingsBinding.MakeTiles;
        WebSettingsBinding.MakeTiles = template => new HttpMapTiles(template, action => action(), new ScriptedHttpHandler(), cache);
        try
        {
            var web = Fresh();
            web.MapTileSource = First;
            using var attach = WebSettingsBinding.Attach(web);
            var firstFolder = Directory.CreateDirectory(KeyFolder(cache, First));
            File.WriteAllBytes(Path.Combine(firstFolder.FullName, "kept.tile"), [1, 2, 3]);

            web.MapTileSource = Second; // replaced: the first source's tiles go
            Assert.False(Directory.Exists(firstFolder.FullName), "the tiles of a replaced source stay on disk");
            var secondFolder = Directory.CreateDirectory(KeyFolder(cache, Second));
            File.WriteAllBytes(Path.Combine(secondFolder.FullName, "kept.tile"), [4, 5, 6]);

            web.MapTileSource = ""; // taken away: the second source's tiles go
            Assert.False(Directory.Exists(secondFolder.FullName), "the tiles of a source taken away stay on disk");
        }
        finally
        {
            WebSettingsBinding.MakeTiles = before;
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    });

    [Fact]
    public void The_tiles_are_kept_inside_the_web_data_folder_so_the_uninstall_removes_them() => OnMain(() =>
    {
        var folder = WebSettingsBinding.TileCacheFolder();
        Assert.NotNull(folder);
        Assert.EndsWith(Path.Combine("nth", "map-tiles"), folder, StringComparison.Ordinal);
        Assert.StartsWith(Marrow.Services.Web.WebProfiles.DefaultUserDataFolder, folder, StringComparison.OrdinalIgnoreCase);
    });
}
