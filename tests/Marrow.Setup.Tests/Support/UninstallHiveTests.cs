using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fixtures;
using Microsoft.Win32;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 (the uninstall entry: "after Setup.exe exits, nth Setup rewrites
// UninstallString and QuietUninstallString to <root>\nth Uninstall.exe"; the uninstall flow) and 10, unit U6
// (ED-NEW-inst-u6-1). On a private RegLoadAppKey hive in a temporary file through the setup's own RegistryUserHive (never
// the real HKCU): the entry nth Setup re-points is put back after Velopack rewrites it on an update, and the uninstall, with a
// fake Update.exe that deletes its key as the real one does, ends with no key, no sign-in value into the root, another
// program's Run value untouched, and nothing in the leftover check; a key Velopack left is named there.
public sealed class UninstallHiveTests : IDisposable
{
    private readonly TestRegistryHive hive = new();
    private readonly UninstallArea area = new();

    public void Dispose()
    {
        area.Dispose();
        hive.Dispose();
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_entry_is_repointed_and_put_back_after_a_Velopack_rewrite_in_the_test_hive()
    {
        var registry = new RegistryUserHive(hive.Root);
        VelopackWritesItsKey();

        Assert.True(UninstallEntry.Repoint(registry, area.Root, 420_000, uninstallerPresent: true).Ours);
        Assert.Equal(UninstallEntry.UninstallCommand(area.Root), Value(UninstallEntry.KeyPath, "UninstallString"));
        Assert.Equal("\"" + Path.Combine(area.Root, "nth Uninstall.exe") + "\" --uninstall --silent", Value(UninstallEntry.KeyPath, "QuietUninstallString"));

        VelopackWritesItsKey(); // an update: Update.exe writes its own strings again
        Assert.Equal("\"" + Path.Combine(area.Root, "Update.exe") + "\" --uninstall", Value(UninstallEntry.KeyPath, "UninstallString"));
        var again = UninstallEntry.Repoint(registry, area.Root, 420_000, uninstallerPresent: true);

        Assert.Equal(3, again.Writes); // the two strings and Velopack's own size
        Assert.Equal(UninstallEntry.UninstallCommand(area.Root), Value(UninstallEntry.KeyPath, "UninstallString"));
        Assert.Equal(UninstallEntry.QuietUninstallCommand(area.Root), Value(UninstallEntry.KeyPath, "QuietUninstallString"));
        Assert.Equal(420_000, Value(UninstallEntry.KeyPath, "EstimatedSize"));
        Assert.Equal("nth", Value(UninstallEntry.KeyPath, "DisplayName"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_uninstall_leaves_no_key_no_sign_in_value_and_another_programs_value_alone_in_the_test_hive()
    {
        var registry = new RegistryUserHive(hive.Root);
        VelopackWritesItsKey();
        UninstallEntry.Repoint(registry, area.Root, 420_000, uninstallerPresent: true);
        using (var run = hive.CreateKey(UninstallEntry.RunKeyPath))
        {
            run.SetValue(UninstallEntry.SignInValueName, "\"" + InstallLayout.CurrentExe(area.Root) + "\" --at-sign-in");
            run.SetValue("Other", "\"C:\\Apps\\other.exe\"");
        }
        area.Runner.RemovesKey = false;
        area.Runner.Before = _ => hive.Root.DeleteSubKeyTree(UninstallEntry.KeyPath); // what the real Update.exe deletes

        var report = await new Uninstaller(area.Ports() with { Registry = registry }).RunAsync(area.Root, wipeData: false);

        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
        Assert.True(report.SignInRemoved);
        Assert.Null(hive.Root.OpenSubKey(UninstallEntry.KeyPath));
        using (var run = hive.Root.OpenSubKey(UninstallEntry.RunKeyPath)!)
            Assert.Equal(["Other"], run.GetValueNames());
        Assert.Empty(report.Leftovers!);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_key_Velopack_left_in_the_test_hive_is_named_in_the_leftover_check()
    {
        var registry = new RegistryUserHive(hive.Root);
        VelopackWritesItsKey();
        area.Runner.RemovesKey = false;

        var report = await new Uninstaller(area.Ports() with { Registry = registry }).RunAsync(area.Root, wipeData: false);

        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
        Assert.Equal([@"HKCU\" + UninstallEntry.KeyPath], report.Leftovers!);
        Assert.NotNull(hive.Root.OpenSubKey(UninstallEntry.KeyPath)); // nth Setup never deletes Velopack's key itself
    }

    /// <summary>What Velopack 1.2.161 writes for NthApp (registry.rs:9-57), into the test hive.</summary>
    private void VelopackWritesItsKey()
    {
        using var key = hive.CreateKey(UninstallEntry.KeyPath);
        var update = Path.Combine(area.Root, "Update.exe");
        key.SetValue("DisplayIcon", InstallLayout.CurrentExe(area.Root));
        key.SetValue("DisplayName", "nth");
        key.SetValue("DisplayVersion", "1.1.0");
        key.SetValue("InstallLocation", area.Root);
        key.SetValue("Publisher", "oak993");
        key.SetValue("QuietUninstallString", "\"" + update + "\" --uninstall --silent");
        key.SetValue("UninstallString", "\"" + update + "\" --uninstall");
        key.SetValue("EstimatedSize", 300_000, RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private object? Value(string subKey, string name)
    {
        using var key = hive.Root.OpenSubKey(subKey);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }
}
