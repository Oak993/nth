using System.Reflection;
using Marrow.Core.Support;
using Marrow.Setup.Port;

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
        Assert.Contains("if (process.ExitCode == 0 && InstallRoot is { } root) Program.FinishAfterSetupExit(root);", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultInstallRoot", gate, StringComparison.Ordinal);
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }
}
