using System.Runtime.InteropServices;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7: the production platform pieces of AvaloniaFrameSource that the fake host stands for elsewhere: the rule
// over the app's windows, the IsIconic/DWMWA_CLOAKED query, and the high-resolution waitable timer with its waiting thread
// (real kernel timers; no window is created, minimised or moved).
public sealed class FrameClockWin32HostTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void No_window_is_seen_only_when_every_shown_window_is_a_hidden_win32_window()
    {
        Func<nint, bool> hidden = handle => handle >= 100;
        Assert.False(AvaloniaFrameSource.NoWindowSeen([], hidden)); // no window: frames go on
        Assert.False(AvaloniaFrameSource.NoWindowSeen([(false, 100)], hidden)); // none shown
        Assert.True(AvaloniaFrameSource.NoWindowSeen([(true, 100)], hidden));
        Assert.True(AvaloniaFrameSource.NoWindowSeen([(true, 100), (false, 1), (true, 101)], hidden)); // an unshown one does not count
        Assert.False(AvaloniaFrameSource.NoWindowSeen([(true, 100), (true, 1)], hidden)); // one seen
        Assert.False(AvaloniaFrameSource.NoWindowSeen([(true, 100), (true, 0)], hidden)); // one not a Win32 window counts as seen
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void The_iconic_and_cloaked_query_answers_for_a_window_that_can_be_seen_and_for_no_window()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.False(Win32FrameHost.Hidden(0)); // no window: neither iconic, and the DWM query fails (not cloaked)
        Assert.False(Win32FrameHost.Hidden(GetDesktopWindow())); // the desktop: never minimised or cloaked
    }

    // Review 2026-10-11 (R-TEST-5): the real kernel timers in real time (once per arming, a re-arming replacing the last, a
    // disposed timer silent, the latency against the 240 Hz margin) are FrameClockWakeTimerNativeTests, run with the native
    // tests. Here only what needs no clock: both kinds exist, and a timer armed for an instant already past fires (a signal,
    // waited for under a hang guard only; nothing about how soon is asserted).
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void Both_kinds_of_wake_timer_exist_and_each_fires_when_armed()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var precise in new[] { true, false })
        {
            var fired = 0;
            using var signal = new SemaphoreSlim(0);
            var timer = WakeTimer.Create(() => Interlocked.Increment(ref fired), job => { job(); signal.Release(); }, precise);
            Assert.NotNull(timer); // Windows 10 1803 and later
            try
            {
                timer.Arm(TimeSpan.FromHours(1));
                timer.Arm(TimeSpan.Zero); // replaces the hour: due at once
                Assert.True(signal.Wait(TimeSpan.FromMinutes(1)), $"the {(precise ? "precise" : "coarse")} timer never fired");
                Assert.Equal(1, Volatile.Read(ref fired));
            }
            finally
            {
                timer.Dispose();
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
}
