using Marrow.Core.Support;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a ("Open nth") and 3 ("View license"); VELOPACK_CHECK.md findings 6 and 8
// (ED-NEW-inst-fix-4, ED-NEW-inst-fix-3). Open nth started nth with the setup's own token, so an elevated setup opened an elevated
// nth; View license left a copy under a new name in %TEMP% each time. Nothing is started here: the decision is tested with
// recorded starts, and the licence copy is written to a temporary folder standing in for %TEMP%.
public sealed class OpenAndLicenseTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(false, true, "direct")]
    [InlineData(false, false, "direct")]
    [InlineData(true, true, "user")]
    [InlineData(true, false, "user")]
    public void An_elevated_setup_opens_nth_only_as_the_signed_in_user_and_never_falls_back_to_its_own_token(bool elevated, bool startWorks, string expected)
    {
        var target = Launch();
        var started = new List<string>();

        var opened = WindowsSetupActions.Open(target, elevated, _ => true,
            launch => { started.Add("direct"); Assert.Same(target, launch); return startWorks; },
            launch => { started.Add("user"); Assert.Same(target, launch); return startWorks; });

        Assert.Equal([expected], started);
        Assert.Equal(startWorks, opened);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_missing_nth_starts_nothing(bool elevated)
    {
        var started = 0;

        Assert.False(WindowsSetupActions.Open(Launch(), elevated, _ => false, _ => ++started > 0, _ => ++started > 0));

        Assert.Equal(0, started);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Open_nth_asks_whether_this_setup_is_elevated_and_goes_through_Explorer_when_it_is()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Screens", "SetupActions.cs"));

        Assert.Contains("Open(ReadLaunchTarget(installRoot), Environment.IsPrivilegedProcess, File.Exists, StartDirect, launch => UserShellStart.Start(launch, OpenPatience));", source, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(5), WindowsSetupActions.OpenPatience);
        var shell = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Port", "UserShellStart.cs"));
        Assert.Contains("ShellExecute", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("runas", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Process.Start", shell, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_licence_copy_has_one_fixed_name_in_nth_Setups_temp_folder_and_leaves_with_the_window()
    {
        var path = WindowsSetupActions.WriteLicenseCopy(area.Temp);

        Assert.Equal(Path.Combine(area.Temp, "nth Setup", "LICENSE.txt"), path);
        Assert.Equal(path, WindowsSetupActions.LicenseCopyPath(area.Temp));
        Assert.True(new FileInfo(path!).Length > 0);
        Assert.Equal(path, WindowsSetupActions.WriteLicenseCopy(area.Temp)); // a second View license writes over the same file
        Assert.Single(Directory.EnumerateFileSystemEntries(area.Temp, "*", SearchOption.AllDirectories), entry => File.Exists(entry));

        WindowsSetupActions.RemoveLicenseCopy(area.Temp);

        Assert.Empty(Directory.EnumerateFileSystemEntries(area.Temp));
        WindowsSetupActions.RemoveLicenseCopy(area.Temp); // nothing left: nothing to do
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Removing_the_licence_copy_keeps_nth_Setups_temp_folder_when_something_else_is_in_it()
    {
        var other = area.Put(Path.Combine(area.Temp, "nth Setup", "setup.log"), "the silent install's log");
        WindowsSetupActions.WriteLicenseCopy(area.Temp);

        WindowsSetupActions.RemoveLicenseCopy(area.Temp);

        Assert.False(File.Exists(WindowsSetupActions.LicenseCopyPath(area.Temp)));
        Assert.Equal("the silent install's log", File.ReadAllText(other));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_window_removes_the_licence_copy_when_its_loop_ends()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Program.cs"));

        Assert.Contains("WindowsSetupActions.RemoveLicenseCopy(Path.GetTempPath());", program, StringComparison.Ordinal);
        var actions = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Screens", "SetupActions.cs"));
        Assert.DoesNotContain("GetTempFileName", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid", actions, StringComparison.Ordinal);
    }

    private static LaunchTarget Launch() =>
        new(@"C:\Users\a\AppData\Local\nth\current\nth.exe", "", @"C:\Users\a\AppData\Local\nth\current", @"C:\Users\a\AppData\Local\nth\current\nth.exe", 0, FromStart: true);
}
