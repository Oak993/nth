using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.App.Tests;

// OD-0035 (sealed b516890f): rollback failure is surfaced, never reported as successful restoration.
// Runtime binding: OD-0020 Part B in AppModel+HydraMerge.cs, rollbackErrors/AggregateException.
public sealed class AppModelHydraMergeRollbackFailureTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-063")]
    public void A_failed_rollback_HEAD_reset_surfaces_both_errors_and_still_restores_working_files_and_index()
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        File.WriteAllText(Path.Combine(project.Path, "kept.txt"), "old kept\n");
        File.WriteAllText(Path.Combine(project.Path, "gone.txt"), "old gone\n");
        repo.Git("add", "kept.txt", "gone.txt"); repo.Git("commit", "-q", "-m", "baseline"); repo.Git("push", "origin", "main");
        var head = repo.Git("rev-parse", "HEAD").Trim();
        var clone = f.Clone(project);
        File.WriteAllText(Path.Combine(clone.Root, "kept.txt"), "remote kept\n");
        File.Delete(Path.Combine(clone.Root, "gone.txt"));
        clone.Git("add", "-A"); clone.Git("commit", "-q", "-m", "remote changes"); clone.Git("push", "origin", "main");
        var remote = clone.Git("rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "team staged\n");
        repo.Git("add", "a.txt");
        var beforeIndex = File.ReadAllBytes(Path.Combine(project.Path, ".git", "index"));
        var syncError = new IOException("offline sync removal failure");
        var rollbackError = new IOException("offline rollback HEAD failure");
        var removalAttempts = 0;
        var rollbackAttempts = 0;
        var failure = f.H.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(call =>
            {
                if (HydraMergeReviewFixture.IsGit(call, "rm"))
                {
                    removalAttempts++;
                    Assert.Equal("remote kept\n", File.ReadAllText(Path.Combine(project.Path, "kept.txt")));
                    throw syncError; // after the actual remote HEAD/index/other-file checkout, before removal.
                }
                if (HydraMergeReviewFixture.GitArgs(call).SequenceEqual(["reset", "--soft", "--quiet", head]))
                {
                    rollbackAttempts++;
                    throw rollbackError; // only restoration fails; the first reset to remote has already succeeded.
                }
            });
            return await Assert.ThrowsAsync<AggregateException>(() => HydraMergeReviewFixture.Sync(new Git(project.Path), head, ["a.txt"]));
        }, AppModelHarness.Bound);
        Assert.Equal(1, removalAttempts);
        Assert.Equal(1, rollbackAttempts);
        Assert.StartsWith("Hydra sync rollback failed.", failure.Message, StringComparison.Ordinal);
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Same(syncError, failure.InnerExceptions[0]);
        Assert.Same(rollbackError, failure.InnerExceptions[1]);
        Assert.NotEqual(head, remote);
        Assert.Equal(remote, repo.Git("rev-parse", "HEAD").Trim()); // failed HEAD restoration cannot be called successful.
        Assert.Equal(beforeIndex, File.ReadAllBytes(Path.Combine(project.Path, ".git", "index")));
        Assert.Equal("team staged\n", File.ReadAllText(Path.Combine(project.Path, "a.txt")));
        Assert.Equal("old kept\n", File.ReadAllText(Path.Combine(project.Path, "kept.txt")));
        Assert.Equal("old gone\n", File.ReadAllText(Path.Combine(project.Path, "gone.txt")));
    }
}