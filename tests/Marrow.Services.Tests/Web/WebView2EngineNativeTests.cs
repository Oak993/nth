using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// Spike (a) on the owner's PC (DESIGN 13.4, run alone, never in the ordinary filters): the real WebView2 runtime. These
// start no window. The hosting checks that need nth's window (the platform view, events beside Avalonia's, chords,
// hiding, capture timings) are spike (a) and (i) by hand; the W7 landing notes list them.
[Trait("Decision", "ED-NEW-web-w7-1")]
[Trait("Feature", "W-7")]
public sealed class WebView2EngineNativeTests
{
    [Fact]
    public void The_installed_runtime_is_found_and_meets_the_minimum()
    {
        using var engine = new WebView2Engine();
        var version = engine.RuntimeVersion();
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.True(engine.CompareVersions(version!, engine.MinimumRuntimeVersion) >= 0, version);
    }

    [Fact]
    public async Task One_environment_is_created_with_the_shared_options_and_reused()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nth-w7-native-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var engine = new WebView2Engine();
            var profiles = new WebProfiles(folder, "en-US");
            var first = await engine.AcquireEnvironmentAsync(profiles.For(WebSurfaceKind.VisusTab), CancellationToken.None);
            var again = await engine.AcquireEnvironmentAsync(profiles.For(WebSurfaceKind.PreviewTab), CancellationToken.None); // ED-NEW-web-sec-res-1: sandboxes have their own
            Assert.NotEqual(0, first);
            Assert.Equal(first, again);
            engine.ResetEnvironment();
            var fresh = await engine.AcquireEnvironmentAsync(profiles.For(WebSurfaceKind.VisusTab), CancellationToken.None);
            Assert.NotEqual(0, fresh);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // the browser process may still hold the folder for a moment
            }
        }
    }

    // ED-NEW-web-sec-res-1: the real runtime accepts a second environment for the sandboxed views next to Visus's (its own folder
    // and arguments, so no "options differ" refusal), makes it only when a sandboxed view asks, and shares it between them.
    [Fact]
    [Trait("Decision", "ED-NEW-web-sec-res-1")]
    public async Task Sandboxed_views_get_an_environment_of_their_own_beside_Visus_on_the_real_runtime()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nth-sec-res-native-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var engine = new WebView2Engine();
            var profiles = new WebProfiles(folder, "en-US");
            var visus = await engine.AcquireEnvironmentAsync(profiles.For(WebSurfaceKind.VisusTab), CancellationToken.None);
            Assert.NotEqual(0, visus);
            Assert.False(Directory.Exists(Path.Combine(folder, "Sandbox")));
            var widget = engine.CreateView(profiles.For(WebSurfaceKind.InlineWidget));
            var sandbox = await engine.AcquireEnvironmentAsync(widget.Creation, CancellationToken.None);
            var app = await engine.AcquireEnvironmentAsync(profiles.For(WebSurfaceKind.McpApp), CancellationToken.None);
            Assert.NotEqual(0, sandbox);
            Assert.NotEqual(visus, sandbox);
            Assert.Equal(sandbox, app);
            Assert.True(Directory.Exists(Path.Combine(folder, "Sandbox", "EBWebView")));
            widget.Dispose();
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // the browser processes may still hold the folder for a moment
            }
        }
    }
}
