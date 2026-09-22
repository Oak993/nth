using System.Globalization;

namespace Marrow.Core.Models;

public sealed record MergeRequestLink(Uri Url, MergeRequestLink.Forge ForgeKind, long Number)
{
    public enum Forge { GitLab, GitHub, Gitea }

    public string Label => (ForgeKind == Forge.GitLab ? "!" : "#") + Number.ToString(CultureInfo.InvariantCulture);
    public string Title => "Merge " + Label;

    public static MergeRequestLink? Parse(Uri url)
    {
        var parts = url.GetComponents(UriComponents.Path, UriFormat.Unescaped)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.IndexOf(parts, "merge_requests");
        if (index >= 1 && index + 1 < parts.Length && PositiveInteger(parts[index + 1], out var number))
            return new(url, Forge.GitLab, number);
        if (parts.Length >= 4 && parts[2] == "pull" && PositiveInteger(parts[3], out number))
            return new(url, Forge.GitHub, number);
        if (parts.Length >= 4 && parts[2] == "pulls" && PositiveInteger(parts[3], out number))
            return new(url, Forge.Gitea, number);
        return null;
    }

    private static bool PositiveInteger(string text, out long value) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) && value > 0;

    public string Prompt
    {
        get
        {
            var number = Number.ToString(CultureInfo.InvariantCulture);
            var landing = ForgeKind switch
            {
                Forge.GitLab => $"Use glab from this directory: `glab mr merge {number} --yes`. If GitLab answers that the request is still being checked (a 405), wait a few seconds and try again; if it reports conflicts, stop and say so instead of resolving them.",
                Forge.GitHub => $"Use gh from this directory: `gh pr merge {number} --merge`. If GitHub reports the pull request is not mergeable, stop and say so instead of resolving it.",
                Forge.Gitea => $"Use tea from this directory: `tea pulls merge {number}`. If tea is not installed or signed in to this host, or the pull request is not mergeable, stop and say so instead of working around it.",
                _ => throw new ArgumentOutOfRangeException(nameof(ForgeKind))
            };
            return $"Merge {Label} now: {Url.AbsoluteUri}\n\n{landing} Once it is merged, sync the checkout: if this directory is on main with a clean working tree, run `git pull --ff-only`; otherwise leave the working tree alone and say so. Do not rebuild, install or relaunch anything. Reply in a few lines with the merged commit and the request's link.";
        }
    }
}
