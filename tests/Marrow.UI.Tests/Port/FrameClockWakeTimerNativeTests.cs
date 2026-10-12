using System.Diagnostics;
using Marrow.UI.Port;
using Xunit.Abstractions;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7, review 2026-10-11 (R-TEST-5): the real waitable timers of AvaloniaFrameSource in real time, moved here
// unchanged from FrameClockWin32HostTests so the routine run holds no wall-clock assertion. They wait and measure on the
// machine's own clock, so they run with the native tests (a quiet PC; the flake rule: a failure counts when it fails alone
// twice). The routine run keeps FrameClockWin32HostTests.Both_kinds_of_wake_timer_exist_and_each_fires_when_armed.
public sealed class FrameClockWakeTimerNativeTests(ITestOutputHelper output)
{
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
}
