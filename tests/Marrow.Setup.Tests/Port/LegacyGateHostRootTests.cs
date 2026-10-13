using System.Reflection;
using Marrow.Core.Support;
using Marrow.Setup.Port;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer merge finding M4 (ED-NEW-inst-merge-4). U5's post-exit hook and U7's fresh root
// were written against the old gate host in Program.cs; U1 had moved that host to Port/LegacyGateHost.cs. These pin the port:
// the host aims the post-exit steps at the folder the gate itself decides about and hands to Setup.exe, never Velopack's
// old default root, and runs them only after Setup.exe exits 0. Nothing is installed or run; HKCU is only read.
public sealed class LegacyGateHostRootTests
{
    private static readonly MethodInfo RootToInstall = typeof(LegacyGateHost).GetMethod("RootToInstall", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(LegacyGateHost), "RootToInstall");

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_explicit_install_folder_is_where_the_post_exit_steps_run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nth-tests", "gate-root-" + Guid.NewGuid().ToString("N"));

        var root = (string)RootToInstall.Invoke(null, [new[] { "--installto", folder }])!;

        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)));
        Assert.False(Directory.Exists(folder)); // deciding writes nothing
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Without_an_install_folder_the_host_uses_the_gates_own_decision_with_the_fresh_root()
    {
        var identity = LegacyGateHost.Identity();

        var root = (string)RootToInstall.Invoke(null, [Array.Empty<string>()])!;

        Assert.NotNull(identity.FreshInstallRoot);
        Assert.Equal(InstallerGate.FindInstall(identity, null).Root, root, ignoreCase: true);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_host_sets_the_root_before_the_gate_runs_and_runs_the_post_exit_steps_only_after_exit_zero()
    {
        var gate = File.ReadAllText(Path.Combine(Repository(), "src", "Marrow.Setup", "Port", "LegacyGateHost.cs"));

        var set = gate.IndexOf("host.InstallRoot = RootToInstall(args);", StringComparison.Ordinal);
        var run = gate.IndexOf("return InstallerGate.Run(host, Identity(), args);", StringComparison.Ordinal);
        Assert.True(set >= 0 && run > set, "the post-exit root is set before the gate decides and runs Setup.exe");
        Assert.Contains("InstallerGate.FindInstall(identity, InstallerGate.ReadInstallTo(args, identity.LaunchDirectory)).Root", gate, StringComparison.Ordinal);
        // ED-NEW-inst-fix-3: the steps also read the hook's result beside the options the gate wrote, and never run after --help
        Assert.Contains("if (RunsAfterExit(help, process.ExitCode) && InstallRoot is { } root) Program.FinishAfterSetupExit(root, InstallOptions.ResultPathFor(options));", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultInstallRoot", gate, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, false)]
    [InlineData(false, -1, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 2, false)]
    public void The_post_exit_steps_follow_only_an_install_or_update_that_exited_zero_never_help(bool help, int exitCode, bool runs) =>
        Assert.Equal(runs, LegacyGateHost.RunsAfterExit(help, exitCode));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Help_changes_nothing_no_preflight_no_options_and_the_engine_is_staged_in_the_named_folder_not_Temp()
    {
        // VELOPACK_CHECK.md "Old branches" items 1 and 2: --help asked a running nth to quit and ran the post-exit steps on the real shell
        // and HKCU; the engine was copied to a GUID folder in %TEMP%.
        var gate = File.ReadAllText(Path.Combine(Repository(), "src", "Marrow.Setup", "Port", "LegacyGateHost.cs"));

        Assert.Contains("var help = InstallerGate.IsHelp(arguments);", gate, StringComparison.Ordinal);
        Assert.Contains("using var preflight = !help && InstallerGate.IsSilent(arguments) && InstallRoot is { } holdRoot", gate, StringComparison.Ordinal);
        Assert.Contains("if (!help) WriteOptions(options, InstallRoot, DesktopShortcut);", gate, StringComparison.Ordinal);
        Assert.Contains("using var scope = StagingScope.Create(localAppData, Path.GetTempPath(), Metadata(\"InstallerVersion\"));", gate, StringComparison.Ordinal);
        Assert.Contains("start.Environment[InstallOptions.EnvironmentVariable] = options;", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid()", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("\"nth Setup-\"", gate, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(true, """{"version":1,"kind":"install","desktopShortcut":true}""")]
    [InlineData(false, """{"version":1,"kind":"install","desktopShortcut":false}""")]
    public void A_silent_install_writes_the_options_the_hook_reads_so_the_desktop_shortcut_is_made_by_default(bool desktopShortcut, string json)
    {
        // VELOPACK_CHECK.md "Old branches" item 3: --silent wrote no install-options.json, so the hook made no desktop shortcut
        using var area = new SweepArea();
        var staging = Directory.CreateDirectory(Path.Combine(area.Base, "staging")).FullName;
        var options = Path.Combine(staging, InstallOptions.OptionsFileName);
        File.WriteAllText(InstallOptions.ResultPathFor(options), "{\"version\":1,\"desktopShortcut\":\"created\"}"); // an earlier run's result

        LegacyGateHost.WriteOptions(options, Path.Combine(area.Base, "fresh"), desktopShortcut);

        Assert.Equal(json, File.ReadAllText(options));
        Assert.False(File.Exists(InstallOptions.ResultPathFor(options)), "an earlier result never stands for this run");
        Assert.False(Directory.Exists(Path.Combine(area.Base, "fresh")), "writing the options installs nothing");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_silent_update_writes_update_options_so_no_desktop_shortcut_is_added()
    {
        using var area = new SweepArea();
        var staging = Directory.CreateDirectory(Path.Combine(area.Base, "staging")).FullName;
        var installed = Path.Combine(area.Base, "nth");
        Directory.CreateDirectory(Path.Combine(installed, "current"));
        File.WriteAllBytes(Path.Combine(installed, "Update.exe"), []);
        File.WriteAllText(Path.Combine(installed, "current", "sq.version"), "<package><metadata><id>NthApp</id><version>1.0.0</version></metadata></package>");
        var options = Path.Combine(staging, InstallOptions.OptionsFileName);

        LegacyGateHost.WriteOptions(options, installed, desktopShortcut: true);

        Assert.Equal("""{"version":1,"kind":"update"}""", File.ReadAllText(options));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_shortcut_choice_comes_from_the_arguments_like_the_window()
    {
        var gate = File.ReadAllText(Path.Combine(Repository(), "src", "Marrow.Setup", "Port", "LegacyGateHost.cs"));

        Assert.Contains("var host = new LegacyGateHost { DesktopShortcut = InstallOptions.DesktopShortcutChoice(args) };", gate, StringComparison.Ordinal);
        Assert.True(new LegacyGateHost().DesktopShortcut, "on by default (PLAN.md 8a)");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_gate_host_and_its_identity_have_one_home_and_Program_keeps_no_copy()
    {
        // ED-NEW-inst-merge-6: U7's tests reflected over Program.Identity() after U1 had moved it, and found null
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var hosts = typeof(LegacyGateHost).Assembly.GetTypes().Where(t => typeof(IInstallerGateHost).IsAssignableFrom(t) && !t.IsInterface).ToArray();

        Assert.Equal(typeof(LegacyGateHost), Assert.Single(hosts));
        Assert.Null(typeof(Program).GetMethod("Identity", Any));
        Assert.NotNull(typeof(LegacyGateHost).GetMethod(nameof(LegacyGateHost.Identity), BindingFlags.Public | BindingFlags.Static));
        Assert.DoesNotContain("\"nth Setup-\"", File.ReadAllText(Path.Combine(Repository(), "src", "Marrow.Setup", "Program.cs")), StringComparison.Ordinal);
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }
}
