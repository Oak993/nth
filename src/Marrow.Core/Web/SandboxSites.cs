// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5, 8.1; ED-NEW-web-w13-1). The synthetic sites sandboxed content runs on.
// Each artifact, MCP App and inline HTML widget is its own site: "invalid" is not on the Public Suffix List, so its
// default rule makes "invalid" a public suffix and every "<label>.invalid" its own registrable domain, which keeps each
// surface in its own renderer process under site isolation and apart from every other's storage. ".invalid" never
// resolves on the network (RFC 6761); nth answers these hosts itself. No Swift counterpart.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Marrow.Core.Web;

/// <summary>The kinds of sandboxed surface, each with its own host prefix.</summary>
public enum SandboxSurface
{
    /// <summary>A Tabula artifact: <c>https://a-&lt;chat&gt;-&lt;id&gt;.invalid/</c>.</summary>
    Artifact,

    /// <summary>An inline HTML widget in the thread: <c>https://w-&lt;id&gt;.invalid/</c>.</summary>
    Widget,

    /// <summary>An MCP App: <c>https://m-&lt;server&gt;-&lt;hash&gt;.invalid/</c>.</summary>
    McpApp,

    /// <summary>The shared offline library host, <c>https://lib-nth.invalid/</c>.</summary>
    Library,
}

public static class SandboxSites
{
    /// <summary>The top-level domain every sandboxed host sits under.</summary>
    public const string TopLevel = "invalid";

    /// <summary>The library host; artifacts load the vendored libraries from it (it sends CORP cross-origin).</summary>
    public const string LibraryHost = "lib-nth.invalid";

    public const string LibraryOrigin = "https://" + LibraryHost;

    /// <summary>A DNS label holds at most 63 characters.</summary>
    public const int MaxLabelLength = 63;

    /// <summary>
    /// An artifact's host. Ids are unique only within a chat (ED-NEW-web-w12-1 item 9), so the label carries the chat too:
    /// "a-", ten hex digits of the SHA-256 of the chat's id, "-", the artifact's id (at most 48 characters, so the label
    /// stays within 63).
    /// </summary>
    public static string ArtifactHost(Guid thread, string id)
    {
        var clean = Label(id, 48);
        return "a-" + Hex(thread.ToString("D", CultureInfo.InvariantCulture), 5) + "-" + clean + "." + TopLevel;
    }

    /// <summary>An inline widget's host: "w-" and the widget id as a label (hashed when it would not fit).</summary>
    public static string WidgetHost(string id) => "w-" + Label(id, MaxLabelLength - 2) + "." + TopLevel;

    /// <summary>An MCP App's host: "m-", the server's name as a label, "-", eight hex digits of its resource's address.</summary>
    public static string AppHost(string server, string resourceUri) =>
        "m-" + Label(server, 40) + "-" + Hex(server + "\n" + resourceUri, 4) + "." + TopLevel;

    /// <summary>The https origin of a host, no trailing slash.</summary>
    public static string Origin(string host) => "https://" + host;

    /// <summary>The surface a host belongs to, or null for any host nth does not answer.</summary>
    public static SandboxSurface? SurfaceOf(string? host)
    {
        if (host is null) return null;
        var lower = host.ToLowerInvariant();
        if (lower == LibraryHost) return SandboxSurface.Library;
        if (!lower.EndsWith("." + TopLevel, StringComparison.Ordinal)) return null;
        var label = lower[..^(TopLevel.Length + 1)];
        if (label.Contains('.', StringComparison.Ordinal) || !IsLabel(label)) return null;
        return label[..2] switch
        {
            "a-" => SandboxSurface.Artifact,
            "w-" => SandboxSurface.Widget,
            "m-" => SandboxSurface.McpApp,
            _ => null,
        };
    }

    /// <summary>True for an https origin (no path) of a sandboxed surface or the library host.</summary>
    public static bool IsSandboxOrigin(string? origin) =>
        origin is not null && Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && uri.AbsolutePath == "/" && SurfaceOf(uri.Host) is not null && origin.TrimEnd('/').Equals(Origin(uri.Host), StringComparison.OrdinalIgnoreCase);

    /// <summary>The origin ("https://host") of an address, or null when it is not an absolute http(s) address.</summary>
    public static string? OriginOf(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;
        return uri.IsDefaultPort ? uri.Scheme + "://" + uri.IdnHost.ToLowerInvariant() : uri.Scheme + "://" + uri.IdnHost.ToLowerInvariant() + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The registrable domain (site) of a sandboxed host under the Public Suffix List's default rule: the host itself,
    /// since "invalid" is not listed and so counts as a public suffix.
    /// </summary>
    public static string? Site(string host) => Marrow.Core.Port.ModelsPalette.DomainVote.Registrable("https://" + host + "/");

    /// <summary>
    /// Text as one DNS label: lower case ASCII letters and digits, every other run one hyphen, no hyphen at either end. A
    /// result longer than <paramref name="max"/>, or an empty one, ends in (or is) eight hex digits of the text's hash, so
    /// two different texts never meet after cutting.
    /// </summary>
    public static string Label(string text, int max)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        var label = builder.ToString().Trim('-');
        if (label.Length == 0) return Hex(text, 4);
        if (label.Length <= max) return label;
        var keep = Math.Max(1, max - 9);
        return label[..Math.Min(keep, label.Length)].TrimEnd('-') + "-" + Hex(text, 4);
    }

    private static bool IsLabel(string label) =>
        label.Length is >= 3 and <= MaxLabelLength && label[0] != '-' && label[^1] != '-'
        && label.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static string Hex(string text, int bytes) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, bytes).ToLowerInvariant();
}
