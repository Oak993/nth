using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Marrow.Testing;
using Microsoft.Win32;

namespace Marrow.App.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 and 10 ("re-pointed registry values in a test hive, not the real HKCU,
// and the key restored after a simulated Velopack rewrite"), unit U5 (ED-NEW-inst-u5-1). RegistryUserHive on real registry
// calls over a private hive loaded from a temporary file (TestRegistryHive): value kinds, unexpanded strings, never creating
// a key, and the uninstall entry and sign-in entry steps end to end. The real HKCU is never opened.
public sealed class RegistryUserHiveTests : IDisposable
{
    private const string Root = @"C:\Users\Ana\AppData\Local\Programs\nth";

    private readonly TestRegistryHive hive = new();

    public void Dispose() => hive.Dispose();

    // What Velopack 1.2.161 writes (registry.rs:9-57; VELOPACK_CHECK.md checks 5 and 8): strings as REG_SZ, numbers as REG_DWORD.
    private void VelopackWritesItsKey(int currentSizeKb = 300_000)
    {
        using var key = hive.CreateKey(UninstallEntry.KeyPath);
        key.SetValue("DisplayIcon", Root + @"\current\nth.exe", RegistryValueKind.String);
        key.SetValue("DisplayName", "nth", RegistryValueKind.String);
        key.SetValue("DisplayVersion", "1.0.1", RegistryValueKind.String);
        key.SetValue("InstallLocation", Root, RegistryValueKind.String);
        key.SetValue("Publisher", "oak993", RegistryValueKind.String); // Velopack writes --packAuthors (decision A)
        key.SetValue("UninstallString", "\"" + Root + "\\Update.exe\" --uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", "\"" + Root + "\\Update.exe\" --uninstall --silent", RegistryValueKind.String);
        key.SetValue("EstimatedSize", currentSizeKb, RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private (object? Value, RegistryValueKind Kind) Read(string subKey, string name)
    {
        using var key = hive.Root.OpenSubKey(subKey)!;
        return (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), key.GetValueKind(name));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Strings_come_back_unexpanded_and_DWORDs_as_int_and_a_missing_key_or_value_as_null()
    {
        using (var key = hive.CreateKey(@"Software\Test"))
        {
            key.SetValue("Text", "plain", RegistryValueKind.String);
            key.SetValue("Expand", @"%LOCALAPPDATA%\Programs\nth", RegistryValueKind.ExpandString);
            key.SetValue("Number", 430_080, RegistryValueKind.DWord);
        }
        var registry = new RegistryUserHive(hive.Root);

        Assert.True(registry.KeyExists(@"Software\Test"));
        Assert.False(registry.KeyExists(@"Software\Missing"));
        Assert.Equal("plain", registry.GetValue(@"Software\Test", "Text"));
        Assert.Equal(@"%LOCALAPPDATA%\Programs\nth", registry.GetValue(@"Software\Test", "Expand"));
        Assert.Equal(430_080, registry.GetValue(@"Software\Test", "Number"));
        Assert.Null(registry.GetValue(@"Software\Test", "Missing"));
        Assert.Null(registry.GetValue(@"Software\Missing", "Text"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Values_are_written_with_their_kind_only_in_a_key_that_exists_and_no_key_is_ever_created()
    {
        hive.CreateKey(@"Software\Test").Dispose();
        var registry = new RegistryUserHive(hive.Root);

        registry.SetString(@"Software\Test", "Text", "value");
        registry.SetDword(@"Software\Test", "Number", 7);

        Assert.Equal<(object?, RegistryValueKind)>(("value", RegistryValueKind.String), Read(@"Software\Test", "Text"));
        Assert.Equal<(object?, RegistryValueKind)>((7, RegistryValueKind.DWord), Read(@"Software\Test", "Number"));
        Assert.Throws<InvalidOperationException>(() => registry.SetString(@"Software\Missing", "Text", "value"));
        Assert.Throws<InvalidOperationException>(() => registry.SetDword(@"Software\Missing", "Number", 1));
        Assert.False(registry.KeyExists(@"Software\Missing"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Deleting_a_missing_value_or_a_value_in_a_missing_key_is_not_an_error()
    {
        using (var key = hive.CreateKey(@"Software\Test")) key.SetValue("Text", "value");
        var registry = new RegistryUserHive(hive.Root);

        registry.DeleteValue(@"Software\Test", "Text");
        registry.DeleteValue(@"Software\Test", "Text");
        registry.DeleteValue(@"Software\Missing", "Text");

        Assert.Null(registry.GetValue(@"Software\Test", "Text"));
        Assert.False(registry.KeyExists(@"Software\Missing"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_uninstall_entry_is_repointed_in_the_test_hive_and_restored_after_Velopacks_rewrite()
    {
        VelopackWritesItsKey();
        var registry = new RegistryUserHive(hive.Root);

        var first = UninstallEntry.Repoint(registry, Root, 430_080, uninstallerPresent: true);

        Assert.Equal(new UninstallRepointResult(true, true, 3, true), first);
        Assert.Equal<(object?, RegistryValueKind)>(("\"" + Root + "\\nth Uninstall.exe\" --uninstall", RegistryValueKind.String), Read(UninstallEntry.KeyPath, "UninstallString"));
        Assert.Equal<(object?, RegistryValueKind)>(("\"" + Root + "\\nth Uninstall.exe\" --uninstall --silent", RegistryValueKind.String), Read(UninstallEntry.KeyPath, "QuietUninstallString"));
        Assert.Equal<(object?, RegistryValueKind)>((430_080, RegistryValueKind.DWord), Read(UninstallEntry.KeyPath, "EstimatedSize"));
        Assert.Equal<(object?, RegistryValueKind)>((1, RegistryValueKind.DWord), Read(UninstallEntry.KeyPath, "NoModify"));
        Assert.Equal<(object?, RegistryValueKind)>((1, RegistryValueKind.DWord), Read(UninstallEntry.KeyPath, "NoRepair"));
        Assert.Equal(0, UninstallEntry.Repoint(registry, Root, 430_080, uninstallerPresent: true).Writes);

        VelopackWritesItsKey(currentSizeKb: 310_000); // Update.exe rewrites it before --veloapp-updated (check 8)
        var hooks = new InstallHooks(new FakeShellLinks(), registry, copyUninstaller: _ => new UninstallerCopyResult(UninstallerCopyOutcome.Copied),
            measureFootprintKb: _ => 431_000, fileExists: path => path == Root + @"\nth Uninstall.exe");
        var report = hooks.AfterUpdate(new InstallHookContext(Root, Path.GetTempPath(), null, "1.0.2"));

        Assert.Equal("uninstall-key: 3 written", report.Steps[^1]);
        Assert.Equal<(object?, RegistryValueKind)>(("\"" + Root + "\\nth Uninstall.exe\" --uninstall", RegistryValueKind.String), Read(UninstallEntry.KeyPath, "UninstallString"));
        Assert.Equal<(object?, RegistryValueKind)>((431_000, RegistryValueKind.DWord), Read(UninstallEntry.KeyPath, "EstimatedSize"));
        Assert.Equal<(object?, RegistryValueKind)>(("nth", RegistryValueKind.String), Read(UninstallEntry.KeyPath, "DisplayName"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Without_Velopacks_key_nothing_is_created_in_the_test_hive()
    {
        var registry = new RegistryUserHive(hive.Root);

        Assert.Equal(new UninstallRepointResult(false, false, 0, true), UninstallEntry.Repoint(registry, Root, 430_080, uninstallerPresent: true));
        Assert.Null(hive.Root.OpenSubKey(UninstallEntry.KeyPath));
        Assert.Null(hive.Root.OpenSubKey("Software"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_sign_in_entry_is_removed_from_the_test_hives_Run_key_and_others_stay()
    {
        using (var run = hive.CreateKey(UninstallEntry.RunKeyPath))
        {
            run.SetValue("nth", "\"" + Root + "\\nth.exe\" --at-sign-in");
            run.SetValue("Other", "\"C:\\Apps\\other.exe\"");
        }
        var registry = new RegistryUserHive(hive.Root);

        Assert.True(UninstallEntry.RemoveSignInEntry(registry, Root));

        using var key = hive.Root.OpenSubKey(UninstallEntry.RunKeyPath)!;
        Assert.Equal(new[] { "Other" }, key.GetValueNames());
    }

    // ---- unit U8 (ED-NEW-inst-u8-1): the key the sign-in entry may have to make, and the entry end to end ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void CreateKey_makes_the_key_and_its_parents_and_leaves_an_existing_key_and_its_values_alone()
    {
        var registry = new RegistryUserHive(hive.Root);

        registry.CreateKey(UninstallEntry.RunKeyPath);
        Assert.True(registry.KeyExists(UninstallEntry.RunKeyPath));
        registry.SetString(UninstallEntry.RunKeyPath, "Other", "kept");
        registry.CreateKey(UninstallEntry.RunKeyPath);

        Assert.Equal("kept", registry.GetValue(UninstallEntry.RunKeyPath, "Other"));
        Assert.Null(hive.Root.OpenSubKey(UninstallEntry.KeyPath)); // the uninstall key is still never made
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_sign_in_entry_is_made_read_turned_off_by_Windows_turned_on_again_and_removed_in_the_test_hive()
    {
        var registry = new RegistryUserHive(hive.Root);
        var launch = new LaunchTarget(Root + @"\current\nth.exe", "", Root + @"\current", Root + @"\current\nth.exe", 0, FromStart: true);
        var command = "\"" + Root + @"\current\nth.exe" + "\" --at-sign-in";

        Assert.Equal(SignInChangeOutcome.Changed, SignInEntry.Enable(registry, Root, launch).Outcome); // the profile had no Run key at all
        Assert.Equal(new SignInState(SignInStatus.On, command), SignInEntry.Read(registry, Root));
        Assert.Equal<(object?, RegistryValueKind)>((command, RegistryValueKind.String), Read(UninstallEntry.RunKeyPath, "nth"));

        // what the Startup apps page writes when the user turns the entry off: a REG_BINARY whose first byte is 3
        using (var approved = hive.CreateKey(SignInEntry.ApprovedKeyPath))
            approved.SetValue("nth", new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, RegistryValueKind.Binary);
        Assert.Equal(SignInStatus.DisabledInWindows, SignInEntry.Read(registry, Root).Status);

        Assert.Equal(SignInChangeOutcome.Changed, SignInEntry.Enable(registry, Root, launch).Outcome);
        Assert.Equal(SignInStatus.On, SignInEntry.Read(registry, Root).Status);
        using (var approved = hive.Root.OpenSubKey(SignInEntry.ApprovedKeyPath)!)
            Assert.Null(approved.GetValue("nth"));

        Assert.Equal(SignInChangeOutcome.Changed, SignInEntry.Disable(registry, Root).Outcome);
        Assert.Equal(SignInStatus.Off, SignInEntry.Read(registry, Root).Status);
        using var run = hive.Root.OpenSubKey(UninstallEntry.RunKeyPath)!;
        Assert.Empty(run.GetValueNames());
    }
}
