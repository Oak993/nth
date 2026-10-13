using System.Text;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-5 security review (DESIGN.md rev 5, 12.6a; ED-NEW-dextra-as5-15): the mission's worktree is the agents'
// folder, so nth's own git steps there never follow its .git link, never run an fsmonitor command and never enter a
// nested repository; a Merge applies exactly the commit the owner's card showed, and a copied folder is copied back only
// as it was reviewed; a copy past the review caps is refused, never scanned without end. Each attack is first shown to
// work against plain git (the control), then shown not to work against nth. Real git, in throwaway folders.
[Trait("Feature", "AS-5")]
[Trait("Decision", "ED-NEW-dextra-as5-15")]
public sealed class MissionWorkspaceSecurityTests
{
    private static readonly Guid Id = Guid.Parse("0f3c9a2e-7b1d-4c55-8e6a-2d4f6b8a9c02");

    private static string Read(string path) => File.ReadAllText(path, Encoding.UTF8);

    /// <summary>A git config value that runs as the fsmonitor command and leaves a marker file when git runs it.</summary>
    private static string Monitor(string marker) => "echo ran >> " + marker.Replace('\\', '/');

    private static string Plain(string directory, params string[] arguments) => GitRepository.GitIn(directory, null, arguments);

    private static void NestedRepository(string folder, string marker)
    {
        Directory.CreateDirectory(folder);
        Plain(folder, "init", "-q");
        File.WriteAllText(Path.Combine(folder, "s.txt"), "s\n");
        Plain(folder, "add", "s.txt");
        Plain(folder, "-c", "user.email=agent@localhost", "-c", "user.name=agent", "commit", "-q", "-m", "nested");
        Plain(folder, "config", "core.fsmonitor", Monitor(marker));
    }

    [Fact]
    public async Task A_redirected_git_link_stops_every_git_step_and_its_fsmonitor_never_runs()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var marker = store.Child("redirected.txt");
        var workspace = await MissionWorkspaces.Create(Id, "Redirect", repository.Root, store.Child("worktrees", "r1"));
        Assert.NotNull(workspace.GitDir);

        // The agent makes a repository of its own and points the worktree's .git at it.
        var evil = Path.Combine(workspace.Path, "tools", "evil");
        NestedRepository(evil, marker);
        File.WriteAllText(Path.Combine(workspace.Path, ".git"), "gitdir: " + Path.Combine(evil, ".git").Replace('\\', '/') + "\n");
        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "a\nchanged\n");

        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Review(workspace));
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.RebaseFirst(workspace));
        Assert.False(File.Exists(marker));

        // The same link as a folder is refused too.
        File.Delete(Path.Combine(workspace.Path, ".git"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Review(workspace));
        Assert.False(File.Exists(marker));

        // Control: plain git in a worktree so redirected runs the agent's command.
        Directory.Delete(Path.Combine(workspace.Path, ".git"));
        File.WriteAllText(Path.Combine(workspace.Path, ".git"), "gitdir: " + Path.Combine(evil, ".git").Replace('\\', '/') + "\n");
        Plain(workspace.Path, "status", "--short");
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task A_nested_repository_is_never_entered_so_its_fsmonitor_never_runs()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var marker = store.Child("nested.txt");
        var workspace = await MissionWorkspaces.Create(Id, "Nested", repository.Root, store.Child("worktrees", "n1"));
        NestedRepository(Path.Combine(workspace.Path, "sub"), marker);

        await MissionWorkspaces.Review(workspace); // commits the nested repository as a gitlink
        File.AppendAllText(Path.Combine(workspace.Path, "sub", "s.txt"), "more\n");
        await MissionWorkspaces.Review(workspace);
        Assert.False(File.Exists(marker));

        // Control: plain git status in the worktree looks into the nested repository and runs its command.
        Plain(workspace.Path, "status", "--short");
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task A_merge_applies_exactly_the_reviewed_commit_and_nothing_written_after_the_review()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await MissionWorkspaces.Create(Id, "Exact", repository.Root, store.Child("worktrees", "e1"));
        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "a\nfixed\n");
        var review = await MissionWorkspaces.Review(workspace);
        Assert.Matches("^[0-9a-f]{40}$", review.Commit!);

        // After the card was raised: a commit on the branch and an uncommitted file.
        File.WriteAllText(Path.Combine(workspace.Path, "late.txt"), "late\n");
        Plain(workspace.Path, "add", "late.txt");
        Plain(workspace.Path, "-c", "user.email=agent@localhost", "-c", "user.name=agent", "commit", "-q", "-m", "late");
        File.WriteAllText(Path.Combine(workspace.Path, "later.txt"), "later\n");

        Assert.Equal(MergeOutcome.Refused, (await MissionWorkspaces.Merge(workspace, new MergeReview(review.Changes, review.Patch))).Outcome);
        var gone = await MissionWorkspaces.Merge(workspace, review with { Commit = new string('0', 39) + "1" });
        Assert.Equal((MergeOutcome.Refused, "The reviewed commit is gone; review the mission again. Nothing was merged."), (gone.Outcome, gone.Refused));

        Assert.Equal(MergeOutcome.Merged, (await MissionWorkspaces.Merge(workspace, review)).Outcome);
        Assert.Equal("a\nfixed\n", Read(repository.Child("a.txt")));
        Assert.False(File.Exists(repository.Child("late.txt")));
        Assert.False(File.Exists(repository.Child("later.txt")));
    }

    [Fact]
    public async Task A_copy_that_changed_after_its_review_is_not_copied_back_and_the_owners_folder_stays_as_it_was()
    {
        using var runtime = new TestRuntime();
        using var owner = GitRepository.NotARepository();
        using var store = new ScratchFolder();
        var source = owner.Child("notes");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "edit.txt"), "old\n");
        var workspace = await MissionWorkspaces.Create(Id, "Notes", source, store.Child("worktrees", "s1"));
        File.WriteAllText(Path.Combine(workspace.Path, "edit.txt"), "new\n");
        var review = await MissionWorkspaces.Review(workspace);
        Assert.NotNull(review.Fingerprint);

        File.WriteAllText(Path.Combine(workspace.Path, "added.txt"), "slipped in\n");
        var added = await MissionWorkspaces.Merge(workspace, review);
        Assert.Equal((MergeOutcome.Refused, "The copy changed after the review; nothing was copied back. Review it again."), (added.Outcome, added.Refused));
        File.Delete(Path.Combine(workspace.Path, "added.txt"));
        File.WriteAllText(Path.Combine(workspace.Path, "edit.txt"), "swapped\n");
        Assert.Equal(MergeOutcome.Refused, (await MissionWorkspaces.Merge(workspace, review)).Outcome);
        Assert.Equal(MergeOutcome.Refused, (await MissionWorkspaces.Merge(workspace, review with { Fingerprint = null })).Outcome);
        Assert.Equal("old\n", Read(Path.Combine(source, "edit.txt")));
        Assert.Equal(new[] { "edit.txt" }, Directory.EnumerateFileSystemEntries(source).Select(Path.GetFileName));

        var again = await MissionWorkspaces.Review(workspace);
        Assert.Equal(MergeOutcome.Merged, (await MissionWorkspaces.Merge(workspace, again)).Outcome);
        Assert.Equal("swapped\n", Read(Path.Combine(source, "edit.txt")));
        Assert.Equal(new[] { "edit.txt" }, Directory.EnumerateFileSystemEntries(source).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_copy_past_the_review_caps_is_refused_and_never_copied_back()
    {
        using var runtime = new TestRuntime();
        using var owner = GitRepository.NotARepository();
        using var store = new ScratchFolder();
        var source = owner.Child("notes");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "a\n");
        var workspace = await MissionWorkspaces.Create(Id, "Notes", source, store.Child("worktrees", "s2"));
        var review = await MissionWorkspaces.Review(workspace, byteCap: 1 << 20, fileCap: 3);
        for (var i = 0; i < 3; i++) File.WriteAllText(Path.Combine(workspace.Path, $"n{i}.txt"), "n\n");

        var error = await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Review(workspace, byteCap: 1 << 20, fileCap: 3));
        Assert.Contains("grew past 3 files", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Merge(workspace, review, byteCap: 1 << 20, fileCap: 3));
        File.WriteAllText(Path.Combine(workspace.Path, "big.txt"), new string('b', 600));
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Review(workspace, byteCap: 500, fileCap: 100));
        Assert.Equal(new[] { "a.txt" }, Directory.EnumerateFileSystemEntries(source).Select(Path.GetFileName));
    }

    [Fact]
    public async Task The_worktrees_admin_folder_is_read_from_the_owners_repository_and_journaled()
    {
        using var runtime = new TestRuntime();
        using var repository = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await MissionWorkspaces.Create(Id, "Journal", repository.Root, store.Child("worktrees", "j1"));
        var admin = Path.GetFullPath(Path.Combine(repository.Root, ".git", "worktrees"));
        Assert.StartsWith(admin, workspace.GitDir!, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(workspace.GitDir));

        var back = MissionWorkspaces.FromJson(MissionWorkspaces.ToJson(workspace));
        Assert.Equal(workspace.GitDir, back!.GitDir);

        // A journal from before the admin folder was kept: nth finds it from the owner's repository, not the link.
        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "a\nold journal\n");
        var review = await MissionWorkspaces.Review(workspace with { GitDir = null });
        Assert.Contains(review.Changes, c => c.Path == "a.txt");
    }
}
