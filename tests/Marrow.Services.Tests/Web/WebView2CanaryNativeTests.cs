using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// The weekly Canary job (.github/workflows/web-canary.yml) installs Edge Canary and sets WEBVIEW2_CHANNEL_SEARCH_KIND=1 and
// WEBVIEW2_RELEASE_CHANNELS=3. This proves the loader really found Canary, so a green run is a run against Canary. Without those
// variables it does nothing (it is a Canary-job test, not a check of the stable runtime).
[Trait("Decision", "ED-NEW-web-w23-4")]
[Trait("Feature", "W-23")]
public sealed class WebView2CanaryNativeTests
{
    [Fact]
    public void With_the_Canary_channel_selected_the_loader_finds_a_Canary_build_that_meets_the_minimum()
    {
        if (Environment.GetEnvironmentVariable("WEBVIEW2_RELEASE_CHANNELS") != "3") return;

        using var engine = new WebView2Engine();
        var version = engine.RuntimeVersion();

        Assert.False(string.IsNullOrWhiteSpace(version), "no WebView2 runtime or Edge channel was found");
        Assert.Contains("canary", version, StringComparison.OrdinalIgnoreCase);
        Assert.True(engine.CompareVersions(version!, engine.MinimumRuntimeVersion) >= 0, version);
    }
}
