using System.Diagnostics;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a and 13 unit U4, VELOPACK_CHECK.md "Old branches" items 2 and 3 (ED-NEW-inst-u4-3; amends
// ED-247). The install hold between two real processes: the other setup is a PowerShell process that takes the very kernel object a setup takes
// (the name InstallHold.MutexName gives) and sleeps. A mutex's abandonment, the thing that frees a waiting nth when its setup dies, only
// exists between processes, so it is proved here and not in the same-process tests. The root is a random temporary path, and the child is
// ended in every case.
public sealed class InstallHoldAcrossProcessesTests
{
    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(60);

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "nth-hold-test-" + Guid.NewGuid().ToString("N"), "Programs", "nth");

    // A process that owns the named mutex, says "held" and sleeps (two minutes at most, in case the test is killed).
    private static Process StartOtherSetup(string mutexName)
    {
        var script = $"$created = $false\n$m = New-Object System.Threading.Mutex($true, '{mutexName}', [ref]$created)\n"
            + "if (-not $created) { [Console]::Out.WriteLine('not-created'); exit 3 }\n[Console]::Out.WriteLine('held')\n[Console]::Out.Flush()\nStart-Sleep -Seconds 120\n";
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        return Process.Start(info) ?? throw new InvalidOperationException("The test process did not start.");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_setup_in_another_process_holds_the_root_for_this_one_and_a_waiting_nth_carries_on_when_that_setup_dies()
    {
        var root = NewRoot();
        using var other = StartOtherSetup(InstallHold.MutexName(root));
        try
        {
            var said = await other.StandardOutput.ReadLineAsync().WaitAsync(Patient);
            Assert.Equal("held", said);

            // Another install of this root is running: the second setup is told so, and an nth that starts waits.
            Assert.True(InstallHold.IsHeld(root));
            Assert.Null(InstallHold.TryAcquire(root));
            Assert.False(InstallHold.WaitWhileHeld(root, TimeSpan.FromMilliseconds(250)));
            var waiting = Task.Run(() => InstallHold.WaitWhileHeld(root, Patient));
            await Task.Delay(400);
            Assert.False(waiting.IsCompleted);

            // The setup dies (killed, crashed, the PC lost power to the process): the hold is abandoned, which frees the waiting nth.
            other.Kill(entireProcessTree: true);
            await other.WaitForExitAsync().WaitAsync(Patient);

            Assert.True(await waiting.WaitAsync(Patient));
            Assert.False(InstallHold.IsHeld(root));
            using var next = InstallHold.TryAcquire(root); // and a setup run again can take the root
            Assert.NotNull(next);
        }
        finally
        {
            if (!other.HasExited) other.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_hold_taken_here_is_seen_as_held_by_another_process()
    {
        var root = NewRoot();
        using var hold = InstallHold.TryAcquire(root);
        Assert.NotNull(hold);
        // The child tries to create the object as its owner; "not-created" means the object already exists, and this process holds it.
        using var other = StartOtherSetup(InstallHold.MutexName(root));
        try
        {
            var said = await other.StandardOutput.ReadLineAsync().WaitAsync(Patient);
            await other.WaitForExitAsync().WaitAsync(Patient);

            Assert.Equal("not-created", said);
            Assert.Equal(3, other.ExitCode);
            Assert.True(InstallHold.IsHeld(root));
        }
        finally
        {
            if (!other.HasExited) other.Kill(entireProcessTree: true);
        }
    }
}
