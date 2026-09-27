using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :79-817.
public sealed class AppModelHydraMergeReviewTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-077")] // App/AppModel+HydraMerge.swift:660-663
    public void An_untracked_reported_directory_is_not_silently_spent()
    {
        using var f = new HydraMergeReviewFixture(origin: false, touched: "newdir");
        Directory.CreateDirectory(Path.Combine(f.Repo.Root, "newdir"));
        File.WriteAllText(Path.Combine(f.Repo.Root, "newdir", "child.txt"), "new child\n");
        f.Run();
        Assert.False(f.Runtime.Turns.Single().HydraMerged);
        var failure = Assert.Single(f.Runtime.HydraMerges);
        Assert.Equal("Hydra did not merge: no remote.: The team's work is in the checkout; there is no origin to push it to.", failure.Detail);
        Assert.Contains("?? newdir/", f.Repo.Git("status", "--porcelain"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("commit-tree")]
    [InlineData("tea")]
    [Trait("Rule", "R-ERR-1")] // App/AppModel+HydraMerge.swift:355-362,368-372,406-408
    public void Coordinator_propagates_the_original_trap(string site)
    {
        using var f = new HydraMergeReviewFixture();
        f.Edit();
        var trap = new SwiftTrapException("test trap");
        var caught = Assert.Throws<SwiftTrapException>(() => f.Run(call =>
        {
            if (site == "commit-tree" ? HydraMergeReviewFixture.IsGit(call, site) : Path.GetFileNameWithoutExtension(call.Executable) == site)
                throw trap;
        }));
        Assert.Same(trap, caught);
        Assert.Empty(f.Runtime.HydraMerges);
        Assert.Empty(f.Notes);
        Assert.False(f.Runtime.IsHydraMerging);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-063")] // App/AppModel+HydraMerge.swift:805-817
    [Trait("Rule", "R-ERR-1")] // App/AppModel+HydraMerge.swift:815-817
    public async Task Sync_propagates_the_original_trap()
    {
        using var f = new HydraMergeReviewFixture();
        var head = f.Repo.Git("rev-parse", "HEAD").Trim();
        var trap = new SwiftTrapException("sync trap");
        using var observation = ShellObserver.Observe(call =>
        {
            if (HydraMergeReviewFixture.IsGit(call, "reset")) throw trap;
        });
        Assert.Same(trap, await Assert.ThrowsAsync<SwiftTrapException>(() => HydraMergeReviewFixture.Sync(new Git(f.Repo.Root), head, [])));
    }
}
