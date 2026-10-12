using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 and VELOPACK_CHECK.md checks 5, 8 and 15, unit U5 (ED-NEW-inst-u5-1).
// The Apps-list entry Velopack writes, re-pointed to nth's own uninstaller with NoModify, NoRepair and the measured size,
// on FakeUserRegistry: only what differs is written, a missing or foreign key is never touched or created, and Velopack's
// rewrite after an update is undone. The footprint is measured in a temporary folder; the sign-in entry is removed only
// when it starts nth. Nothing here touches the real HKCU.
public sealed class UninstallEntryTests : IDisposable
{
    private const string Root = @"C:\Users\Ana\AppData\Local\Programs\nth";
    private const string Ours = "\"" + Root + "\\nth Uninstall.exe\" --uninstall";
    private const string OursQuiet = Ours + " --silent";

    private readonly string temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(temp, recursive: true); }
        catch (IOException) { } // a temporary folder; Windows cleans %TEMP%
    }

    private static FakeUserRegistry Installed()
    {
        var registry = new FakeUserRegistry();
        registry.WriteVelopackUninstallKey(Root);
        return registry;
    }

    private static object? Value(IUserRegistry registry, string name) => registry.GetValue(UninstallEntry.KeyPath, name);

    private void WriteFile(string relative, int bytes)
    {
        var path = Path.Combine(temp, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_key_is_Velopacks_and_the_strings_name_nths_uninstaller_in_the_root()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NthApp", UninstallEntry.KeyPath);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", UninstallEntry.RunKeyPath);
        Assert.Equal("nth", UninstallEntry.SignInValueName);
        Assert.Equal(Ours, UninstallEntry.UninstallCommand(Root));
        Assert.Equal(OursQuiet, UninstallEntry.QuietUninstallCommand(Root));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Expected_holds_the_strings_only_while_the_uninstaller_exists_and_never_the_site_links()
    {
        var full = UninstallEntry.Expected(Root, 430_080, uninstallerPresent: true);
        Assert.Equal(new[] { "UninstallString", "QuietUninstallString", "NoModify", "NoRepair", "EstimatedSize" }, full.Select(v => v.Key));
        Assert.Equal(new object[] { Ours, OursQuiet, 1, 1, 430_080 }, full.Select(v => v.Value));

        var noUninstaller = UninstallEntry.Expected(Root, null, uninstallerPresent: false);
        Assert.Equal(new[] { "NoModify", "NoRepair" }, noUninstaller.Select(v => v.Key));
        Assert.DoesNotContain(full, v => v.Key is "URLInfoAbout" or "HelpLink" or "DisplayName" or "Publisher");
        Assert.Equal(int.MaxValue, UninstallEntry.Expected(Root, long.MaxValue, false).Single(v => v.Key == "EstimatedSize").Value);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Repoint_moves_Velopacks_key_to_nths_uninstaller_and_leaves_its_other_values()
    {
        var registry = Installed();

        var result = UninstallEntry.Repoint(registry, Root, 430_080, uninstallerPresent: true);

        Assert.Equal(new UninstallRepointResult(KeyFound: true, Ours: true, Writes: 3, UninstallerPresent: true), result);
        Assert.Equal(Ours, Value(registry, "UninstallString"));
        Assert.Equal(OursQuiet, Value(registry, "QuietUninstallString"));
        Assert.Equal(430_080, Value(registry, "EstimatedSize"));
        Assert.Equal(1, Value(registry, "NoModify"));
        Assert.Equal(1, Value(registry, "NoRepair"));
        Assert.Equal("nth", Value(registry, "DisplayName"));
        Assert.Equal(Root + @"\current\nth.exe", Value(registry, "DisplayIcon"));
        Assert.Equal(Root, Value(registry, "InstallLocation"));
        Assert.Equal(3, registry.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_second_repoint_writes_nothing_and_Velopacks_rewrite_after_an_update_is_undone()
    {
        var registry = Installed();
        UninstallEntry.Repoint(registry, Root, 430_080, uninstallerPresent: true);

        Assert.Equal(0, UninstallEntry.Repoint(registry, Root, 430_080, uninstallerPresent: true).Writes);

        registry.WriteVelopackUninstallKey(Root, currentSizeKb: 310_000); // Update.exe, before --veloapp-updated (check 8)
        var again = UninstallEntry.Repoint(registry, Root, 440_000, uninstallerPresent: true);
        Assert.Equal(3, again.Writes);
        Assert.Equal(Ours, Value(registry, "UninstallString"));
        Assert.Equal(440_000, Value(registry, "EstimatedSize"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Without_nths_uninstaller_Velopacks_working_strings_stay()
    {
        var registry = Installed();

        var result = UninstallEntry.Repoint(registry, Root, 430_080, uninstallerPresent: false);

        Assert.Equal(new UninstallRepointResult(true, true, 1, false), result);
        Assert.Equal("\"" + Root + "\\Update.exe\" --uninstall", Value(registry, "UninstallString"));
        Assert.Equal("\"" + Root + "\\Update.exe\" --uninstall --silent", Value(registry, "QuietUninstallString"));
        Assert.Equal(430_080, Value(registry, "EstimatedSize"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unknown_size_keeps_Velopacks()
    {
        var registry = Installed();

        UninstallEntry.Repoint(registry, Root, estimatedSizeKb: null, uninstallerPresent: true);

        Assert.Equal(300_000, Value(registry, "EstimatedSize"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_missing_key_is_never_created()
    {
        var registry = new FakeUserRegistry();

        var result = UninstallEntry.Repoint(registry, Root, 430_080, uninstallerPresent: true);

        Assert.Equal(new UninstallRepointResult(false, false, 0, true), result);
        Assert.False(registry.KeyExists(UninstallEntry.KeyPath));
        Assert.Equal(0, registry.Writes);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(@"D:\Apps\nth")]
    [InlineData(@"C:\Users\Ana\AppData\Local\Programs\nth-old")]
    [InlineData("")]
    public void A_key_for_another_folder_is_left_as_it_is(string location)
    {
        var registry = Installed();
        registry.Seed(UninstallEntry.KeyPath, "InstallLocation", location);

        var result = UninstallEntry.Repoint(registry, Root, 430_080, uninstallerPresent: true);

        Assert.Equal(new UninstallRepointResult(true, false, 0, true), result);
        Assert.Equal(0, registry.Writes);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(Root + @"\")]
    [InlineData(@"c:\users\ana\appdata\local\programs\NTH")]
    public void The_root_matches_with_a_trailing_separator_or_another_case(string location)
    {
        var registry = Installed();
        registry.Seed(UninstallEntry.KeyPath, "InstallLocation", location);

        Assert.True(UninstallEntry.Repoint(registry, Root, 1, uninstallerPresent: true).Ours);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_location_that_is_not_text_counts_as_another_install()
    {
        var registry = Installed();
        registry.Seed(UninstallEntry.KeyPath, "InstallLocation", 7);

        Assert.False(UninstallEntry.Repoint(registry, Root, 1, uninstallerPresent: true).Ours);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_footprint_is_the_newest_full_package_the_app_and_Update_exe_in_KB()
    {
        WriteFile(@"current\nth.exe", 300 * 1024);
        WriteFile(@"current\runtimes\win-x64\native\lib.dll", 100 * 1024);
        WriteFile(@"packages\NthApp-1.0.0-full.nupkg", 999 * 1024);
        WriteFile(@"packages\NthApp-1.0.1-full.nupkg", 200 * 1024);
        WriteFile(@"packages\NthApp-1.0.1-delta.nupkg", 50 * 1024);
        WriteFile("Update.exe", 20 * 1024);
        WriteFile("nth.exe", 7 * 1024); // the root stub is not counted (T_disk's formula)
        File.SetLastWriteTimeUtc(Path.Combine(temp, @"packages\NthApp-1.0.0-full.nupkg"), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(temp, @"packages\NthApp-1.0.1-full.nupkg"), new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(300 + 100 + 200 + 20, UninstallEntry.MeasureFootprintKb(temp));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_footprint_is_unknown_without_the_app_folder()
    {
        WriteFile("Update.exe", 20 * 1024);

        Assert.Null(UninstallEntry.MeasureFootprintKb(temp));
        Assert.Null(UninstallEntry.MeasureFootprintKb(Path.Combine(temp, "missing")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_footprint_without_packages_or_Update_exe_is_the_app_alone()
    {
        WriteFile(@"current\nth.exe", 64 * 1024);

        Assert.Equal(64, UninstallEntry.MeasureFootprintKb(temp));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("\"C:\\Program Files\\nth\\nth.exe\" --at-sign-in", @"C:\Program Files\nth\nth.exe")]
    [InlineData(@"C:\nth\nth.exe --at-sign-in", @"C:\nth\nth.exe")]
    [InlineData(@"C:\nth\nth.exe", @"C:\nth\nth.exe")]
    [InlineData("  \"C:\\nth\\nth.exe\"  ", @"C:\nth\nth.exe")]
    [InlineData("\"\"", null)]
    [InlineData("\"C:\\nth\\nth.exe", null)]
    [InlineData("   ", null)]
    public void The_program_of_a_command_line_is_its_first_token(string command, string? program)
    {
        Assert.Equal(program, UninstallEntry.CommandExecutable(command));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_sign_in_entry_goes_only_when_it_starts_a_program_in_the_root()
    {
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, "nth", "\"" + Root + "\\current\\nth.exe\" --at-sign-in");
        registry.Seed(UninstallEntry.RunKeyPath, "Other", "\"C:\\Apps\\other.exe\"");

        Assert.True(UninstallEntry.RemoveSignInEntry(registry, Root));
        Assert.Null(registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
        Assert.Equal("\"C:\\Apps\\other.exe\"", registry.GetValue(UninstallEntry.RunKeyPath, "Other"));
        Assert.False(UninstallEntry.RemoveSignInEntry(registry, Root));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("\"C:\\Apps\\nth\\nth.exe\"")]
    [InlineData("\"C:\\Users\\Ana\\AppData\\Local\\Programs\\nth-old\\nth.exe\"")]
    [InlineData("nth.exe --at-sign-in")]
    [InlineData("")]
    public void A_sign_in_entry_named_nth_that_starts_something_else_stays(string command)
    {
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, "nth", command);

        Assert.False(UninstallEntry.RemoveSignInEntry(registry, Root));
        Assert.Equal(command, registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
        Assert.Equal(0, registry.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void No_Run_key_or_a_DWORD_value_is_not_an_error()
    {
        Assert.False(UninstallEntry.RemoveSignInEntry(new FakeUserRegistry(), Root));
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, "nth", 1);
        Assert.False(UninstallEntry.RemoveSignInEntry(registry, Root));
    }
}
