// PORT-ONLY (OD-0062 Velopack, OD-0100 "Marrow's own updates come from the owner's GitHub"; first-release UE-1): where Marrow finds its own new version.
namespace Marrow.Core.Port.SelfUpdate;

/// <summary>
/// The release feed: the public GitHub repository Oak993/marrow-releases (OD-0100, answered 2026-10-04: created by the lead, public, so a
/// relative updates without a token). Velopack's GithubSource reads it; releases are published there only with the owner's word for each
/// publish. This is the one named constant for the repository.
/// </summary>
public static class MarrowReleaseFeed
{
    /// <summary>The Velopack package id scripts/package-windows.ps1 packs under (ED-235): the id of every package the feed holds.</summary>
    public const string PackageId = "NthApp";

    /// <summary>The repository, owner and name.</summary>
    public const string Repository = "Oak993/marrow-releases";

    /// <summary>The repository's web address, the URL Velopack's GithubSource takes.</summary>
    public const string Url = "https://github.com/" + Repository;

    /// <summary>
    /// The page the About row's "open the release" button opens (ED-319: the offer is always the newest release, so GitHub's own
    /// "latest" address names it without guessing the tag vpk gave it).
    /// </summary>
    public const string LatestReleasePage = Url + "/releases/latest";

    /// <summary>True when a feed value names a GitHub repository (an http or https address with an owner and a name), the kind Velopack reads; false for the empty feed and for any other address.</summary>
    public static bool IsRepositoryUrl(string feed)
    {
        if (!Uri.TryCreate(feed, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return false;
        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Trim('/').Split('/').Length == 2;
    }
}
