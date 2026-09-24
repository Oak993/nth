using System.Globalization;
using System.Text;
using Marrow.App;
using Marrow.Core.Port;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.App;

// Tests for App/AppUpdater.swift (B2 1fbf81e) :412-442 (installerScript), this part's own record
// PR-UPDATE-056, run as a real out-of-process PowerShell script (AppUpdater.InstallerScript) exactly as
// WindowsUpdateInstallBoundary.LaunchDetachedInstaller writes and launches it, over real files under
// MARROW_PORT_DATA_DIR (R-TEST-9). The 0.25 s poll interval and roughly 60 s give-up ceiling (:421, :424)
// are real out-of-process wall-clock waits an out-of-process script cannot see TestRuntime's manual clock
// (R-TEST-5), so these tests drive the script's own fourth, test-only PollIntervalSeconds argument instead
// of waiting a full 60 s for the give-up case.
public sealed class AppUpdaterInstallerScriptTests
{
    // A full path: CreateProcessW's lpApplicationName (ProcessLauncher.ExecutableURL) searches only the
    // calling process's own current directory for a bare module name, never PATH.
    private static readonly string PowerShellExe = Path.Join(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    private static string TempRoot()
    {
        var root = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Join(root, "installer-script", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string WriteScript(string root)
    {
        var path = Path.Combine(root, "install.ps1");
        File.WriteAllText(path, AppUpdater.InstallerScript);
        return path;
    }

    /// <summary>A real child process that exits almost immediately, then really has exited by the time its pid
    /// is handed to the installer script (:421 kill -0 must find nothing).</summary>
    private static int SpawnAndAwaitExit()
    {
        var child = new ProcessLauncher { ExecutableURL = PowerShellExe, Arguments = ["-NoProfile", "-Command", "exit 0"] };
        child.Run();
        var pid = child.ProcessIdentifier;
        child.WaitUntilExit();
        return pid;
    }

    private static ProcessLauncher SpawnLongLived()
    {
        var child = new ProcessLauncher
        {
            ExecutableURL = PowerShellExe,
            Arguments = ["-NoProfile", "-Command", "Start-Sleep -Seconds 120"],
        };
        child.Run();
        return child;
    }

    private static ProcessLauncher RunInstaller(string script, int pid, string newPath, string dest, double pollIntervalSeconds)
    {
        var launcher = new ProcessLauncher
        {
            ExecutableURL = PowerShellExe,
            Arguments =
            [
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                pid.ToString(CultureInfo.InvariantCulture), newPath, dest,
                pollIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ],
            StandardOutput = new ProcessPipe(),
        };
        launcher.StandardError = launcher.StandardOutput;
        launcher.Run();
        return launcher;
    }

    private static string ReadAllOutput(ProcessLauncher launcher) =>
        Encoding.UTF8.GetString(launcher.StandardOutput!.ReadDataToEndOfFile());

    [Fact]
    [Trait("Req", "PR-UPDATE-056")] // App/AppUpdater.swift:420-438
    public void After_the_watched_process_exits_it_swaps_a_stale_backup_aside_and_cleans_up_on_success()
    {
        var root = TempRoot();
        var dest = Path.Combine(root, "Marrow Code");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "old.txt"), "old");
        var staging = Path.Combine(root, "staging");
        var neu = Path.Combine(staging, "Marrow Code");
        Directory.CreateDirectory(neu);
        File.WriteAllText(Path.Combine(neu, "new.txt"), "new");
        // A stale hidden sibling from a previous, never-cleaned-up run: removed before the swap (:426-427).
        var backup = Path.Combine(root, ".Marrow Code.previous");
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "stale.txt"), "stale");
        var script = WriteScript(root);
        var pid = SpawnAndAwaitExit();

        var installer = RunInstaller(script, pid, neu, dest, pollIntervalSeconds: 0.05);
        var output = ReadAllOutput(installer);
        installer.WaitUntilExit();

        Assert.Equal(0, installer.TerminationStatus);
        Assert.Equal("", output);
        Assert.True(File.Exists(Path.Combine(dest, "new.txt")), "the staged files must be in place");
        Assert.False(File.Exists(Path.Combine(dest, "old.txt")), "the old files must be gone");
        Assert.False(Directory.Exists(backup), "the backup is removed once the move-in succeeds");
        Assert.False(Directory.Exists(staging), "the staging tree is always cleaned up");
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-056")] // App/AppUpdater.swift:431-438
    public void A_move_in_failure_logs_the_exact_verbatim_line_and_restores_the_backup()
    {
        var root = TempRoot();
        var dest = Path.Combine(root, "Marrow Code");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "old.txt"), "old");
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(staging);
        // NEW does not exist: the source of the move-in is missing, so Move-Item fails and the script's own
        // catch branch (:433-437) runs.
        var neu = Path.Combine(staging, "missing-app");
        var script = WriteScript(root);
        var pid = SpawnAndAwaitExit();

        var installer = RunInstaller(script, pid, neu, dest, pollIntervalSeconds: 0.05);
        var output = ReadAllOutput(installer);
        installer.WaitUntilExit();

        Assert.Equal(1, installer.TerminationStatus); // exit 1
        Assert.Equal("Could not move the update into place", output.TrimEnd('\r', '\n'));
        // The old app is put back exactly as it was; nothing of this text ever reaches the user (UI-UPDATE-034's
        // alert only ever carries install()'s own thrown errors, never the installer script's log lines).
        Assert.True(File.Exists(Path.Combine(dest, "old.txt")));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-056")] // App/AppUpdater.swift:421-424
    public void When_the_watched_process_never_quits_the_script_gives_up_logs_verbatim_and_leaves_the_install_untouched()
    {
        var root = TempRoot();
        var dest = Path.Combine(root, "Marrow Code");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "old.txt"), "old");
        var staging = Path.Combine(root, "staging");
        var neu = Path.Combine(staging, "Marrow Code");
        Directory.CreateDirectory(neu);
        File.WriteAllText(Path.Combine(neu, "new.txt"), "new");
        var script = WriteScript(root);
        var alive = SpawnLongLived();
        try
        {
            // 240 polls at 0.01 s is a real 2.4 s wait, well inside this test's own timeout.
            var installer = RunInstaller(script, alive.ProcessIdentifier, neu, dest, pollIntervalSeconds: 0.01);
            var output = ReadAllOutput(installer);
            installer.WaitUntilExit();

            Assert.Equal(1, installer.TerminationStatus);
            Assert.Equal("The app did not quit", output.TrimEnd('\r', '\n'));
            Assert.True(File.Exists(Path.Combine(dest, "old.txt")), "the installed app is never touched");
            Assert.False(File.Exists(Path.Combine(dest, "new.txt")));
            Assert.False(Directory.Exists(staging), "the staging tree is removed even on give-up");
        }
        finally
        {
            alive.Terminate();
        }
    }
}
