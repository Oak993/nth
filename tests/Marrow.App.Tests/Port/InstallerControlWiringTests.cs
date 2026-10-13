using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a, unit U4 (ED-NEW-inst-u4-1 and -3; amends ED-247). How the app is wired to nth Setup: the
// control pipe's name (a development build has its own and never answers an installer), the install root the app finds from where it runs,
// and the wait at start while a setup holds that root. The wait runs on a random temporary root, so no real install is touched.
public sealed class InstallerControlWiringTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "nth-wiring-test-" + Guid.NewGuid().ToString("N"), "Programs", "nth");

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_pipe_is_named_for_the_user_and_a_development_build_never_shares_the_release_name()
    {
        const string sid = "S-1-5-21-1-2-3-1001";

        var name = DroppyCodeApp.ControlPipeName(sid);

        Assert.StartsWith("nth-", name, StringComparison.Ordinal);
        Assert.Contains(sid, name, StringComparison.Ordinal);
        Assert.Contains("-control", name, StringComparison.Ordinal);
        Assert.Equal(AppInfo.Current.IsDevelopment, name.StartsWith("nth-dev-", StringComparison.Ordinal));
        Assert.NotEqual(NthControl.PipeName(sid, development: !AppInfo.Current.IsDevelopment), name);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_install_root_is_the_folder_above_current_where_update_exe_sits_and_a_dev_or_portable_run_has_none()
    {
        var root = NewRoot();
        var current = Path.Combine(root, "current");
        Directory.CreateDirectory(current);
        try
        {
            Assert.Null(Program.InstalledRoot(current + Path.DirectorySeparatorChar));
            File.WriteAllText(Path.Combine(root, "Update.exe"), string.Empty);

            Assert.Equal(root, Program.InstalledRoot(current + Path.DirectorySeparatorChar));
            Assert.Equal(root, Program.InstalledRoot(current));
            Assert.True(Program.InstalledByVelopack(current));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(root))!, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void With_no_setup_holding_the_root_the_start_wait_returns_at_once_and_a_dev_run_does_not_wait_at_all()
    {
        var root = NewRoot();
        var current = Path.Combine(root, "current");
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(root, "Update.exe"), string.Empty);
        try
        {
            var started = DateTime.UtcNow;
            DroppyCodeApp.WaitForInstallerToFinish(current + Path.DirectorySeparatorChar);
            DroppyCodeApp.WaitForInstallerToFinish(AppContext.BaseDirectory); // not a Velopack install: nothing to wait for

            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(root))!, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task An_nth_started_inside_a_root_a_setup_holds_waits_without_a_window_until_the_setup_lets_go()
    {
        var root = NewRoot();
        var current = Path.Combine(root, "current");
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(root, "Update.exe"), string.Empty);
        var hold = InstallHold.TryAcquire(root);
        try
        {
            Assert.NotNull(hold);
            var waiting = Task.Run(() => DroppyCodeApp.WaitForInstallerToFinish(current + Path.DirectorySeparatorChar));
            await Task.Delay(400);
            Assert.False(waiting.IsCompleted);

            hold.Dispose();

            await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            hold?.Dispose();
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(root))!, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Main_takes_the_instance_lock_first_then_waits_for_a_setup_and_the_pipe_opens_beside_the_toast_pipe()
    {
        var source = File.ReadAllText(ProgramSource());

        var lockAt = source.IndexOf("TryAcquireSingleInstanceLock(out var singleInstanceLock)", StringComparison.Ordinal);
        var waitAt = source.IndexOf("DroppyCodeApp.WaitForInstallerToFinish(AppContext.BaseDirectory)", StringComparison.Ordinal);
        var startupAt = source.IndexOf("DesktopPerformanceProbe.BeginStartup()", StringComparison.Ordinal);
        var toastAt = source.IndexOf("DroppyCodeApp.ListenForNotificationArguments(shell.Delegate, launchReady.Task)", StringComparison.Ordinal);
        var controlAt = source.IndexOf("DroppyCodeApp.ListenForInstallerControl(shell, launchReady.Task)", StringComparison.Ordinal);

        Assert.True(lockAt > 0 && lockAt < waitAt, "the wait comes after the instance lock, so a second launch never waits");
        Assert.True(waitAt < startupAt, "and before any desktop work, so no window shows");
        Assert.True(toastAt > 0 && toastAt < controlAt, "the pipe opens once the shell exists, beside the toast pipe");
        Assert.True(source.IndexOf("using var installerControl", StringComparison.Ordinal) > 0, "and is disposed with the app");
    }

    private static string ProgramSource([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        return Path.Combine(repository, "src", "Marrow.App", "Program.cs");
    }
}
