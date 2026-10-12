using Marrow.Core.Web;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

[Trait("Decision", "ED-NEW-web-w9-5")]
public sealed class PreviewWatcherTests
{
    [Fact]
    public void Static_watch_uses_the_shared_tree_watcher_with_64_KB_buffer()
    {
        using var folder = new ScratchFolder(); using var rig = new WebRig();
        using var reload = new PreviewReload(rig.OpenLive(WebSurfaceKind.PreviewTab), rig.Time);
        reload.Watch(folder.Root);
        Assert.Equal(65536, reload.WatchBufferSize);
    }

    [Fact]
    public async Task Every_batch_including_error_is_debounced_and_error_recreates_the_watch()
    {
        using var folder = new ScratchFolder(); using var rig = new WebRig(); var surface = rig.OpenLive(WebSurfaceKind.PreviewTab);
        using var reload = new PreviewReload(surface, rig.Time); reload.Watch(folder.Root);
        var generation = reload.WatchGeneration;
        reload.OnBatch(new FileWatcherBatch([folder.Child("fixture.txt")], false)); rig.Time.Advance(TimeSpan.FromMilliseconds(100));
        reload.OnBatch(new FileWatcherBatch([], true));
        Assert.True(reload.WatchGeneration > generation); Assert.Equal(65536, reload.WatchBufferSize);
        rig.Time.Advance(TimeSpan.FromMilliseconds(149)); await reload.WhenIdle; Assert.DoesNotContain("Reload", WebRig.Fake(surface).Log);
        rig.Time.Advance(TimeSpan.FromMilliseconds(1)); await reload.WhenIdle; Assert.Equal(1, WebRig.Fake(surface).Log.Count(s => s == "Reload"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w9cx-1")]
    public async Task Changes_only_in_hidden_files_never_reload_a_page_the_site_does_not_serve_them_in()
    {
        using var folder = new ScratchFolder(); using var rig = new WebRig(); var surface = rig.OpenLive(WebSurfaceKind.PreviewTab);
        using var reload = new PreviewReload(surface, rig.Time); reload.Watch(folder.Root);
        var fake = WebRig.Fake(surface);
        reload.OnBatch(new FileWatcherBatch([folder.Child(".git", "index"), folder.Child(".git", "refs", "heads", "main.lock"), folder.Child(".env")], false));
        rig.Time.Advance(TimeSpan.FromMilliseconds(400)); await reload.WhenIdle;
        Assert.DoesNotContain("Reload", fake.Log);
        // One served file among them is a change; a dot inside a name (app.min.js) is not a hidden file; a path outside the root counts.
        reload.OnBatch(new FileWatcherBatch([folder.Child(".git", "index"), folder.Child("assets", "app.min.js")], false));
        rig.Time.Advance(TimeSpan.FromMilliseconds(150)); await reload.WhenIdle;
        Assert.Equal(1, fake.Log.Count(s => s == "Reload"));
        reload.OnBatch(new FileWatcherBatch([Path.Combine(Path.GetTempPath(), ".outside", "x.txt")], false));
        rig.Time.Advance(TimeSpan.FromMilliseconds(150)); await reload.WhenIdle;
        Assert.Equal(2, fake.Log.Count(s => s == "Reload"));
        // An overflow means anything changed, hidden or not.
        reload.OnBatch(new FileWatcherBatch([], true));
        rig.Time.Advance(TimeSpan.FromMilliseconds(150)); await reload.WhenIdle;
        Assert.Equal(3, fake.Log.Count(s => s == "Reload"));
    }

    [Theory]
    [InlineData(WebLiveness.Suspended)]
    [InlineData(WebLiveness.Discarded)]
    public async Task Non_live_preview_is_stale_even_without_an_observed_file_batch(WebLiveness state)
    {
        using var rig = new WebRig(); var surface = rig.OpenLive(WebSurfaceKind.PreviewTab); var fake = WebRig.Fake(surface);
        using var reload = new PreviewReload(surface, rig.Time);
        surface.SetLiveness(state); surface.RaiseChanged(); await reload.WhenIdle;
        Assert.DoesNotContain("Reload", fake.Log);
        surface.SetLiveness(WebLiveness.Live); await reload.WhenIdle;
        Assert.Equal(1, fake.Log.Count(s => s == "Reload"));
    }

    [Theory]
    [InlineData(WebLiveness.Suspended)]
    [InlineData(WebLiveness.Discarded)]
    public async Task Changes_while_not_live_are_stale_and_reload_once_on_resume(WebLiveness state)
    {
        using var rig = new WebRig(); var surface = rig.OpenLive(WebSurfaceKind.PreviewTab); var fake = WebRig.Fake(surface);
        using var reload = new PreviewReload(surface, rig.Time);
        surface.SetLiveness(state);
        reload.FileChanged(); rig.Time.Advance(TimeSpan.FromMilliseconds(150)); await reload.WhenIdle;
        Assert.DoesNotContain("Reload", fake.Log);
        surface.SetLiveness(WebLiveness.Live); await reload.WhenIdle;
        Assert.Equal(1, fake.Log.Count(s => s == "Reload"));
        surface.RaiseChanged(); await reload.WhenIdle; Assert.Equal(1, fake.Log.Count(s => s == "Reload"));
    }

    [Fact]
    public async Task A_file_after_the_last_reload_requires_turn_end_reload_even_if_HMR_happened_earlier()
    {
        using var rig = new WebRig(); var surface = rig.OpenLive(WebSurfaceKind.PreviewTab); var fake = WebRig.Fake(surface);
        using var reload = new PreviewReload(surface, rig.Time);
        reload.BeginTurn(); reload.HotReloaded(); reload.FileChanged(); rig.Time.Advance(TimeSpan.FromMilliseconds(150)); reload.EndTurn(filesEdited: false); await reload.WhenIdle;
        Assert.Equal(1, fake.Log.Count(s => s == "Reload"));
        fake.RaiseNavigationCompleted(surface.Url!); await reload.WhenIdle;
        reload.BeginTurn(); reload.EndTurn(filesEdited: false); await reload.WhenIdle;
        Assert.Equal(1, fake.Log.Count(s => s == "Reload"));
    }

    [Fact]
    public async Task Turn_end_does_not_bypass_a_pending_batch_debounce()
    {
        using var rig = new WebRig(); var surface = rig.OpenLive(WebSurfaceKind.PreviewTab); var fake = WebRig.Fake(surface);
        using var reload = new PreviewReload(surface, rig.Time);
        reload.BeginTurn(); reload.OnBatch(new FileWatcherBatch(["fixture.txt"], false)); reload.EndTurn(filesEdited: false);
        await reload.WhenIdle; Assert.DoesNotContain("Reload", fake.Log);
        rig.Time.Advance(TimeSpan.FromMilliseconds(149)); await reload.WhenIdle; Assert.DoesNotContain("Reload", fake.Log);
        rig.Time.Advance(TimeSpan.FromMilliseconds(1)); await reload.WhenIdle; Assert.Equal(1, fake.Log.Count(s => s == "Reload"));
    }
}
