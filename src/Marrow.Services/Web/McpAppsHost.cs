// PORT-ONLY: web surfaces W19, the MCP Apps host (DESIGN.md 7.6, 6.5, 8.1-8.4; ED-NEW-web-w19-1, -2, -3; owner 2026-10-11
// "web surfaces approved"). A tool whose definition names a ui:// resource draws its result in an MCP App: nth reads the
// page with resources/read and serves it from the app's own sandboxed site https://m-<server>-<hash>.invalid/ in the
// artifacts profile, with a Content-Security-Policy built from the page's _meta.ui.csp and never wider. Domains outside the
// app's site stay closed until the user allows them on a card; wildcards and 'unsafe-eval' need "Allow broad access". The
// page talks to nth over web messages (origin-checked) with the MCP Apps protocol (McpAppsBridge in Core): tools/call reaches
// only that server's app-visible tools, through the normal approval; ui/message asks once per chat and lands as an
// attributed row, wrapped as untrusted for the model, never as approval; ui/update-model-context is a visible chip; links
// go through the link setting; display modes are the App session's. The mount is one page in one view, whatever its display
// mode; the App moves the view between the thread, the App pane and the pop-out window. No Swift counterpart.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Support;
using Marrow.Core.Web;
using Marrow.Services.MCP;

namespace Marrow.Services.Web;

/// <summary>What an MCP App page asks the host for besides network (<c>_meta.ui.permissions</c>).</summary>
public sealed record McpAppPermissions(bool Camera, bool Microphone, bool Geolocation, bool ClipboardWrite)
{
    public static readonly McpAppPermissions None = new(false, false, false, false);

    /// <summary>DESIGN 7.6 and 5.5: the kinds the page may ask the user for. Clipboard writes stay closed from script (8.4).</summary>
    public bool Prompts(WebPermissionKind kind) => kind switch
    {
        WebPermissionKind.Camera => Camera,
        WebPermissionKind.Microphone => Microphone,
        WebPermissionKind.Location => Geolocation,
        _ => false,
    };

    /// <summary>The Permissions-Policy features opened to the page's own origin.</summary>
    public IReadOnlyList<string> Features =>
        [.. new[] { (Camera, "camera"), (Microphone, "microphone"), (Geolocation, "geolocation") }.Where(f => f.Item1).Select(f => f.Item2)];

    /// <summary>True when navigator.mediaDevices must stay (camera or microphone).</summary>
    public bool KeepsMedia => Camera || Microphone;
}

/// <summary>An MCP App page as read from its server.</summary>
public sealed record McpAppPage(string Server, string Uri, string Html, McpUiCsp Csp)
{
    public McpAppPermissions Permissions { get; init; } = McpAppPermissions.None;

    /// <summary>The page's <c>_meta.ui.prefersBorder</c>, or null.</summary>
    public bool? PrefersBorder { get; init; }
}

/// <summary>One of the server's tools as the app sees it.</summary>
public sealed record McpAppTool(string Name, string? Title, bool VisibleToApp);

/// <summary>The MCP side of the host: MCPHub in the app (<see cref="McpHubAppServers"/>), a fake in tests.</summary>
public interface IMcpAppServers
{
    Task<McpAppPage> ReadPageAsync(string server, string uri, CancellationToken cancellationToken);

    Task<IReadOnlyList<McpAppTool>> ToolsAsync(string server, CancellationToken cancellationToken);

    /// <summary>Calls one of the server's tools for its app; the answer is the MCP CallToolResult.</summary>
    Task<JsonObject> CallToolAsync(string server, string tool, JsonObject arguments, CancellationToken cancellationToken);

    /// <summary>Reads one of the server's resources; the answer is the MCP ReadResourceResult.</summary>
    Task<JsonObject> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken);
}

/// <summary>What the user sees of an app: nth's cards, the thread's rows and chips, the link setting and the theme.</summary>
public interface IMcpAppHostUi
{
    bool Dark { get; }

    event Action? ThemeChanged;

    /// <summary>"shipyard's Release board wants to reach: ..." with Allow for shipyard / Not now (and "Allow broad access").</summary>
    Task<McpAppDomainDecision> AskDomainsAsync(McpAppMount app, McpAppDomainRequest request);

    /// <summary>"Let shipyard post messages in this chat?"</summary>
    Task<bool> AskMessagesAsync(McpAppMount app);

    /// <summary>"shipyard wants to run promote_release" with Allow once / Always for shipyard / Deny.</summary>
    Task<McpAppToolDecision> AskToolAsync(McpAppMount app, string tool, string? title);

    /// <summary>A declared camera, microphone or location request (DESIGN 5.5).</summary>
    Task<bool> AskPermissionAsync(McpAppMount app, WebPermissionKind kind);

    /// <summary>True when the chat asks before tools run (Supervised).</summary>
    bool ToolsNeedApproval(Guid thread);

    /// <summary>Posts an app's message as its own row and sends it to the model; null when sent, else why not.</summary>
    string? PostMessage(McpAppMount app, string text);

    /// <summary>Replaces the app's context chip (an empty text removes it).</summary>
    void SetModelContext(McpAppMount app, string text);

    /// <summary>A web link the app or the user in it asked to open, through the link setting.</summary>
    void OpenLink(McpAppMount app, string url);
}

/// <summary>Which tool call an app draws, and where.</summary>
public sealed record McpAppLaunch(Guid Thread, string Server, string ResourceUri)
{
    /// <summary>The tool's name on its server.</summary>
    public string? ToolName { get; init; }

    /// <summary>What the cards call the app: the tool's title ("Release board"), else its name.</summary>
    public string? Title { get; init; }

    /// <summary>The tool's definition (name, title, description, inputSchema) for hostContext.toolInfo.</summary>
    public JsonObject? Tool { get; init; }

    /// <summary>The call's id in the thread, for hostContext.toolInfo.id.</summary>
    public string? ToolCallId { get; init; }
}

/// <summary>The size an app asked for (<c>ui/notifications/size-changed</c>).</summary>
public readonly record struct McpAppSize(double? Width, double? Height);

/// <summary>The MCP Apps host: one per process, bound by the App at start.</summary>
public sealed class McpAppsHost
{
    private readonly Action<string> log;

    public McpAppsHost(IMcpAppServers servers, McpAppGrantStore grants, IMcpAppHostUi ui, Action<Action>? post = null, TimeProvider? time = null, Action<string>? log = null)
    {
        Servers = servers;
        Grants = grants;
        Ui = ui;
        Post = post ?? (static action => action());
        Time = time ?? TimeProvider.System;
        this.log = log ?? (static _ => { });
    }

    public static McpAppsHost? Shared { get; private set; }

    public static void Bind(McpAppsHost host) => Shared = host;

    public IMcpAppServers Servers { get; }

    public McpAppGrantStore Grants { get; }

    public IMcpAppHostUi Ui { get; }

    internal Action<Action> Post { get; }

    internal TimeProvider Time { get; }

    /// <summary>How long a closing app may take to answer ui/resource-teardown.</summary>
    public static readonly TimeSpan TeardownTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>The app's own site address.</summary>
    public static string Address(string server, string uri) => SandboxSites.Origin(SandboxSites.AppHost(server, uri)) + "/";

    /// <summary>Mounts an app in a sandboxed view (WebSurfaceKind.McpApp): reads its page and tools, then loads it.</summary>
    public McpAppMount Mount(WebSurfaceView view, McpAppLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(launch);
        if (view.Kind != WebSurfaceKind.McpApp) throw new ArgumentException("An MCP App runs in an McpApp view.", nameof(view));
        var mount = new McpAppMount(this, view, launch);
        mount.Start();
        return mount;
    }

    internal void Log(string message) => log("web: " + message);
}

/// <summary>One MCP App page in one view.</summary>
public sealed class McpAppMount : IWebResourceHandler, IDisposable
{
    private const int MaxLogCharacters = 500;
    private readonly McpAppsHost host;
    private readonly CancellationTokenSource life = new();
    private readonly McpAppsBridge bridge;
    private IReadOnlySet<string> appTools = new HashSet<string>(StringComparer.Ordinal);
    private McpAppPage? page;
    private McpAppDomainGrant? grant;
    private bool askedDomains;
    private bool messagesRefused;
    private bool disposed;

    internal McpAppMount(McpAppsHost host, WebSurfaceView view, McpAppLaunch launch)
    {
        this.host = host;
        View = view;
        Launch = launch;
        SiteHost = SandboxSites.AppHost(launch.Server, launch.ResourceUri);
        Origin = SandboxSites.Origin(SiteHost);
        Context = new McpAppHostContext
        {
            Dark = host.Ui.Dark,
            Variables = McpAppStyles.Variables(host.Ui.Dark),
            Tool = launch.Tool,
            ToolCallId = launch.ToolCallId,
        };
        bridge = new McpAppsBridge(Send, InitializeResult, name => appTools.Contains(name));
        bridge.Requested += OnRequested;
        bridge.Noticed += OnNoticed;
        bridge.BecameReady += () => Ready?.Invoke();
        bridge.TornDown += () => TornDown?.Invoke();
        view.WebMessageReceived += OnMessage;
        view.NavigationStarting += OnNavigationStarting;
        view.SandboxNavigationBlocked += OnBlocked;
        view.PermissionRequested += OnPermission;
        host.Ui.ThemeChanged += OnThemeChanged;
    }

    public WebSurfaceView View { get; }

    public McpAppLaunch Launch { get; }

    public string Server => Launch.Server;

    /// <summary>What the cards call the app ("Release board").</summary>
    public string Title => Launch.Title is { Length: > 0 } title ? title : Launch.ToolName is { Length: > 0 } name ? name : "app";

    /// <summary>The app's own site.</summary>
    public string SiteHost { get; }

    public string Origin { get; }

    public string Address => Origin + "/";

    /// <summary>The page, once read; null while loading or when reading it failed.</summary>
    public McpAppPage? Page => page;

    /// <summary>Why the page could not load, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The host context the app sees now.</summary>
    public McpAppHostContext Context { get; private set; }

    public McpAppDisplayMode DisplayMode => Context.DisplayMode;

    public McpAppBridgeState State => bridge.State;

    /// <summary>The display modes the app said it supports (null: it said nothing, so all).</summary>
    public IReadOnlyList<McpAppDisplayMode>? AvailableModes => bridge.AvailableModes;

    /// <summary>The App session's answer to ui/request-display-mode: the mode it switched to. Null: stays where it is.</summary>
    public Func<McpAppDisplayMode, McpAppDisplayMode>? DisplayModeHandler { get; set; }

    /// <summary>The page was read and is loading in the view.</summary>
    public event Action? Loaded;

    /// <summary>The page could not be read; <see cref="Failure"/> says why.</summary>
    public event Action<string>? Failed;

    /// <summary>The app finished its handshake.</summary>
    public event Action? Ready;

    /// <summary>The app answered ui/resource-teardown (or was never ready).</summary>
    public event Action? TornDown;

    /// <summary>The app asked for a size (inline height comes from it).</summary>
    public event Action<McpAppSize>? SizeRequested;

    internal McpAppsBridge Bridge => bridge;

    internal void Start() => Observe(LoadAsync());

    private async Task LoadAsync()
    {
        McpAppPage read;
        IReadOnlyList<McpAppTool> tools;
        try
        {
            var pageTask = host.Servers.ReadPageAsync(Server, Launch.ResourceUri, life.Token);
            var toolsTask = ToolsOrNoneAsync();
            read = await pageTask.ConfigureAwait(false);
            tools = await toolsTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (life.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            var reason = error.Message is { Length: > 0 } message ? message : "The server's app page could not be read.";
            host.Post(() =>
            {
                if (disposed) return;
                Failure = reason;
                host.Log($"mcp app {Server}: the page {Launch.ResourceUri} could not be read: {reason}");
                Failed?.Invoke(reason);
            });
            return;
        }

        host.Post(() =>
        {
            if (disposed) return;
            page = read;
            appTools = tools.Where(t => t.VisibleToApp).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            grant = host.Grants.Get(Server).Domains.GetValueOrDefault(Launch.ResourceUri);
            View.SetSandbox(new WebSandbox(Origin, this, SandboxScripts.McpAppScripts(read.Permissions.KeepsMedia))
            {
                Prompts = kind => page?.Permissions.Prompts(kind) == true,
                AllowsFrame = AllowsFrame,
            });
            View.Navigate(Address);
            Loaded?.Invoke();
            AskDomains();
        });
    }

    private async Task<IReadOnlyList<McpAppTool>> ToolsOrNoneAsync()
    {
        try
        {
            return await host.Servers.ToolsAsync(Server, life.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            host.Log($"mcp app {Server}: its tools could not be listed: {error.Message}");
            return [];
        }
    }

    // ---- the tool call it draws (from the App session, on the main actor) ----

    public void SetToolInputPartial(JsonObject arguments) => bridge.SetToolInputPartial(arguments);

    public void SetToolInput(JsonObject arguments) => bridge.SetToolInput(arguments);

    public void SetToolResult(JsonObject callToolResult) => bridge.SetToolResult(callToolResult);

    public void SetToolCancelled(string reason) => bridge.SetToolCancelled(reason);

    /// <summary>Changes what the app is told about its container (mode, size, theme); only the differences go out.</summary>
    public void UpdateContext(Func<McpAppHostContext, McpAppHostContext> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var before = Context;
        Context = change(before);
        var changes = McpAppHostContext.Changes(before, Context);
        if (changes.Count > 0 && bridge.State == McpAppBridgeState.Ready) bridge.ChangeHostContext(changes);
    }

    /// <summary>Asks the app to save its state and waits for its answer, at most <see cref="McpAppsHost.TeardownTimeout"/>.</summary>
    public async Task TeardownAsync(string reason = "closed")
    {
        if (bridge.State is McpAppBridgeState.Closed) return;
        var done = new TaskCompletionSource();
        void Finished() => done.TrySetResult();
        bridge.TornDown += Finished;
        try
        {
            bridge.Teardown(reason);
            if (bridge.State is McpAppBridgeState.Closed) return;
            await Task.WhenAny(done.Task, Task.Delay(McpAppsHost.TeardownTimeout, host.Time)).ConfigureAwait(true);
        }
        finally
        {
            bridge.TornDown -= Finished;
        }
    }

    /// <summary>Loads the page again (a new document; the bridge hands it the tool's data again once it is initialized).</summary>
    public void Reload()
    {
        if (!disposed && page is not null) View.Navigate(Address);
    }

    // ---- the site ----

    public WebResourceResponse? Answer(WebResourceRequest request)
    {
        if (!Uri.TryCreate(request.Uri, UriKind.Absolute, out var uri)) return Forbidden(request, "not an address");
        if (uri.Scheme is "data" or "blob") return null; // the page's own data, governed by the CSP
        var requestHost = uri.IdnHost.ToLowerInvariant();
        var secure = uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort;
        if (secure && requestHost == SiteHost) return Own(request, uri);
        if (SandboxSites.SurfaceOf(requestHost) is not null) return Forbidden(request, "another sandbox's site");
        if (uri.Scheme is "https" or "wss" && page is { } current && McpAppDomains.Matches(requestHost, McpAppDomains.Hosts(current.Csp, grant))) return null;
        return Forbidden(request, "not a domain the user allowed");
    }

    private WebResourceResponse Own(WebResourceRequest request, Uri uri)
    {
        if (request.Method is not ("GET" or "HEAD")) return Status(405, "Method Not Allowed", SandboxHeaders.Blocked());
        if (page is not { } current) return Status(404, "Not Found", SandboxHeaders.Blocked(), "The app's page is not loaded.");
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (path is not ("" or "index.html")) return Status(404, "Not Found", SandboxHeaders.Blocked(), "Not in this app: " + path);
        var csp = CspBuilder.McpApp(current.Csp, McpAppDomains.Allowed(current.Csp, grant), grant?.Broad == true);
        var headers = SandboxHeaders.Document("text/html; charset=utf-8", csp, SandboxHeaders.PermissionsPolicyAllowing(current.Permissions.Features));
        return new WebResourceResponse(200, "OK", headers, request.Method == "HEAD" ? [] : Encoding.UTF8.GetBytes(current.Html));
    }

    // Frames reach only a host the user allowed (frame-src in the CSP keeps them to the declared frame domains).
    private bool AllowsFrame(string url) =>
        page is { } current && current.Csp.FrameDomains.Count > 0 && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && McpAppDomains.Matches(uri.IdnHost.ToLowerInvariant(), McpAppDomains.Hosts(current.Csp, grant));

    private WebResourceResponse Forbidden(WebResourceRequest request, string why)
    {
        host.Log($"mcp app {Server}: blocked {request.Method} {ArtifactMount.Redact(request.Uri)} ({why})");
        return Status(403, "Forbidden", SandboxHeaders.Blocked(), "Blocked by nth: this app cannot reach this address.");
    }

    private static WebResourceResponse Status(int code, string reason, IReadOnlyList<KeyValuePair<string, string>> headers, string? text = null) =>
        new(code, reason, headers, text is null ? [] : Encoding.UTF8.GetBytes(text));

    // ---- consent ----

    private void AskDomains()
    {
        if (askedDomains || page is not { } current) return;
        if (McpAppDomains.Request(Server, Title, current.Csp) is not { } request) return;
        if (McpAppDomains.Allowed(current.Csp, grant) is not null) return;
        askedDomains = true;
        Observe(DecideDomainsAsync(current, request));
    }

    private async Task DecideDomainsAsync(McpAppPage current, McpAppDomainRequest request)
    {
        McpAppDomainDecision decision;
        try
        {
            decision = await host.Ui.AskDomainsAsync(this, request).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            host.Log($"mcp app {Server}: the domain card failed: {error.Message}");
            return;
        }

        if (decision == McpAppDomainDecision.NotNow) return;
        var given = new McpAppDomainGrant(McpAppDomains.Fingerprint(current.Csp), decision == McpAppDomainDecision.AllowBroad && request.HasBroad);
        host.Grants.Update(Server, g => g with { Domains = new Dictionary<string, McpAppDomainGrant>(g.Domains, StringComparer.Ordinal) { [Launch.ResourceUri] = given } });
        host.Post(() =>
        {
            if (disposed || !ReferenceEquals(page, current)) return;
            grant = given;
            host.Log($"mcp app {Server}: the user allowed its declared domains{(given.Broad ? " with broad access" : "")}");
            Reload(); // the CSP is the document's: only a new one picks the grant up
        });
    }

    // ---- the page's messages ----

    private void Send(string json) => host.Post(() =>
    {
        if (!disposed) View.Engine?.PostMessage(json);
    });

    private JsonObject InitializeResult()
    {
        var current = page;
        var allowed = current is null ? null : McpAppDomains.Allowed(current.Csp, grant);
        var permissions = new JsonObject();
        if (current?.Permissions is { } asked)
        {
            if (asked.Camera) permissions["camera"] = new JsonObject();
            if (asked.Microphone) permissions["microphone"] = new JsonObject();
            if (asked.Geolocation) permissions["geolocation"] = new JsonObject();
        }

        static JsonArray List(IReadOnlyList<string>? items) => [.. (items ?? []).Select(i => (JsonNode)JsonValue.Create(i)!)];
        return new JsonObject
        {
            ["protocolVersion"] = McpAppProtocol.Version,
            ["hostInfo"] = new JsonObject { ["name"] = "nth", ["version"] = HostVersion },
            ["hostCapabilities"] = new JsonObject
            {
                ["openLinks"] = new JsonObject(),
                ["serverTools"] = new JsonObject(),
                ["serverResources"] = new JsonObject(),
                ["logging"] = new JsonObject(),
                ["sandbox"] = new JsonObject
                {
                    ["permissions"] = permissions,
                    ["csp"] = new JsonObject
                    {
                        ["connectDomains"] = List(allowed?.ConnectDomains),
                        ["resourceDomains"] = List(allowed?.ResourceDomains),
                        ["frameDomains"] = List(allowed?.FrameDomains),
                        ["baseUriDomains"] = List(allowed?.BaseUriDomains),
                    },
                },
            },
            ["hostContext"] = Context.ToJson(),
        };
    }

    private static string HostVersion { get; } = typeof(McpAppsHost).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private void OnMessage(object? sender, WebMessage message)
    {
        if (disposed) return;
        if (!string.Equals(message.SourceOrigin?.TrimEnd('/'), Origin, StringComparison.OrdinalIgnoreCase))
        {
            host.Log($"mcp app {Server}: ignored a message from {message.SourceOrigin}");
            return;
        }

        bridge.Receive(message.Json);
    }

    private void OnNoticed(McpAppNotice notice)
    {
        switch (notice.Method)
        {
            case McpAppProtocol.SizeChanged:
                var width = Number(notice.Params["width"]);
                var height = Number(notice.Params["height"]);
                if (width is not null || height is not null) SizeRequested?.Invoke(new McpAppSize(width, height));
                return;
            case McpAppProtocol.Log:
                var level = McpJson.Text(notice.Params["level"]) ?? "info";
                var data = notice.Params["data"] is { } node ? McpJson.Text(node) ?? node.ToJsonString() : "";
                host.Log($"mcp app {Server} [{Cap(level, 16)}]: {Cap(data, MaxLogCharacters)}");
                return;
        }
    }

    private void OnRequested(McpAppRequest request)
    {
        switch (request.Method)
        {
            case McpAppProtocol.ToolsCall:
                Observe(CallToolAsync(request));
                return;
            case McpAppProtocol.ResourcesRead:
                Observe(ReadResourceAsync(request));
                return;
            case McpAppProtocol.Message:
                Observe(PostMessageAsync(request));
                return;
            case McpAppProtocol.UpdateModelContext:
                UpdateModelContext(request);
                return;
            case McpAppProtocol.OpenLink:
                OpenLink(request);
                return;
            case McpAppProtocol.RequestDisplayMode:
                RequestDisplayMode(request);
                return;
            default:
                bridge.Fail(request, "Method not found: " + request.Method, McpAppsBridge.MethodNotFoundCode);
                return;
        }
    }

    private async Task CallToolAsync(McpAppRequest request)
    {
        var name = McpJson.Text(request.Params["name"]) ?? "";
        var arguments = request.Params["arguments"] as JsonObject ?? [];
        try
        {
            var grants = host.Grants.Get(Server);
            if (host.Ui.ToolsNeedApproval(Launch.Thread) && !grants.AlwaysTools.Contains(name))
            {
                var decision = await host.Ui.AskToolAsync(this, name, Title).ConfigureAwait(false);
                if (decision == McpAppToolDecision.Deny)
                {
                    Reply(() => bridge.Fail(request, "The user denied " + name + "."));
                    return;
                }

                if (decision == McpAppToolDecision.AlwaysForServer)
                    host.Grants.Update(Server, g => g with { AlwaysTools = new HashSet<string>(g.AlwaysTools, StringComparer.Ordinal) { name } });
            }

            var result = await host.Servers.CallToolAsync(Server, name, (JsonObject)arguments.DeepClone(), life.Token).ConfigureAwait(false);
            Reply(() => bridge.Answer(request, result));
        }
        catch (OperationCanceledException) when (life.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Reply(() => bridge.Fail(request, error.Message is { Length: > 0 } message ? message : "The tool failed."));
        }
    }

    private async Task ReadResourceAsync(McpAppRequest request)
    {
        if (McpJson.Text(request.Params["uri"]) is not { Length: > 0 } uri)
        {
            bridge.Fail(request, "resources/read needs a uri.", McpAppsBridge.InvalidParamsCode);
            return;
        }

        try
        {
            var result = await host.Servers.ReadResourceAsync(Server, uri, life.Token).ConfigureAwait(false);
            Reply(() => bridge.Answer(request, result));
        }
        catch (OperationCanceledException) when (life.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Reply(() => bridge.Fail(request, error.Message is { Length: > 0 } message ? message : "The resource could not be read."));
        }
    }

    private async Task PostMessageAsync(McpAppRequest request)
    {
        var text = MessageText(request.Params);
        if (string.IsNullOrWhiteSpace(text))
        {
            bridge.Fail(request, "ui/message needs text content.", McpAppsBridge.InvalidParamsCode);
            return;
        }

        if (messagesRefused)
        {
            bridge.Fail(request, "The user did not allow messages from " + Server + ".");
            return;
        }

        try
        {
            if (!host.Grants.Get(Server).MessageChats.Contains(Launch.Thread))
            {
                var allowed = await host.Ui.AskMessagesAsync(this).ConfigureAwait(false);
                if (!allowed)
                {
                    Reply(() =>
                    {
                        messagesRefused = true;
                        bridge.Fail(request, "The user did not allow messages from " + Server + ".");
                    });
                    return;
                }

                host.Grants.Update(Server, g => g with { MessageChats = new HashSet<Guid>(g.MessageChats) { Launch.Thread } });
            }

            Reply(() =>
            {
                if (host.Ui.PostMessage(this, text) is { } refused) bridge.Fail(request, refused);
                else bridge.Answer(request, []);
            });
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Reply(() => bridge.Fail(request, error.Message));
        }
    }

    /// <summary>The text of a ui/message: its content block (or blocks), text items only.</summary>
    internal static string MessageText(JsonObject parameters)
    {
        if (McpJson.Text(parameters["role"]) is { } role && role != "user") return "";
        return Texts(parameters["content"]);
    }

    private static string Texts(JsonNode? content)
    {
        // Typed arms: a collection expression here would build a JsonArray, which cannot adopt a node that has a parent.
        IEnumerable<JsonNode?> blocks = content switch
        {
            JsonArray array => array.AsEnumerable(),
            JsonObject single => new JsonNode?[] { single },
            _ => Array.Empty<JsonNode?>(),
        };
        return string.Join("\n", blocks.OfType<JsonObject>()
            .Where(b => McpJson.Text(b["type"]) == "text")
            .Select(b => McpJson.Text(b["text"]))
            .OfType<string>());
    }

    private void UpdateModelContext(McpAppRequest request)
    {
        var text = Texts(request.Params["content"]);
        if (request.Params["structuredContent"] is JsonObject structured)
            text = text.Length == 0 ? structured.ToJsonString() : text + "\n" + structured.ToJsonString();
        host.Ui.SetModelContext(this, text);
        bridge.Answer(request, []);
    }

    private void OpenLink(McpAppRequest request)
    {
        var url = McpJson.Text(request.Params["url"]);
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            bridge.Fail(request, "Only web links open from an app.", McpAppsBridge.InvalidParamsCode);
            return;
        }

        host.Log($"mcp app {Server}: a link to {ArtifactMount.Redact(url)} was offered to the link setting");
        host.Ui.OpenLink(this, uri.AbsoluteUri);
        bridge.Answer(request, []);
    }

    private void RequestDisplayMode(McpAppRequest request)
    {
        var current = DisplayMode;
        if (McpAppProtocol.ParseMode(McpJson.Text(request.Params["mode"])) is { } wanted && wanted != current && bridge.Supports(wanted)
            && DisplayModeHandler is { } handler)
        {
            current = handler(wanted);
        }

        bridge.Answer(request, new JsonObject { ["mode"] = McpAppProtocol.SpecName(current) });
    }

    // A handler's own failure is logged, never lost.
    private void Observe(Task task)
    {
        if (task.IsCompletedSuccessfully) return;
        task.ContinueWith(t => host.Log($"mcp app {Server}: {t.Exception?.GetBaseException().GetType().Name}: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Reply(Action action) => host.Post(() =>
    {
        if (!disposed) action();
    });

    // ---- the view's events ----

    private void OnNavigationStarting(object? sender, WebNavigationStarting e)
    {
        if (e.IsMainFrame) bridge.NewDocument();
    }

    private void OnBlocked(object? sender, WebSandboxNavigation e)
    {
        var web = Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
        if (e.IsUserInitiated && web)
        {
            host.Log($"mcp app {Server}: a link to {ArtifactMount.Redact(e.Url)} was offered to the link setting");
            host.Ui.OpenLink(this, e.Url);
        }
        else host.Log($"mcp app {Server}: cancelled a {(e.IsNewWindow ? "new window" : e.IsMainFrame ? "navigation" : "frame navigation")} to {ArtifactMount.Redact(e.Url)}");
    }

    private void OnPermission(object? sender, WebPermissionRequest e)
    {
        if (disposed || page?.Permissions.Prompts(e.Kind) != true || e.Decision.IsCompleted) return;
        e.Defer();
        Observe(AskPermissionAsync(e));
    }

    private async Task AskPermissionAsync(WebPermissionRequest e)
    {
        try
        {
            var allowed = await host.Ui.AskPermissionAsync(this, e.Kind).ConfigureAwait(false);
            e.Respond(allowed && !disposed ? WebPermissionDecision.AllowOnce : WebPermissionDecision.Deny);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            host.Log($"mcp app {Server}: the permission card failed: {error.Message}");
            e.Respond(WebPermissionDecision.Deny);
        }
    }

    private void OnThemeChanged() => host.Post(() =>
    {
        if (disposed) return;
        var dark = host.Ui.Dark;
        UpdateContext(c => c with { Dark = dark, Variables = McpAppStyles.Variables(dark) });
    });

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) && number >= 0 ? number : null;

    private static string Cap(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        life.Cancel();
        View.WebMessageReceived -= OnMessage;
        View.NavigationStarting -= OnNavigationStarting;
        View.SandboxNavigationBlocked -= OnBlocked;
        View.PermissionRequested -= OnPermission;
        host.Ui.ThemeChanged -= OnThemeChanged;
        bridge.Close();
        life.Dispose();
    }
}

internal static class McpJson
{
    /// <summary>The string of a JSON string node, else null.</summary>
    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>
/// The user's answers per server, kept across restarts in one small JSON file: the domain grants per page (bound to the
/// declaration they answered), the chats the app may post messages in, the tools it may always run, and the pop-out size.
/// </summary>
public sealed class McpAppGrantStore
{
    public const string FileName = "mcp-apps.json";
    private readonly object gate = new();
    private readonly string? path;
    private Dictionary<string, McpAppServerGrants>? servers;

    /// <param name="path">The file; null keeps the answers in memory only (tests, and a data folder that cannot be written).</param>
    public McpAppGrantStore(string? path) => this.path = path;

    /// <summary>The process's file under the data folder.</summary>
    public static string DefaultPath() => Path.Combine(DevDataRoot.LocalApplicationData(), AppInfo.DataFolderName, FileName);

    public event Action<string>? Changed;

    public McpAppServerGrants Get(string server)
    {
        lock (gate) return Load().GetValueOrDefault(server) ?? new McpAppServerGrants();
    }

    public void Update(string server, Func<McpAppServerGrants, McpAppServerGrants> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (gate)
        {
            var all = Load();
            all[server] = change(all.GetValueOrDefault(server) ?? new McpAppServerGrants());
            Save(all);
        }

        Changed?.Invoke(server);
    }

    private Dictionary<string, McpAppServerGrants> Load()
    {
        if (servers is not null) return servers;
        servers = new Dictionary<string, McpAppServerGrants>(StringComparer.Ordinal);
        if (path is null || !File.Exists(path)) return servers;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path))?["servers"] is not JsonObject root) return servers;
            foreach (var (name, node) in root)
                if (node is JsonObject entry) servers[name] = Read(entry);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable file asks again; it is rewritten on the next answer.
        }

        return servers;
    }

    private static McpAppServerGrants Read(JsonObject entry)
    {
        var domains = new Dictionary<string, McpAppDomainGrant>(StringComparer.Ordinal);
        if (entry["domains"] is JsonObject declared)
            foreach (var (uri, node) in declared)
                if (McpJson.Text(node?["fingerprint"]) is { Length: > 0 } fingerprint)
                    domains[uri] = new McpAppDomainGrant(fingerprint, node?["broad"] is JsonValue broad && broad.TryGetValue<bool>(out var b) && b);
        var chats = new HashSet<Guid>();
        if (entry["messageChats"] is JsonArray ids)
            foreach (var id in ids)
                if (Guid.TryParse(McpJson.Text(id), out var chat)) chats.Add(chat);
        var tools = new HashSet<string>(StringComparer.Ordinal);
        if (entry["alwaysTools"] is JsonArray names)
            foreach (var name in names)
                if (McpJson.Text(name) is { Length: > 0 } tool) tools.Add(tool);
        (double, double)? size = entry["window"] is JsonObject window && Size(window["width"]) is { } w && Size(window["height"]) is { } h ? (w, h) : null;
        return new McpAppServerGrants { Domains = domains, MessageChats = chats, AlwaysTools = tools, WindowSize = size };
    }

    private static double? Size(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) && number > 0 && number < 100_000 ? number : null;

    private void Save(Dictionary<string, McpAppServerGrants> all)
    {
        if (path is null) return;
        var root = new JsonObject();
        foreach (var (name, grants) in all.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var domains = new JsonObject();
            foreach (var (uri, given) in grants.Domains.OrderBy(p => p.Key, StringComparer.Ordinal))
                domains[uri] = new JsonObject { ["fingerprint"] = given.Fingerprint, ["broad"] = given.Broad };
            var entry = new JsonObject
            {
                ["domains"] = domains,
                ["messageChats"] = new JsonArray([.. grants.MessageChats.Select(c => (JsonNode)JsonValue.Create(c.ToString("D"))!)]),
                ["alwaysTools"] = new JsonArray([.. grants.AlwaysTools.Order(StringComparer.Ordinal).Select(t => (JsonNode)JsonValue.Create(t)!)]),
            };
            if (grants.WindowSize is { } size) entry["window"] = new JsonObject { ["width"] = size.Width, ["height"] = size.Height };
            root[name] = entry;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Marrow.Core.Port.FileOps.WriteAtomic(path, Encoding.UTF8.GetBytes(new JsonObject { ["version"] = 1, ["servers"] = root }.ToJsonString(Indented)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Kept in memory for this run; the next answer tries again.
        }
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}

/// <summary>The MCP side of the host over MCPHub: the page, the tools, calls and reads, as the MCP Apps protocol carries them.</summary>
public sealed class McpHubAppServers(MCPHub hub) : IMcpAppServers
{
    public async Task<McpAppPage> ReadPageAsync(string server, string uri, CancellationToken cancellationToken)
    {
        var resource = await hub.ReadUiResource(server, uri).WaitAsync(cancellationToken).ConfigureAwait(false);
        var csp = resource.Ui.Csp;
        var permissions = resource.Ui.Permissions;
        return new McpAppPage(resource.Server, resource.Uri, resource.Html,
            new McpUiCsp([.. csp.ConnectDomains], [.. csp.ResourceDomains], [.. csp.FrameDomains], [.. csp.BaseUriDomains]))
        {
            Permissions = new McpAppPermissions(permissions.Camera, permissions.Microphone, permissions.Geolocation, permissions.ClipboardWrite),
            PrefersBorder = resource.Ui.PrefersBorder,
        };
    }

    public async Task<IReadOnlyList<McpAppTool>> ToolsAsync(string server, CancellationToken cancellationToken)
    {
        var tools = await hub.AllTools().WaitAsync(cancellationToken).ConfigureAwait(false);
        return [.. tools.Where(t => string.Equals(t.Server, server, StringComparison.Ordinal)).Select(t => new McpAppTool(t.Name, t.Title, t.IsVisibleToApp))];
    }

    public async Task<JsonObject> CallToolAsync(string server, string tool, JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = JSONValue.Parse(arguments.ToJsonString()) ?? JSONValue.FromDictionaryLiteral();
        var result = await hub.Call("mcp__" + server + "__" + tool, args, MCPCaller.App).WaitAsync(cancellationToken).ConfigureAwait(false);
        return CallToolResult(result);
    }

    public async Task<JsonObject> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken)
    {
        var contents = await hub.ReadResource(server, uri).WaitAsync(cancellationToken).ConfigureAwait(false);
        var list = new JsonArray();
        foreach (var item in contents)
        {
            var entry = new JsonObject { ["uri"] = item.Uri };
            if (item.MimeType is { } type) entry["mimeType"] = type;
            if (item.Text is { } text) entry["text"] = text;
            if (item.Blob is { } blob) entry["blob"] = blob;
            if (item.Meta is { } meta && JsonNode.Parse(meta.CompactString) is JsonObject metaObject) entry["_meta"] = metaObject;
            list.Add(entry);
        }

        return new JsonObject { ["contents"] = list };
    }

    /// <summary>The MCP CallToolResult of a hub result: text, images and resources as content, the structured content and _meta.</summary>
    public static JsonObject CallToolResult(MCPHub.Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var content = new JsonArray();
        if (result.Text.Length > 0) content.Add(new JsonObject { ["type"] = "text", ["text"] = result.Text });
        foreach (var image in result.Images) content.Add(new JsonObject { ["type"] = "image", ["data"] = image.Data, ["mimeType"] = image.MimeType });
        foreach (var resource in result.Resources)
        {
            if (resource.Embedded)
            {
                var inner = new JsonObject { ["uri"] = resource.Uri };
                if (resource.MimeType is { } type) inner["mimeType"] = type;
                if (resource.Text is { } text) inner["text"] = text;
                content.Add(new JsonObject { ["type"] = "resource", ["resource"] = inner });
            }
            else
            {
                var link = new JsonObject { ["type"] = "resource_link", ["uri"] = resource.Uri, ["name"] = resource.Name ?? resource.Uri };
                if (resource.MimeType is { } type) link["mimeType"] = type;
                if (resource.Description is { } description) link["description"] = description;
                content.Add(link);
            }
        }

        var answer = new JsonObject { ["content"] = content, ["isError"] = result.IsError };
        if (result.StructuredContent is { } structured && JsonNode.Parse(structured.CompactString) is JsonObject structuredObject) answer["structuredContent"] = structuredObject;
        if (result.Meta is { } meta && JsonNode.Parse(meta.CompactString) is JsonObject metaObject) answer["_meta"] = metaObject;
        return answer;
    }
}
