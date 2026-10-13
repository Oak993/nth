using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY: final security gate N11 (ED-NEW-secfinal-24). "Open at sign-in" (SignInEntry) and Dextra's "Start with Windows (hidden)"
// wrote the same HKCU Run value, "nth", so each overwrote or deleted the other and the release switch read On for Dextra's entry.
// Dextra's entry now has its own value, "nth Dextra" (UninstallEntry.DextraStartValueName). The sign-in switch reads, writes and
// deletes only "nth"; the uninstall hook removes both values, each only when it starts a program inside the install root.
// Fake hive only; no real Windows settings.
[Trait("Decision", "ED-NEW-secfinal-24")]
public sealed class SignInDextraEntryTests
{
    private const string Root = FakeShellLinks.DefaultRoot;
    private const string Dextra = "nth Dextra";
    private static readonly string DextraCommand = "\"" + Root + "\\current\\nth.exe\" --hidden";
    private const string OtherInstall = "\"D:\\old\\nth.exe\" --hidden";
    private static readonly LaunchTarget Launch = new(Root + @"\current\nth.exe", "", Root + @"\current", "", 0, false);

    private static FakeUserRegistry WithDextra()
    {
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, Dextra, DextraCommand);
        return registry;
    }

    [Fact]
    public void Dextras_value_name_is_its_own()
    {
        Assert.Equal("nth Dextra", UninstallEntry.DextraStartValueName);
        Assert.NotEqual(UninstallEntry.SignInValueName, UninstallEntry.DextraStartValueName);
    }

    [Fact]
    public void Dextras_entry_does_not_make_the_sign_in_switch_read_on()
    {
        var registry = WithDextra();

        Assert.Equal(SignInStatus.Off, SignInEntry.Read(registry, Root).Status);
        Assert.False(SignInEntry.Read(registry, Root).IsOn);
    }

    [Fact]
    public void Turning_the_sign_in_switch_on_writes_only_its_own_value_and_leaves_dextras()
    {
        var registry = WithDextra();

        Assert.Equal(SignInChangeOutcome.Changed, SignInEntry.Enable(registry, Root, Launch).Outcome);

        Assert.Equal(SignInEntry.Command(Launch), registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
        Assert.Equal(DextraCommand, registry.GetValue(UninstallEntry.RunKeyPath, Dextra));
    }

    [Fact]
    public void Turning_the_sign_in_switch_off_deletes_only_its_own_value_and_leaves_dextras()
    {
        var registry = WithDextra();
        SignInEntry.Enable(registry, Root, Launch);

        Assert.Equal(SignInChangeOutcome.Changed, SignInEntry.Disable(registry, Root).Outcome);

        Assert.Null(registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
        Assert.Equal(DextraCommand, registry.GetValue(UninstallEntry.RunKeyPath, Dextra));
        Assert.Equal(SignInChangeOutcome.Unchanged, SignInEntry.Disable(registry, Root).Outcome);
    }

    [Fact]
    public void The_uninstall_hook_removes_dextras_value_and_its_approval_when_it_starts_this_install()
    {
        var registry = WithDextra();
        registry.Seed(SignInEntry.ApprovedKeyPath, Dextra, new byte[] { 3, 0, 0, 0 });
        registry.Seed(UninstallEntry.RunKeyPath, "Other", @"C:\Other\other.exe");

        Assert.True(UninstallEntry.RemoveSignInEntry(registry, Root));

        Assert.Null(registry.GetValue(UninstallEntry.RunKeyPath, Dextra));
        Assert.Null(registry.GetValue(SignInEntry.ApprovedKeyPath, Dextra));
        Assert.Equal(@"C:\Other\other.exe", registry.GetValue(UninstallEntry.RunKeyPath, "Other"));
        Assert.False(UninstallEntry.RemoveSignInEntry(registry, Root));
    }

    [Fact]
    public void The_uninstall_hook_removes_both_values_when_both_are_this_installs()
    {
        var registry = WithDextra();
        SignInEntry.Enable(registry, Root, Launch);

        Assert.True(UninstallEntry.RemoveSignInEntry(registry, Root));

        Assert.Null(registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
        Assert.Null(registry.GetValue(UninstallEntry.RunKeyPath, Dextra));
    }

    [Fact]
    public void The_uninstall_hook_leaves_a_dextra_value_that_starts_another_install()
    {
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, Dextra, OtherInstall);

        Assert.False(UninstallEntry.RemoveSignInEntry(registry, Root));

        Assert.Equal(OtherInstall, registry.GetValue(UninstallEntry.RunKeyPath, Dextra));
        Assert.Equal(0, registry.Writes);
    }
}
