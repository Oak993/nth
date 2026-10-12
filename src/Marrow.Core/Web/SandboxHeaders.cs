// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5, 8.4; ED-NEW-web-w13-1). The headers nth sends on every sandboxed response
// it answers: the policy, origin-keyed agent clusters, a same-origin opener and resource policy (the library host sends
// cross-origin so artifacts can load it), no MIME sniffing, no referrer, no DNS prefetch, and a Permissions-Policy that
// denies every device, fullscreen, pointer lock, payment, passkeys and clipboard writes. No Swift counterpart.
namespace Marrow.Core.Web;

public static class SandboxHeaders
{
    /// <summary>
    /// DESIGN 6.5 and the sandboxed column of 8.4: every powerful feature off. Unknown names are ignored by the engine,
    /// so the list also carries features newer runtimes add.
    /// </summary>
    public const string PermissionsPolicy =
        "accelerometer=(), ambient-light-sensor=(), bluetooth=(), camera=(), clipboard-read=(), clipboard-write=(), " +
        "display-capture=(), fullscreen=(), gamepad=(), geolocation=(), gyroscope=(), hid=(), " +
        "identity-credentials-get=(), idle-detection=(), local-fonts=(), magnetometer=(), microphone=(), " +
        "midi=(), otp-credentials=(), payment=(), pointer-lock=(), publickey-credentials-create=(), " +
        "publickey-credentials-get=(), screen-wake-lock=(), serial=(), speaker-selection=(), storage-access=(), usb=(), " +
        "web-share=(), window-management=(), xr-spatial-tracking=(), browsing-topics=(), attribution-reporting=(), " +
        "compute-pressure=(), direct-sockets=()";

    /// <summary>The headers of a sandboxed page or file served from its own site.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Document(string contentType, string csp) =>
    [
        new("Content-Type", contentType),
        new("Content-Security-Policy", csp),
        new("Origin-Agent-Cluster", "?1"),
        new("Cross-Origin-Opener-Policy", "same-origin"),
        new("Cross-Origin-Resource-Policy", "same-origin"),
        new("X-Content-Type-Options", "nosniff"),
        new("Referrer-Policy", "no-referrer"),
        new("Permissions-Policy", PermissionsPolicy),
        new("X-DNS-Prefetch-Control", "off"),
        new("Cache-Control", "no-store"),
    ];

    /// <summary>
    /// The library host's headers: CORP cross-origin and CORS for every origin, since module scripts and fonts are
    /// fetched in CORS mode from each artifact's own site. The files never change for a given build.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Library(string contentType) =>
    [
        new("Content-Type", contentType),
        new("Content-Security-Policy", CspBuilder.Library),
        new("Origin-Agent-Cluster", "?1"),
        new("Cross-Origin-Resource-Policy", "cross-origin"),
        new("Access-Control-Allow-Origin", "*"),
        new("X-Content-Type-Options", "nosniff"),
        new("Referrer-Policy", "no-referrer"),
        new("X-DNS-Prefetch-Control", "off"),
        new("Cache-Control", "public, max-age=31536000, immutable"),
    ];

    /// <summary>The answer to a request nth blocks: no body, nothing sniffable.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Blocked() =>
    [
        new("Content-Type", "text/plain; charset=utf-8"),
        new("X-Content-Type-Options", "nosniff"),
        new("Cache-Control", "no-store"),
    ];

    /// <summary>The media type nth serves a path with (by extension); unknown types are served as bytes.</summary>
    public static string ContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs" or ".cjs" or ".jsx" or ".tsx" or ".ts" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" or ".map" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".ico" => "image/x-icon",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".txt" or ".md" or ".csv" or ".tsv" or ".mmd" or ".nthui" => "text/plain; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            ".wasm" => "application/wasm",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => "application/octet-stream",
        };
    }
}
