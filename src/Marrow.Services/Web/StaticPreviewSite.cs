using System.Text;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.6; ED-NEW-web-w9-2. A synthetic preview site, read-only and rooted to one project.
public sealed class StaticPreviewSite : IWebResourceHandler
{
    private readonly string root;
    public StaticPreviewSite(string project, string root)
    {
        this.root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // SandboxSites supplies the same DNS-safe identity hashing used by the other synthetic sites.
        Origin = "https://p-" + SandboxSites.Label(project, 61) + ".invalid";
    }
    public string Origin { get; }
    public string Root => root;
    public WebResourceResponse? Answer(WebResourceRequest request)
    {
        if (!request.Uri.StartsWith(Origin + "/", StringComparison.OrdinalIgnoreCase)) return Error(403, "Forbidden");
        if (request.Method is not ("GET" or "HEAD")) return Error(405, "Method Not Allowed");
        try
        {
            // Split the original encoded URL before Uri can normalize dot segments away.
            var encoded = request.Uri[(Origin.Length + 1)..].Split('?', '#')[0];
            var decoded = Uri.UnescapeDataString(encoded);
            var segments = decoded.Replace('\\', '/').Split('/');
            if (segments.Any(s => s is "." or ".." || s.Contains(':', StringComparison.Ordinal) || s.Contains('\0', StringComparison.Ordinal))) return Error(403, "Forbidden");
            var relative = decoded.Length == 0 ? "index.html" : decoded;
            var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return Error(403, "Forbidden");
            var current = root;
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return Error(403, "Forbidden");
            foreach (var segment in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current)) return Error(404, "Not Found");
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return Error(403, "Forbidden");
            }
            if (Directory.Exists(path)) return Error(404, "Not Found");
            var bytes = request.Method == "HEAD" ? [] : File.ReadAllBytes(path);
            return new WebResourceResponse(200, "OK", [new("Content-Type", Mime(path)), new("X-Content-Type-Options", "nosniff"), new("Cache-Control", "no-store")], bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Error(404, "Not Found"); }
    }
    private static WebResourceResponse Error(int code, string reason) => new(code, reason, [new("Content-Type", "text/plain; charset=utf-8")], Encoding.UTF8.GetBytes(reason));
    private static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8", ".css" => "text/css; charset=utf-8", ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" or ".map" => "application/json", ".svg" => "image/svg+xml", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif", ".webp" => "image/webp", ".ico" => "image/x-icon", ".woff" => "font/woff", ".woff2" => "font/woff2",
        ".wasm" => "application/wasm", ".txt" => "text/plain; charset=utf-8", _ => "application/octet-stream",
    };
}
