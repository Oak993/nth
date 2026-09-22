using System.Text;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class MergeRequestLinkTests
{
    [Theory] // PR-GIT-052..054
    [InlineData("https://gitlab.example/group/sub/project/-/merge_requests/95/diffs", MergeRequestLink.Forge.GitLab, 95, "!95")]
    [InlineData("https://gitlab.example/x/merge_requests/1", MergeRequestLink.Forge.GitLab, 1, "!1")]
    [InlineData("https://github.com/owner/repo/pull/95", MergeRequestLink.Forge.GitHub, 95, "#95")]
    [InlineData("https://enterprise.example/owner/repo/pull/+95/files?x=1#discussion", MergeRequestLink.Forge.GitHub, 95, "#95")]
    [InlineData("https://codeberg.org/owner/repo/pulls/95", MergeRequestLink.Forge.Gitea, 95, "#95")]
    [InlineData("https://forgejo.example/owner/repo/pulls/9223372036854775807", MergeRequestLink.Forge.Gitea, long.MaxValue, "#9223372036854775807")]
    [InlineData("https://any.example/merge_requests/bad/pull/5", MergeRequestLink.Forge.GitHub, 5, "#5")]
    public void Recognizes_source_path_shapes_without_restricting_hosts(string url, MergeRequestLink.Forge forge, long number, string label)
    {
        var parsed = MergeRequestLink.Parse(new Uri(url));
        Assert.NotNull(parsed); Assert.Equal(forge, parsed.ForgeKind);
        Assert.Equal(number, parsed.Number); Assert.Equal(label, parsed.Label);
        Assert.Equal("Merge " + label, parsed.Title); Assert.Equal(new Uri(url), parsed.Url);
    }

    [Theory] // PR-GIT-052
    [InlineData("https://host/merge_requests/95")]
    [InlineData("https://host/x/merge_requests/bad/merge_requests/95")]
    [InlineData("https://host/o/r/pull/0")]
    [InlineData("https://host/o/r/pulls/-1")]
    [InlineData("https://host/o/r/Pull/95")]
    [InlineData("https://host/o/r/pull/1.0")]
    [InlineData("https://host/o/r/pull/9223372036854775808")]
    [InlineData("https://host/o/r/pull/%2095")]
    [InlineData("https://host/o/r/pull")]
    [InlineData("https://host/o/r/issues/95")]
    [InlineData("https://host/prefix/o/r/pull/95")]
    public void Rejects_nonmatching_shapes_and_invalid_integers(string url)
        => Assert.Null(MergeRequestLink.Parse(new Uri(url)));

    [Theory] // PR-GIT-055, independent verbatim Expected fixtures
    [InlineData("https://host/g/p/-/merge_requests/95", "!95", "Use glab from this directory: `glab mr merge 95 --yes`. If GitLab answers that the request is still being checked (a 405), wait a few seconds and try again; if it reports conflicts, stop and say so instead of resolving them.")]
    [InlineData("https://host/o/r/pull/95", "#95", "Use gh from this directory: `gh pr merge 95 --merge`. If GitHub reports the pull request is not mergeable, stop and say so instead of resolving it.")]
    [InlineData("https://host/o/r/pulls/95", "#95", "Use tea from this directory: `tea pulls merge 95`. If tea is not installed or signed in to this host, or the pull request is not mergeable, stop and say so instead of working around it.")]
    public void Prompts_match_source_bytes_for_all_three_forges(string url, string label, string landing)
    {
        var expected = $"Merge {label} now: {url}\n\n{landing} Once it is merged, sync the checkout: if this directory is on main with a clean working tree, run `git pull --ff-only`; otherwise leave the working tree alone and say so. Do not rebuild, install or relaunch anything. Reply in a few lines with the merged commit and the request's link.";
        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(MergeRequestLink.Parse(new Uri(url))!.Prompt));
    }
}
