// PORT-ONLY: web surfaces W19, the inline HTML widget host (DESIGN.md 7.1 item 2, 7.3 html_widget, 7.6 "Inline", 6.5; ED-NEW-web-w19-3).
// A free-form widget from an answer (the html_widget tool or a fenced nth-html block, W24) is a sandboxed page in the thread:
// its own site https://w-<id>.invalid/, the artifacts profile, the same hardening as artifacts, nth's offline libraries,
// and no network, no chat, no files: of window.nth only the theme and the ready and error reports answer, everything else
// says it is unavailable here. Its height comes from the page (widget-size.js), clamped by the thread (InlineWebLive.Height).
// The site's library host is shared with artifacts (SandboxLibrarySite). No Swift counterpart.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>One inline HTML widget in one view.</summary>
public sealed class InlineWidgetMount : IWebResourceHandler, IDisposable
{
    /// <summary>What a widget hears when it asks for anything but the theme.</summary>
    public const string UnavailableMessage = "Not available in an inline widget.";

    private readonly ArtifactLibraryFiles libraries;
    private readonly Func<ArtifactTheme> theme;
    private readonly Action<Action> post;
    private readonly Action<string> log;
    private readonly HashSet<string> missingReported = new(StringComparer.Ordinal);
    private bool disposed;

    public InlineWidgetMount(WebSurfaceView view, string id, string html, ArtifactLibraryFiles libraries, Func<ArtifactTheme>? theme = null,
        Action<Action>? post = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (view.Kind != WebSurfaceKind.InlineWidget) throw new ArgumentException("An inline widget runs in an InlineWidget view.", nameof(view));
        View = view;
        Id = id;
        Html = html ?? "";
        this.libraries = libraries;
        this.theme = theme ?? (static () => ArtifactTheme.Default);
        this.post = post ?? (static action => action());
        this.log = log ?? (static _ => { });
        SiteHost = SandboxSites.WidgetHost(id);
        Origin = SandboxSites.Origin(SiteHost);
        view.SetSandbox(new WebSandbox(Origin, this, [.. SandboxScripts.DocumentScripts, SandboxScripts.WidgetSize]));
        view.WebMessageReceived += OnMessage;
        view.SandboxNavigationBlocked += OnBlocked;
        view.Navigate(Address);
    }

    public WebSurfaceView View { get; }

    public string Id { get; }

    public string Html { get; }

    public string SiteHost { get; }

    public string Origin { get; }

    public string Address => Origin + "/";

    /// <summary>The height the page asked for, in DIPs (before the thread clamps it).</summary>
    public double? RequestedHeight { get; private set; }

    /// <summary>The page's height changed.</summary>
    public event Action<double>? SizeRequested;

    /// <summary>The page loaded and reported ready (its title).</summary>
    public event Action<string>? Ready;

    /// <summary>The page reported an error it did not catch.</summary>
    public event Action<ArtifactRuntimeError>? ErrorReported;

    /// <summary>The user clicked a web link in the widget: it goes to the link setting.</summary>
    public event Action<string>? LinkRequested;

    // ---- the site ----

    public WebResourceResponse? Answer(WebResourceRequest request)
    {
        if (!Uri.TryCreate(request.Uri, UriKind.Absolute, out var uri)) return Forbidden(request, "not an address");
        if (uri.Scheme is "data" or "blob") return null;
        var host = uri.IdnHost.ToLowerInvariant();
        var secure = uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort;
        if (secure && host == SandboxSites.LibraryHost) return SandboxLibrarySite.Answer(libraries, request, uri, Report);
        if (secure && host == SiteHost) return Own(request, uri);
        return Forbidden(request, SandboxSites.SurfaceOf(host) is not null ? "another sandbox's site" : "widgets have no network");
    }

    private WebResourceResponse Own(WebResourceRequest request, Uri uri)
    {
        if (request.Method is not ("GET" or "HEAD")) return SandboxLibrarySite.Status(405, "Method Not Allowed", SandboxHeaders.Blocked());
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (path is not ("" or "index.html")) return SandboxLibrarySite.Status(404, "Not Found", SandboxHeaders.Blocked(), "Not in this widget: " + path);
        var rewrites = new List<ArtifactCdnRewrite>();
        var page = ArtifactPages.Html(Html, libraries.Catalog, rewrites);
        foreach (var rewrite in rewrites.Where(r => r.IsMissing)) Report(rewrite.Original);
        var headers = SandboxHeaders.Document("text/html; charset=utf-8", CspBuilder.Artifact());
        return new WebResourceResponse(200, "OK", headers, request.Method == "HEAD" ? [] : Encoding.UTF8.GetBytes(page));
    }

    private WebResourceResponse Forbidden(WebResourceRequest request, string why)
    {
        log($"web: widget {Id}: blocked {request.Method} {ArtifactMount.Redact(request.Uri)} ({why})");
        return SandboxLibrarySite.Status(403, "Forbidden", SandboxHeaders.Blocked(), "Blocked by nth: widgets cannot reach this address.");
    }

    private void Report(string url)
    {
        if (url.Length == 0) return;
        lock (missingReported)
            if (!missingReported.Add(url)) return;
        log($"web: widget {Id} asked for {url}, which nth has no offline copy of");
    }

    // ---- window.nth, the widget's part ----

    private void OnMessage(object? sender, WebMessage message)
    {
        if (disposed) return;
        if (!string.Equals(message.SourceOrigin?.TrimEnd('/'), Origin, StringComparison.OrdinalIgnoreCase))
        {
            log($"web: widget {Id}: ignored a message from {message.SourceOrigin}");
            return;
        }

        JsonObject root;
        try
        {
            if (JsonNode.Parse(message.Json) is not JsonObject parsed) return;
            root = parsed;
        }
        catch (JsonException)
        {
            return;
        }

        if (Number(root["nth"]) != 1 || McpJson.Text(root["type"]) is not { } type) return;
        var session = McpJson.Text(root["s"]) is { Length: > 0 and <= 64 } s ? s : "";
        switch (type)
        {
            case "ready":
                var title = McpJson.Text(root["title"]) ?? "";
                post(() => Ready?.Invoke(title));
                return;
            case "error":
                var error = new ArtifactRuntimeError(Cap(McpJson.Text(root["message"]) ?? "Unknown error", 2000), Cap(McpJson.Text(root["stack"]) ?? "", 8000));
                post(() => ErrorReported?.Invoke(error));
                return;
            case "size":
                if (Number(root["height"]) is { } height && height > 0 && height < 100_000 && height != RequestedHeight)
                {
                    RequestedHeight = height;
                    post(() => SizeRequested?.Invoke(height));
                }

                return;
        }

        if (Number(root["id"]) is not { } id || id <= 0) return;
        if (type == "theme.get") Reply(session, (long)id, new JsonObject { ["ok"] = true, ["result"] = Theme() });
        else Reply(session, (long)id, new JsonObject { ["ok"] = false, ["error"] = new JsonObject { ["code"] = ArtifactBridgeErrors.Unavailable, ["message"] = UnavailableMessage } });
    }

    /// <summary>Tells the page nth's theme changed.</summary>
    public void PushTheme() => PostJson(new JsonObject { ["nth"] = 1, ["event"] = "theme", ["theme"] = Theme() });

    private JsonObject Theme()
    {
        var current = theme();
        var variables = new JsonObject();
        foreach (var (name, value) in current.Variables)
            if (name.StartsWith("--nth-", StringComparison.Ordinal)) variables[name] = value;
        return new JsonObject { ["dark"] = current.Dark, ["variables"] = variables };
    }

    private void Reply(string session, long id, JsonObject body)
    {
        body["nth"] = 1;
        body["s"] = session;
        body["id"] = id;
        PostJson(body);
    }

    private void PostJson(JsonObject body)
    {
        var json = body.ToJsonString();
        post(() =>
        {
            if (!disposed) View.Engine?.PostMessage(json);
        });
    }

    private void OnBlocked(object? sender, WebSandboxNavigation e)
    {
        if (e.IsUserInitiated && Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            log($"web: widget {Id}: a link to {ArtifactMount.Redact(e.Url)} was offered to the link setting");
            LinkRequested?.Invoke(e.Url);
        }
    }

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;

    private static string Cap(string text, int max) => text.Length <= max ? text : text[..max];

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        View.WebMessageReceived -= OnMessage;
        View.SandboxNavigationBlocked -= OnBlocked;
    }
}

/// <summary>DESIGN 6.5: nth's library host as artifacts and widgets see it: the offline libraries and nth's runtime files.</summary>
internal static class SandboxLibrarySite
{
    public static WebResourceResponse Answer(ArtifactLibraryFiles libraries, WebResourceRequest request, Uri uri, Action<string> reportMissing)
    {
        if (request.Method is not ("GET" or "HEAD")) return Status(405, "Method Not Allowed", SandboxHeaders.Blocked());
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        string? text = null;
        if (path.StartsWith(ArtifactLibraryCatalog.RuntimePrefix, StringComparison.Ordinal))
        {
            var name = path[ArtifactLibraryCatalog.RuntimePrefix.Length..];
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            text = name switch
            {
                "loader.js" => SandboxScripts.Loader(libraries.Catalog),
                "missing.js" => Missing(query["u"] ?? "", script: true, reportMissing),
                "missing.css" => Missing(query["u"] ?? "", script: false, reportMissing),
                _ when SandboxScripts.RuntimeFiles.Contains(name) => SandboxScripts.Read(name),
                _ => null,
            };
        }

        var bytes = text is not null ? Encoding.UTF8.GetBytes(text) : libraries.Read(path);
        if (bytes is null) return Status(404, "Not Found", SandboxHeaders.Blocked(), "Not in nth's offline libraries: " + path);
        return new WebResourceResponse(200, "OK", SandboxHeaders.Library(SandboxHeaders.ContentType(path)), request.Method == "HEAD" ? [] : bytes);
    }

    private static string Missing(string url, bool script, Action<string> report)
    {
        report(url);
        return script ? SandboxScripts.Missing(url) : "/* nth has no offline copy of " + url.Replace("*/", "* /", StringComparison.Ordinal) + " */\n";
    }

    public static WebResourceResponse Status(int code, string reason, IReadOnlyList<KeyValuePair<string, string>> headers, string? text = null) =>
        new(code, reason, headers, text is null ? [] : Encoding.UTF8.GetBytes(text));
}
