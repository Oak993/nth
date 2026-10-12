using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Web;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Web;

// PORT-ONLY: web surfaces W19 test helpers (kept out of the test class: R-TEST-2 allows no nested types). A scripted MCP server and
// the app layer over the fake web engine; everything static it touched is put back on Dispose.
internal sealed class McpAppsAppServers : IMcpAppServers
{
    public Task<McpAppPage> ReadPageAsync(string server, string uri, CancellationToken cancellationToken) =>
        Task.FromResult(new McpAppPage(server, uri, "<!doctype html><title>Release board</title><p>v2.4.1</p>", McpUiCsp.None));

    public Task<IReadOnlyList<McpAppTool>> ToolsAsync(string server, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<McpAppTool>>([new McpAppTool("list_releases", "Release board", true)]);

    public Task<JsonObject> CallToolAsync(string server, string tool, JsonObject arguments, CancellationToken cancellationToken) =>
        Task.FromResult(new JsonObject { ["content"] = new JsonArray(), ["isError"] = false });

    public Task<JsonObject> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken) =>
        Task.FromResult(new JsonObject { ["contents"] = new JsonArray() });
}

// The app layer over the fake engine; everything static it touched is put back after.
internal sealed class McpAppsAppRig : IDisposable
{
    internal const string Resource = "ui://shipyard/release-board.html";
    internal static readonly McpAppToolEntry Board = new("shipyard", "list_releases", Resource) { Title = "Release board" };
    internal static readonly McpAppToolEntry Ledger = new("shipyard", "show_ledger", "ui://shipyard/ledger.html") { Title = "Ledger" };

    private readonly Func<WebSurface> factory = WebSurfaces.Factory;
    private readonly Func<IWebViewSurface, Control>? live = WebSurfaces.LiveFactory;

    public McpAppsAppRig(ThreadRuntime runtime)
    {
        WebSurfaces.Reset();
        McpAppSessions.Reset();
        InlineWidgetSession.Reset();
        WebSurfaces.Factory = static () => new WebSurface(new FakeWebEngine(), new WebProfiles(@"C:\nth-test\WebView2", "en-US"), post: static action => action());
        WebSurfaces.LiveFactory = static _ => new Border();
        Grants = new McpAppGrantStore(null);
        Ui = new McpAppHostUi(thread => thread == runtime.ThreadID ? runtime : null);
        Host = new McpAppsHost(new McpAppsAppServers(), Grants, Ui, static action => action());
        McpAppSessions.HostSource = () => Host;
        McpAppCatalog.Set([Board, Ledger]);
        AppPane.Register();
    }

    public McpAppGrantStore Grants { get; }

    public McpAppHostUi Ui { get; }

    public McpAppsHost Host { get; }

    public void Dispose()
    {
        foreach (var session in McpAppSessions.All()) session.Window?.CloseQuietly();
        McpAppSessions.Reset();
        InlineWidgetSession.Reset();
        McpAppSessions.HostSource = static () => McpAppsHost.Shared;
        McpAppCatalog.Set([]);
        WebSurfaces.Reset();
        WebSurfaces.Factory = factory;
        WebSurfaces.LiveFactory = live;
    }
}
