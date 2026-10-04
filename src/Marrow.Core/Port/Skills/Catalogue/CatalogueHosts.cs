// PORT-ONLY: first-release feature unit SK-1. ONDEMAND-PLAN.md 3.2's host list, as code the downloader (SK-3) and the gate share.
namespace Marrow.Core.Port.Skills.Catalogue;

/// <summary>
/// "HTTPS only. The first URL must be on the catalogue's host list (www.python.org, nodejs.org, download.documentfoundation.org, github.com,
/// raw.githubusercontent.com, files.pythonhosted.org, registry.npmjs.org, cdn.playwright.dev)" (ONDEMAND-PLAN.md 3.2). Redirects to mirrors are the
/// downloader's business (the pinned hash, not the host, makes the bytes trusted); this check is for the URLs the catalogue itself names.
/// </summary>
public static class CatalogueHosts
{
    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "www.python.org",
        "nodejs.org",
        "download.documentfoundation.org",
        "github.com",
        "raw.githubusercontent.com",
        "files.pythonhosted.org",
        "registry.npmjs.org",
        "cdn.playwright.dev",
    };

    /// <summary>
    /// An absolute https URL on the default port, with no user information, whose host is exactly on the list (no subdomain, no trailing dot, no address).
    /// ENGINEERING DECISION (ED-294): the port and user-information limits close the look-alike forms (<c>https://nodejs.org@evil.example</c>).
    /// </summary>
    public static bool IsAllowed(string? url)
    {
        if (string.IsNullOrEmpty(url) || url != url.Trim()) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.UserInfo.Length != 0 || !uri.IsDefaultPort) return false;
        return Allowed.Contains(uri.Host);
    }
}
