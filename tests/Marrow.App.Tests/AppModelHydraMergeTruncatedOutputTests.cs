using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Services.Port;

namespace Marrow.App.Tests;

// Source: B3 52bc792, App/AppModel+HydraMerge.swift:657-663,790-814; Services/Git.swift:452-457.
public sealed class AppModelHydraMergeTruncatedOutputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-077")]
    public void A_successful_short_reply_drops_unanswered_missing_paths_positionally(bool includeTerminator)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        string[] paths = ["a.txt", "b-middle.txt", "c-tail.txt"];
        foreach (var path in paths.Skip(1)) File.WriteAllText(Path.Combine(project.Path, path), "baseline\n");
        repo.Git("add", "b-middle.txt", "c-tail.txt");
        repo.Git("commit", "-q", "-m", "tracked paths");
        foreach (var path in paths) File.Delete(Path.Combine(project.Path, path));
        var lead = f.AddLead(project, "Team", paths.Reverse().ToArray());
        f.Start(lead);
        Assert.Equal(paths, HydraMergeProofFixture.Property<ValueList<string>>(f.Work(lead, project), "Paths"));

        var calls = new List<ShellInvocation>();
        var observedReplies = 0;
        using (GitBatchOutput.RetainedCharacters.Override((reference, among, output) =>
        {
            Assert.Equal("HEAD", reference);
            Assert.Equal(paths, among);
            Assert.Equal("blob\0blob\0blob\0", output);
            observedReplies++;
            return output.IndexOf('\0', StringComparison.Ordinal) + (includeTerminator ? 1 : 0);
        }))
        using (ShellObserver.Observe(calls.Add))
        {
            // Split keeps an empty trailing answer, so a retained NUL also keeps the next path.
            Assert.Equal(paths.Take(includeTerminator ? 2 : 1),
                HydraMergeProofFixture.Property<ValueList<string>>(f.Work(lead, project), "Paths"));
        }
        Assert.Equal(1, observedReplies);
        var batch = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "cat-file"));
        Assert.Equal(["cat-file", "--batch-check=%(objecttype)", "-Z"], HydraMergeReviewFixture.GitArgs(batch));
        Assert.Equal(Encoding.UTF8.GetBytes("HEAD:a.txt\0HEAD:b-middle.txt\0HEAD:c-tail.txt\0"), batch.Input);
        var ignored = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "check-ignore"));
        Assert.True(calls.IndexOf(ignored) < calls.IndexOf(batch));
        Assert.Equal(Encoding.UTF8.GetBytes("a.txt\0b-middle.txt\0c-tail.txt\0"), ignored.Input);
        Assert.Equal(paths, HydraMergeProofFixture.Property<ValueList<string>>(f.Work(lead, project), "Paths"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-063")]
    public void A_successful_short_remote_reply_treats_unanswered_changed_paths_as_removal_candidates(bool includeTerminator)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        string[] paths = ["b-first.txt", "c-middle.txt", "d-tail.txt"];
        foreach (var path in paths) File.WriteAllText(Path.Combine(project.Path, path), "baseline\n");
        repo.Git("add", "b-first.txt", "c-middle.txt", "d-tail.txt");
        repo.Git("commit", "-q", "-m", "baseline");
        repo.Git("push", "origin", "main");
        var head = repo.Git("rev-parse", "HEAD").Trim();
        var clone = f.Clone(project);
        foreach (var path in paths) File.WriteAllText(Path.Combine(clone.Root, path), "remote\n");
        clone.Git("add", "b-first.txt", "c-middle.txt", "d-tail.txt");
        clone.Git("commit", "-q", "-m", "remote changes");
        clone.Git("push", "origin", "main");
        var remote = clone.Git("rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "team working\n");
        var calls = new List<ShellInvocation>();
        var observedReplies = 0;

        var synced = f.H.Main.Loop.Run(async () =>
        {
            using var observer = ShellObserver.Observe(calls.Add);
            using var shortened = GitBatchOutput.RetainedCharacters.Override((reference, among, output) =>
            {
                Assert.Equal("origin/main", reference);
                Assert.Equal(paths, among);
                Assert.Equal("blob\0blob\0blob\0", output);
                observedReplies++;
                return output.IndexOf('\0', StringComparison.Ordinal) + (includeTerminator ? 1 : 0);
            });
            return await HydraMergeReviewFixture.Sync(new Git(project.Path), head, ["a.txt"]);
        }, AppModelHarness.Bound);

        Assert.True(synced);
        Assert.Equal(1, observedReplies);
        Assert.Equal(remote, repo.Git("rev-parse", "HEAD").Trim());
        Assert.Equal("team working\n", File.ReadAllText(Path.Combine(project.Path, "a.txt")));
        var kept = paths.Take(includeTerminator ? 2 : 1).ToArray();
        var gone = paths.Skip(kept.Length).ToArray();
        foreach (var path in kept) Assert.Equal("remote\n", File.ReadAllText(Path.Combine(project.Path, path)));
        foreach (var path in gone)
        {
            Assert.False(File.Exists(Path.Combine(project.Path, path)));
            Assert.False(repo.Raw("ls-files", "--error-unmatch", path).Succeeded);
            Assert.True(repo.Raw("cat-file", "-e", "HEAD:" + path).Succeeded);
        }
        var batch = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "cat-file"));
        Assert.Equal(["cat-file", "--batch-check=%(objecttype)", "-Z"], HydraMergeReviewFixture.GitArgs(batch));
        Assert.Equal(Encoding.UTF8.GetBytes("origin/main:b-first.txt\0origin/main:c-middle.txt\0origin/main:d-tail.txt\0"), batch.Input);
        Assert.Contains(calls, call => HydraMergeReviewFixture.GitArgs(call).SequenceEqual(
            new[] { "checkout", "--quiet", "origin/main", "--" }.Concat(kept)));
        Assert.Contains(calls, call => HydraMergeReviewFixture.GitArgs(call).SequenceEqual(
            new[] { "rm", "--quiet", "-f", "--" }.Concat(gone)));
    }
}
