using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Web;
using Xunit;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for the web surfaces security review's finding L7, gap after the first fix (ED-NEW-web-sec-mcpapps-7;
// DESIGN.md 7.6, 8.1). Write but NEVER run as an implementing agent: the lead takes native.lock and schedules it alone. An MCP
// App page in a real WebView2 declares "*" as its connect domain and the user allows broad access, so its CSP holds
// "connect-src https: wss:" in place of "*". The page fetches https addresses on any host and port and opens wss sockets:
// the engine's CSP refuses none of them, exactly as nth's own check (McpAppDomains.Reaches) passes them, and nth's request
// filter blocks none of them. Plain http and ws are refused by nth's check (unit-tested in McpAppSecureSchemeTests); from the
// page's https origin the engine also blocks them as mixed content, which raises no CSP event, so they are not probed here.
// The hosts are under .example, which never resolves, so nothing leaves the machine.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-sec-mcpapps-7")]
public sealed class McpAppStarNativeTests(WindowManagerNativeSession native)
{
    private static readonly McpUiCsp Declared = new(["*"], [], [], []);

    private static readonly (string Kind, string Url)[] Probes =
    [
        ("fetch", "https://api.shipyard.example/x"),
        ("fetch", "https://other.example:9443/x"),
        ("socket", "wss://live.shipyard.example/s"),
        ("socket", "wss://other.example:7000/s"),
    ];

    private static string Page() =>
        """
        <!doctype html><html><head><meta charset="utf-8"><title>star</title></head><body><script>
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
    public void Broad_star_reaches_any_host_over_https_and_wss_in_the_engine_as_in_nths_check() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new McpAppReachNativeRig(native, Declared, Page(), broad: true));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Granted), "The domain card's Allow broad access never became the page's grant.", 10000);
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Report()) is not null, "The page never reported.", 30000);
            var report = JsonNode.Parse(native.OnUi(() => rig.Report())!)!.AsObject();
            var refused = report["refused"]!.AsArray().Select(n => n!.GetValue<string>())
                .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var parsed) ? parsed.GetLeftPart(UriPartial.Authority) : u)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var grant = new McpAppDomainGrant(McpAppDomains.Fingerprint(Declared), Broad: true);
            List<string> logs;
            lock (rig.Logs) logs = [.. rig.Logs];

            foreach (var (_, url) in Probes)
            {
                Assert.True(McpAppDomains.Reaches(url, Declared, grant), url + ": nth's check refuses it");
                Assert.False(refused.Contains(Authority(url)), url + ": the engine's CSP refused it");
                Assert.DoesNotContain(logs, line => line.Contains("blocked", StringComparison.Ordinal) && line.Contains(Authority(url) + "/", StringComparison.Ordinal));
            }
        }
        finally
        {
            native.OnUi(rig.Dispose);
        }
    });
}
