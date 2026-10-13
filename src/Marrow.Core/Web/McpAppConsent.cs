// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "Declared domains need the user's OK", "ui/message is attributed", 5.5; ED-NEW-web-w19-2).
// What an MCP App must ask the user before it gets it, and what the user said:
//   domains   a widget that declares connect, resource, frame or base-URI domains asks once, on first mount: "shipyard's
//             Release board wants to reach: api.shipyard.dev, cdn.shipyard.dev", Allow for shipyard / Not now. Until
//             allowed those directives stay empty. Wildcards and 'unsafe-eval' are dropped unless the user turns on "Allow
//             broad access" in the card's details. A changed list asks again (the grant is bound to the list's fingerprint).
//   messages  the first ui/message per app and chat asks "Let shipyard post messages in this chat?".
//   tools     "Always in this chat" on a tool call card is remembered per server, tool and chat (ED-NEW-web-sec-mcpapps-2).
//   window    the pop-out window's size, per server (DESIGN 4 table: "App pane and pop-out window ... per server").
// Everything here is plain data; the services keep it in a JSON file. No Swift counterpart.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Localisation;

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
    public string AllowText => UiText.Format("mcpapps.consent.allow_for", Server);

    /// <summary>True when the declaration holds wildcards or 'unsafe-eval' that only "Allow broad access" would let through.</summary>
    public bool HasBroad => Broad.Count > 0;

    /// <summary>
    /// + ED-NEW-web-sec-mcpapps-4 (L7): what "Allow broad access" would let through, named on the card ("Allow broad access
    /// also allows: *.shipyard-assets.dev, localhost:5173, 'unsafe-eval'"), the first <see cref="McpAppDomains.MaxNamed"/>
    /// by name and the rest counted; null when the declaration holds nothing broad.
    /// </summary>
    public string? BroadText => HasBroad ? UiText.Format("mcpapps.consent.broad_entries", McpAppDomains.Named(Broad.Select(b => McpAppDomains.Display(b.Source)))) : null;
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

    /// <summary>
    /// "Always in this chat": this server's tool runs without a card again, in this chat only (ED-NEW-web-sec-mcpapps-2; it
    /// was every chat, "Always for shipyard").
    /// </summary>
    AlwaysInChat,
}

/// <summary>One tool the user let a server's apps run without asking, in one chat (ED-NEW-web-sec-mcpapps-2).</summary>
public readonly record struct McpAppChatTool(Guid Chat, string Tool);

/// <summary>
/// + ED-NEW-web-sec-mcpapps-2 (web surfaces security review, M7): the arguments a tool card shows, exactly as they will be
/// sent. Indented JSON with every control, format (bidi, zero-width) and separator character escaped, so the page cannot
/// draw lines or reorder text on nth's card; cut at <see cref="MaxCharacters"/> with the full length said.
/// </summary>
public static class McpAppToolCard
{
    public const int MaxCharacters = 1500;

    // JavaScriptEncoder.Create keeps letters of every script readable and escapes controls, the HTML-sensitive characters and
    // everything outside the BMP, but it lets some invisible characters through as they are (U+202E RIGHT-TO-LEFT OVERRIDE
    // among them), so Plain escapes those after it.
    private static readonly System.Text.Json.JsonSerializerOptions Readable = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    /// <summary>"Arguments: { ... }", or "No arguments." for an empty object.</summary>
    public static string Arguments(System.Text.Json.Nodes.JsonObject? arguments)
    {
        if (arguments is null || arguments.Count == 0) return UiText.Get("mcpapps.card.tool_no_arguments");
        var json = Plain(arguments.ToJsonString(Readable));
        if (json.Length > MaxCharacters)
        {
            var cut = char.IsHighSurrogate(json[MaxCharacters - 1]) ? MaxCharacters - 1 : MaxCharacters;
            json = UiText.Format("mcpapps.card.tool_arguments_cut", json[..cut], cut, json.Length);
        }

        return UiText.Format("mcpapps.card.tool_arguments", json);
    }

    // Every control (but the indenting new line), format (bidi, zero-width), separator (but the space), private-use or
    // unassigned character becomes \uXXXX. Outside its strings the writer puts only structure, digits, the space and the
    // indenting new line, so each escape lands inside a JSON string and the text stays the same JSON.
    private static string Plain(string json)
    {
        StringBuilder? plain = null;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (c is '\n' or ' ' || char.GetUnicodeCategory(c) is not (UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned))
            {
                plain?.Append(c);
                continue;
            }

            plain ??= new StringBuilder(json.Length + 16).Append(json, 0, i);
            plain.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }

        return plain?.ToString() ?? json;
    }
}

public static class McpAppDomains
{
    /// <summary>The words "Allow broad access" and the risk they name, shown in the card's details.</summary>
    public static string BroadAccessTitle => UiText.Get("mcpapps.consent.broad_title");

    public static string BroadAccessRisk => UiText.Get("mcpapps.consent.broad_risk");

    public static string NotNowText => UiText.Get("mcpapps.consent.not_now");

    /// <summary>The consent request for a declaration, or null when it declares nothing outside the view's own origin.</summary>
    public static McpAppDomainRequest? Request(string server, string title, McpUiCsp declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        if (!declared.DeclaresAny) return null;
        var all = declared.ConnectDomains.Concat(declared.ResourceDomains).Concat(declared.FrameDomains).Concat(declared.BaseUriDomains).ToList();
        var strict = CspBuilder.Clean(all, allowBroad: false);
        var broad = CspBuilder.Clean(all, allowBroad: true);
        var broadOnly = strict.Dropped.Where(d => d.Reason == CspBuilder.WildcardReason || d.Reason == CspBuilder.UnsafeEvalReason || d.Reason == CspBuilder.LocalNetworkReason).ToList(); // + ED-NEW-web-w26-4
        var refused = broad.Dropped.ToList();
        var domains = strict.Kept.Select(Display).Distinct(StringComparer.Ordinal).ToList();
        if (domains.Count == 0 && broadOnly.Count == 0) return null;
        return new McpAppDomainRequest(server, title, domains, broadOnly, refused, Fingerprint(declared));
    }

    /// <summary>"shipyard's Release board wants to reach: a, b".</summary>
    public static string Sentence(string server, string title, IReadOnlyList<string> domains) =>
        UiText.Format("mcpapps.consent.sentence", server, string.IsNullOrWhiteSpace(title) ? UiText.Get("mcpapps.consent.app_word") : title, string.Join(", ", domains));

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
    /// The hosts a view may load from or connect to after the grant (the request filter matches whole addresses with
    /// <see cref="Reaches"/>). Exact hosts as "host" (or "host:port"); a broad wildcard as "*.example.com"; "*" as itself.
    /// </summary>
    public static IReadOnlyList<string> Hosts(McpUiCsp declared, McpAppDomainGrant? grant)
    {
        if (Allowed(declared, grant) is not { } allowed) return [];
        return HostsOf(allowed.ConnectDomains.Concat(allowed.ResourceDomains).Concat(allowed.FrameDomains), grant!.Broad);
    }

    /// <summary>
    /// ED-NEW-web-w26-5 (W26 security review): the hosts a view may show in a frame after the grant: its declared frame
    /// domains only, never a connect or resource domain (the CSP's frame-src says the same; this is nth's own check).
    /// </summary>
    public static IReadOnlyList<string> FrameHosts(McpUiCsp declared, McpAppDomainGrant? grant) =>
        Allowed(declared, grant) is { } allowed ? HostsOf(allowed.FrameDomains, grant!.Broad) : [];

    private static List<string> HostsOf(IEnumerable<string> declared, bool broad)
    {
        var sources = CspBuilder.Clean(declared, broad).Kept;
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

    /// <summary>How many entries a card names before it counts the rest ("and 12 more").</summary>
    public const int MaxNamed = 24;

    /// <summary>Entries as a card names them: the first <see cref="MaxNamed"/>, then "and N more".</summary>
    public static string Named(IEnumerable<string> entries)
    {
        var all = entries.Distinct(StringComparer.Ordinal).ToList();
        var named = string.Join(", ", all.Take(MaxNamed));
        return all.Count <= MaxNamed ? named : named + ", " + UiText.Format("mcpapps.consent.and_more", all.Count - MaxNamed);
    }

    /// <summary>
    /// + ED-NEW-web-sec-mcpapps-4 (web surfaces security review, L7): nth's own check of an address an App's view loads,
    /// matched against the granted sources the way the CSP's host-source matches them, so the two never disagree: the
    /// source's scheme (http also takes https, ws also wss; https never takes wss), the source's port or else the scheme's
    /// default port, the exact host or, for "*.example.com", a host under it; "*" (broad access) takes any web address. The
    /// path is left to the CSP. False before the grant, or when the declaration changed since.
    /// </summary>
    public static bool Reaches(string url, McpUiCsp declared, McpAppDomainGrant? grant) =>
        Allowed(declared, grant) is { } allowed
        && ReachesAny(url, CspBuilder.Clean(allowed.ConnectDomains.Concat(allowed.ResourceDomains).Concat(allowed.FrameDomains), grant!.Broad).Kept);

    /// <summary>As <see cref="Reaches"/>, for a frame: its declared frame domains only, and only an http(s) address.</summary>
    public static bool ReachesFrame(string url, McpUiCsp declared, McpAppDomainGrant? grant) =>
        Allowed(declared, grant) is { } allowed && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        && ReachesAny(url, CspBuilder.Clean(allowed.FrameDomains, grant!.Broad).Kept);

    // A placeholder label for a wildcard source, so that Uri parses it like any other host.
    private const string WildcardLabel = "nth-wildcard";

    private static bool ReachesAny(string url, IReadOnlyList<string> sources)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https" or "ws" or "wss")) return false;
        foreach (var source in sources)
            if (SourceMatches(source, target)) return true;
        return false;
    }

    private static bool SourceMatches(string source, Uri target)
    {
        if (source == "*") return true;
        if (source.StartsWith('\'')) return false;
        var wildcard = source.Contains("://*.", StringComparison.Ordinal);
        var text = wildcard ? source.Replace("://*.", "://" + WildcardLabel + ".", StringComparison.Ordinal) : source;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var allowed)) return false;
        var scheme = allowed.Scheme;
        if (!(scheme == target.Scheme || (scheme == "http" && target.Scheme == "https") || (scheme == "ws" && target.Scheme == "wss"))) return false;
        if (allowed.IsDefaultPort ? !target.IsDefaultPort : allowed.Port != target.Port) return false;
        var host = target.IdnHost.ToLowerInvariant();
        var allowedHost = allowed.IdnHost.ToLowerInvariant();
        if (!wildcard) return host == allowedHost;
        var suffix = allowedHost[WildcardLabel.Length..];
        return host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.Ordinal);
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

    /// <summary>
    /// The tools the user let run without a card, each in one chat ("Always in this chat"; ED-NEW-web-sec-mcpapps-2: it was
    /// every chat).
    /// </summary>
    public IReadOnlySet<McpAppChatTool> AlwaysTools { get; init; } = new HashSet<McpAppChatTool>();

    /// <summary>The pop-out window's size (DIP), or null for the default.</summary>
    public (double Width, double Height)? WindowSize { get; init; }
}
