using System.Globalization;
using Marrow.Core.Port;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/MergeRequestLink.swift (B2 1fbf81e), :1-62.
public sealed record MergeRequestLink
{
    // :6-11
    public enum Forge { Gitlab, Github, Gitea }

    // :13-15; Swift: forge is prefixed because Forge names the nested type (R-NAME-3).
    public SwiftURL Url { get; }
    public Forge LinkForge { get; }
    public long Number { get; }

    private MergeRequestLink(SwiftURL url, Forge linkForge, long number)
    {
        Url = url;
        LinkForge = linkForge;
        Number = number;
    }

    // :17-18
    public string Label => (LinkForge == Forge.Gitlab ? "!" : "#") + Number.ToString(CultureInfo.InvariantCulture);

    // :23-39
    public static MergeRequestLink? Create(SwiftURL url)
    {
        var parts = url.PathComponents.Where(part => part != "/").ToArray(); // :24
        var index = Array.FindIndex(parts, part => string.Equals(part, "merge_requests", StringComparison.Ordinal)); // :25
        if (index >= 1 && index + 1 < parts.Length && SwiftNumber.ParseInt(parts[index + 1]) is { } gitlabNumber && gitlabNumber > 0)
            return new MergeRequestLink(url, Forge.Gitlab, gitlabNumber); // :25-28, :38
        if (parts.Length >= 4 && parts[2] == "pull" && SwiftNumber.ParseInt(parts[3]) is { } githubNumber && githubNumber > 0)
            return new MergeRequestLink(url, Forge.Github, githubNumber); // :29-31, :38
        if (parts.Length >= 4 && parts[2] == "pulls" && SwiftNumber.ParseInt(parts[3]) is { } giteaNumber && giteaNumber > 0)
            return new MergeRequestLink(url, Forge.Gitea, giteaNumber); // :32-34, :38
        return null; // :35-37
    }

    // :41-42
    public string Title => "Merge " + Label;

    // :47-61
    public string Prompt
    {
        get
        {
            var number = Number.ToString(CultureInfo.InvariantCulture);
            var land = LinkForge switch // :48-55
            {
                Forge.Gitlab => "Use glab from this directory: `glab mr merge " + number + " --yes`. If GitLab answers that the request is still being checked (a 405), wait a few seconds and try again; if it reports conflicts, stop and say so instead of resolving them.",
                Forge.Github => "Use gh from this directory: `gh pr merge " + number + " --merge`. If GitHub reports the pull request is not mergeable, stop and say so instead of resolving it.",
                Forge.Gitea => "Use tea from this directory: `tea pulls merge " + number + "`. If tea is not installed or signed in to this host, or the pull request is not mergeable, stop and say so instead of working around it.",
                _ => throw new ArgumentOutOfRangeException(nameof(LinkForge)),
            };
            // :56-60; Swift's multiline literal has LF line breaks and no final line break.
            return "Merge " + Label + " now: " + Url.AbsoluteString + "\n\n" + land +
                " Once it is merged, sync the checkout: if this directory is on main with a clean working tree, run `git pull --ff-only`; otherwise leave the working tree alone and say so. Do not rebuild, install or relaunch anything. Reply in a few lines with the merged commit and the request's link.";
        }
    }
}
