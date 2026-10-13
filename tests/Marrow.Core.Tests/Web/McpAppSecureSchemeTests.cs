using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces security review, finding L7, gap after the first fix (DESIGN.md 7.6, 8.1). Clean dropped plain http
// and ws sources to internet hosts only when they named one host: a wildcard ("http://*.shipyard.dev", "ws://*.x.dev") kept
// its plain scheme under broad access, and "*" let nth's own check pass http and ws to any host, where the filter before the
// review passed https and wss only. Now a plain wildcard is dropped like a plain host unless it is on this computer or its
// local network, and "*" reaches https and wss only, in nth's check and in the CSP alike.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-7")]
public sealed class McpAppSecureSchemeTests
{
    [Fact]
    public void A_plain_wildcard_to_the_internet_is_dropped_even_under_broad_access()
    {
        foreach (var broad in new[] { false, true })
        {
            var cleaned = CspBuilder.Clean(["http://*.shipyard.dev", "ws://*.x.dev", "HTTP://*.Shipyard.dev/feed"], broad);
            Assert.Empty(cleaned.Kept);
            Assert.All(cleaned.Dropped, d => Assert.Equal(CspBuilder.NotSecureReason, d.Reason));
        }

        // On this computer or its local network a plain wildcard stays broad access.
        Assert.Empty(CspBuilder.Clean(["http://*.local", "ws://*.localhost"], allowBroad: false).Kept);
        Assert.Equal(["http://*.local", "ws://*.localhost"], CspBuilder.Clean(["http://*.local", "ws://*.localhost"], allowBroad: true).Kept);
    }

    [Fact]
    public void Broad_access_never_reaches_the_internet_over_a_plain_wildcard()
    {
        var declared = new McpUiCsp(["http://*.shipyard.dev", "ws://*.x.dev", "https://*.shipyard-assets.dev"], [], [], []);
        var grant = new McpAppDomainGrant(McpAppDomains.Fingerprint(declared), Broad: true);
        Assert.False(McpAppDomains.Reaches("http://a.shipyard.dev/x", declared, grant));
        Assert.False(McpAppDomains.Reaches("https://a.shipyard.dev/x", declared, grant));
        Assert.False(McpAppDomains.Reaches("ws://a.x.dev/socket", declared, grant));
        Assert.True(McpAppDomains.Reaches("https://img.shipyard-assets.dev/logo.png", declared, grant));

        var policy = CspBuilder.McpApp(declared, declared, allowBroad: true);
        Assert.DoesNotContain("http://", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("ws://", policy, StringComparison.Ordinal);

        var request = McpAppDomains.Request("shipyard", "Release board", declared)!;
        Assert.Equal(["https://*.shipyard-assets.dev"], request.Broad.Select(b => b.Source));
        Assert.Contains(request.Refused, d => d.Source == "http://*.shipyard.dev" && d.Reason == CspBuilder.NotSecureReason);
    }

    [Fact]
    public void Star_reaches_https_and_wss_only_and_the_csp_says_the_same()
    {
        var everything = new McpUiCsp(["*"], ["*"], ["*"], []);
        var all = new McpAppDomainGrant(McpAppDomains.Fingerprint(everything), Broad: true);
        Assert.True(McpAppDomains.Reaches("https://anything.example/x", everything, all));
        Assert.True(McpAppDomains.Reaches("wss://anything.example:7000/", everything, all));
        Assert.False(McpAppDomains.Reaches("http://anything.example/x", everything, all));
        Assert.False(McpAppDomains.Reaches("ws://anything.example/socket", everything, all));
        Assert.True(McpAppDomains.ReachesFrame("https://anything.example/", everything, all));
        Assert.False(McpAppDomains.ReachesFrame("http://anything.example/", everything, all));

        var policy = CspBuilder.Parse(CspBuilder.McpApp(everything, everything, allowBroad: true));
        Assert.Equal(["https:", "wss:"], policy["connect-src"]);
        Assert.Equal(["https:"], policy["frame-src"]);
        Assert.Contains("https:", policy["img-src"]);
        Assert.Contains("https:", policy["script-src"]);
        Assert.DoesNotContain(policy.Values.SelectMany(v => v), s => s is "*" or "http:" or "ws:");
    }
}
