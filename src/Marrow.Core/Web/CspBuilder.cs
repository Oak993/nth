// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5, 7.6, 8.1; ED-NEW-web-w13-1). The Content-Security-Policy of every
// sandboxed response: the design's fixed policy for artifacts and inline widgets, widened only by exact domains the user
// allowed (an artifact's "Allow network"), and for MCP Apps by the domains the server declared in _meta.ui.csp that the
// user allowed, never wider than declared. No Swift counterpart.
using System.Net;
using System.Text;
using Marrow.Core.Localisation;

namespace Marrow.Core.Web;

/// <summary>An MCP App's declared <c>_meta.ui.csp</c> (MCP Apps, io.modelcontextprotocol/ui, 2026-01-26).</summary>
public sealed record McpUiCsp(
    IReadOnlyList<string> ConnectDomains,
    IReadOnlyList<string> ResourceDomains,
    IReadOnlyList<string> FrameDomains,
    IReadOnlyList<string> BaseUriDomains)
{
    public static McpUiCsp None { get; } = new([], [], [], []);

    /// <summary>True when the server declared any domain outside its own origin, which needs the user's OK first.</summary>
    public bool DeclaresAny => ConnectDomains.Count + ResourceDomains.Count + FrameDomains.Count + BaseUriDomains.Count > 0;
}

/// <summary>One declared source and why it was dropped (shown in the consent card's details).</summary>
public sealed record CspDroppedSource(string Source, string Reason);

/// <summary>The sources kept from a declaration, normalised, and the ones dropped.</summary>
public sealed record CspSourceList(IReadOnlyList<string> Kept, IReadOnlyList<CspDroppedSource> Dropped)
{
    public static CspSourceList Empty { get; } = new([], []);
}

public static class CspBuilder
{
    /// <summary>
    /// DESIGN 6.5, verbatim: what an artifact or an inline widget may load. Scripts and styles from itself, inline, and the
    /// library host; images and media from itself and data or blob URLs; fonts from the library host; nothing else.
    /// </summary>
    public const string ArtifactDefault =
        "default-src 'none'; script-src 'self' 'unsafe-inline' https://lib-nth.invalid; " +
        "style-src 'self' 'unsafe-inline' https://lib-nth.invalid; img-src 'self' data: blob:; " +
        "font-src https://lib-nth.invalid data:; connect-src 'none'; frame-src 'none'; form-action 'none'; " +
        "base-uri 'none'; worker-src 'none'; manifest-src 'none'; media-src 'self' data: blob:";

    /// <summary>The library host's own responses: scripts, styles and fonts that run nothing as a page.</summary>
    public const string Library = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// An artifact's policy. <paramref name="network"/> holds the exact hosts the user allowed for the current version
    /// ("Allow network"); they join connect-src and img-src as https sources, nothing else changes (WebRTC and navigation
    /// stay blocked by other means).
    /// </summary>
    public static string Artifact(IReadOnlyCollection<string>? network = null)
    {
        if (network is not { Count: > 0 }) return ArtifactDefault;
        var sources = network.Select(host => "https://" + host.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        var directives = Parse(ArtifactDefault);
        directives["connect-src"] = sources;
        directives["img-src"] = [.. directives["img-src"], .. sources];
        return Write(directives);
    }

    /// <summary>
    /// An MCP App's policy (DESIGN 7.6): the artifact policy, plus the declared sources the user allowed. Resource domains
    /// join script, style, image, font and media sources; connect domains connect-src; frame domains frame-src; base-URI
    /// domains base-uri. A source is used only when the server declared it and <paramref name="allowed"/> holds it, so
    /// the result is never wider than the declaration. Until the user allows, pass nothing and the directives stay closed.
    /// </summary>
    public static string McpApp(McpUiCsp declared, McpUiCsp? allowed, bool allowBroad)
    {
        if (allowed is null) return ArtifactDefault;
        var directives = Parse(ArtifactDefault);
        var resources = Granted(declared.ResourceDomains, allowed.ResourceDomains, allowBroad);
        var connect = Granted(declared.ConnectDomains, allowed.ConnectDomains, allowBroad);
        var frames = Granted(declared.FrameDomains, allowed.FrameDomains, allowBroad);
        var bases = Granted(declared.BaseUriDomains, allowed.BaseUriDomains, allowBroad);
        foreach (var name in new[] { "script-src", "style-src", "img-src", "font-src", "media-src" })
            if (resources.Count > 0) directives[name] = [.. directives[name], .. resources.Where(s => name == "script-src" || !s.StartsWith('\''))];
        if (connect.Count > 0) directives["connect-src"] = connect.Where(s => !s.StartsWith('\'')).ToList();
        if (frames.Count > 0) directives["frame-src"] = frames.Where(s => !s.StartsWith('\'')).ToList();
        if (bases.Count > 0) directives["base-uri"] = bases.Where(s => !s.StartsWith('\'')).ToList();
        foreach (var name in directives.Keys.ToList())
            if (directives[name].Count == 0) directives[name] = ["'none'"];
            else if (directives[name].Count > 1) directives[name] = directives[name].Where(s => s != "'none'").Distinct(StringComparer.Ordinal).ToList();
        return Write(directives);
    }

    /// <summary>
    /// Cleans declared sources. Kept: absolute http, https, ws and wss origins (with an optional path) and bare host
    /// names (taken as https), host names lower-cased. Dropped, with the reason: anything with a space, a semicolon, a
    /// comma or a quote that could inject a directive; other schemes; keywords; wildcards (<c>*</c>, <c>*.example.com</c>)
    /// and <c>'unsafe-eval'</c> unless <paramref name="allowBroad"/>; IP addresses when <paramref name="allowIp"/> is false.
    /// </summary>
    /// <remarks>ED-NEW-web-w26-4: localhost, *.local and the like, loopback, private and link-local addresses (WebPolicy.IsDevHost) are dropped unless <paramref name="allowBroad"/>, with <see cref="LocalNetworkReason"/>.</remarks>
    public static CspSourceList Clean(IEnumerable<string>? declared, bool allowBroad, bool allowIp = true)
    {
        if (declared is null) return CspSourceList.Empty;
        var kept = new List<string>();
        var dropped = new List<CspDroppedSource>();
        foreach (var raw in declared)
        {
            var text = raw?.Trim() ?? "";
            if (text.Length == 0) continue;
            var (source, reason) = CleanOne(text, allowBroad, allowIp);
            if (source is null) dropped.Add(new CspDroppedSource(text, reason!));
            else if (!kept.Contains(source, StringComparer.Ordinal)) kept.Add(source);
        }

        return new CspSourceList(kept, dropped);
    }

    /// <summary>Splits a policy into its directives, in order (names lower case).</summary>
    public static Dictionary<string, List<string>> Parse(string policy)
    {
        var directives = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var part in policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            directives[tokens[0].ToLowerInvariant()] = tokens.Skip(1).ToList();
        }

        return directives;
    }

    private static string Write(Dictionary<string, List<string>> directives)
    {
        var builder = new StringBuilder();
        foreach (var (name, sources) in directives)
        {
            if (builder.Length > 0) builder.Append("; ");
            builder.Append(name);
            foreach (var source in sources) builder.Append(' ').Append(source);
        }

        return builder.ToString();
    }

    private static List<string> Granted(IReadOnlyList<string> declared, IReadOnlyList<string> allowed, bool allowBroad)
    {
        var mine = Clean(declared, allowBroad).Kept;
        var yes = Clean(allowed, allowBroad).Kept.ToHashSet(StringComparer.Ordinal);
        return mine.Where(yes.Contains).ToList();
    }

    /// <summary>ED-NEW-web-w26-4: why a source on this computer or its local network needs broad access.</summary>
    public static string LocalNetworkReason => UiText.Get("mcpapps.reason.local_network");

    /// <summary>Why a wildcard source is dropped unless broad access is allowed.</summary>
    public static string WildcardReason => UiText.Get("mcpapps.reason.wildcard");

    /// <summary>Why 'unsafe-eval' is dropped unless broad access is allowed.</summary>
    public static string UnsafeEvalReason => UiText.Get("mcpapps.reason.unsafe_eval");

    /// <summary>Why a plain http or ws source to a site on the internet was dropped (ED-NEW-web-sec-mcpapps-4).</summary>
    public static string NotSecureReason => UiText.Get("mcpapps.reason.not_secure");

    private static (string? Source, string? Reason) CleanOne(string text, bool allowBroad, bool allowIp)
    {
        if (text.Any(c => char.IsWhiteSpace(c) || c is ';' or ',' or '"' or '\\' or '<' or '>'))
            return (null, UiText.Get("mcpapps.reason.not_single_source"));
        if (text.StartsWith('\''))
        {
            if (text == "'unsafe-eval'" && allowBroad) return (text, null);
            return (null, text == "'unsafe-eval'" ? UnsafeEvalReason : UiText.Get("mcpapps.reason.not_domain"));
        }

        if (text.Contains('\'', StringComparison.Ordinal)) return (null, UiText.Get("mcpapps.reason.not_single_source"));
        if (text == "*") return allowBroad ? (text, null) : (null, WildcardReason);
        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        var scheme = schemeEnd < 0 ? "https" : text[..schemeEnd].ToLowerInvariant();
        if (schemeEnd < 0 && text.Contains(':', StringComparison.Ordinal) && !HasPortOnly(text)) return (null, UiText.Get("mcpapps.reason.not_web_address"));
        if (scheme is not ("https" or "http" or "wss" or "ws")) return (null, UiText.Get("mcpapps.reason.not_web_address"));
        var rest = schemeEnd < 0 ? text : text[(schemeEnd + 3)..];
        if (rest.StartsWith("*.", StringComparison.Ordinal) || rest.Contains('*', StringComparison.Ordinal))
        {
            if (!allowBroad) return (null, WildcardReason);
            var after = rest.TrimStart('*', '.');
            return after.Length > 0 && Uri.CheckHostName(after.Split('/', ':')[0]) == UriHostNameType.Dns
                ? (scheme + "://*." + after.ToLowerInvariant(), null) : (null, WildcardReason);
        }

        if (!Uri.TryCreate(scheme + "://" + rest, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return (null, UiText.Get("mcpapps.reason.not_web_address"));
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            if (!allowIp) return (null, UiText.Get("mcpapps.reason.ip_address"));
        }
        else if (uri.HostNameType != UriHostNameType.Dns || IPAddress.TryParse(uri.Host, out _)) return (null, UiText.Get("mcpapps.reason.not_web_address"));

        // + ED-NEW-web-w26-4 (W26 security review): a host on this computer or its local network is broad access too.
        if (!allowBroad && WebPolicy.IsDevHost(uri.Host)) return (null, LocalNetworkReason);
        // + ED-NEW-web-sec-mcpapps-4 (web surfaces security review, L7): plain http and ws reach a host on this computer or its
        // local network only (which is broad access already); to a site on the internet nth's own check refused them while
        // the CSP let them through. Dropped here, so the card, the CSP and nth's check say the same.
        if (scheme is "http" or "ws" && !WebPolicy.IsDevHost(uri.Host)) return (null, NotSecureReason);
        var host = uri.IdnHost.ToLowerInvariant();
        var port = uri.IsDefaultPort ? "" : ":" + uri.Port;
        var path = uri.AbsolutePath == "/" && !rest.EndsWith('/') ? "" : uri.AbsolutePath;
        return (scheme + "://" + host + port + (path == "/" ? "" : path), null);
    }

    private static bool HasPortOnly(string text)
    {
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var end = text.IndexOf('/', StringComparison.Ordinal);
        var port = end < 0 ? text[(colon + 1)..] : text[(colon + 1)..end];
        return port.Length > 0 && port.All(char.IsAsciiDigit);
    }
}
