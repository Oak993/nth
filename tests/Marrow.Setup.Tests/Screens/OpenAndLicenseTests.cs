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

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Open_nth_takes_Start_s_launch_target_through_the_shell_the_program_binds_and_the_roots_own_without_one()
    {
        // ED-NEW-inst-picks-2: the real shell is constructed in the two Program files only (App.Tests' ShellLinksBindingTests), so the actions take a factory
        var shell = new Marrow.Testing.FakeShellLinks();
        var root = Marrow.Testing.FakeShellLinks.DefaultRoot;
        var exe = root + @"\current\nth.exe";
        shell.AddFile(exe);
        shell.Seed(Marrow.Testing.FakeShellLinks.StartLink, new ShellLink(exe, "--from-start", root + @"\current", exe, 0));
        var bound = new WindowsSetupActions(area.Temp, () => shell).ReadLaunchTarget(root);

        Assert.Equal(exe, bound.Target);
        Assert.Equal("--from-start", bound.Arguments);
        Assert.True(bound.FromStart);

        var own = DesktopShortcut.LaunchTarget(root, null, File.Exists, Environment.ExpandEnvironmentVariables);
        Assert.Equal(own, new WindowsSetupActions(area.Temp).ReadLaunchTarget(root)); // a session built without a shell
        var asked = 0;
        var unreachable = new WindowsSetupActions(area.Temp, () => { asked++; throw new System.Runtime.InteropServices.COMException("the shell does not answer", unchecked((int)0x80004005)); });
        Assert.Equal(own, unreachable.ReadLaunchTarget(root)); // and one whose shell cannot be reached
        Assert.Equal(1, asked);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_actions_never_construct_the_real_shell_and_the_program_binds_it()
    {
        var actions = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Screens", "SetupActions.cs"));
        var program = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Program.cs"));

        Assert.DoesNotContain("new ShellLinks(", actions, StringComparison.Ordinal);
        Assert.Contains("actions: new WindowsSetupActions(() => new ShellLinks())", program, StringComparison.Ordinal);
    }

    private static LaunchTarget Launch() =>
        new(@"C:\Users\a\AppData\Local\nth\current\nth.exe", "", @"C:\Users\a\AppData\Local\nth\current", @"C:\Users\a\AppData\Local\nth\current\nth.exe", 0, FromStart: true);
}
