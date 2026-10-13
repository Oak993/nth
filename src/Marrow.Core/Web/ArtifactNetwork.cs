// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 "Network"; ED-NEW-web-w13-2). An artifact asks for network access with
// <meta name="nth-connect" content="api.example.com data.example.org">. The artifact (and so possibly an attacker's
// prompt) writes that tag, so the consent card treats it as untrusted: exact host names only, each shown with its
// registrable domain; wildcards and IP addresses are refused and the card says which entries were dropped. No Swift
// counterpart.
using System.Net;
using System.Text.RegularExpressions;
using Marrow.Core.Localisation;
using Marrow.Core.Port.ModelsPalette;

namespace Marrow.Core.Web;

/// <summary>One host an artifact declared, with the registrable domain the card shows in bold.</summary>
public sealed record ArtifactNetworkHost(string Host, string RegistrableDomain);

/// <summary>What an artifact declared: the hosts that may be allowed, and the entries refused with the reason.</summary>
public sealed record ArtifactNetworkRequest(IReadOnlyList<ArtifactNetworkHost> Hosts, IReadOnlyList<CspDroppedSource> Dropped)
{
    public static ArtifactNetworkRequest None { get; } = new([], []);

    public bool IsEmpty => Hosts.Count == 0 && Dropped.Count == 0;

    /// <summary>The host names, in declared order.</summary>
    public IReadOnlyList<string> HostNames => Hosts.Select(h => h.Host).ToList();
}

public static partial class ArtifactNetwork
{
    /// <summary>The meta tag's name.</summary>
    public const string MetaName = "nth-connect";

    /// <summary>The consent card's warning, word for word (DESIGN 6.5).</summary>
    public static string Warning => UiText.Get("mcpapps.consent.network_warning");

    /// <summary>At most this many hosts are offered; the rest are dropped with a reason.</summary>
    public const int MaxHosts = 16;

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"([a-zA-Z-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    /// <summary>Reads every nth-connect tag of a page (their contents joined) and checks each entry.</summary>
    public static ArtifactNetworkRequest Declared(string? html)
    {
        if (string.IsNullOrEmpty(html)) return ArtifactNetworkRequest.None;
        var entries = new List<string>();
        foreach (Match tag in MetaTag().Matches(html))
        {
            string? name = null, content = null;
            foreach (Match attribute in Attribute().Matches(tag.Value))
            {
                var value = attribute.Groups[2].Success ? attribute.Groups[2].Value : attribute.Groups[3].Success ? attribute.Groups[3].Value : attribute.Groups[4].Value;
                switch (attribute.Groups[1].Value.ToLowerInvariant())
                {
                    case "name": name = value; break;
                    case "content": content = WebUtility.HtmlDecode(value); break;
                }
            }

            if (MetaName.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase) && content is not null)
                entries.AddRange(content.Split([',', ' ', '\t', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries));
        }

        return Check(entries);
    }

    /// <summary>Checks declared entries: exact host names (an https:// prefix and a trailing slash are allowed).</summary>
    public static ArtifactNetworkRequest Check(IEnumerable<string> entries)
    {
        var hosts = new List<ArtifactNetworkHost>();
        var dropped = new List<CspDroppedSource>();
        foreach (var raw in entries)
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;
            var text = entry;
            if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) text = text[8..];
            else if (text.Contains("://", StringComparison.Ordinal))
            {
                dropped.Add(new CspDroppedSource(entry, UiText.Get("mcpapps.reason.not_https")));
                continue;
            }

            text = text.TrimEnd('/');
            if (text.Contains('*', StringComparison.Ordinal)) dropped.Add(new CspDroppedSource(entry, CspBuilder.WildcardReason));
            else if (IPAddress.TryParse(text.Trim('[', ']'), out _)) dropped.Add(new CspDroppedSource(entry, UiText.Get("mcpapps.reason.ip_address")));
            else if (text.Contains('/', StringComparison.Ordinal) || text.Contains(':', StringComparison.Ordinal) || text.Contains('@', StringComparison.Ordinal)
                     || Uri.CheckHostName(text) != UriHostNameType.Dns || !text.Contains('.', StringComparison.Ordinal))
                dropped.Add(new CspDroppedSource(entry, UiText.Get("mcpapps.reason.not_site_name")));
            else
            {
                var host = new System.Globalization.IdnMapping().GetAscii(text).ToLowerInvariant();
                if (SandboxSites.SurfaceOf(host) is not null || host.EndsWith(".invalid", StringComparison.Ordinal) || host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
                    dropped.Add(new CspDroppedSource(entry, UiText.Get("mcpapps.reason.not_internet_site")));
                else if (hosts.Any(h => h.Host == host)) continue;
                else if (hosts.Count >= MaxHosts) dropped.Add(new CspDroppedSource(entry, UiText.Get("mcpapps.reason.too_many_sites")));
                else hosts.Add(new ArtifactNetworkHost(host, DomainVote.Registrable("https://" + host + "/") ?? host));
            }
        }

        return new ArtifactNetworkRequest(hosts, dropped);
    }
}
