using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Web;
using Marrow.Services.MCP;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// W19 Native (DESIGN 13.4 "MCP Apps against the ext-apps reference servers", 14 W19 "reference servers render"; run alone by
// the lead at the native pass, never in the ordinary filters). The real reference servers of modelcontextprotocol/ext-apps,
// built in a checkout the lead names with NTH_EXT_APPS_DIR (examples/<name>-server/dist/index.js, started with --stdio as
// their README says), each driven through nth's real MCP client and nth's real MCP Apps host: the hub lists the server's app
// tools (the ui:// address read from _meta), reads each page with resources/read as text/html;profile=mcp-app, and the host
// serves it from the app's own m-*.invalid site with the bridge and the hardening before any page script and a CSP no wider
// than the page declared (its domains stay closed until the user allows them); the app's handshake is answered with the host
// context of an inline view. The page's own drawing in WebView2 inside nth's window (the card, the pane, the pop-out window,
// the live and snapshot switch) is the native window's check by hand, as ED-NEW-web-w19-4 and -5 list.
[Trait("Decision", "ED-NEW-web-w19-3")]
[Trait("Feature", "W-19")]
public sealed class McpAppsReferenceNativeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    private static IReadOnlyList<(string Server, string Entry)> ReferenceServers()
    {
        var root = Environment.GetEnvironmentVariable("NTH_EXT_APPS_DIR");
        Assert.False(string.IsNullOrEmpty(root), "Set NTH_EXT_APPS_DIR to a checkout of modelcontextprotocol/ext-apps with its examples built (npm run build).");
        var found = Directory.EnumerateDirectories(Path.Combine(root!, "examples"), "*-server")
            .Select(dir => (Server: Path.GetFileName(dir)[..^"-server".Length], Entry: Path.Combine(dir, "dist", "index.js")))
            .Where(server => File.Exists(server.Entry))
            .OrderBy(server => server.Server, StringComparer.Ordinal)
            .ToList();
        Assert.True(found.Count > 0, "No built reference server under " + root + "/examples (run npm run build there).");
        return found;
    }

    [Fact]
    public async Task Every_reference_server_lists_its_apps_and_each_page_mounts_on_its_own_site_within_its_declared_csp()
    {
        var checkedPages = 0;
        foreach (var (server, entry) in ReferenceServers())
        {
            var hub = new MCPHub();
            await hub.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
            {
                [server] = new MCPServerSpec.Stdio("node", new ValueList<string>([entry, "--stdio"]),
                    new ValueMap<string, string>(new Dictionary<string, string>(), SwiftStringComparer.Instance)),
            });
            try
            {
                var tools = await hub.AllTools().WaitAsync(Bound);
                var apps = tools.Where(tool => tool.Server == server && tool.UiResourceUri is { Length: > 0 }).ToList();
                Assert.True(apps.Count > 0, server + " lists no tool with a ui:// view.");
                foreach (var tool in apps)
                {
                    await MountsAsync(hub, server, tool);
                    checkedPages++;
                }
            }
            finally
            {
                await hub.Reload();
            }
        }

        Assert.True(checkedPages > 0);
    }

    private static async Task MountsAsync(MCPHub hub, string server, MCPHub.Tool tool)
    {
        var servers = new McpHubAppServers(hub);
        var uri = tool.UiResourceUri!;
        var page = await servers.ReadPageAsync(server, uri, CancellationToken.None).WaitAsync(Bound);
        Assert.Contains("<", page.Html, StringComparison.Ordinal);

        using var web = new WebRig();
        var user = new FakeMcpAppUser();
        var host = new McpAppsHost(servers, new McpAppGrantStore(null), user, log: _ => { });
        var view = web.OpenLive(WebSurfaceKind.McpApp, url: null);
        using var mount = host.Mount(view, new McpAppLaunch(Guid.NewGuid(), server, uri)
        {
            ToolName = tool.Name,
            Title = tool.Title,
            Tool = new JsonObject { ["name"] = tool.Name },
            ToolCallId = "call_1",
        });
        var fake = WebRig.Fake(view);
        var deadline = DateTime.UtcNow + Bound;
        while (!fake.Log.Any(line => line.StartsWith("Navigate ", StringComparison.Ordinal)) && mount.Failure is null && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(mount.Failure is null, server + " · " + tool.Name + ": " + mount.Failure);

        var origin = SandboxSites.Origin(SandboxSites.AppHost(server, uri));
        Assert.Equal(origin, mount.Origin);
        var media = page.Permissions.KeepsMedia;
        Assert.Equal(SandboxScripts.McpAppScripts(keepMedia: media), fake.DocumentScripts);
        var served = fake.Request(origin + "/", WebResourceKind.Document)!;
        Assert.Equal(200, served.StatusCode);
        Assert.Equal(page.Html, Encoding.UTF8.GetString(served.Body));

        // Nothing the user has not allowed: a page that declares outside domains asked for them, and until then it reaches none.
        var csp = CspBuilder.Parse(served.Headers.First(header => header.Key == "Content-Security-Policy").Value);
        Assert.Equal(["'none'"], csp["connect-src"]);
        var declares = page.Csp.ConnectDomains.Count + page.Csp.ResourceDomains.Count + page.Csp.FrameDomains.Count + page.Csp.BaseUriDomains.Count > 0;
        Assert.Equal(declares, user.DomainCards.Count == 1);

        // The app's handshake as the ext-apps App class sends it: the answer carries an inline view's host context.
        fake.RaiseMessage(origin, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = McpAppProtocol.Initialize,
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = McpAppProtocol.Version,
                ["appInfo"] = new JsonObject { ["name"] = server, ["version"] = "1.0.0" },
                ["appCapabilities"] = new JsonObject { ["availableDisplayModes"] = new JsonArray("inline", "fullscreen", "pip") },
            },
        }.ToJsonString());
        var reply = fake.Log.Where(line => line.StartsWith("Post ", StringComparison.Ordinal)).Select(line => JsonNode.Parse(line[5..])!.AsObject())
            .Last(message => message["id"]?.ToJsonString() == "1" && message["method"] is null);
        Assert.Equal("inline", reply["result"]!["hostContext"]!["displayMode"]!.GetValue<string>());
        Assert.Equal("desktop", reply["result"]!["hostContext"]!["platform"]!.GetValue<string>());
    }
}
