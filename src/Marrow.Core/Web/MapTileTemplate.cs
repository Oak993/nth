using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Marrow.Core.Localisation;

// PORT-ONLY: web surfaces W10 (DESIGN.md 7.2 "Street tiles only if the user adds a tile source in Settings; off by default because it sends
// locations to a third party", 9 "Map street tiles source (none)"; ED-NEW-web-w10-5). No Swift counterpart. The address a reader adds as
// their tile source: an https address with {z}, {x} and {y}. Nothing else is a tile source: no plain http, no credentials in the address,
// no fragment, no other placeholder, and a host that names this PC or a private network is refused so a tile request never reaches a
// service on the reader's own machine or LAN.

namespace Marrow.Core.Web;

/// <summary>A checked tile source address.</summary>
public sealed record MapTileTemplate(string Text, string Host)
{
    /// <summary>The longest address kept.</summary>
    public const int MaxLength = 512;

    /// <summary>The credit shown on the map for a source: its host, since the reader's own source has no credit text of its own.</summary>
    public string Attribution => UiText.Format("nthui.map.attribution", Host);

    /// <summary>The tile's address (z/x/y).</summary>
    public string UrlFor(int zoom, int x, int y) => Text
        .Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{x}", x.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{y}", y.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>Checks an address. True with the template; false with nothing for anything that is not a safe tile address.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out MapTileTemplate? template)
    {
        template = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.Length > MaxLength) return false;
        foreach (var placeholder in new[] { "{z}", "{x}", "{y}" })
            if (trimmed.Occurrences(placeholder) != 1) return false;
        // Any other brace is a placeholder this build does not know.
        if (trimmed.Replace("{z}", "", StringComparison.Ordinal).Replace("{x}", "", StringComparison.Ordinal).Replace("{y}", "", StringComparison.Ordinal).AsSpan().IndexOfAny('{', '}') >= 0) return false;
        // The placeholders stand in for numbers, so they are checked as 0 in a real address.
        var sample = trimmed.Replace("{z}", "0", StringComparison.Ordinal).Replace("{x}", "0", StringComparison.Ordinal).Replace("{y}", "0", StringComparison.Ordinal);
        if (!Uri.TryCreate(sample, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return false;
        // The host must be in the address itself, never in a placeholder (the sample's host is not the template's host).
        var authorityEnd = trimmed.IndexOf('/', "https://".Length);
        if (authorityEnd < 0 || trimmed[..authorityEnd].Contains('{', StringComparison.Ordinal)) return false;
        if (IsPrivateHost(uri)) return false;
        template = new MapTileTemplate(trimmed, uri.IdnHost);
        return true;
    }

    private static bool IsPrivateHost(Uri uri)
    {
        var host = uri.IdnHost;
        if (host.Length == 0 || WebPolicy.IsDevHost(host)) return true;
        return !host.Contains('.', StringComparison.Ordinal) && !host.Contains(':', StringComparison.Ordinal); // a single label names a machine on a network
    }
}

internal static class MapTileTemplateText
{
    /// <summary>How many times <paramref name="part"/> occurs in <paramref name="text"/>.</summary>
    internal static int Occurrences(this string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
