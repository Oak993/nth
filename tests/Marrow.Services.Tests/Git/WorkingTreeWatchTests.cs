using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Git;

public sealed class WorkingTreeWatchTests
{
    [Fact]
    [Trait("Req", "PR-GIT-071")] // Services/Git/WorkingTreeWatch.swift:92-108
    public void Shared_returns_the_same_watch_for_a_directory()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var first = WorkingTreeWatch.Shared(folder.Root, git);
        Assert.NotNull(first);
        Assert.Same(first, WorkingTreeWatch.Shared(folder.Root, git));
    }

    [Fact]
    [Trait("Req", "PR-GIT-072")] // Services/Git/WorkingTreeWatch.swift:130-155; ED-113
    [Trait("Req", "PR-GIT-078")] // Services/Git/WorkingTreeWatch.swift:92-93,247,296
    public async Task External_git_directory_keeps_a_registered_streamless_watch_and_captures_fresh()
    {
        using var root = new ScratchFolder();
        using var git = new ScratchFolder();
        var watch = WorkingTreeWatch.Shared(root.Root, git.Root);
        Assert.NotNull(watch);
        var captures = 0;
        Task<string> Capture(string? scratch, IReadOnlyList<string>? paths)
        {
            Assert.Null(scratch);
            Assert.Null(paths);
            return Task.FromResult($"tree-{++captures}");
        }
        Assert.Equal("tree-1", await watch.CurrentTree(root.Child("index"), Capture));
        Assert.Equal("tree-2", await watch.CurrentTree(root.Child("index"), Capture));
        Assert.Same(watch, WorkingTreeWatch.Shared(root.Root, git.Root));
    }

    [Fact]
    [Trait("Req", "PR-GIT-073")] // Services/Git/WorkingTreeWatch.swift:180-190
    [Trait("Req", "PR-GIT-157")] // Services/Git/WorkingTreeWatch.swift:207-225
    public void Lost_batch_increments_generation_once_and_requires_full_capture()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        var before = watch.Generation;
        watch.NoteBatch([folder.Child("a"), folder.Child("b")], everything: true);
        Assert.Equal(before + 1, watch.Generation);
        Assert.Null(watch.ChangedPaths);
    }

    [Fact]
    [Trait("Req", "PR-GIT-074")] // Services/Git/WorkingTreeWatch.swift:213-217,237-239
    public void Changed_paths_collapse_to_top_level_after_sixty_four()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        watch.NoteBatch(Enumerable.Range(0, 65).Select(i => folder.Child("build", $"d{i}", "file")).ToArray(), false);
        Assert.Equal(["build"], watch.ChangedPaths);
    }

    [Fact]
    [Trait("Req", "PR-GIT-075")] // Services/Git/WorkingTreeWatch.swift:228-235
    public void Pathspecs_sort_and_drop_nested_paths()
    {
        Assert.Equal(["a", "b", "c"], WorkingTreeWatch.Pathspecs(["b", "a/z", "a", "c", "b/d"]));
    }

    [Fact]
    [Trait("Req", "PR-GIT-076")] // Services/Git/WorkingTreeWatch.swift:246-273
    public async Task Cookie_event_allows_a_cached_tree_to_be_reused()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        var count = 0;
        Task<string> Capture(string? _, IReadOnlyList<string>? __) => Task.FromResult($"tree-{++count}");
        Assert.Equal("tree-1", await watch.CurrentTree(folder.Child("index"), Capture));
        Assert.Equal("tree-1", await watch.CurrentTree(folder.Child("index"), Capture));
        Assert.Equal(1, count);
    }

    [Fact]
    [Trait("Req", "PR-GIT-076")] // Services/Git/WorkingTreeWatch.swift:15-18,241-273; ED-161
    public async Task Write_through_a_handle_closed_before_the_cookie_is_counted()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var tracked = folder.Child("tracked.txt");
        File.WriteAllText(tracked, "a");
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        var count = 0;
        Task<string> Capture(string? _, IReadOnlyList<string>? __) => Task.FromResult($"tree-{++count}");
        Assert.Equal("tree-1", await watch.CurrentTree(folder.Child("index"), Capture));
        using (var handle = new FileStream(tracked, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            handle.WriteByte((byte)'b');
            handle.Flush();
        }
        Assert.Equal("tree-2", await watch.CurrentTree(folder.Child("index"), Capture));
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Oracle", "O-55")] // Services/Git/WorkingTreeWatch.swift:15-18,241-273
    [Trait("Decision", "ED-161")]
    public async Task Write_through_a_handle_still_open_is_counted_once_the_handle_closes()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var tracked = folder.Child("tracked.txt");
        File.WriteAllText(tracked, "a");
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        var count = 0;
        Task<string> Capture(string? _, IReadOnlyList<string>? __) => Task.FromResult($"tree-{++count}");
        Assert.Equal("tree-1", await watch.CurrentTree(folder.Child("index"), Capture));
        var handle = new FileStream(tracked, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            handle.WriteByte((byte)'b');
            handle.Flush();
            Assert.Equal("tree-1", await watch.CurrentTree(folder.Child("index"), Capture));
            Assert.Equal(1, count);
        }
        finally
        {
            handle.Dispose();
        }
        Assert.Equal("tree-2", await watch.CurrentTree(folder.Child("index"), Capture));
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Req", "PR-GIT-076")] // Services/Git/WorkingTreeWatch.swift:246-273; ED-161
    public async Task A_leftover_cookie_file_does_not_force_a_fresh_capture()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        File.WriteAllBytes(Path.Combine(git, "droppy-code-sync-1"), []);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        var count = 0;
        Task<string> Capture(string? _, IReadOnlyList<string>? __) => Task.FromResult($"tree-{++count}");
        Assert.Equal("tree-1", await watch.CurrentTree(folder.Child("index"), Capture));
        Assert.Equal("tree-1", await watch.CurrentTree(folder.Child("index"), Capture));
        Assert.Equal(1, count);
    }

    [Fact]
    [Trait("Req", "PR-GIT-077")] // Services/Git/WorkingTreeWatch.swift:159-170,271; OD-0008 A3
    public async Task Teardown_drops_the_stream_and_shared_scratch()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        File.WriteAllBytes(watch.ScratchPath, []);
        watch.TearDown();
        await watch.TeardownCompleted;
        Assert.False(watch.HasStream);
        Assert.False(File.Exists(watch.ScratchPath));
    }

    [Fact]
    [Trait("Req", "PR-GIT-079")] // Services/Git/WorkingTreeWatch.swift:290-337
    public async Task A_same_generation_capture_is_shared()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        Task<string> Capture(string? _, IReadOnlyList<string>? __) { count++; return gate.Task; }
        var first = watch.CurrentTree(folder.Child("index"), Capture);
        var second = watch.CurrentTree(folder.Child("index"), Capture);
        gate.SetResult("tree");
        Assert.Equal("tree", await first);
        Assert.Equal("tree", await second);
        Assert.Equal(1, count);
    }

    [Fact]
    [Trait("Req", "PR-GIT-153")] // Services/Git/WorkingTreeWatch.swift:191-194
    [Trait("Req", "PR-GIT-154")] // Services/Git/WorkingTreeWatch.swift:195-196
    public void Cookie_and_git_paths_do_not_change_generation()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        var before = watch.Generation;
        watch.NoteBatch([git + "\\droppy-code-sync-7", git + "\\objects\\x"], false);
        Assert.Equal(before, watch.Generation);
        Assert.Equal((ulong)7, watch.CookieSeen);
    }

    [Fact]
    [Trait("Req", "PR-GIT-155")] // Services/Git/WorkingTreeWatch.swift:197-203
    public void Root_event_requires_whole_tree()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        watch.NoteBatch([folder.Root], false);
        Assert.Null(watch.ChangedPaths);
    }

    [Fact]
    [Trait("Req", "PR-GIT-156")] // Services/Git/WorkingTreeWatch.swift:204-205
    public void Root_entry_contributes_its_name_and_nested_entry_its_parent()
    {
        using var folder = new ScratchFolder();
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        watch.NoteBatch([folder.Child("readme"), folder.Child("src", "sub", "file")], false);
        Assert.Equal(["readme", "src/sub"], watch.ChangedPaths!.Order(StringComparer.Ordinal));
    }
}


