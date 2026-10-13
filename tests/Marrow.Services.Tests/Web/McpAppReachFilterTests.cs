using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review, finding L7 (DESIGN.md 7.6, 8.1). nth's own request filter for an MCP App and the
// CSP it serves disagreed on ws: and ports: a declared port was refused, any port of a declared host passed, an https grant
// let wss through, and plain http and ws to the internet sat in the CSP while nth refused them. Over the fake engine, after
// the user's Allow, the filter now passes exactly the addresses the CSP's sources match, and the CSP holds no plain source
// to the internet; a frame reaches its declared frame domain on its declared port only.
[Trait("Decision", "ED-NEW-web-sec-mcpapps-4")]
public sealed class McpAppReachFilterTests
{
    private static readonly McpUiCsp Declared = new(
        ["https://api.shipyard.dev:8443", "https://plain.shipyard.dev", "wss://live.shipyard.dev", "http://insecure.shipyard.dev", "ws://feed.shipyard.dev"],
        ["cdn.shipyard.dev"],
        ["https://embed.shipyard.dev:8443"],
        []);

    private static string Header(WebResourceResponse response, string name) => response.Headers.First(h => h.Key == name).Value;

    [Fact]
    public void The_filter_passes_exactly_what_the_csp_allows_scheme_and_port_included()
    {
        using var rig = new McpAppsRig(csp: Declared);
        rig.Ui.DomainAnswer = McpAppDomainDecision.Allow;
        var (_, view) = rig.Open();

        var csp = CspBuilder.Parse(Header(view.Request(rig.Origin + "/", WebResourceKind.Document)!, "Content-Security-Policy"));
        Assert.Equal(["https://api.shipyard.dev:8443", "https://plain.shipyard.dev", "wss://live.shipyard.dev"], csp["connect-src"]);
        Assert.Equal(["https://embed.shipyard.dev:8443"], csp["frame-src"]);
        Assert.DoesNotContain(csp.Values.SelectMany(v => v), s => s.StartsWith("http:", StringComparison.Ordinal) || s.StartsWith("ws:", StringComparison.Ordinal));
        Assert.DoesNotContain("insecure", rig.Ui.DomainCards.Single().Sentence, StringComparison.Ordinal);

        Assert.Null(view.Request("https://api.shipyard.dev:8443/releases", WebResourceKind.Fetch));
        Assert.Null(view.Request("https://plain.shipyard.dev/releases", WebResourceKind.Fetch));
        Assert.Null(view.Request("wss://live.shipyard.dev/socket", WebResourceKind.WebSocket));
        Assert.Null(view.Request("https://cdn.shipyard.dev/app.js", WebResourceKind.Script));
        foreach (var (url, kind) in new[]
        {
            ("https://api.shipyard.dev/releases", WebResourceKind.Fetch), // not the declared port
            ("https://api.shipyard.dev:9443/releases", WebResourceKind.Fetch),
            ("https://plain.shipyard.dev:9443/releases", WebResourceKind.Fetch), // a declared host on another port
            ("wss://plain.shipyard.dev/socket", WebResourceKind.WebSocket), // an https grant is not a socket grant
            ("ws://live.shipyard.dev/socket", WebResourceKind.WebSocket), // never down to plain ws
            ("http://plain.shipyard.dev/releases", WebResourceKind.Fetch),
            ("http://insecure.shipyard.dev/", WebResourceKind.Fetch), // plain http to the internet: dropped
            ("ws://feed.shipyard.dev/", WebResourceKind.WebSocket),
        })
        {
            Assert.True(view.Request(url, kind) is { StatusCode: 403 }, kind + " " + url);
        }

        Assert.False(view.RaiseNavigationStarting("https://embed.shipyard.dev:8443/chart", userInitiated: false, mainFrame: false).Cancel);
        Assert.True(view.RaiseNavigationStarting("https://embed.shipyard.dev/chart", userInitiated: false, mainFrame: false).Cancel);
        Assert.True(view.RaiseNavigationStarting("https://api.shipyard.dev:8443/chart", userInitiated: false, mainFrame: false).Cancel);
    }
}
