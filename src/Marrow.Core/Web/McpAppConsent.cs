// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "Declared domains need the user's OK", "ui/message is attributed", 5.5; ED-NEW-web-w19-2).
// What an MCP App must ask the user before it gets it, and what the user said:
//   domains   a widget that declares connect, resource, frame or base-URI domains asks once, on first mount: "shipyard's
//             Release board wants to reach: api.shipyard.dev, cdn.shipyard.dev", Allow for shipyard / Not now. Until
//             allowed those directives stay empty. Wildcards and 'unsafe-eval' are dropped unless the user turns on "Allow
//             broad access" in the card's details. A changed list asks again (the grant is bound to the list's fingerprint).
//   messages  the first ui/message per app and chat asks "Let shipyard post messages in this chat?".
//   tools     "Always for shipyard" on a tool call card is remembered per server and tool.
//   window    the pop-out window's size, per server (DESIGN 4 table: "App pane and pop-out window ... per server").
// Everything here is plain data; the services keep it in a JSON file. No Swift counterpart.
using System.Security.Cryptography;
using System.Text;

namespace Marrow.Core.Web;

/// <summary>The consent card's content for one view's declared domains.</summary>
public sealed record McpAppDomainRequest(
    string Server,
    string Title,
    IReadOnlyList<string> Domains,
    IReadOnlyList<CspDroppedSource> Broad,
    IReadOnlyList<CspDroppedSource> Refused,
    string Fingerprint)
{
    /// <summary>The card's sentence: "shipyard's Release board wants to reach: api.shipyard.dev, cdn.shipyard.dev".</summary>
    public string Sentence => McpAppDomains.Sentence(Server, Title, Domains.Count > 0 ? Domains : Broad.Select(b => McpAppDomains.Display(b.Source)).ToList());

    /// <summary>"Allow for shipyard".</summary>
    public string AllowText => "Allow for " + Server;

    /// <summary>True when the declaration holds wildcards or 'unsafe-eval' that only "Allow broad access" would let through.</summary>
    public bool HasBroad => Broad.Count > 0;
}

/// <summary>The user's answer to a domain card.</summary>
public enum McpAppDomainDecision
{
    NotNow,
    Allow,

    /// <summary>Allow, with "Allow broad access" turned on in the details.</summary>
    AllowBroad,
}

/// <summary>What the user allowed for one view's declaration.</summary>
public sealed record McpAppDomainGrant(string Fingerprint, bool Broad);

/// <summary>The user's answer to a tool card ("shipyard wants to run promote_release").</summary>
public enum McpAppToolDecision
{
    Deny,
    AllowOnce,
    AlwaysForServer,
}

public static class McpAppDomains
{
    /// <summary>The words "Allow broad access" and the risk they name, shown in the card's details.</summary>
    public const string BroadAccessTitle = "Allow broad access";

    public const string BroadAccessRisk =
        "Wildcard sites let this app reach any site that matches them, and 'unsafe-eval' lets it run text as code. " +
        "Anything it shows, computes or you type into it can be sent to those sites.";

    public const string NotNowText = "Not now";

    /// <summary>The consent request for a declaration, or null when it declares nothing outside the view's own origin.</summary>
    public static McpAppDomainRequest? Request(string server, string title, McpUiCsp declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        if (!declared.DeclaresAny) return null;
        var all = declared.ConnectDomains.Concat(declared.ResourceDomains).Concat(declared.FrameDomains).Concat(declared.BaseUriDomains).ToList();
        var strict = CspBuilder.Clean(all, allowBroad: false);
        var broad = CspBuilder.Clean(all, allowBroad: true);
        var broadOnly = strict.Dropped.Where(d => d.Reason is "a wildcard" or "lets the page run text as code").ToList();
        var refused = broad.Dropped.ToList();
        var domains = strict.Kept.Select(Display).Distinct(StringComparer.Ordinal).ToList();
        if (domains.Count == 0 && broadOnly.Count == 0) return null;
        return new McpAppDomainRequest(server, title, domains, broadOnly, refused, Fingerprint(declared));
    }

    /// <summary>"shipyard's Release board wants to reach: a, b".</summary>
    public static string Sentence(string server, string title, IReadOnlyList<string> domains) =>
        $"{server}'s {(string.IsNullOrWhiteSpace(title) ? "app" : title)} wants to reach: {string.Join(", ", domains)}";

    /// <summary>A source as the card names it: the host (and port, and path when not just "/"), without the https scheme.</summary>
    public static string Display(string source)
    {
        if (source.StartsWith("https://", StringComparison.Ordinal)) source = source[8..];
        return source.TrimEnd('/');
    }

    /// <summary>The declaration's fingerprint: any change to any list (order aside) asks again.</summary>
    public static string Fingerprint(McpUiCsp declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        static string Part(string name, IEnumerable<string> list) =>
            name + "=" + string.Join(" ", list.Select(s => s.Trim()).Where(s => s.Length > 0).Order(StringComparer.Ordinal));
        var text = string.Join("\n",
            Part("connect", declared.ConnectDomains), Part("resource", declared.ResourceDomains),
            Part("frame", declared.FrameDomains), Part("base", declared.BaseUriDomains));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>The allow-list a grant stands for: the declaration itself when the fingerprint still matches, else nothing.</summary>
    public static McpUiCsp? Allowed(McpUiCsp declared, McpAppDomainGrant? grant) =>
        grant is { } given && given.Fingerprint == Fingerprint(declared) ? declared : null;

    /// <summary>
    /// The hosts a view may load from or connect to after the grant, for the request filter (the CSP is the engine's own
    /// check; this is nth's). Exact hosts as "host" (or "host:port"); a broad wildcard as "*.example.com"; "*" as itself.
    /// </summary>
    public static IReadOnlyList<string> Hosts(McpUiCsp declared, McpAppDomainGrant? grant)
    {
        if (Allowed(declared, grant) is not { } allowed) return [];
        var sources = CspBuilder.Clean(allowed.ConnectDomains.Concat(allowed.ResourceDomains).Concat(allowed.FrameDomains), grant!.Broad).Kept;
        var hosts = new List<string>();
        foreach (var source in sources)
        {
            if (source.StartsWith('\'')) continue;
            if (source == "*") { hosts.Add("*"); continue; }
            var rest = source[(source.IndexOf("://", StringComparison.Ordinal) + 3)..];
            var host = rest.Split('/')[0].ToLowerInvariant();
            if (!hosts.Contains(host, StringComparer.Ordinal)) hosts.Add(host);
        }

        return hosts;
    }

    /// <summary>True when <paramref name="host"/> (with its port when not 443) matches one of <paramref name="allowed"/>.</summary>
    public static bool Matches(string host, IReadOnlyList<string> allowed)
    {
        host = host.ToLowerInvariant();
        foreach (var entry in allowed)
        {
            if (entry == "*") return true;
            if (entry.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = entry[1..];
                if (host.EndsWith(suffix, StringComparison.Ordinal) && host.Length > suffix.Length) return true;
                continue;
            }

            if (entry == host) return true;
        }

        return false;
    }
}

/// <summary>Everything the user decided for one MCP server's apps.</summary>
public sealed record McpAppServerGrants
{
    /// <summary>Domain grants by UI resource address.</summary>
    public IReadOnlyDictionary<string, McpAppDomainGrant> Domains { get; init; } = new Dictionary<string, McpAppDomainGrant>(StringComparer.Ordinal);

    /// <summary>The chats (thread ids) where the user let this server's apps post messages.</summary>
    public IReadOnlySet<Guid> MessageChats { get; init; } = new HashSet<Guid>();

    /// <summary>Tools the user allowed always ("Always for shipyard").</summary>
    public IReadOnlySet<string> AlwaysTools { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The pop-out window's size (DIP), or null for the default.</summary>
    public (double Width, double Height)? WindowSize { get; init; }
}
