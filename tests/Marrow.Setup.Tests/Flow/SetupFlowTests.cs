using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Flow;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 (steps 3 and 5) and 10, "Gate routing"; unit U1 (ED-NEW-inst-u1-1).
// InstallerGate's decision (ED-247, OD-0072, its own tests unchanged in Marrow.Core.Tests) picks the first screen and
// keeps its exit codes; the installed root comes from the uninstall entry (a fake hive here, never the real HKCU), so the
// 1.0.0 install at %LOCALAPPDATA%\NthApp is updated where it is. Install roots are fake folders in a temp directory; the
// "Update.exe" in them is an empty file that nothing runs.
public sealed class SetupFlowTests : IDisposable
{
    private const string PackageId = "NthApp";
    private readonly string localAppData = Path.Combine(Path.GetTempPath(), "nth-setup-flow-" + Guid.NewGuid().ToString("N"));
    private readonly FakeUninstallKeys keys = new();

    public void Dispose()
    {
        if (Directory.Exists(localAppData)) Directory.Delete(localAppData, recursive: true);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Nothing_installed_opens_Welcome_for_the_new_per_user_root_and_closing_it_exits_0()
    {
        var route = Route("1.1.0");

        Assert.Equal(SetupScreen.Welcome, route.Screen);
        Assert.Equal(InstallerAction.Install, route.Action);
        Assert.Equal(Path.Combine(localAppData, "Programs", "nth"), route.TargetRoot);
        Assert.Null(route.Installed.Version);
        Assert.Equal(0, route.ExitCodeWhenClosed);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void An_older_install_at_NthApp_opens_Update_and_is_updated_in_place_not_at_the_new_root()
    {
        var legacy = Install(Path.Combine(localAppData, PackageId), "1.0.0");

        var route = Route("1.1.0");

        Assert.Equal(SetupScreen.Update, route.Screen);
        Assert.Equal(InstallerAction.Upgrade, route.Action);
        Assert.Equal(legacy, route.TargetRoot);
        Assert.Equal("1.0.0", route.Installed.Version);
        Assert.False(route.Installed.FromUninstallKey);
        Assert.Equal(0, route.ExitCodeWhenClosed);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_uninstall_entrys_folder_wins_over_NthApp()
    {
        Install(Path.Combine(localAppData, PackageId), "1.0.0");
        var moved = Install(Path.Combine(localAppData, "Programs", "nth"), "1.0.5");
        keys.Locations[PackageId] = "\"" + moved + "\\\"";

        var route = Route("1.1.0");

        Assert.Equal(SetupScreen.Update, route.Screen);
        Assert.Equal(moved, route.TargetRoot);
        Assert.Equal("1.0.5", route.Installed.Version);
        Assert.True(route.Installed.FromUninstallKey);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_stale_uninstall_entry_is_passed_over()
    {
        var legacy = Install(Path.Combine(localAppData, PackageId), "1.0.0");
        keys.Locations[PackageId] = Path.Combine(localAppData, "gone");

        var route = Route("1.1.0");

        Assert.Equal(legacy, route.TargetRoot);
        Assert.False(route.Installed.FromUninstallKey);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_same_version_opens_Up_to_date_and_exits_0()
    {
        Install(Path.Combine(localAppData, PackageId), "1.1.0");

        var route = Route("1.1.0");

        Assert.Equal(SetupScreen.UpToDate, route.Screen);
        Assert.Equal(InstallerAction.AlreadyUpToDate, route.Action);
        Assert.Equal(0, route.ExitCodeWhenClosed);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_newer_install_opens_Newer_installed_and_exits_with_the_gates_refusal()
    {
        Install(Path.Combine(localAppData, PackageId), "1.2.0");

        var route = Route("1.1.0");

        Assert.Equal(SetupScreen.NewerInstalled, route.Screen);
        Assert.Equal(InstallerAction.RefuseNewerInstalled, route.Action);
        Assert.Equal(InstallerGate.RefusedExitCode, route.ExitCodeWhenClosed);
        Assert.Equal(1, route.ExitCodeWhenClosed);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Installto_is_the_root_as_given_relative_to_the_setups_folder()
    {
        var chosen = Install(Path.Combine(localAppData, "elsewhere", "nth"), "1.0.0");
        keys.Locations[PackageId] = Install(Path.Combine(localAppData, PackageId), "1.0.9");

        var route = SetupFlow.Route("1.1.0", PackageId, ["--installto", Path.Combine("elsewhere", "nth")], localAppData, localAppData, keys);

        Assert.Equal(SetupScreen.Update, route.Screen);
        Assert.Equal(chosen, route.TargetRoot);
        Assert.Equal("1.0.0", route.Installed.Version);

        var fresh = SetupFlow.Route("1.1.0", PackageId, ["-t", Path.Combine(localAppData, "new")], localAppData, localAppData, keys);
        Assert.Equal(SetupScreen.Welcome, fresh.Screen);
        Assert.Equal(Path.Combine(localAppData, "new"), fresh.TargetRoot);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Uninstall_mode_opens_Uninstall_only_before_the_app_argument_separator()
    {
        Install(Path.Combine(localAppData, PackageId), "1.1.0");

        Assert.Equal(SetupScreen.Uninstall, SetupFlow.Route("1.1.0", PackageId, ["--uninstall"], localAppData, localAppData, keys).Screen);
        Assert.Equal(SetupScreen.UpToDate, SetupFlow.Route("1.1.0", PackageId, ["--", "--uninstall"], localAppData, localAppData, keys).Screen);
        Assert.Equal(0, SetupFlow.ExitCodeWhenClosed(SetupScreen.Uninstall));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_setup_version_that_is_not_SemVer_is_refused_before_anything_is_decided()
    {
        Assert.Throws<FormatException>(() => Route(""));
        var problem = SetupFlow.Problem(localAppData);
        Assert.Equal(SetupScreen.Problem, problem.Screen);
        Assert.Equal(1, problem.ExitCodeWhenClosed);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Only_a_completed_signed_or_unsigned_check_allows_install()
    {
        Assert.True(SetupFlow.BlocksInstall(new SignatureResult(SignatureState.Invalid, null, unchecked((int)0x80096010))));
        Assert.False(SetupFlow.BlocksInstall(new SignatureResult(SignatureState.Unsigned, null, unchecked((int)0x800B0100))));
        Assert.False(SetupFlow.BlocksInstall(new SignatureResult(SignatureState.Signed, "Publisher", 0)));
        Assert.True(SetupFlow.BlocksInstall(SignatureResult.Pending));
    }

    private SetupRoute Route(string version) => SetupFlow.Route(version, PackageId, [], localAppData, localAppData, keys);

    private static string Install(string root, string version)
    {
        Directory.CreateDirectory(Path.Combine(root, "current"));
        File.WriteAllBytes(Path.Combine(root, "Update.exe"), []);
        File.WriteAllText(Path.Combine(root, "current", "sq.version"), $"<package><metadata><id>NthApp</id><version>{version}</version></metadata></package>");
        return root;
    }
}
