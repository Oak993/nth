using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces security review, finding L7 (DESIGN.md 7.6 "Declared domains need the user's OK", "Wildcard
// entries ... are dropped unless the user turns on Allow broad access in the card's details, which names the risk"). The
// broad-access card never named the wildcard, local or 'unsafe-eval' entries it grants; and ws: and non-default ports were
// handled one way by the CSP and another by nth's own check (a declared port was refused, any port of a declared host
// passed, an https grant let wss through, plain http and ws to the internet were in the CSP but refused). Now the card names
// every broad entry, plain http and ws reach only the local hosts broad access covers, and nth's check matches an address
// the way the CSP's host-source does.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-4")]
public sealed class McpAppReachTests
{
    private static readonly McpUiCsp Declared = new(
        ["https://api.shipyard.dev:8443", "https://plain.shipyard.dev", "wss://live.shipyard.dev", "http://insecure.shipyard.dev", "ws://feed.shipyard.dev"],
        ["cdn.shipyard.dev", "*.shipyard-assets.dev", "http://localhost:5173", "'unsafe-eval'"],
        ["https://embed.shipyard.dev:8443"],
        []);

    private static McpAppDomainGrant Grant(bool broad) => new(McpAppDomains.Fingerprint(Declared), broad);

    [Fact]
    public void The_broad_access_card_names_every_entry_it_would_let_through()
    {
        var request = McpAppDomains.Request("shipyard", "Release board", Declared)!;
        Assert.Equal("Allow broad access also allows: *.shipyard-assets.dev, http://localhost:5173, 'unsafe-eval'", request.BroadText);
        Assert.DoesNotContain("localhost", request.Sentence, StringComparison.Ordinal); // the plain sentence stays exact domains

        Assert.Null(McpAppDomains.Request("shipyard", "Release board", new McpUiCsp(["https://api.shipyard.dev"], [], [], []))!.BroadText);

        var many = new McpUiCsp([.. Enumerable.Range(0, 30).Select(i => "*.s" + i + ".shipyard.dev")], [], [], []);
        var named = McpAppDomains.Request("shipyard", "Release board", many)!.BroadText!;
        Assert.StartsWith("Allow broad access also allows: *.s0.shipyard.dev, ", named, StringComparison.Ordinal);
        Assert.Contains("*.s23.shipyard.dev", named, StringComparison.Ordinal);
        Assert.DoesNotContain("*.s24.shipyard.dev", named, StringComparison.Ordinal);
        Assert.EndsWith(", and 6 more", named, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_http_and_ws_reach_only_a_local_host_under_broad_access()
    {
        foreach (var broad in new[] { false, true })
        {
            var cleaned = CspBuilder.Clean(["http://insecure.shipyard.dev", "ws://feed.shipyard.dev", "https://api.shipyard.dev", "wss://live.shipyard.dev"], broad);
            Assert.Equal(["https://api.shipyard.dev", "wss://live.shipyard.dev"], cleaned.Kept);
            Assert.Equal(["http://insecure.shipyard.dev", "ws://feed.shipyard.dev"], cleaned.Dropped.Where(d => d.Reason == CspBuilder.NotSecureReason).Select(d => d.Source));
        }

        Assert.Empty(CspBuilder.Clean(["http://localhost:5173", "ws://127.0.0.1:9000"], allowBroad: false).Kept);
        Assert.Equal(["http://localhost:5173", "ws://127.0.0.1:9000"], CspBuilder.Clean(["http://localhost:5173", "ws://127.0.0.1:9000"], allowBroad: true).Kept);

        var policy = CspBuilder.Parse(CspBuilder.McpApp(Declared, Declared, allowBroad: true));
        Assert.DoesNotContain(policy.Values.SelectMany(v => v), s => s.Contains("insecure", StringComparison.Ordinal) || s.Contains("feed.", StringComparison.Ordinal));
        Assert.DoesNotContain("insecure", McpAppDomains.Request("shipyard", "Release board", Declared)!.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void Nths_check_matches_scheme_and_port_as_the_csp_does()
    {
        var grant = Grant(broad: false);
        Assert.True(McpAppDomains.Reaches("https://api.shipyard.dev:8443/releases", Declared, grant));
        Assert.False(McpAppDomains.Reaches("https://api.shipyard.dev/releases", Declared, grant)); // the declared port only
        Assert.False(McpAppDomains.Reaches("https://API.shipyard.dev:9443/releases", Declared, grant));
        Assert.True(McpAppDomains.Reaches("https://plain.shipyard.dev/x", Declared, grant));
        Assert.True(McpAppDomains.Reaches("https://plain.shipyard.dev:443/x", Declared, grant));
        Assert.False(McpAppDomains.Reaches("https://plain.shipyard.dev:8443/x", Declared, grant)); // no port: the default one only
        Assert.False(McpAppDomains.Reaches("wss://plain.shipyard.dev/socket", Declared, grant)); // an https grant is not a socket grant
        Assert.False(McpAppDomains.Reaches("http://plain.shipyard.dev/x", Declared, grant)); // never down to plain http
        Assert.True(McpAppDomains.Reaches("wss://live.shipyard.dev/socket", Declared, grant));
        Assert.False(McpAppDomains.Reaches("ws://live.shipyard.dev/socket", Declared, grant));
        Assert.False(McpAppDomains.Reaches("https://live.shipyard.dev/", Declared, grant));
        Assert.True(McpAppDomains.Reaches("https://cdn.shipyard.dev/app.js", Declared, grant));
        Assert.False(McpAppDomains.Reaches("http://insecure.shipyard.dev/", Declared, grant));
        Assert.False(McpAppDomains.Reaches("ws://feed.shipyard.dev/", Declared, grant));
        Assert.False(McpAppDomains.Reaches("https://img.shipyard-assets.dev/logo.png", Declared, grant)); // broad only
        Assert.False(McpAppDomains.Reaches("http://localhost:5173/src/main.ts", Declared, grant));
        Assert.False(McpAppDomains.Reaches("https://api.shipyard.dev:8443/x", Declared, null));
        Assert.False(McpAppDomains.Reaches("https://api.shipyard.dev:8443/x", Declared, grant with { Fingerprint = "changed" }));
        Assert.False(McpAppDomains.Reaches("ftp://api.shipyard.dev:8443/x", Declared, grant));
        Assert.False(McpAppDomains.Reaches("not an address", Declared, grant));
    }

    [Fact]
    public void Broad_access_reaches_its_wildcards_and_local_hosts_on_their_own_ports_only()
    {
        var grant = Grant(broad: true);
        Assert.True(McpAppDomains.Reaches("https://img.shipyard-assets.dev/logo.png", Declared, grant));
        Assert.True(McpAppDomains.Reaches("https://a.b.shipyard-assets.dev/logo.png", Declared, grant));
        Assert.False(McpAppDomains.Reaches("https://shipyard-assets.dev/logo.png", Declared, grant));
        Assert.False(McpAppDomains.Reaches("https://img.shipyard-assets.dev:8443/logo.png", Declared, grant));
        Assert.False(McpAppDomains.Reaches("https://shipyard-assets.dev.evil.example/", Declared, grant));
        Assert.True(McpAppDomains.Reaches("http://localhost:5173/src/main.ts", Declared, grant));
        Assert.True(McpAppDomains.Reaches("https://localhost:5173/src/main.ts", Declared, grant)); // http also takes https, as the CSP does
        Assert.False(McpAppDomains.Reaches("http://localhost:3000/", Declared, grant));
        Assert.False(McpAppDomains.Reaches("http://insecure.shipyard.dev/", Declared, grant));

        var everything = new McpUiCsp(["*"], [], [], []);
        var all = new McpAppDomainGrant(McpAppDomains.Fingerprint(everything), true);
        Assert.True(McpAppDomains.Reaches("wss://anything.example:7000/", everything, all));
        Assert.False(McpAppDomains.Reaches("https://anything.example/", everything, all with { Broad = false }));
    }

    [Fact]
    public void A_frame_reaches_its_declared_frame_domain_on_its_declared_port_only()
    {
        var grant = Grant(broad: false);
        Assert.True(McpAppDomains.ReachesFrame("https://embed.shipyard.dev:8443/chart", Declared, grant));
        Assert.False(McpAppDomains.ReachesFrame("https://embed.shipyard.dev/chart", Declared, grant));
        Assert.False(McpAppDomains.ReachesFrame("https://api.shipyard.dev:8443/chart", Declared, grant)); // a connect domain is not a frame domain
        Assert.False(McpAppDomains.ReachesFrame("wss://embed.shipyard.dev:8443/chart", Declared, grant));
        Assert.False(McpAppDomains.ReachesFrame("https://embed.shipyard.dev:8443/chart", Declared, null));
    }
}
