using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Git;

// Audit fix (svc): Services/Git/WorkingTreeWatch.swift:159-170,308-337. tearDown stops the stream and schedules the scratch cleanup but
// never invalidates the capture task, so a capture in flight when the watch is torn down still returns its tree from currentTree.
public sealed class WorkingTreeWatchTeardownCaptureTests
{
    [Trait("Req", "PR-GIT-079")] // Services/Git/WorkingTreeWatch.swift:159-170,308-337
    [Fact]
    public async Task A_valid_capture_still_returns_its_tree_when_the_watch_is_torn_down_meanwhile()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        Task<string> Capture(string? _, IReadOnlyList<string>? __)
        {
            watch.TearDown();
            return Task.FromResult("tree");
        }

        Assert.Equal("tree", await watch.CurrentTree(folder.Child("index"), Capture));
        await watch.TeardownCompleted;
    }
}
