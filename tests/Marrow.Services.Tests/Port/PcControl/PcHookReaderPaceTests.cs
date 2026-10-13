using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// ED-NEW-perf-idle-6: the hook reader no longer wakes every 1 to 4 ms for as long as the hooks are in (hours, while a stream is
// paused or the chords are armed). It looks at the ring quickly while input is arriving or a gate asks, and every 20 ms
// otherwise. The pure decision, and the gate's stamp the reader reads, on a manual clock (the hooks themselves never run in tests).
[Trait("Decision", "ED-NEW-pcctl-17")]
public sealed class PcHookReaderPaceTests
{
    [Theory]
    [InlineData(true, 0L, null, 1)]
    [InlineData(true, 60_000L, null, 1)]
    [InlineData(false, 0L, null, 4)]
    [InlineData(false, 249L, null, 4)]
    [InlineData(false, 250L, null, 20)]
    [InlineData(false, 3_600_000L, null, 20)]
    [InlineData(false, 3_600_000L, 0L, 4)]
    [InlineData(false, 3_600_000L, 1_999L, 4)]
    [InlineData(false, 3_600_000L, 2_000L, 20)]
    public void The_reader_sleeps_one_millisecond_after_records_four_while_input_or_a_gate_is_recent_and_twenty_otherwise(
        bool foundRecords, long sinceRecordMs, long? sinceGateMs, int expected) =>
        Assert.Equal(expected, PcLowLevelHooks.ReaderSleepMs(foundRecords, sinceRecordMs, sinceGateMs));

    [Fact]
    public void A_quiet_reader_looks_at_most_fifty_times_a_second_and_the_heartbeat_still_falls_due_inside_a_few_looks()
    {
        Assert.True(1000.0 / PcLowLevelHooks.IdleSleepMs <= 50);
        Assert.True(PcLowLevelHooks.HeartbeatMs / PcLowLevelHooks.IdleSleepMs >= 10); // the 250 ms beat is on time to within a tenth
    }

    [Fact]
    public void A_gate_check_stamps_the_monitor_so_the_reader_stays_quick_and_the_heartbeat_does_not()
    {
        var clock = new PcManualClock();
        var monitor = new PhysicalInputMonitor(clock);
        Assert.Null(monitor.SinceGateMs);
        monitor.Installed(true);
        monitor.LastInputSeen(clock.NowMs); // the reader's heartbeat reads the tick this way, and is not a gate
        Assert.Null(monitor.SinceGateMs);

        clock.Advance(500);
        ((IPhysicalInput)monitor).LastInput(clock.NowMs); // the lease's gate
        Assert.Equal(0, monitor.SinceGateMs);
        clock.Advance(1_500);
        Assert.Equal(1_500, monitor.SinceGateMs);
        Assert.Equal(4, PcLowLevelHooks.ReaderSleepMs(false, 3_600_000, monitor.SinceGateMs));
        clock.Advance(500);
        Assert.Equal(20, PcLowLevelHooks.ReaderSleepMs(false, 3_600_000, monitor.SinceGateMs));
    }
}
