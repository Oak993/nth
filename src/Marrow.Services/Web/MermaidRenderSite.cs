// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "inside a hidden, sandboxed artifacts-profile view (no network, no bridge)"; 6.5 for the
// sandbox; ED-NEW-web-w16c-1). No Swift counterpart. The one site the hidden diagram renderer runs on: https://w-nth-diagrams.invalid,
// answered by nth itself. It serves one page (the vendored Mermaid and nth's runner) and, from the library host, exactly one file
// (the Mermaid script). Every other address, whether another sandbox's, the network's or a file of the library host, is refused,
// so the page can neither fetch nor beacon anything; there is no bridge script, so it cannot message nth either. The page's policy
// is the artifact default: scripts from itself and the library host, no connection of any kind.
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

public sealed class MermaidRenderSite(ArtifactLibraryFiles libraries) : IWebResourceHandler
{
    /// <summary>The runner file embedded in this assembly.</summary>
    public const string RunnerFile = "diagram-runner.js";

    private readonly object gate = new();
    private readonly List<string> refused = [];
    private readonly Logger log = new(Logger.Subsystem, "web");

    /// <summary>The renderer's host (a widget-shaped name, so the sandbox's hardening and isolation apply to it).</summary>
    public static string Host { get; } = SandboxSites.WidgetHost("nth-diagrams");

    public static string Origin { get; } = SandboxSites.Origin(Host);

    public static string Address { get; } = Origin + "/";

    /// <summary>The requests refused so far (address and why), for tests and the log.</summary>
    public IReadOnlyList<string> Refused
    {
        get
        {
            lock (gate) return [.. refused];
        }
    }

    /// <summary>The path of the Mermaid script on the library host, or null when the build has none.</summary>
    public string? MermaidPath => libraries.Catalog.ById("mermaid")?.Script;

    /// <summary>The page: Mermaid from the library host, then nth's runner inline.</summary>
    public string Page()
    {
        var builder = new StringBuilder("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"color-scheme\" content=\"light dark\">\n<title>nth diagrams</title>\n");
        if (MermaidPath is { } path) builder.Append("<script src=\"").Append(ArtifactLibraryCatalog.Url(path)).Append("\"></script>\n");
        builder.Append("</head>\n<body>\n<script>\n").Append(SandboxScripts.Read(RunnerFile)).Append("\n</script>\n</body>\n</html>\n");
        return builder.ToString();
    }

    public WebResourceResponse? Answer(WebResourceRequest request)
    {
        if (!Uri.TryCreate(request.Uri, UriKind.Absolute, out var uri)) return Refuse(request, "not an address");
        // The page's own data (a data: font, a blob), governed by the policy; nothing in it can leave.
        if (uri.Scheme is "data" or "blob") return null;
        var host = uri.IdnHost.ToLowerInvariant();
        var secure = uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort;
        if (!secure) return Refuse(request, "not a secure address of nth's");
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (host == Host) return Own(request, path);
        if (host == SandboxSites.LibraryHost) return Library(request, path);
        return Refuse(request, SandboxSites.SurfaceOf(host) is not null ? "another sandbox's site" : "not one of nth's sites");
    }

    private WebResourceResponse Own(WebResourceRequest request, string path)
    {
        if (request.Method is not ("GET" or "HEAD")) return Status(405, "Method Not Allowed", "Method Not Allowed");
        if (path is not ("" or "index.html")) return Status(404, "Not Found", "Not in the renderer: " + path);
        var body = request.Method == "HEAD" ? [] : Encoding.UTF8.GetBytes(Page());
        return new WebResourceResponse(200, "OK", SandboxHeaders.Document("text/html; charset=utf-8", CspBuilder.ArtifactDefault), body);
    }

    private WebResourceResponse Library(WebResourceRequest request, string path)
    {
        if (request.Method is not ("GET" or "HEAD")) return Status(405, "Method Not Allowed", "Method Not Allowed");
        // One file: the Mermaid script. Fonts, the other libraries and nth's runtime files are not the renderer's to load.
        if (MermaidPath is not { } allowed || path != allowed || libraries.Read(path) is not { } bytes)
            return Refuse(request, "not the renderer's library file");
        return new WebResourceResponse(200, "OK", SandboxHeaders.Library(SandboxHeaders.ContentType(path)), request.Method == "HEAD" ? [] : bytes);
    }

    private WebResourceResponse Refuse(WebResourceRequest request, string why)
    {
        var address = ArtifactMount.Redact(request.Uri);
        lock (gate)
        {
            if (refused.Count < 200) refused.Add(address + " (" + why + ")");
        }
        log.Info($"web: diagram renderer: blocked {request.Kind} {address} ({why})");
        return Status(403, "Forbidden", "Blocked by nth: the diagram renderer cannot reach this address.");
    }

    private static WebResourceResponse Status(int code, string reason, string text) =>
        new(code, reason, SandboxHeaders.Blocked(), Encoding.UTF8.GetBytes(text));
}
