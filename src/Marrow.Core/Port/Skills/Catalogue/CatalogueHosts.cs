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
    /// ED-NEW-dl-13: hosts a catalogue item may name for its licence file alone, never for its source or an artifact. gitlab.freedesktop.org is the Poppler project's own home: T4's build
    /// (on github.com) is packaged by a third party from conda-forge's build, and Poppler's own COPYING at the release tag is served from nowhere on the plan's list.
    /// </summary>
    public static IReadOnlySet<string> LicenceOnly { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gitlab.freedesktop.org" };

    /// <summary>
    /// An absolute https URL on the default port, with no user information, whose host is exactly on the list (no subdomain, no trailing dot, no address).
    /// ENGINEERING DECISION (ED-294): the port and user-information limits close the look-alike forms (<c>https://nodejs.org@evil.example</c>).
    /// </summary>
    public static bool IsAllowed(string? url) => Host(url) is { } host && Allowed.Contains(host);

    /// <summary>The same check for an item's licence URL, which may also be on <see cref="LicenceOnly"/> (ED-NEW-dl-13).</summary>
    public static bool IsAllowedLicence(string? url) => Host(url) is { } host && (Allowed.Contains(host) || LicenceOnly.Contains(host));

    private static string? Host(string? url)
    {
        if (string.IsNullOrEmpty(url) || url != url.Trim()) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.UserInfo.Length != 0 || !uri.IsDefaultPort) return null;
        return uri.Host;
    }
}
