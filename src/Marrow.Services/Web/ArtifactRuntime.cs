// PORT-ONLY: web surfaces W13, the artifacts runtime (DESIGN.md 6.5; ED-NEW-web-w13-1 .. -4). Every artifact runs on its
// own site, https://a-<chat hash>-<id>.invalid, answered by nth itself: its pages per kind, a project's files at their
// paths, the CSP and isolation headers on every response, and the library host for the offline libraries. Nothing else
// is answered: another artifact's site, a widget's or an app's is refused, and so is every other address unless the
// user allowed exactly that site for this version (the network grant). A mount binds one sandboxed view to one artifact:
// its site, the hardening and bridge scripts, the bridge's messages, and the blocked navigations the pane offers as
// "Open link from the artifact". No Swift counterpart.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

public sealed class ArtifactRuntime
{
    private readonly Logger log = new(Logger.Subsystem, "web");

    /// <param name="post">Runs work on the UI thread (replies to pages, the pane's events); inline when null.</param>
    public ArtifactRuntime(ArtifactStore store, ArtifactLibraryFiles libraries, IArtifactBridgeHost? host = null, IArtifactModelClient? model = null,
        IArtifactToolClient? tools = null, TimeProvider? time = null, Action<Action>? post = null)
    {
        Store = store;
        Libraries = libraries;
        Host = host ?? UnboundArtifactBridgeHost.Instance;
        Model = model;
        Tools = tools;
        Data = new ArtifactData(store);
        Meter = new ArtifactModelMeter(time);
        Exporter = new ArtifactExporter(libraries, AllowedNetwork);
        Post = post ?? (static work => work());
    }

    /// <summary>The process's runtime; null until the app binds one.</summary>
    public static ArtifactRuntime? Shared { get; private set; }

    /// <summary>Program.Main binds the process's runtime once.</summary>
    public static void Bind(ArtifactRuntime runtime) => Shared = runtime;

    public ArtifactStore Store { get; }

    public ArtifactLibraryFiles Libraries { get; }

    public ArtifactData Data { get; }

    public ArtifactModelMeter Meter { get; }

    /// <summary>Every export format; a standalone page keeps the version's network grant.</summary>
    public ArtifactExporter Exporter { get; }

    /// <summary>The pane's consent cards, file sheet, composer and theme; refuses everything until Tabula binds it.</summary>
    public IArtifactBridgeHost Host { get; set; }

    public IArtifactModelClient? Model { get; set; }

    public IArtifactToolClient? Tools { get; set; }

    /// <summary>Settings › Tabula › "Artifacts may use your model" (Ask each artifact by default).</summary>
    public Func<ArtifactModelPolicy> ModelPolicy { get; set; } = static () => ArtifactModelPolicy.Ask;

    internal Action<Action> Post { get; }

    /// <summary>An artifact's own address.</summary>
    public static string Address(Guid thread, string id) => SandboxSites.Origin(SandboxSites.ArtifactHost(thread, id)) + "/";

    /// <summary>Binds a sandboxed view (Tabula's preview) to an artifact and loads it; null shows the latest version.</summary>
    public ArtifactMount Mount(WebSurfaceView view, Guid thread, string id, int? version = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var normal = ArtifactStore.NormalizeId(id) ?? throw new ArgumentException("Not an artifact id.", nameof(id));
        return new ArtifactMount(this, view, new ArtifactAddress(thread, normal), version);
    }

    /// <summary>
    /// The sites a version declares in its nth-connect tag, checked (DESIGN 6.5): what the network consent card lists,
    /// with the entries it dropped. Only HTML artifacts and projects with an HTML entry can declare any.
    /// </summary>
    public ArtifactNetworkRequest NetworkRequest(ArtifactSnapshot snapshot) =>
        snapshot.Artifact.Kind switch
        {
            ArtifactKind.Html => ArtifactNetwork.Declared(snapshot.Text),
            ArtifactKind.Project when snapshot.Version.Entry is { } entry && IsHtml(entry) && snapshot.Files!.TryGetValue(entry, out var page) => ArtifactNetwork.Declared(page),
            _ => ArtifactNetworkRequest.None,
        };

    /// <summary>
    /// The sites a version may reach: the user's grant, when it holds exactly the sites this version declares (a version
    /// that changes the list asks again); none otherwise.
    /// </summary>
    public IReadOnlyList<string> AllowedNetwork(ArtifactSnapshot snapshot)
    {
        var granted = Data.Grants(new ArtifactAddress(snapshot.Artifact.Thread, snapshot.Artifact.Id)).NetworkHosts;
        if (granted is not { Count: > 0 }) return [];
        var declared = NetworkRequest(snapshot).HostNames.Select(h => h.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        return declared.SetEquals(granted) ? granted : [];
    }

    internal static bool IsHtml(string path) => path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    internal void Log(string message) => log.Info("web: " + message);
}

/// <summary>A runtime error a page reported, for the pane's calm bar ("This artifact hit an error", Ask to fix).</summary>
public sealed record ArtifactRuntimeError(string Message, string Stack);

public sealed class ArtifactMount : IWebResourceHandler, IDisposable
{
    private readonly ArtifactRuntime runtime;
    private readonly ArtifactBridge bridge;
    private readonly IArtifactBridgeHost themeSource;
    private readonly HashSet<string> missingReported = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private ArtifactSnapshot? snapshot;
    private bool disposed;

    internal ArtifactMount(ArtifactRuntime runtime, WebSurfaceView view, ArtifactAddress artifact, int? version)
    {
        this.runtime = runtime;
        View = view;
        Artifact = artifact;
        Version = version;
        Host = SandboxSites.ArtifactHost(artifact.Thread, artifact.Id);
        Origin = SandboxSites.Origin(Host);
        bridge = new ArtifactBridge(runtime, this);
        view.SetSandbox(new WebSandbox(Origin, this, SandboxScripts.DocumentScripts));
        view.WebMessageReceived += OnMessage;
        view.NavigationStarting += OnNavigationStarting;
        view.SandboxNavigationBlocked += OnBlocked;
        runtime.Store.Changed += OnStoreChanged;
        runtime.Data.GrantsChanged += OnGrantsChanged;
        themeSource = runtime.Host;
        themeSource.ThemeChanged += OnThemeChanged;
        view.Navigate(Address);
    }

    public WebSurfaceView View { get; }

    public ArtifactAddress Artifact { get; }

    public Guid Thread => Artifact.Thread;

    public string Id => Artifact.Id;

    /// <summary>The version shown; null follows the latest.</summary>
    public int? Version { get; private set; }

    /// <summary>The artifact's own site (a-&lt;chat hash&gt;-&lt;id&gt;.invalid).</summary>
    public string Host { get; }

    public string Origin { get; }

    public string Address => Origin + "/";

    /// <summary>A user's click on a link left the artifact: offer it to the link setting as "Open link from the artifact".</summary>
    public event Action<string>? LinkRequested;

    /// <summary>The page reported a runtime error.</summary>
    public event Action<ArtifactRuntimeError>? ErrorReported;

    /// <summary>The page loaded (its title).</summary>
    public event Action<string>? Ready;

    /// <summary>A CDN file the page asked for that nth has no offline copy of (the address).</summary>
    public event Action<string>? LibraryMissing;

    /// <summary>The model use changed: a call started or ended, or a limit paused the artifact (the header's meter).</summary>
    public event Action<ArtifactModelStatus>? ModelStatusChanged;

    /// <summary>A model call finished; its tokens count toward the plan (the header's cost meter).</summary>
    public event Action<ArtifactCompletionResult>? ModelUsed;

    /// <summary>The words of "Open link from the artifact" (the trust strip's offer).</summary>
    public const string OpenLinkTitle = "Open link from the artifact";

    /// <summary>Shows another version (null for the latest) and reloads.</summary>
    public void Show(int? version)
    {
        lock (gate)
        {
            Version = version;
            snapshot = null;
        }

        Reload();
    }

    /// <summary>Loads the artifact again (its bridge calls of the old document are cancelled).</summary>
    public void Reload()
    {
        if (!disposed) View.Navigate(Address);
    }

    /// <summary>
    /// Streaming preview (DESIGN 6.4; web W14): the page morphs into <paramref name="html"/>, the newest text of a version
    /// being written, so its state and scroll survive; the next full load shows the version itself.
    /// </summary>
    public void Morph(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (disposed) return;
        var json = new System.Text.Json.Nodes.JsonObject { ["nth"] = 1, ["event"] = "morph", ["html"] = html }.ToJsonString();
        runtime.Post(() => View.Engine?.PostMessage(json));
    }

    /// <summary>The version the mount serves, read once and kept until the artifact changes.</summary>
    public ArtifactSnapshot? Snapshot()
    {
        lock (gate)
        {
            if (snapshot is not null) return snapshot;
            try
            {
                snapshot = runtime.Store.Read(Thread, Id, Version);
            }
            catch (ArtifactException)
            {
                return null;
            }

            return snapshot;
        }
    }

    // ---- the site ----

    public WebResourceResponse? Answer(WebResourceRequest request)
    {
        if (!Uri.TryCreate(request.Uri, UriKind.Absolute, out var uri)) return Forbidden(request, "not an address");
        if (uri.Scheme is "data" or "blob") return null; // the page's own data, governed by the CSP
        var host = uri.IdnHost.ToLowerInvariant();
        var secure = uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort;
        if (secure && host == SandboxSites.LibraryHost) return Library(request, uri);
        if (secure && host == Host) return Own(request, uri);
        if (SandboxSites.SurfaceOf(host) is not null) return Forbidden(request, "another sandbox's site");
        if (secure && Snapshot() is { } current && runtime.AllowedNetwork(current).Contains(host, StringComparer.Ordinal)) return null;
        return Forbidden(request, "not one of nth's sites");
    }

    private WebResourceResponse Library(WebResourceRequest request, Uri uri) => SandboxLibrarySite.Answer(runtime.Libraries, request, uri, Report);

    private WebResourceResponse Own(WebResourceRequest request, Uri uri)
    {
        if (request.Method is not ("GET" or "HEAD")) return Status(405, "Method Not Allowed", SandboxHeaders.Blocked());
        if (Snapshot() is not { } current) return Status(404, "Not Found", SandboxHeaders.Blocked(), "This artifact no longer exists.");
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var catalog = runtime.Libraries.Catalog;
        var rewrites = new List<ArtifactCdnRewrite>();
        var title = current.Artifact.Title;
        (string Type, string Body)? page = current.Artifact.Kind switch
        {
            ArtifactKind.Project => ProjectFile(current, path, catalog, rewrites),
            ArtifactKind.Svg when path == ArtifactPages.SvgPath => ("image/svg+xml", current.Text ?? ""),
            _ when path is "" or "index.html" => ("text/html; charset=utf-8", SinglePage(current, title, catalog, rewrites)),
            _ => null,
        };
        foreach (var rewrite in rewrites.Where(r => r.IsMissing)) Report(rewrite.Original);
        if (page is not { } found) return Status(404, "Not Found", SandboxHeaders.Blocked(), "Not in this artifact: " + path);
        var csp = CspBuilder.Artifact(runtime.AllowedNetwork(current).ToList());
        var body = request.Method == "HEAD" ? [] : Encoding.UTF8.GetBytes(found.Body);
        return new WebResourceResponse(200, "OK", SandboxHeaders.Document(found.Type, csp), body);
    }

    internal static string SinglePage(ArtifactSnapshot current, string title, ArtifactLibraryCatalog catalog, List<ArtifactCdnRewrite>? rewrites)
    {
        var text = current.Text ?? "";
        return current.Artifact.Kind switch
        {
            ArtifactKind.Html => ArtifactPages.Html(text, catalog, rewrites),
            ArtifactKind.React => ArtifactPages.React(text, current.Artifact.Language == "tsx" ? "artifact.tsx" : "artifact.jsx", title, catalog, rewrites),
            ArtifactKind.Svg => ArtifactPages.Svg(title, catalog),
            ArtifactKind.Mermaid => ArtifactPages.Mermaid(text, title, catalog),
            ArtifactKind.Markdown => ArtifactPages.Document(ArtifactMarkdown.ToHtml(text), title, catalog),
            ArtifactKind.Code => ArtifactPages.Code(text, current.Artifact.Language, title, catalog),
            ArtifactKind.NthUi => ArtifactPages.Code(text, null, title, catalog),
            _ => throw new ArgumentOutOfRangeException(nameof(current)),
        };
    }

    private static (string, string)? ProjectFile(ArtifactSnapshot current, string path, ArtifactLibraryCatalog catalog, List<ArtifactCdnRewrite> rewrites)
    {
        var files = current.Files!;
        var entry = current.Version.Entry ?? ArtifactStore.DefaultEntry;
        if (path.Length == 0) path = entry;
        else if (path.EndsWith('/')) path += "index.html";
        if (path == entry && !ArtifactRuntime.IsHtml(entry) && files.ContainsKey(entry))
            return ("text/html; charset=utf-8", ArtifactPages.ProjectComponent(entry, files, current.Artifact.Title, catalog, rewrites));
        if (!files.TryGetValue(path, out var text)) return null;
        if (ArtifactRuntime.IsHtml(path)) return ("text/html; charset=utf-8", ArtifactPages.ProjectHtml(text, path, files, catalog, rewrites));
        var type = SandboxHeaders.ContentType(path);
        return (type, ArtifactPages.IsRewritable(path) ? catalog.RewriteCdn(text, rewrites) : text);
    }

    private void Report(string url)
    {
        if (url.Length == 0) return;
        bool first;
        lock (gate) first = missingReported.Add(url);
        if (!first) return;
        runtime.Log($"artifact {Id} asked for {url}, which nth has no offline copy of");
        runtime.Post(() => LibraryMissing?.Invoke(url));
    }

    private WebResourceResponse Forbidden(WebResourceRequest request, string why)
    {
        runtime.Log($"artifact {Id}: blocked {request.Kind} {Redact(request.Uri)} ({why})");
        return Status(403, "Forbidden", SandboxHeaders.Blocked(), "Blocked by nth: artifacts cannot reach this address.");
    }

    private static WebResourceResponse Status(int code, string reason, IReadOnlyList<KeyValuePair<string, string>> headers, string? text = null) =>
        new(code, reason, headers, text is null ? [] : Encoding.UTF8.GetBytes(text));

    /// <summary>An address for the log: its origin and path only (no query, which may carry what the page computed).</summary>
    internal static string Redact(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.GetLeftPart(UriPartial.Path) : url.Split('?', '#')[0];

    // ---- the view's events ----

    private void OnMessage(object? sender, WebMessage message) => bridge.Receive(message);

    private void OnNavigationStarting(object? sender, WebNavigationStarting e)
    {
        if (e.IsMainFrame) bridge.NewDocument();
    }

    private void OnBlocked(object? sender, WebSandboxNavigation e)
    {
        var web = Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
        if (e.IsUserInitiated && web)
        {
            runtime.Log($"artifact {Id}: a link to {Redact(e.Url)} was offered to the link setting");
            LinkRequested?.Invoke(e.Url);
        }
        else runtime.Log($"artifact {Id}: cancelled a {(e.IsNewWindow ? "new window" : e.IsMainFrame ? "navigation" : "frame navigation")} to {Redact(e.Url)}");
    }

    private void OnStoreChanged(ArtifactChange change)
    {
        if (change.Thread != Thread || change.Id != Id) return;
        lock (gate) snapshot = null;
    }

    private void OnGrantsChanged(ArtifactAddress artifact)
    {
        if (artifact != Artifact || Snapshot() is not { } current) return;
        // A network grant changes the page's CSP, which only a new document picks up.
        if (runtime.NetworkRequest(current).Hosts.Count > 0) runtime.Post(Reload);
    }

    private void OnThemeChanged() => runtime.Post(bridge.PushTheme);

    internal void RaiseReady(string title) => runtime.Post(() => Ready?.Invoke(title));

    internal void RaiseError(ArtifactRuntimeError error) => runtime.Post(() => ErrorReported?.Invoke(error));

    internal void RaiseModelStatus() => runtime.Post(() => ModelStatusChanged?.Invoke(runtime.Meter.Status(Artifact)));

    internal void RaiseModelUsed(ArtifactCompletionResult result) => runtime.Post(() => ModelUsed?.Invoke(result));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        View.WebMessageReceived -= OnMessage;
        View.NavigationStarting -= OnNavigationStarting;
        View.SandboxNavigationBlocked -= OnBlocked;
        runtime.Store.Changed -= OnStoreChanged;
        runtime.Data.GrantsChanged -= OnGrantsChanged;
        themeSource.ThemeChanged -= OnThemeChanged;
        bridge.Close();
    }
}
