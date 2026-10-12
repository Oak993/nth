using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

/// <summary>
/// W19 test rig: the web rig over the fake engine, an MCP Apps host over a recorded server (shipyard's Release board, shaped
/// like the ext-apps reference servers' pages and answers) and a scripted user. The app's side of the protocol is played by
/// the test with <see cref="Send"/>; nth's replies are read from the fake view's log.
/// </summary>
internal sealed class McpAppsRig : IDisposable
{
    public static readonly Guid Chat = Guid.Parse("8a1f0c2e-6b7d-4e3f-9a8b-1c2d3e4f5a6b");
    public const string Resource = "ui://shipyard/release-board.html";
    public const string Html = "<!doctype html><html><head><title>Release board</title></head><body><div id=app></div></body></html>";

    public static readonly McpUiCsp Declared = new(["https://api.shipyard.dev"], ["cdn.shipyard.dev", "*.shipyard-assets.dev"], [], []);

    public McpAppsRig(McpUiCsp? csp = null, McpAppPermissions? permissions = null, McpAppGrantStore? grants = null)
    {
        Servers.Page = new McpAppPage("shipyard", Resource, Html, csp ?? Declared) { Permissions = permissions ?? McpAppPermissions.None };
        Grants = grants ?? new McpAppGrantStore(null);
        Host = new McpAppsHost(Servers, Grants, Ui, log: Logs.Add);
    }

    public WebRig Web { get; } = new();

    public FakeMcpAppServers Servers { get; } = new();

    public FakeMcpAppUser Ui { get; } = new();

    public McpAppGrantStore Grants { get; }

    public McpAppsHost Host { get; }

    public List<string> Logs { get; } = [];

    public McpAppMount? Mount { get; private set; }

    public FakeWebView? View { get; private set; }

    public string Origin => SandboxSites.Origin(SandboxSites.AppHost("shipyard", Resource));

    public (McpAppMount Mount, FakeWebView View) Open()
    {
        var view = Web.OpenLive(WebSurfaceKind.McpApp, url: null);
        Mount = Host.Mount(view, new McpAppLaunch(Chat, "shipyard", Resource)
        {
            ToolName = "list_releases",
            Title = "Release board",
            Tool = new JsonObject { ["name"] = "list_releases", ["title"] = "Release board" },
            ToolCallId = "call_1",
        });
        View = WebRig.Fake(view);
        return (Mount, View);
    }

    /// <summary>The app posts one JSON-RPC message from its own origin (or another).</summary>
    public void Send(JsonObject message, string? origin = null)
    {
        message["jsonrpc"] = "2.0";
        View!.RaiseMessage(origin ?? Origin, message.ToJsonString());
    }

    /// <summary>The app's handshake: ui/initialize, then initialized. Returns nth's initialize result.</summary>
    public JsonObject Handshake(params string[] modes)
    {
        var parameters = new JsonObject
        {
            ["protocolVersion"] = McpAppProtocol.Version,
            ["clientInfo"] = new JsonObject { ["name"] = "release-board", ["version"] = "1.0.0" },
            ["appCapabilities"] = new JsonObject(),
        };
        if (modes.Length > 0) parameters["appCapabilities"]!["availableDisplayModes"] = new JsonArray([.. modes.Select(m => (JsonNode)JsonValue.Create(m)!)]);
        Send(new JsonObject { ["id"] = "init", ["method"] = McpAppProtocol.Initialize, ["params"] = parameters });
        var result = Reply("init")["result"]!.AsObject();
        Send(new JsonObject { ["method"] = McpAppProtocol.Initialized });
        return result;
    }

    /// <summary>Every message nth posted to the page, in order.</summary>
    public List<JsonObject> Posted() =>
        [.. View!.Log.Where(l => l.StartsWith("Post ", StringComparison.Ordinal)).Select(l => JsonNode.Parse(l[5..])!.AsObject())];

    /// <summary>nth's reply to the request with this id.</summary>
    public JsonObject Reply(string id) => Posted().Last(m => m["id"]?.GetValue<string>() == id && m["method"] is null);

    public bool Replied(string id) => Posted().Any(m => m["id"]?.GetValue<string>() == id && m["method"] is null);

    public List<string> Navigations() => [.. View!.Log.Where(l => l.StartsWith("Navigate ", StringComparison.Ordinal))];

    public JsonObject Call(string id, string method, JsonObject parameters)
    {
        Send(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters });
        return Reply(id);
    }

    public void Dispose()
    {
        Mount?.Dispose();
        Web.Dispose();
    }
}

internal sealed class FakeMcpAppServers : IMcpAppServers
{
    public McpAppPage? Page { get; set; }

    public Exception? PageError { get; set; }

    public List<McpAppTool> Tools { get; } =
    [
        new("list_releases", "Release board", true),
        new("promote_release", "Promote", true),
        new("delete_everything", null, false), // the model's only
    ];

    public List<(string Server, string Tool, JsonObject Arguments)> Calls { get; } = [];

    public List<(string Server, string Uri)> Reads { get; } = [];

    public Task<McpAppPage> ReadPageAsync(string server, string uri, CancellationToken cancellationToken) =>
        PageError is { } error ? Task.FromException<McpAppPage>(error) : Task.FromResult(Page!);

    public Task<IReadOnlyList<McpAppTool>> ToolsAsync(string server, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<McpAppTool>>(server == "shipyard" ? Tools : []);

    public Task<JsonObject> CallToolAsync(string server, string tool, JsonObject arguments, CancellationToken cancellationToken)
    {
        Calls.Add((server, tool, arguments));
        return Task.FromResult(new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Promoted v2.4.1" }),
            ["structuredContent"] = new JsonObject { ["version"] = "v2.4.1", ["stage"] = "production" },
            ["isError"] = false,
        });
    }

    public Task<JsonObject> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken)
    {
        Reads.Add((server, uri));
        return Task.FromResult(new JsonObject { ["contents"] = new JsonArray(new JsonObject { ["uri"] = uri, ["mimeType"] = "application/json", ["text"] = "{\"releases\":3}" }) });
    }
}

/// <summary>The user, scripted: each card records what it showed and answers as told.</summary>
internal sealed class FakeMcpAppUser : IMcpAppHostUi
{
    public bool Dark { get; set; } = true;

    public event Action? ThemeChanged;

    public void ChangeTheme(bool dark)
    {
        Dark = dark;
        ThemeChanged?.Invoke();
    }

    public McpAppDomainDecision DomainAnswer { get; set; } = McpAppDomainDecision.NotNow;

    public List<McpAppDomainRequest> DomainCards { get; } = [];

    public bool MessageAnswer { get; set; }

    public int MessageCards { get; set; }

    public McpAppToolDecision ToolAnswer { get; set; } = McpAppToolDecision.Deny;

    public List<string> ToolCards { get; } = [];

    public bool PermissionAnswer { get; set; }

    public List<WebPermissionKind> PermissionCards { get; } = [];

    public bool Supervised { get; set; } = true;

    public List<string> Messages { get; } = [];

    public string? MessageRefusal { get; set; }

    public List<string> Contexts { get; } = [];

    public List<string> Links { get; } = [];

    public Task<McpAppDomainDecision> AskDomainsAsync(McpAppMount app, McpAppDomainRequest request)
    {
        DomainCards.Add(request);
        return Task.FromResult(DomainAnswer);
    }

    public Task<bool> AskMessagesAsync(McpAppMount app)
    {
        MessageCards++;
        return Task.FromResult(MessageAnswer);
    }

    public Task<McpAppToolDecision> AskToolAsync(McpAppMount app, string tool, string? title)
    {
        ToolCards.Add(tool);
        return Task.FromResult(ToolAnswer);
    }

    public Task<bool> AskPermissionAsync(McpAppMount app, WebPermissionKind kind)
    {
        PermissionCards.Add(kind);
        return Task.FromResult(PermissionAnswer);
    }

    public bool ToolsNeedApproval(Guid thread) => Supervised;

    public string? PostMessage(McpAppMount app, string text)
    {
        if (MessageRefusal is { } refusal) return refusal;
        Messages.Add(text);
        return null;
    }

    public void SetModelContext(McpAppMount app, string text) => Contexts.Add(text);

    public void OpenLink(McpAppMount app, string url) => Links.Add(url);
}
