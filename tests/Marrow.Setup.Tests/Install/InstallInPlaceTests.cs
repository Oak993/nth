using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Install;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 10, "Gate routing" (an install at %LOCALAPPDATA%\NthApp is updated in place,
// not duplicated at the new root), unit U10 (ED-NEW-inst-u10-1). nth 1.0.0 lives at <LOCALAPPDATA>\NthApp; the window's route
// names that folder as the target, the window's install run hands it to Setup.exe as --installto, the fake Setup.exe
// writes there and nowhere else, and the uninstall entry is re-pointed at that same folder. The fake machine is a temp folder
// under a long non-ASCII user name; no real Setup.exe, registry or shell is touched.
public sealed class InstallInPlaceTests
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Decision", "ED-247")]
    public void An_older_install_at_NthApp_is_updated_where_it_is_through_the_windows_run_and_the_new_root_is_never_made()
    {
        using var scenario = new InstallScenario(VelopackScenarios.Build("upgrade"), update: true, rootUnderLocal: "NthApp");
        var route = SetupFlow.Route("1.1.0", "NthApp", [], scenario.Local, scenario.Local, new FakeUninstallKeys());
        Assert.Equal(SetupScreen.Update, route.Screen);
        Assert.Equal(scenario.Root, route.TargetRoot);
        Assert.Equal(InstallScenario.OldVersion, route.Installed.Version);
        Assert.Equal(Path.Combine(scenario.Local, "NthApp"), scenario.Request.Root);

        scenario.Play();

        var command = Assert.IsType<VelopackCommand>(scenario.Runner.Command);
        Assert.Equal(["--silent", "--log", scenario.Request.LogPath, "--installto", route.TargetRoot], command.Arguments);
        Assert.Equal(route.TargetRoot, command.InstallRoot);
        Assert.True(File.Exists(Path.Combine(route.TargetRoot, "current", "sq.version")));
        Assert.False(Directory.Exists(Path.Combine(scenario.Local, "Programs", "nth")), "an update never makes a second copy at the new root");
        Assert.Null(scenario.Runner.RollbackFolder);
        Assert.True(scenario.Hive.IsRepointed(route.TargetRoot));
        Assert.Equal(InstallPhase.Succeeded, scenario.Frames[^1].Frame.Phase);
        Assert.Equal(1, scenario.After.Calls);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_program_gives_the_run_the_root_it_decided_and_nothing_else()
    {
        // Program.InstallFlowFor is private and starts a real Setup.exe, so its wiring is read from the source; where the run goes is
        // Program.RunRoot's, tested below (VELOPACK_CHECK.md finding 2, ED-NEW-inst-fix-1: the folder chosen in Options was ignored)
        var source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Program.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("new InstallRequest(update, RunRoot(route, session.InstallRoot), route.Installed.Version, InstallOptions.ForAction(route.Action, session.DesktopShortcut), folder, sizes)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new InstallRequest(update, route.TargetRoot,", source, StringComparison.Ordinal);
        Assert.Contains("route = SetupFlow.Route(version, LegacyGateHost.Metadata(\"InstallerAppId\"), args, AppContext.BaseDirectory, localAppData, new RegistryUninstallKeys());", source, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("Install", @"D:\Apps\nth", @"D:\Apps\nth")]
    [InlineData("Install", "", null)]
    [InlineData("Install", "   ", null)]
    [InlineData("Upgrade", @"D:\Apps\nth", null)]
    public void A_fresh_install_goes_to_the_folder_chosen_in_Options_and_an_update_stays_where_nth_is(string action, string chosen, string? expected)
    {
        var target = Path.Combine(Path.GetTempPath(), "nth-tests", "route-root");
        var route = new SetupRoute(SetupScreen.Welcome, Enum.Parse<InstallerAction>(action), new InstalledRoot(target, null, false), target);

        Assert.Equal(expected ?? target, Program.RunRoot(route, chosen));
    }
}
