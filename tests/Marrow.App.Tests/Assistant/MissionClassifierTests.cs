using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 12.1a, 12.3 point 2; ED-NEW-dextra-as5-12): the classifier sees a mission chat's approval
// with the mission's worktree, its working folder, nth's own folders and a real probe of the working folder. A git.cmd an
// agent planted there makes a bare "git status" under cmd Unknown (it asks); with none it is plain work; a write into nth's
// own folders is a hard deny; a removal is plain work only inside the worktree. Real files in a throwaway folder.
[Trait("Decision", "ED-NEW-dextra-as5-12")]
[Trait("Feature", "AS-5")]
public sealed class MissionClassifierTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private string Folder(string name)
    {
        var path = Path.Combine(temp.Folder, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static ApprovalRequest Ask(ApprovalRequest.Kind kind, string title) =>
        new("r1", kind, title, new ValueList<ApprovalRequest.Option>([
            new("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new("deny", "Deny", ApprovalRequest.Option.Role.Decline),
        ]));

    private MissionRecord Record(string worktree, string working) =>
        new(new Mission(Guid.NewGuid(), "Fix the login timeout", "dextra", MissionOrigin.Owner, MissionState.Working))
        {
            Workspace = new MissionWorkspace(MissionWorkspaceKind.Worktree, Path.Combine(temp.Folder, "owner"), worktree) { WorkingFolder = working },
        };

    [Fact]
    public void A_bare_command_is_shadowed_only_by_a_runnable_file_of_its_name_and_an_unreadable_name_counts_as_shadowed()
    {
        var folder = Folder("work");
        Assert.False(MissionClassifier.Shadowed(folder, "git"));
        File.WriteAllText(Path.Combine(folder, "git.txt"), "notes");
        File.WriteAllText(Path.Combine(folder, "gitk.cmd"), "@echo off");
        Assert.False(MissionClassifier.Shadowed(folder, "git"));
        File.WriteAllText(Path.Combine(folder, "GIT.Cmd"), "@echo off");
        Assert.True(MissionClassifier.Shadowed(folder, "git"));

        var other = Folder("other");
        File.WriteAllText(Path.Combine(other, "npm.ps1"), "");
        Assert.True(MissionClassifier.Shadowed(other, "npm"));
        File.WriteAllText(Path.Combine(other, "node.exe"), "");
        Assert.True(MissionClassifier.Shadowed(other, "node.exe"));
        Assert.False(MissionClassifier.Shadowed(other, "dotnet.exe"));

        Assert.False(MissionClassifier.Shadowed(Path.Combine(temp.Folder, "missing"), "git"));
        Assert.True(MissionClassifier.Shadowed(folder, @"..\git"));
        Assert.True(MissionClassifier.Shadowed(folder, "g*"));
        Assert.True(MissionClassifier.Shadowed(folder, ""));
        Assert.True(MissionClassifier.Shadowed("", "git"));
    }

    [Fact]
    public void The_context_places_the_worktree_the_working_folder_and_nths_own_folders()
    {
        var worktree = Folder("worktree");
        var working = Folder(Path.Combine("worktree", "app"));
        var store = Folder("store");
        var environment = new ValueMap<string, string>(new Dictionary<string, string> { ["TEMP"] = Folder("tmp") });
        var context = MissionClassifier.For(Record(worktree, working), [store, " "], environment);
        Assert.Equal(worktree, context.MissionWorktree);
        Assert.Equal(working, context.WorkingDirectory);
        Assert.Equal(new[] { worktree }, context.AccessFolders);
        Assert.Equal(new[] { store }, context.ProtectedRoots);
        Assert.Same(environment, context.Environment);
        Assert.NotNull(context.CommandShadowed);

        var bare = MissionClassifier.For(new MissionRecord(new Mission(Guid.NewGuid(), "X", "dextra", MissionOrigin.Owner, MissionState.Planning)), []);
        Assert.Null(bare.MissionWorktree);
        Assert.Null(bare.WorkingDirectory);
        Assert.Empty(bare.AccessFolders);
        Assert.Contains(MissionClassifier.EnvironmentKeys, k => bare.Environment.ContainsKey(k));
    }

    [Fact]
    public void A_planted_git_cmd_makes_git_status_ask_a_write_into_nths_folders_is_a_hard_deny_and_removal_is_work_only_inside()
    {
        var worktree = Folder("worktree");
        var store = Folder("store");
        var context = MissionClassifier.For(Record(worktree, worktree), [store]);

        Assert.Equal(ActionClass.Work, ActionClassifier.ClassifyApproval(Ask(ApprovalRequest.Kind.Command, "git status"), context, ShellKind.Cmd, ProviderKind.Claude).Class);
        File.WriteAllText(Path.Combine(worktree, "git.cmd"), "@echo off");
        Assert.Equal(ActionClass.Unknown, ActionClassifier.ClassifyApproval(Ask(ApprovalRequest.Kind.Command, "git status"), context, ShellKind.Cmd, ProviderKind.Claude).Class);

        Assert.Equal(ActionClass.HardDeny, ActionClassifier.ClassifyApproval(Ask(ApprovalRequest.Kind.FileChange, Path.Combine(store, "missions", "x.json")), context, provider: ProviderKind.Claude).Class);
        Assert.Equal(ActionClass.Work, ActionClassifier.ClassifyApproval(Ask(ApprovalRequest.Kind.FileChange, Path.Combine(worktree, "src", "a.cs")), context, provider: ProviderKind.Claude).Class);

        var inside = ActionClassifier.ClassifyApproval(Ask(ApprovalRequest.Kind.Command, "Remove-Item -Recurse -Force '" + Path.Combine(worktree, "bin") + "'"), context, ShellKind.PowerShell, ProviderKind.Claude);
        Assert.Equal(ActionClass.Work, inside.Class);
        var outside = ActionClassifier.ClassifyApproval(Ask(ApprovalRequest.Kind.Command, "Remove-Item -Recurse -Force '" + Folder("owner") + "'"), context, ShellKind.PowerShell, ProviderKind.Claude);
        Assert.Equal(ActionClass.Destructive, outside.Class);
    }
}
