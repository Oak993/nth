using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Flow;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 10, "Gate routing", unit U10 (ED-NEW-inst-u10-1). InstallerGate's own tests
// stay in Marrow.Core.Tests; SetupFlowTests pins each route on its own. This matrix puts the two readings of the same
// folders side by side, for every outcome (nothing installed; nth 1.0.0 at %LOCALAPPDATA%\NthApp; an install the uninstall
// entry moved to Programs\nth; the same version; a newer one): the window opens the right screen and closes with the gate's
// exit code, and the --silent gate (no window, no message) runs Setup.exe at that same root, or runs nothing and ends with
// the same code. An install at NthApp is updated where it is, never copied to the new root. The roots are fake folders in a
// temp directory; the gate's host is a recorder and nothing is started.
public sealed class GateRoutingMatrixTests : IDisposable
{
    private const string PackageId = "NthApp";
    private readonly string localAppData = Path.Combine(Path.GetTempPath(), "nth-gate-matrix-" + Guid.NewGuid().ToString("N"));
    private readonly FakeUninstallKeys keys = new();

    public void Dispose()
    {
        if (Directory.Exists(localAppData)) Directory.Delete(localAppData, recursive: true);
    }

    [Theory]
    [InlineData("none", null, SetupScreen.Welcome, InstallerAction.Install, 0, true)]
    [InlineData("NthApp", "1.0.0", SetupScreen.Update, InstallerAction.Upgrade, 0, true)]
    [InlineData("Programs", "1.0.5", SetupScreen.Update, InstallerAction.Upgrade, 0, true)]
    [InlineData("NthApp", "1.1.0", SetupScreen.UpToDate, InstallerAction.AlreadyUpToDate, 0, false)]
    [InlineData("Programs", "1.1.0", SetupScreen.UpToDate, InstallerAction.AlreadyUpToDate, 0, false)]
    [InlineData("NthApp", "1.2.0", SetupScreen.NewerInstalled, InstallerAction.RefuseNewerInstalled, 1, false)]
    [InlineData("Programs", "1.2.0", SetupScreen.NewerInstalled, InstallerAction.RefuseNewerInstalled, 1, false)]
    [Trait("Decision", "OD-0072")]
    [Trait("Decision", "ED-247")]
    public void Each_outcome_opens_its_screen_and_the_silent_gate_decides_the_same_at_the_same_root_with_the_same_exit_code(string where, string? installed, SetupScreen screen, InstallerAction action, int closeCode, bool runsSetup)
    {
        var root = Seed(where, installed);

        var route = SetupFlow.Route("1.1.0", PackageId, [], localAppData, localAppData, keys);
        Assert.Equal(screen, route.Screen);
        Assert.Equal(action, route.Action);
        Assert.Equal(closeCode, route.ExitCodeWhenClosed);

        var silent = new RecordingGateHost();
        var exit = InstallerGate.Run(silent, Identity("1.1.0"), ["--silent"]);

        Assert.Empty(silent.Messages); // no window and no message box
        if (runsSetup)
        {
            var run = Assert.Single(silent.SetupRuns);
            Assert.Equal(["--installto", route.TargetRoot, "--silent"], run); // where the window would install, said explicitly
            Assert.Equal(root ?? Path.Combine(localAppData, "Programs", "nth"), route.TargetRoot, ignoreCase: true);
            Assert.Equal(0, exit);
        }
        else
        {
            Assert.Empty(silent.SetupRuns);
            Assert.Equal(closeCode, exit); // the silent run ends with the code the window closes with
        }
    }

    [Theory]
    [InlineData("none", null)]
    [InlineData("NthApp", "1.0.0")]
    [Trait("Decision", "OD-0072")]
    public void A_failing_Setup_exe_keeps_its_own_exit_code_silently_and_in_the_window_s_gate(string where, string? installed)
    {
        Seed(where, installed);

        foreach (var arguments in new string[][] { ["--silent"], [] })
        {
            var host = new RecordingGateHost(setupExitCode: 1);
            Assert.Equal(1, InstallerGate.Run(host, Identity("1.1.0"), arguments));
            Assert.Single(host.SetupRuns);
            Assert.Empty(host.Messages);
        }
    }

    [Theory]
    [InlineData("1.1.0", false, "nth 1.1.0 is already installed and up to date.")]
    [InlineData("1.2.0", true, "A newer version of nth (1.2.0) is already installed.")]
    [Trait("Decision", "OD-0072")]
    public void Without_silent_the_same_and_the_newer_outcomes_say_so_once_and_run_nothing(string installed, bool warning, string text)
    {
        Seed("NthApp", installed);
        var host = new RecordingGateHost();

        var exit = InstallerGate.Run(host, Identity("1.1.0"), []);

        Assert.Equal(warning ? InstallerGate.RefusedExitCode : 0, exit);
        Assert.Empty(host.SetupRuns);
        Assert.Equal([("nth Setup", text, warning)], host.Messages);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void An_install_at_NthApp_beside_an_empty_new_root_is_updated_where_it_is_and_the_new_root_is_never_made()
    {
        var legacy = Seed("NthApp", "1.0.0")!;
        var host = new RecordingGateHost();

        InstallerGate.Run(host, Identity("1.1.0"), ["--silent"]);
        var route = SetupFlow.Route("1.1.0", PackageId, ["--silent"], localAppData, localAppData, keys);

        Assert.Equal(legacy, route.TargetRoot);
        Assert.Equal(["--installto", legacy, "--silent"], Assert.Single(host.SetupRuns));
        Assert.False(Directory.Exists(Path.Combine(localAppData, "Programs")));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Main_sends_silent_and_help_to_the_gate_before_any_lock_or_window_and_uninstall_mode_before_both()
    {
        // Program.Main starts a real setup when nothing is installed, so the order of its first decisions is read from the source
        var source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "Marrow.Setup", "Program.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int At(string text) => source.IndexOf(text, StringComparison.Ordinal);

        var uninstall = At("if (SetupFlow.IsUninstall(args) && !InstallerGate.IsHelp(args) && UiSmoke.Read(args) is null) return Uninstall(args, started);");
        var gate = At("if (InstallerGate.IsSilent(args) || InstallerGate.IsHelp(args)) return LegacyGateHost.Run(args);");
        var taken = At("instance = SingleInstance.TryAcquire();");
        var window = At("var (exitCode, installChosen) = RunWindow(args, instance, smoke, started);");

        Assert.True(uninstall > 0 && gate > uninstall && taken > gate && window > taken, $"uninstall {uninstall}, gate {gate}, lock {taken}, window {window}");
        Assert.Contains("return installChosen ? LegacyGateHost.Run(args) : exitCode;", source, StringComparison.Ordinal); // an in-window install without the engine ends in the same gate
    }

    private InstallerIdentity Identity(string version) =>
        new("nth", version, Path.Combine(localAppData, PackageId), localAppData, Path.Combine(localAppData, "Programs", "nth"), () => keys.InstallLocation(PackageId));

    /// <summary>nth <paramref name="version"/> at the old default root, or at Programs\nth with the uninstall entry saying so; nothing for "none".</summary>
    private string? Seed(string where, string? version)
    {
        if (where == "none") return null;
        var root = where == "NthApp" ? Path.Combine(localAppData, PackageId) : Path.Combine(localAppData, "Programs", "nth");
        Directory.CreateDirectory(Path.Combine(root, "current"));
        File.WriteAllBytes(Path.Combine(root, "Update.exe"), []);
        File.WriteAllText(Path.Combine(root, "current", "sq.version"), $"<package><metadata><id>NthApp</id><version>{version}</version></metadata></package>");
        if (where == "Programs") keys.Locations[PackageId] = root;
        return root;
    }
}
