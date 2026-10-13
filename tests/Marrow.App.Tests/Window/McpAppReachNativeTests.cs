using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Web;
using Xunit;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for the web surfaces security review's finding L7 (ED-NEW-web-sec-mcpapps-4; DESIGN.md 7.6, 8.1). Write but
// NEVER run as an implementing agent: the lead takes native.lock and schedules it alone. An MCP App page in a real WebView2,
// with connect domains on a non-default port, on the default port and for sockets, allowed by the user, fetches and opens
// sockets to each declared host on its own port, on another port and on the other scheme. For every address the engine's
// CSP refuses it exactly when nth's own check (McpAppDomains.Reaches) refuses it, and nth's request filter refuses none of
// the addresses its check passes. The hosts are under .example, which never resolves, so nothing leaves the machine.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-sec-mcpapps-4")]
public sealed class McpAppReachNativeTests(WindowManagerNativeSession native)
{
    private static readonly McpUiCsp Declared = new(["https://api.shipyard.example:8443", "https://plain.shipyard.example", "wss://live.shipyard.example"], [], [], []);

    private static readonly (string Kind, string Url)[] Probes =
    [
        ("fetch", "https://api.shipyard.example:8443/x"),
        ("fetch", "https://api.shipyard.example/x"),
        ("fetch", "https://api.shipyard.example:9443/x"),
        ("fetch", "https://plain.shipyard.example/x"),
        ("fetch", "https://plain.shipyard.example:9443/x"),
        ("socket", "wss://plain.shipyard.example/s"),
        ("socket", "wss://live.shipyard.example/s"),
        ("socket", "wss://live.shipyard.example:8443/s"),
        ("fetch", "https://live.shipyard.example/x"),
    ];

    private static string Page() =>
        """
        <!doctype html><html><head><meta charset="utf-8"><title>reach</title></head><body><script>
        (() => {
          const r = { refused: [], done: 0 };
          addEventListener('securitypolicyviolation', (e) => r.refused.push(String(e.blockedURI)));
          const done = () => { r.done++; };
          const fetchIt = (url) => { try { fetch(url, { mode: 'no-cors' }).then(done, done); } catch (e) { r.refused.push(url); done(); } };
          const socket = (url) => { try { const s = new WebSocket(url); s.onopen = done; s.onerror = done; } catch (e) { r.refused.push(url); done(); } };
        """
        + string.Concat(Probes.Select(p => "  " + (p.Kind == "fetch" ? "fetchIt" : "socket") + "('" + p.Url + "');\n"))
        + """
          setTimeout(() => { document.title = 'nth-reach:' + JSON.stringify(r); }, 5000);
        })();
        </script></body></html>
        """;

    private static string Authority(string url) => new Uri(url).GetLeftPart(UriPartial.Authority);

    [Fact]
    public void The_engines_csp_and_nths_own_check_refuse_the_same_addresses_ports_and_sockets_included() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new McpAppReachNativeRig(native, Declared, Page()));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Granted), "The domain card's Allow never became the page's grant.", 10000);
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Report()) is not null, "The page never reported.", 30000);
            var report = JsonNode.Parse(native.OnUi(() => rig.Report())!)!.AsObject();
            var refused = report["refused"]!.AsArray().Select(n => n!.GetValue<string>())
                .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var parsed) ? parsed.GetLeftPart(UriPartial.Authority) : u)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var grant = new McpAppDomainGrant(McpAppDomains.Fingerprint(Declared), Broad: false);
            List<string> logs;
            lock (rig.Logs) logs = [.. rig.Logs];

            foreach (var (_, url) in Probes)
            {
                var passes = McpAppDomains.Reaches(url, Declared, grant);
                var cspRefused = refused.Contains(Authority(url));
                Assert.True(cspRefused != passes, $"{url}: the CSP {(cspRefused ? "refused" : "let it through")}, nth's check {(passes ? "passes" : "refuses")} it");
                if (passes) Assert.DoesNotContain(logs, line => line.Contains("blocked", StringComparison.Ordinal) && line.Contains(Authority(url) + "/", StringComparison.Ordinal));
            }
        }
        finally
        {
            native.OnUi(rig.Dispose);
        }
    });
}
