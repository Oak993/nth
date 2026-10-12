using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 isolation headers). Every sandboxed page carries the CSP and the isolation
// headers; the library host is the one cross-origin resource, readable by every artifact's site and runnable by none.
[Trait("Decision", "ED-NEW-web-w13-1")]
public sealed class SandboxHeadersTests
{
    private static string? Header(IReadOnlyList<KeyValuePair<string, string>> headers, string name) =>
        headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    [Fact]
    public void A_sandboxed_page_carries_the_policy_and_every_isolation_header()
    {
        var headers = SandboxHeaders.Document("text/html; charset=utf-8", CspBuilder.ArtifactDefault);
        Assert.Equal(CspBuilder.ArtifactDefault, Header(headers, "Content-Security-Policy"));
        Assert.Equal("?1", Header(headers, "Origin-Agent-Cluster"));
        Assert.Equal("same-origin", Header(headers, "Cross-Origin-Opener-Policy"));
        Assert.Equal("same-origin", Header(headers, "Cross-Origin-Resource-Policy"));
        Assert.Equal("nosniff", Header(headers, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(headers, "Referrer-Policy"));
        Assert.Equal("off", Header(headers, "X-DNS-Prefetch-Control"));
        Assert.Equal("no-store", Header(headers, "Cache-Control"));
    }

    [Fact]
    public void Permissions_policy_denies_every_device_and_the_features_the_design_names()
    {
        var policy = SandboxHeaders.PermissionsPolicy.Split(", ").ToDictionary(p => p.Split('=')[0], p => p.Split('=')[1]);
        foreach (var feature in new[] { "camera", "microphone", "geolocation", "usb", "serial", "hid", "bluetooth", "midi", "fullscreen", "pointer-lock", "payment", "publickey-credentials-get", "publickey-credentials-create", "clipboard-write", "clipboard-read", "display-capture" })
            Assert.Equal("()", policy[feature]);
        Assert.All(policy.Values, value => Assert.Equal("()", value));
    }

    [Fact]
    public void The_library_host_is_cross_origin_readable_and_runs_nothing_as_a_page()
    {
        var headers = SandboxHeaders.Library("text/javascript; charset=utf-8");
        Assert.Equal("cross-origin", Header(headers, "Cross-Origin-Resource-Policy"));
        Assert.Equal("*", Header(headers, "Access-Control-Allow-Origin"));
        Assert.Equal(CspBuilder.Library, Header(headers, "Content-Security-Policy"));
        Assert.Equal("nosniff", Header(headers, "X-Content-Type-Options"));
        Assert.Null(Header(SandboxHeaders.Blocked(), "Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Content_types_follow_the_extension()
    {
        Assert.Equal("text/html; charset=utf-8", SandboxHeaders.ContentType("a/index.html"));
        Assert.Equal("text/javascript; charset=utf-8", SandboxHeaders.ContentType("main.tsx"));
        Assert.Equal("text/css; charset=utf-8", SandboxHeaders.ContentType("style.css"));
        Assert.Equal("image/svg+xml", SandboxHeaders.ContentType("logo.svg"));
        Assert.Equal("font/woff2", SandboxHeaders.ContentType("katex/fonts/KaTeX_Main-Regular.woff2"));
    }
}
