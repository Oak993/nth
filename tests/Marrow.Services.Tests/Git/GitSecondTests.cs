using System.Text;
using Marrow.Core.Support;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using GitValue = Marrow.Services.Git.Git;

namespace Marrow.Services.Tests.Git;

// Part 2 of Services.Git.Git, the index-path cache of Git.swift:489-538 (PR-GIT-027). Git.worktreeIndexPaths is one
// process-wide map, so these tests run in a non-parallel collection (R-TEST-5). Expectations are read from the Swift
// and from the system git's own answers (R-TEST-10) in real folders under MARROW_PORT_DATA_DIR (R-TEST-9).
[Collection(GitIndexPathCacheCollection.Name)]
public sealed class GitSecondTests
{
    [Fact]
    [Trait("Req", "PR-GIT-027")] // Services/Git/Git.swift:525-537
    public async Task A_linked_worktrees_index_path_is_gits_absolute_answer_cached_under_the_directory()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var worktree = AddWorktree(repository, "wt");
        var expected = GitRepository.GitIn(worktree, null, "rev-parse", "--path-format=absolute", "--git-path", "index").Trim();
        Assert.Equal(expected, await new GitValue(worktree).IndexPath());
        var cached = GitValue.WorktreeIndexPaths.WithLock((ref Dictionary<string, (GitValue.GitFileStamp Stamp, string? Path)> paths) =>
            paths.TryGetValue(worktree, out var entry) ? entry : ((GitValue.GitFileStamp Stamp, string? Path)?)null);
        Assert.NotNull(cached);
        Assert.Equal(expected, cached.Value.Path);
        Assert.Equal(new GitValue.GitFileStamp(worktree + "\\.git", LoginEnvironment.Current), cached.Value.Stamp);
    }

    [Fact]
    [Trait("Req", "PR-GIT-027")] // Services/Git/Git.swift:526-528
    public async Task An_unchanged_git_file_answers_from_the_cache_without_asking_git()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var worktree = AddWorktree(repository, "wt");
        var git = new GitValue(worktree);
        var first = await git.IndexPath();
        Assert.NotNull(first);
        // The worktree's own git directory is moved away: git can no longer answer for the folder at all.
        var admin = first[..^"/index".Length];
        Directory.Move(admin, admin + "-moved");
        Assert.False(GitRepository.Raw(worktree, null, "rev-parse", "--git-path", "index").Succeeded);
        Assert.Equal(first, await git.IndexPath());
    }

    [Fact]
    [Trait("Req", "PR-GIT-027")] // Services/Git/Git.swift:492-505,526-535
    public async Task A_changed_git_file_asks_git_again_and_replaces_the_cached_answer()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var worktree = AddWorktree(repository, "wt");
        var git = new GitValue(worktree);
        var first = await git.IndexPath();
        Assert.NotNull(first);
        var admin = first[..^"/index".Length];
        Directory.Move(admin, admin + "-moved");
        // A .git file naming the moved directory: its size changes, so the stamp no longer matches.
        File.WriteAllBytes(worktree + "\\.git", Encoding.UTF8.GetBytes("gitdir: " + admin + "-moved\n"));
        var expected = GitRepository.GitIn(worktree, null, "rev-parse", "--path-format=absolute", "--git-path", "index").Trim();
        Assert.NotEqual(first, expected);
        Assert.Equal(expected, await git.IndexPath());
        Assert.Equal(expected, CachedPath(worktree));
    }

    [Fact]
    [Trait("Req", "PR-GIT-027")] // Services/Git/Git.swift:529-535
    public async Task A_failed_rev_parse_answers_nil_and_caches_nil()
    {
        using var runtime = new TestRuntime();
        using var folder = GitRepository.NotARepository();
        File.WriteAllBytes(folder.Child(".git"), Encoding.UTF8.GetBytes("gitdir: " + folder.Child("missing") + "\n"));
        Assert.Null(await new GitValue(folder.Root).IndexPath());
        var entry = GitValue.WorktreeIndexPaths.WithLock((ref Dictionary<string, (GitValue.GitFileStamp Stamp, string? Path)> paths) =>
            paths.TryGetValue(folder.Root, out var found) ? found : ((GitValue.GitFileStamp Stamp, string? Path)?)null);
        Assert.NotNull(entry);
        Assert.Null(entry.Value.Path);
    }

    [Fact]
    [Trait("Req", "PR-GIT-027")] // Services/Git/Git.swift:515-516,525,531
    public async Task A_directory_with_no_git_of_its_own_asks_git_every_time_and_is_never_cached()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var sub = repository.Child("sub");
        Directory.CreateDirectory(sub);
        var expected = GitRepository.GitIn(sub, null, "rev-parse", "--path-format=absolute", "--git-path", "index").Trim();
        Assert.Equal(expected, await new GitValue(sub).IndexPath());
        Assert.False(IsCached(sub));
    }

    [Fact]
    [Trait("Req", "PR-GIT-027")] // Services/Git/Git.swift:531-535
    public async Task The_whole_map_is_cleared_before_an_insert_once_it_holds_64_entries()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        var first = AddWorktree(repository, "one");
        var second = AddWorktree(repository, "two");
        var stamp = new GitValue.GitFileStamp(first + "\\.git", LoginEnvironment.Current);
        GitValue.WorktreeIndexPaths.WithLock((ref Dictionary<string, (GitValue.GitFileStamp Stamp, string? Path)> paths) =>
        {
            paths.Clear();
            for (var index = 0; index < 63; index++) paths["C:\\filler\\" + index] = (stamp, "C:/filler/index");
        });
        // 63 entries: the insert makes 64 and nothing is cleared.
        await new GitValue(first).IndexPath();
        Assert.Equal(64, CacheCount());
        Assert.True(IsCached("C:\\filler\\0"));
        // A cache hit inserts nothing, so the full map stands.
        await new GitValue(first).IndexPath();
        Assert.Equal(64, CacheCount());
        // 64 entries: this insert first clears everything, its own entry included.
        await new GitValue(second).IndexPath();
        Assert.Equal(1, CacheCount());
        Assert.True(IsCached(second));
        Assert.False(IsCached(first));
    }

    [Fact]
    [Trait("Req", "PR-GIT-027")] // Services/Git/Git.swift:492-505
    public void A_stamp_holds_the_files_identity_date_size_and_the_two_overrides_joined_by_a_NUL()
    {
        using var folder = GitRepository.NotARepository();
        var path = folder.Child("dotgit");
        var other = folder.Child("other");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("gitdir: x\n"));
        File.WriteAllBytes(other, Encoding.UTF8.GetBytes("gitdir: x\n"));
        var none = new Dictionary<string, string>();
        var stamp = new GitValue.GitFileStamp(path, none);
        Assert.NotNull(stamp.Identifier);
        Assert.NotNull(stamp.Modified);
        Assert.Equal(10L, stamp.Size);
        Assert.Equal("\0", stamp.Overrides);
        Assert.Equal(stamp, new GitValue.GitFileStamp(path, none));
        // ED-044: the identifier is the file's own (volume serial, file index), so another file of the same size differs.
        Assert.NotEqual(stamp.Identifier, new GitValue.GitFileStamp(other, none).Identifier);
        var overridden = new GitValue.GitFileStamp(path, new Dictionary<string, string> { ["GIT_DIR"] = "d", ["GIT_INDEX_FILE"] = "i" });
        Assert.Equal("d\0i", overridden.Overrides);
        Assert.NotEqual(stamp, overridden);
        Assert.Equal("\0i", new GitValue.GitFileStamp(path, new Dictionary<string, string> { ["GIT_INDEX_FILE"] = "i" }).Overrides);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("gitdir: xy\n"));
        Assert.NotEqual(stamp, new GitValue.GitFileStamp(path, none));
    }

    [Fact]
    [Trait("Req", "PR-GIT-027")] // Services/Git/Git.swift:499-502
    public void A_stamp_of_a_file_that_cannot_be_read_has_no_identifier_date_or_size()
    {
        using var folder = GitRepository.NotARepository();
        var stamp = new GitValue.GitFileStamp(folder.Child("absent"), new Dictionary<string, string>());
        Assert.Null(stamp.Identifier);
        Assert.Null(stamp.Modified);
        Assert.Null(stamp.Size);
        Assert.Equal("\0", stamp.Overrides);
    }

    // ---- helpers ----

    /// <summary>A linked worktree of <paramref name="repository"/> beside its root, on a new branch.</summary>
    private static string AddWorktree(GitRepository repository, string name)
    {
        var path = repository.Root + "-" + name;
        repository.Git("worktree", "add", "-q", "-b", name, path);
        return path;
    }

    private static string? CachedPath(string directory) =>
        GitValue.WorktreeIndexPaths.WithLock((ref Dictionary<string, (GitValue.GitFileStamp Stamp, string? Path)> paths) =>
            paths.TryGetValue(directory, out var entry) ? entry.Path : null);

    private static bool IsCached(string directory) =>
        GitValue.WorktreeIndexPaths.WithLock((ref Dictionary<string, (GitValue.GitFileStamp Stamp, string? Path)> paths) =>
            paths.ContainsKey(directory));

    private static int CacheCount() =>
        GitValue.WorktreeIndexPaths.WithLock((ref Dictionary<string, (GitValue.GitFileStamp Stamp, string? Path)> paths) => paths.Count);
}
