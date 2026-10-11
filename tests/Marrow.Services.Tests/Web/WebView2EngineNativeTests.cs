using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// Spike (a) on the owner's PC (DESIGN 13.4, run alone, never in the ordinary filters): the real WebView2 runtime. These
// start no window. The hosting checks that need nth's window (the platform view, events beside Avalonia's, chords,
// hiding, capture timings) are spike (a) and (i) by hand; the W7 landing notes list them.
[Trait("Decision", "ED-NEW-web-1")]
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
            var again = await engine.AcquireEnvironmentAsync(profiles.For(WebSurfaceKind.Artifact), CancellationToken.None);
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
}
