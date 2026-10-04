// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3 = DL-3 of skills-ONDEMAND-PLAN.md section 7):
// which hosts the first URL of a download may name (section 3.2).
namespace Marrow.Services.Port.Downloads;

/// <summary>
/// The hosts a download's FIRST url may be on, compared case-insensitively and exactly (no sub-domain matching). The first url must also
/// be https. Redirects are not held to the list (a mirror host is not known beforehand: the pinned hash, not the host, makes the bytes
/// trusted) but only to https (section 3.2).
/// </summary>
public sealed class DownloadPolicy
{
    /// <summary>The catalogue's host list, section 3.2 verbatim.</summary>
    public static IReadOnlyList<string> CatalogueHosts { get; } =
    [
        "www.python.org", "nodejs.org", "download.documentfoundation.org", "github.com", "raw.githubusercontent.com",
        "files.pythonhosted.org", "registry.npmjs.org", "cdn.playwright.dev",
    ];

    private readonly HashSet<string> hosts;

    public DownloadPolicy(IEnumerable<string> allowedHosts) =>
        hosts = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase);

    /// <summary>The catalogue's hosts and nothing else.</summary>
    public static DownloadPolicy Catalogue() => new(CatalogueHosts);

    public IReadOnlyCollection<string> AllowedHosts => hosts;

    /// <summary>True for an https url on an allowed host.</summary>
    public bool AllowsFirstUrl(Uri url) => url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && hosts.Contains(url.Host);
}
