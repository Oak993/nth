using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: final security gate S4, middle bullet (ED-NEW-secfinal-26). Discard runs git in the workspace record's Repository (worktree remove, then
// branch -D and prune). A record whose Repository was changed to another repository made nth run those steps there. A workspace is now discarded
// only when its Repository is the repository of the mission's own project folder (the source the mission was made from, which its record keeps);
// otherwise Discard refuses with a MissionWorkspaceError and neither git nor the folder is touched. Real git, in throwaway folders under MARROW_PORT_DATA_DIR.
[Trait("Decision", "ED-NEW-secfinal-26")]
public sealed class MissionWorkspaceRecordedSourceTests
{
    private static readonly Guid Id = Guid.Parse("0f3c9a2e-7b1d-4c55-8e6a-2d4f6b8a9c0e");

    [Fact]
    public async Task A_record_that_names_another_repository_is_refused_and_neither_that_repository_nor_the_folder_is_touched()
    {
        using var runtime = new TestRuntime();
        using var mine = GitRepository.WithCommit();
        using var other = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await MissionWorkspaces.Create(Id, "Discard", mine.Root, store.Child("worktrees", "r1"));
        Assert.NotNull(workspace.Branch);
        // Another repository that happens to have a branch of the same name: the record's Repository now points at it.
        other.Git("branch", workspace.Branch);
        File.WriteAllText(Path.Combine(workspace.Path, "work.txt"), "mission work");
        var tampered = workspace with { Repository = other.Root };

        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Discard(tampered, mine.Root));

        Assert.Equal("mission work", File.ReadAllText(Path.Combine(workspace.Path, "work.txt")));
        Assert.Contains(workspace.Branch, other.Git("branch", "--list", workspace.Branch), StringComparison.Ordinal);
        Assert.Contains(workspace.Branch, mine.Git("branch", "--list", workspace.Branch), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_missions_own_repository_is_discarded_with_its_folder_and_branch()
    {
        using var runtime = new TestRuntime();
        using var mine = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await MissionWorkspaces.Create(Id, "Discard", mine.Root, store.Child("worktrees", "r2"));

        await MissionWorkspaces.Discard(workspace, mine.Root);

        Assert.False(Directory.Exists(workspace.Path));
        Assert.Equal("", mine.Git("branch", "--list", "nth/*").Trim());
    }

    [Fact]
    public async Task A_project_folder_inside_the_repository_and_a_forward_slash_spelling_are_the_same_repository()
    {
        using var runtime = new TestRuntime();
        using var mine = GitRepository.WithCommit();
        mine.Write("app/readme.txt", "inside\n");
        mine.CommitAll("app", 1_700_000_100);
        using var store = new ScratchFolder();
        var source = mine.Child("app");
        var workspace = await MissionWorkspaces.Create(Id, "Inside", source, store.Child("worktrees", "r3"));

        await MissionWorkspaces.Discard(workspace, source.Replace('\\', '/'));

        Assert.False(Directory.Exists(workspace.Path));
    }

    [Fact]
    public async Task A_source_that_is_gone_or_is_no_repository_refuses_and_deletes_nothing()
    {
        using var runtime = new TestRuntime();
        using var mine = GitRepository.WithCommit();
        using var store = new ScratchFolder();
        var workspace = await MissionWorkspaces.Create(Id, "Discard", mine.Root, store.Child("worktrees", "r4"));
        var plain = store.Child("plain");
        Directory.CreateDirectory(plain);

        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Discard(workspace, store.Child("gone")));
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Discard(workspace, plain));
        await Assert.ThrowsAsync<MissionWorkspaceError>(() => MissionWorkspaces.Discard(workspace, ""));

        Assert.True(Directory.Exists(workspace.Path));
        Assert.Contains(workspace.Branch!, mine.Git("branch", "--list", workspace.Branch!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_copy_has_no_repository_to_check_and_is_discarded_as_before()
    {
        using var runtime = new TestRuntime();
        using var store = new ScratchFolder();
        var owner = store.Child("owner");
        var copy = store.Child("worktrees", "s1");
        Directory.CreateDirectory(owner);
        Directory.CreateDirectory(copy);
        File.WriteAllText(Path.Combine(copy, "work.txt"), "copy");

        await MissionWorkspaces.Discard(new MissionWorkspace(MissionWorkspaceKind.Snapshot, owner, copy), owner);

        Assert.False(Directory.Exists(copy));
    }
}
