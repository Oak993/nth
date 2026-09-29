using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Git;

// Audit fix (svc): Services/Git/WorkingTreeWatch.swift:25-34. IndexStamp reads the index file's attributes with try?, so any failure (an empty
// or unreadable path) gives a stamp with nil fields and currentTree carries on to the capture instead of throwing.
public sealed class WorkingTreeWatchIndexStampTests
{
    [Trait("Req", "PR-GIT-079")] // Services/Git/WorkingTreeWatch.swift:25-34,290-337
    [Fact]
    public async Task An_empty_index_path_gives_an_empty_stamp_and_the_capture_still_runs()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        var captures = 0;
        Task<string> Capture(string? _, IReadOnlyList<string>? __) => Task.FromResult($"tree-{++captures}");

        Assert.Equal("tree-1", await watch.CurrentTree("", Capture));
    }
}
