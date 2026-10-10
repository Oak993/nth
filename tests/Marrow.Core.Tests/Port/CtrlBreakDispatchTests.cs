using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;
using Microsoft.Win32.SafeHandles;

namespace Marrow.Core.Tests.Port;

public sealed class CtrlBreakDispatchTests
{
    [Fact]
    [Trait("Decision", "ED-242")]
    public void A_failed_launch_releases_its_reservation_without_inheriting_the_callers_context()
    {
        using var entered = new ManualResetEventSlim();
        using var reserved = new SafeProcessHandle((nint)123, ownsHandle: false);
        var marker = new AsyncLocal<string?> { Value = "caller" };
        string? inherited = "unobserved";
        using var sender = ProcessNative.CtrlBreakSender.Override((pid, handle) =>
        {
            inherited = marker.Value;
            entered.Set();
            throw new System.ComponentModel.Win32Exception(5);
        });
        ProcessNative.SendCtrlBreak(456, reserved);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => reserved.IsClosed, TimeSpan.FromSeconds(5)));
        Assert.Null(inherited);
        Assert.Equal("caller", marker.Value);
    }

    [Fact]
    [Trait("Decision", "ED-242")]
    public void Helper_creation_returns_before_launch_finishes_and_keeps_the_reserved_handle()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        using var reserved = new SafeProcessHandle((nint)123, ownsHandle: false);
        var caller = Environment.CurrentManagedThreadId;
        var senderThread = caller;
        var wasClosed = true;
        using var sender = ProcessNative.CtrlBreakSender.Override((pid, handle) =>
        {
            senderThread = Environment.CurrentManagedThreadId;
            wasClosed = handle!.IsClosed;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(2));
            handle.Dispose();
            finished.Set();
        });
        try
        {
            ProcessNative.SendCtrlBreak(456, reserved);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(finished.IsSet, "The caller waited for helper creation.");
            Assert.NotEqual(caller, senderThread);
            Assert.False(wasClosed);
            Assert.False(reserved.IsClosed);
        }
        finally
        {
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        }
        Assert.True(reserved.IsClosed);
    }

    [Theory]
    [Trait("Decision", "ED-242")]
    [Trait("Rule", "R-PROC-1")]
    [InlineData(1)]
    [InlineData(3)]
    public void A_blocked_signal_launcher_does_not_delay_the_real_childs_force_kill(int seconds)
    {
        using var runtime = new TestRuntime();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        using var sender = ProcessNative.CtrlBreakSender.Override((pid, handle) =>
        {
            entered.Set();
            try { release.Wait(TimeSpan.FromSeconds(30)); }
            finally { handle?.Dispose(); finished.Set(); }
        });
        var child = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellEncoded("Start-Sleep 120"),
            Environment = ShellScripts.HostEnvironment(),
        };
        child.Run();
        try
        {
            var call = System.Diagnostics.Stopwatch.StartNew();
            if (seconds == 1) TerminationLadder.Shell(child);
            else TerminationLadder.Stdio(child, () => { });
            Assert.True(call.Elapsed < TimeSpan.FromSeconds(1), "The ladder waited for helper startup.");
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Contains(TimeSpan.FromSeconds(seconds), runtime.Time.TimerDeadlines);
            runtime.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromTicks(1));
            Assert.True(child.IsRunning);
            Assert.False(finished.IsSet);
            runtime.Advance(TimeSpan.FromTicks(1));
            Assert.True(SpinWait.SpinUntil(() => !child.IsRunning, TimeSpan.FromSeconds(5)));
            Assert.False(finished.IsSet, "Force kill waited for the blocked signal launcher.");
        }
        finally
        {
            if (child.IsRunning) child.KillTree();
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        }
    }
}
