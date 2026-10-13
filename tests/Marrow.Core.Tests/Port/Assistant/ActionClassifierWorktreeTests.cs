using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-5 security review (DESIGN.md rev 5, 12.1a; ED-NEW-dextra-as5-13): nth's store holds the missions' worktrees,
// so the protected-folder rule spares the asking chat's own worktree and nothing else: not its .no-hooks sibling, not a
// name that only starts like it, not another mission's worktree, not the store's own files, and not the worktree at all
// when a protected root lies inside it. The worktree's .git link stays outside plain work.
[Trait("Feature", "AS-5")]
[Trait("Decision", "ED-NEW-dextra-as5-13")]
public sealed class ActionClassifierWorktreeTests
{
    private const string Store = @"C:\nth";
    private const string Worktree = @"C:\nth\worktrees\m1";

    private static ClassifierContext Context(string? worktree, params string[] roots) => new()
    {
        MissionWorktree = worktree,
        WorkingDirectory = worktree,
        AccessFolders = worktree is null ? ValueList<string>.Empty : new ValueList<string>([worktree]),
        ProtectedRoots = new ValueList<string>(roots),
        CommandShadowed = (_, _) => false,
    };

    private static ActionClass Write(string path, ClassifierContext context) =>
        ActionClassifier.ClassifyApproval(new ApprovalRequest("r1", ApprovalRequest.Kind.FileChange, path, new ValueList<ApprovalRequest.Option>([
            new("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new("deny", "Deny", ApprovalRequest.Option.Role.Decline),
        ])), context, provider: ProviderKind.Claude).Class;

    [Fact]
    public void The_chats_own_worktree_in_the_store_is_work_and_every_other_path_in_the_store_is_a_hard_deny()
    {
        var context = Context(Worktree, Store);
        Assert.Equal(ActionClass.Work, Write(Worktree + @"\src\login.cs", context));
        Assert.Equal(ActionClass.Work, Write(Worktree, context));
        Assert.Equal(ActionClass.HardDeny, Write(Worktree + @".no-hooks\pre-commit", context));
        Assert.Equal(ActionClass.HardDeny, Write(@"C:\nth\worktrees\m1x\a.cs", context));
        Assert.Equal(ActionClass.HardDeny, Write(@"C:\nth\worktrees\m2\src\login.cs", context));
        Assert.Equal(ActionClass.HardDeny, Write(@"C:\nth\missions\m1.json", context));
        Assert.Equal(ActionClass.HardDeny, Write(Worktree + @"\..\m2\a.cs", context));
        Assert.Equal(ActionClass.WriteElsewhere, Write(Worktree + @"\.git", context));
        Assert.Equal(ActionClass.WriteElsewhere, Write(Worktree + @"\.git\config", context));
    }

    [Fact]
    public void Without_a_worktree_or_with_a_protected_root_inside_it_nothing_in_the_store_is_spared()
    {
        Assert.Equal(ActionClass.HardDeny, Write(Worktree + @"\src\login.cs", Context(null, Store)));
        // A protected root inside the worktree: the worktree is no carve-out at all.
        var nested = Context(Worktree, Store, Worktree + @"\settings");
        Assert.Equal(ActionClass.HardDeny, Write(Worktree + @"\settings\x.json", nested));
        Assert.Equal(ActionClass.HardDeny, Write(Worktree + @"\src\login.cs", nested));
        // The worktree being the store itself spares nothing either.
        Assert.Equal(ActionClass.HardDeny, Write(@"C:\nth\missions\m1.json", Context(Store, Store)));
        // Outside the store the worktree rule is unchanged.
        Assert.Equal(ActionClass.Work, Write(@"D:\work\m1\a.cs", Context(@"D:\work\m1", Store)));
    }
}
