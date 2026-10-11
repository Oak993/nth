using System.Diagnostics;
using System.Runtime.InteropServices;
using Marrow.UI.Port;
using Xunit.Abstractions;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7: the production platform pieces of AvaloniaFrameSource that the fake host stands for elsewhere: the rule
// over the app's windows, the IsIconic/DWMWA_CLOAKED query, and the high-resolution waitable timer with its waiting thread
// (real kernel timers; no window is created, minimised or moved).
public sealed class FrameClockWin32HostTests(ITestOutputHelper output)
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

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void The_wake_timer_fires_once_per_arming_a_new_arming_replaces_the_last_and_disposed_it_never_fires()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fired = 0;
        using var signal = new SemaphoreSlim(0);
        var timer = WakeTimer.Create(() => Interlocked.Increment(ref fired), job => { job(); signal.Release(); }, precise: true);
        Assert.NotNull(timer); // Windows 10 1803 and later
        try
        {
            timer.Arm(TimeSpan.FromMilliseconds(400));
            timer.Arm(TimeSpan.FromMilliseconds(5)); // replaces the 400 ms arming
            Assert.True(signal.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(signal.Wait(TimeSpan.FromMilliseconds(600))); // the 400 ms arming is gone, and one-shot
            Assert.Equal(1, Volatile.Read(ref fired));
        }
        finally
        {
            timer.Dispose();
        }
        timer.Arm(TimeSpan.FromMilliseconds(1)); // disposed: nothing
        Assert.False(signal.Wait(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(1, Volatile.Read(ref fired));
    }

    // Without the high-resolution timer the looks at the windows use a timer of the system's resolution: it fires, once.
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void The_coarse_wake_timer_fires_once_per_arming()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fired = 0;
        using var signal = new SemaphoreSlim(0);
        var timer = WakeTimer.Create(() => Interlocked.Increment(ref fired), job => { job(); signal.Release(); }, precise: false);
        Assert.NotNull(timer);
        try
        {
            timer.Arm(TimeSpan.FromMilliseconds(20));
            Assert.True(signal.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(signal.Wait(TimeSpan.FromMilliseconds(200)));
            Assert.Equal(1, Volatile.Read(ref fired));
        }
        finally
        {
            timer.Dispose();
        }
    }

    // The margin of a held request is 1.5 refreshes (6.25 ms at 240 Hz) for the timer's lateness plus the UI thread reaching
    // the posted wake-up (at Normal, ahead of the frame's render job). Measured (Debug, the test host under the full test
    // run): see ED-NEW-lean-7. The limits keep the timer itself well inside the 240 Hz margin.
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void The_wake_timer_fires_well_within_the_margin_of_a_240_hz_display()
    {
        if (!OperatingSystem.IsWindows()) return;
        long firedAt = 0;
        using var signal = new SemaphoreSlim(0);
        var timer = WakeTimer.Create(() => Volatile.Write(ref firedAt, Stopwatch.GetTimestamp()), job => { job(); signal.Release(); }, precise: true);
        Assert.NotNull(timer);
        var late = new List<double>();
        try
        {
            var delay = TimeSpan.FromMilliseconds(4);
            for (var i = 0; i < 40; i++)
            {
                var armedAt = Stopwatch.GetTimestamp();
                timer.Arm(delay);
                Assert.True(signal.Wait(TimeSpan.FromSeconds(2)));
                late.Add((Stopwatch.GetElapsedTime(armedAt, Volatile.Read(ref firedAt)) - delay).TotalMilliseconds);
            }
        }
        finally
        {
            timer.Dispose();
        }
        late.Sort();
        double At(double q) => late[(int)Math.Min(late.Count - 1, Math.Floor(q * late.Count))];
        output.WriteLine($"wake timer lateness ms: min {late[0]:F3} p50 {At(.5):F3} p90 {At(.9):F3} max {late[^1]:F3}");
        Assert.True(late[0] > -1.0, $"fired {-late[0]:F3} ms early");
        Assert.True(At(.5) < 2.0, $"median lateness {At(.5):F3} ms (limit 2)");
        Assert.True(At(.9) < 4.0, $"90th percentile lateness {At(.9):F3} ms (limit 4; the 240 Hz margin is 6.25)");
    }

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
}
