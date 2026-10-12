// PORT-ONLY: web surfaces W11, where a model may send Visus (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "Cross-origin rule
// (exfiltration guard)", "URL policy: Tactus's policy applies to every model navigation; 'unknown URL means stop' holds for
// links the model invents. URLs from the conversation, the page or search results are allowed to be opened", "Hard denies: ...
// the Tactus URL hard-deny list, internal schemes"; ED-NEW-web-w11-1). No Swift counterpart. Pure rules: the hard-deny list of
// addresses, which addresses count as known, which origins a navigation would carry text away from, and how the card shows an
// address with its query string spelled out.
using System.Text.RegularExpressions;

namespace Marrow.Core.Web;

/// <summary>One part of an address as the cross-origin card shows it: plain, or a query value to highlight.</summary>
public sealed record WebUrlPart(string Text, bool Highlight);

/// <summary>One query parameter of an address, decoded for the card ("q = the page's words").</summary>
public sealed record WebQueryField(string Name, string Value, bool FromPage);

public static partial class WebAgentUrls
{
    /// <summary>
    /// Sites nth never lets a model drive, whatever the conversation says (the Tactus deny list's web half: password vaults and the
    /// account-security pages of the big sign-in providers, where one click can lock the user out or hand over a vault).
    /// </summary>
    private static readonly (string Host, string? PathPrefix, string Why)[] denied =
    [
        ("vault.bitwarden.com", null, "a password vault"),
        ("vault.bitwarden.eu", null, "a password vault"),
        ("my.1password.com", null, "a password vault"),
        ("my.1password.eu", null, "a password vault"),
        ("start.1password.com", null, "a password vault"),
        ("lastpass.com", "/vault", "a password vault"),
        ("app.dashlane.com", null, "a password vault"),
        ("keepersecurity.com", "/vault", "a password vault"),
        ("app.nordpass.com", null, "a password vault"),
        ("pass.proton.me", null, "a password vault"),
        ("myaccount.google.com", "/security", "an account's security settings"),
        ("myaccount.google.com", "/signinoptions", "an account's security settings"),
        ("account.microsoft.com", "/security", "an account's security settings"),
        ("account.live.com", "/proofs", "an account's security settings"),
        ("mysignins.microsoft.com", null, "an account's security settings"),
        ("appleid.apple.com", null, "an account's security settings"),
        ("account.apple.com", null, "an account's security settings"),
        ("github.com", "/settings/security", "an account's security settings"),
        ("github.com", "/settings/tokens", "an account's security settings"),
        ("github.com", "/settings/keys", "an account's security settings"),
    ];

    /// <summary>
    /// Why a model may never open <paramref name="url"/>, or null when it may: anything but http and https (internal pages,
    /// <c>file:</c>, <c>javascript:</c>, <c>data:</c>, app schemes), and the deny list.
    /// </summary>
    public static string? DeniedReason(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "there is no address";
        if (!WebPolicy.IsWeb(url))
            return WebPolicy.SchemeOf(url) is { } scheme ? $"{scheme}: addresses are not web pages; a model may open only http and https pages" : "this is not a web address";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "this is not a web address";
        if (!string.IsNullOrEmpty(uri.UserInfo)) return "an address with a user name or password in it is never opened for a model";
        var host = uri.Host.ToLowerInvariant().TrimEnd('.');
        foreach (var (deniedHost, prefix, why) in denied)
        {
            if (host != deniedHost && !host.EndsWith("." + deniedHost, StringComparison.Ordinal)) continue;
            if (prefix is null || uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return $"{host} is {why}; nth never lets a model use it";
        }
        return null;
    }

    /// <summary>
    /// The origins whose text a navigation to <paramref name="target"/> could carry away (DESIGN 5.10): every origin the model
    /// read in this chat other than the target's own, minus the pairs the user allowed always. Empty means no approval is needed.
    /// </summary>
    public static IReadOnlyList<string> CrossOriginSources(IEnumerable<string> readOrigins, string? target, Func<string, string, bool>? pairAllowed = null)
    {
        ArgumentNullException.ThrowIfNull(readOrigins);
        var to = WebPolicy.OriginOf(target);
        if (to is null) return [];
        return readOrigins
            .Where(from => !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            .Where(from => pairAllowed is null || !pairAllowed(from, to))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>A key for a pair of sites ("https://a.test -> https://b.test"), for the "Always for this site pair" store.</summary>
    public static string PairKey(string from, string to) => from.ToLowerInvariant() + " -> " + to.ToLowerInvariant();

    /// <summary>An address without its fragment and trailing slash, for matching known addresses.</summary>
    public static string Normalize(string url)
    {
        var trimmed = url.Trim();
        var hash = trimmed.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0) trimmed = trimmed[..hash];
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var text = uri.GetLeftPart(UriPartial.Query);
            return text.EndsWith('/') && uri.AbsolutePath == "/" && uri.Query.Length == 0 ? text.TrimEnd('/') : text;
        }
        return trimmed;
    }

    [GeneratedRegex(@"https?://[^\s<>""'`\)\]\}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Addresses();

    /// <summary>The http and https addresses written in a text (a chat message, a page's text), trailing punctuation dropped.</summary>
    public static IEnumerable<string> FindAddresses(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (Match match in Addresses().Matches(text))
        {
            var value = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            if (WebPolicy.IsWeb(value)) yield return value;
        }
    }

    /// <summary>True for a search engine's results page (DESIGN 5.10: they count as their own origin; nth opens them for a query).</summary>
    public static bool IsSearchResults(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        foreach (var engine in SearchEngines.All)
        {
            var sample = new Uri(SearchEngines.QueryUrl(engine, "x"));
            if (string.Equals(sample.Host, uri.Host, StringComparison.OrdinalIgnoreCase) && string.Equals(sample.AbsolutePath, uri.AbsolutePath, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The query string of <paramref name="url"/> (and any key=value fragment), decoded, one field per parameter; a field is
    /// marked from the page when its value (at least 3 characters) appears in <paramref name="readText"/>.
    /// </summary>
    public static IReadOnlyList<WebQueryField> QueryFields(string url, Func<string, bool>? readText = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return [];
        var fields = new List<WebQueryField>();
        var fragment = uri.Fragment.TrimStart('#');
        foreach (var part in new[] { uri.Query.TrimStart('?'), fragment.Contains('=', StringComparison.Ordinal) ? fragment : "" })
        {
            if (part.Length == 0) continue;
            foreach (var pair in part.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var at = pair.IndexOf('=', StringComparison.Ordinal);
                var name = Decode(at < 0 ? pair : pair[..at]);
                var value = at < 0 ? "" : Decode(pair[(at + 1)..]);
                var fromPage = value.Trim().Length >= 3 && readText is not null && readText(value.Trim());
                fields.Add(new WebQueryField(name, value, fromPage));
            }
        }
        return fields;
    }

    /// <summary>The address split for the card: the part before the query plain, every query value highlighted.</summary>
    public static IReadOnlyList<WebUrlPart> Parts(string url)
    {
        var parts = new List<WebUrlPart>();
        var query = url.IndexOf('?', StringComparison.Ordinal);
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        var cut = query >= 0 ? query : hash;
        if (cut < 0) return [new WebUrlPart(url, false)];
        parts.Add(new WebUrlPart(url[..cut], false));
        var rest = url[cut..];
        foreach (Match match in QueryPieces().Matches(rest))
        {
            parts.Add(new WebUrlPart(match.Groups["head"].Value, false));
            if (match.Groups["value"].Value.Length > 0) parts.Add(new WebUrlPart(match.Groups["value"].Value, true));
        }
        return parts;
    }

    [GeneratedRegex(@"(?<head>[?#&][^=&#]*=?)(?<value>[^&#]*)", RegexOptions.CultureInvariant)]
    private static partial Regex QueryPieces();

    private static string Decode(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return text;
        }
    }
}
