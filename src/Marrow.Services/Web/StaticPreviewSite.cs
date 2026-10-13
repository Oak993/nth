using System.Runtime.InteropServices;
using System.Text;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.6; ED-NEW-web-w9-2. A synthetic preview site, read-only and rooted to one project.
public sealed class StaticPreviewSite : IWebResourceHandler
{
    private readonly string root;
    private readonly Func<string, string?> longPath;

    /// <param name="longPath">
    /// ED-NEW-secfinal-21: a path's real long form (what GetLongPathName answers: every 8.3 alias spelled out), or null when the file
    /// system cannot name it. The default asks Windows; a test passes a fake mapping for a volume that has no 8.3 names.
    /// </param>
    public StaticPreviewSite(string project, string root, Func<string, string?>? longPath = null)
    {
        this.root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        this.longPath = longPath ?? NativeLongPath;
        // SandboxSites supplies the same DNS-safe identity hashing used by the other synthetic sites.
        Origin = "https://p-" + SandboxSites.Label(project, 61) + ".invalid";
    }
    /// <summary>ED-NEW-web-w9cx-1: a file is answered whole from memory on the thread that asked, so a larger one is refused (413) and never read.</summary>
    public const long MaxFileBytes = 128L * 1024 * 1024;
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
            // ED-NEW-web-w9cx-1: a leading dot is a hidden file or folder (.env, .git, .npmrc, .ssh), never served, so a previewed page cannot read the project's secrets.
            if (segments.Any(s => s.StartsWith('.') || s.Contains(':', StringComparison.Ordinal) || s.Contains('\0', StringComparison.Ordinal))) return Error(403, "Forbidden");
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
            // ED-NEW-secfinal-21: the leading-dot rule above reads the text of the request, but Windows also answers a file's 8.3 alias
            // (/ENV~1 opens .env, /GIT~1/config opens .git/config). The path is resolved to its real long name, and the same hidden-name rule
            // runs on that; a segment that is not spelled exactly as the file system names it (any alias) is refused.
            if (RealSegments(path) is not { } real) return Error(404, "Not Found");
            var asked = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
            if (real.Count != asked.Length) return Error(403, "Forbidden");
            for (var i = 0; i < asked.Length; i++)
            {
                if (real[i].StartsWith('.') || !string.Equals(real[i], asked[i], StringComparison.OrdinalIgnoreCase)) return Error(403, "Forbidden");
            }
            if (new FileInfo(path).Length > MaxFileBytes) return Error(413, "Payload Too Large");
            var bytes = request.Method == "HEAD" ? [] : File.ReadAllBytes(path);
            return new WebResourceResponse(200, "OK", [new("Content-Type", Mime(path)), new("X-Content-Type-Options", "nosniff"), new("Cache-Control", "no-store")], bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Error(404, "Not Found"); }
    }

    /// <summary>
    /// ED-NEW-secfinal-21: the names, from the preview's folder down, that the file system gives the existing file or folder
    /// <paramref name="full"/> (a full path under the folder): its real long names, so an 8.3 alias comes back as the name it stands for.
    /// Null when the path cannot be resolved or does not resolve to somewhere under the folder.
    /// </summary>
    public IReadOnlyList<string>? RealSegments(string full)
    {
        try
        {
            var rootLong = longPath(root);
            var fullLong = longPath(Path.GetFullPath(full));
            if (rootLong is null || fullLong is null) return null;
            rootLong = rootLong.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullLong.StartsWith(rootLong + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return fullLong[(rootLong.Length + 1)..].Split(Path.DirectorySeparatorChar);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // GetLongPathName spells every 8.3 alias of an existing path out. The extended-length form keeps a deep path working.
    private static string? NativeLongPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return path;
        const string Extended = "\\\\?\\";
        const string ExtendedUnc = "\\\\?\\UNC\\";
        var extended = path.StartsWith(Extended, StringComparison.Ordinal) ? path
            : path.StartsWith("\\\\", StringComparison.Ordinal) ? ExtendedUnc + path[2..]
            : Extended + path;
        var buffer = new char[Math.Max(extended.Length + 64, 512)];
        while (true)
        {
            var length = GetLongPathNameW(extended, buffer, (uint)buffer.Length);
            if (length == 0) return null;
            if (length < buffer.Length)
            {
                var answer = new string(buffer, 0, (int)length);
                if (answer.StartsWith(ExtendedUnc, StringComparison.Ordinal)) return "\\\\" + answer[ExtendedUnc.Length..];
                return answer.StartsWith(Extended, StringComparison.Ordinal) ? answer[Extended.Length..] : answer;
            }
            buffer = new char[length + 1];
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetLongPathNameW(string lpszShortPath, [Out] char[] lpszLongPath, uint cchBuffer);

    private static WebResourceResponse Error(int code, string reason) => new(code, reason, [new("Content-Type", "text/plain; charset=utf-8")], Encoding.UTF8.GetBytes(reason));
    private static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8", ".css" => "text/css; charset=utf-8", ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" or ".map" => "application/json", ".svg" => "image/svg+xml", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif", ".webp" => "image/webp", ".ico" => "image/x-icon", ".woff" => "font/woff", ".woff2" => "font/woff2",
        ".wasm" => "application/wasm", ".txt" => "text/plain; charset=utf-8", _ => "application/octet-stream",
    };
}
