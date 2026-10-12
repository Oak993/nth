using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY: installer C4b U8 finding 1, PLAN 8. Fake hive only; no real Windows settings.
public sealed class SignInStartupCleanupTests
{
    private const string Root = FakeShellLinks.DefaultRoot;
    private static readonly LaunchTarget Launch = new(Root + @"\current\nth.exe", "", Root + @"\current", "", 0, false);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Reenable_after_direct_uninstall_clears_the_stale_disabled_startup_flag()
    {
        var registry = ProfileWithOwnedEntry(3);
        Assert.True(UninstallEntry.RemoveSignInEntry(registry, Root));

        SignInEntry.Enable(registry, Root, Launch);

        Assert.True(SignInEntry.Read(registry, Root).IsOn);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Direct_uninstall_removes_owned_Run_and_StartupApproved_values_only()
    {
        foreach (var status in new byte[] { 2, 3, 6, 7 })
        {
            var registry = ProfileWithOwnedEntry(status);
            registry.Seed(UninstallEntry.RunKeyPath, "Other", @"C:\Other\other.exe");
            var otherApproval = new byte[] { 3, 0, 0, 0 };
            registry.Seed(SignInEntry.ApprovedKeyPath, "Other", otherApproval);

            Assert.True(UninstallEntry.RemoveSignInEntry(registry, Root));

            Assert.Null(registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
            Assert.Null(registry.GetValue(SignInEntry.ApprovedKeyPath, "nth"));
            Assert.Equal(@"C:\Other\other.exe", registry.GetValue(UninstallEntry.RunKeyPath, "Other"));
            Assert.Same(otherApproval, registry.GetValue(SignInEntry.ApprovedKeyPath, "Other"));
            Assert.False(UninstallEntry.RemoveSignInEntry(registry, Root));
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Enable_with_absent_Run_clears_stale_disabled_approval_independently_of_uninstall()
    {
        var registry = new FakeUserRegistry();
        registry.Seed(SignInEntry.ApprovedKeyPath, "nth", Approval(3));
        var otherApproval = Approval(7);
        registry.Seed(SignInEntry.ApprovedKeyPath, "Other", otherApproval);

        var changed = SignInEntry.Enable(registry, Root, Launch);

        Assert.Equal(SignInChangeOutcome.Changed, changed.Outcome);
        Assert.Null(registry.GetValue(SignInEntry.ApprovedKeyPath, "nth"));
        Assert.True(SignInEntry.Read(registry, Root).IsOn);
        Assert.Same(otherApproval, registry.GetValue(SignInEntry.ApprovedKeyPath, "Other"));
        var writes = registry.Writes;
        Assert.Equal(SignInChangeOutcome.Unchanged, SignInEntry.Enable(registry, Root, Launch).Outcome);
        Assert.Equal(writes, registry.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Uninstall_does_not_claim_a_stale_approval_without_an_owned_Run_command()
    {
        foreach (var foreign in new object?[] { @"C:\Foreign\nth.exe", 1, null })
        {
            var registry = new FakeUserRegistry();
            if (foreign is not null) registry.Seed(UninstallEntry.RunKeyPath, "nth", foreign);
            var approval = Approval(3);
            registry.Seed(SignInEntry.ApprovedKeyPath, "nth", approval);

            Assert.False(UninstallEntry.RemoveSignInEntry(registry, Root));

            Assert.Same(approval, registry.GetValue(SignInEntry.ApprovedKeyPath, "nth"));
            Assert.Equal(foreign, registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
            Assert.Equal(0, registry.Writes);
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Enable_does_not_clear_a_foreign_Run_entries_disabled_approval()
    {
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, "nth", @"C:\Foreign\nth.exe");
        var approval = Approval(3);
        registry.Seed(SignInEntry.ApprovedKeyPath, "nth", approval);

        Assert.Equal(SignInChangeOutcome.Foreign, SignInEntry.Enable(registry, Root, Launch).Outcome);

        Assert.Same(approval, registry.GetValue(SignInEntry.ApprovedKeyPath, "nth"));
        Assert.Equal(@"C:\Foreign\nth.exe", registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
        Assert.Equal(0, registry.Writes);
    }

    private static FakeUserRegistry ProfileWithOwnedEntry(byte status)
    {
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, "nth", SignInEntry.Command(Launch));
        registry.Seed(SignInEntry.ApprovedKeyPath, "nth", Approval(status));
        return registry;
    }

    private static byte[] Approval(byte status) => [status, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
}
