using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-w9-2")]
public sealed class PreviewDiscoveryTests
{
    [Fact]
    public async Task Only_listening_ports_of_the_script_tree_are_probed_and_explicit_url_wins()
    {
        var probed = new List<int>();
        var discovery = new PreviewDiscovery
        {
            ProcessTree = _ => [10, 11],
            Listeners = () => [new(11, 5173), new(99, 3000), new(10, 9000)],
            Head = (url, _) => { probed.Add(new Uri(url).Port); return Task.FromResult(true); }
        };
        Assert.Equal("http://localhost:5173/", (await discovery.FindAsync(10)).First());
        Assert.Equal([5173, 9000], probed);
        probed.Clear();
        Assert.Equal(["https://dev.test/"], await discovery.FindAsync(10, "https://dev.test/"));
        Assert.Empty(probed);
        discovery.ProcessTree = _ => [];
        Assert.Empty(await discovery.FindAsync(10)); Assert.Empty(probed);
    }

    [Fact]
    public void Static_files_are_root_confined_read_only_and_requests_never_fall_through_to_the_network()
    {
        var root = Path.Combine(Path.GetTempPath(), "w9-static-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<p>fixture</p>");
            var site = new StaticPreviewSite("fixture", root);
            Assert.Equal(200, site.Answer(new(site.Origin + "/", "GET", WebResourceKind.Document))!.StatusCode);
            Assert.Equal(403, site.Answer(new(site.Origin + "/%2e%2e/secret", "GET", WebResourceKind.Fetch))!.StatusCode);
            Assert.Equal(405, site.Answer(new(site.Origin + "/index.html", "POST", WebResourceKind.Document))!.StatusCode);
            Assert.Equal(403, site.Answer(new("https://example.test/", "GET", WebResourceKind.Fetch))!.StatusCode);
            Assert.Equal(404, site.Answer(new(site.Origin + "/missing", "GET", WebResourceKind.Document))!.StatusCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Static_reload_is_debounced_preserves_scroll_and_a_hot_reload_suppresses_turn_end_reload()
    {
        using var rig = new WebRig();
        var surface = rig.OpenLive(WebSurfaceKind.PreviewTab);
        var view = WebRig.Fake(surface);
        view.CdpHandler = (method, _, _) => Task.FromResult(method == "Runtime.evaluate" ? "{\"result\":{\"value\":123}}" : "{}");
        using var reload = new PreviewReload(surface, rig.Time);
        reload.FileChanged(); rig.Time.Advance(TimeSpan.FromMilliseconds(100)); reload.FileChanged();
        rig.Time.Advance(TimeSpan.FromMilliseconds(149)); Assert.DoesNotContain("Reload False", view.Log);
        rig.Time.Advance(TimeSpan.FromMilliseconds(1)); await reload.WhenIdle;
        Assert.Equal(1, view.Log.Count(x => x == "Reload False"));
        view.RaiseNavigationCompleted(surface.Url!); await reload.WhenIdle;
        Assert.Contains(view.CdpCalls, x => x.Method == "Runtime.evaluate" && x.Params.Contains("scrollTo(0,123)", StringComparison.Ordinal));
        reload.BeginTurn(); reload.HotReloaded(); reload.EndTurn(filesEdited: true); await reload.WhenIdle;
        Assert.Equal(1, view.Log.Count(x => x == "Reload False"));
    }
}
