using System.Diagnostics;
using System.Globalization;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the Native idle soak of DESIGN.md 16.1 for AS-10 (the lead runs it alone, in a quiet window; it takes the hours it says). A
// scheduler on the real clock holds thirty duties of every kind and one that runs every half hour on a quick launcher; the process's working
// set and processor time are sampled once a minute. After the first hour (warm-up) the working set must not grow by more than 25 MB, the
// scheduler must have run the half-hourly duty on schedule (once each half hour, never two at once), and the process must have used under
// 1 % of one core. NTH_SOAK_HOURS sets the length (8 by default; 1 for a quick look). The simulated, bounded version is
// AssistantSchedulerIdleSoakTests; this is the real clock, GC and timer cost.
[Trait("Decision", "ED-NEW-dextra-as10-17")]
[Trait("Feature", "AS-10")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class IdleSoakNativeTests
{
    private static IEnumerable<Duty> Duties()
    {
        for (var i = 0; i < 10; i++) yield return SchedulerRig.Daily("late-" + i, 20 + i % 3, i * 5);
        for (var i = 0; i < 5; i++) yield return new Duty("event-" + i, "Event " + i, "dextra", new AppEventTrigger(AppEventKind.WindowReset), "React.");
        for (var i = 0; i < 5; i++) yield return new Duty("manual-" + i, "Manual " + i, "dextra", new ManualTrigger(), "Only when asked.");
        for (var i = 0; i < 5; i++) yield return SchedulerRig.Daily("off-" + i, 7, 0) with { Enabled = false };
        for (var i = 0; i < 4; i++) yield return SchedulerRig.Every("far-" + i, 24 * 60) with { Since = DateTimeOffset.UtcNow };
        yield return SchedulerRig.Every("half", 30) with { Since = DateTimeOffset.UtcNow };
    }

    [Fact]
    public async Task Thirty_duties_on_the_real_clock_for_hours_hold_their_memory_and_use_almost_no_processor()
    {
        var hours = double.TryParse(Environment.GetEnvironmentVariable("NTH_SOAK_HOURS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var given) && given > 0 ? given : 8;
        var store = new MemoryDutyStore();
        store.Put("dextra", Duties().ToArray());
        var launcher = new FakeDutyLauncher();
        using var scheduler = new AssistantScheduler(new AssistantSchedulerOptions { Store = store, Launcher = launcher, AssistantIds = () => ["dextra"] });
        scheduler.Start();
        using var process = Process.GetCurrentProcess();
        var started = DateTimeOffset.UtcNow;
        var cpuStart = process.TotalProcessorTime;
        long warmSet = 0;
        var wholeMinutes = (int)(hours * 60);
        for (var minute = 1; minute <= wholeMinutes; minute++)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            if (launcher.LastMission is { } mission && scheduler.Duties.Any(v => v.Running)) scheduler.MissionEnded(mission, DutyRunOutcome.Done, "RESULT: nothing_changed");
            process.Refresh();
            if (minute == 60 || (wholeMinutes < 60 && minute == wholeMinutes / 2 + 1)) warmSet = process.WorkingSet64;
        }

        process.Refresh();
        var elapsed = DateTimeOffset.UtcNow - started;
        var used = (process.TotalProcessorTime - cpuStart).TotalSeconds / elapsed.TotalSeconds;
        Assert.True(process.WorkingSet64 - warmSet < 25L * 1024 * 1024, $"The working set grew from {warmSet / 1024 / 1024} MB to {process.WorkingSet64 / 1024 / 1024} MB while idle.");
        Assert.True(used < 0.01, $"The process used {used:P2} of a core while idle.");
        var runs = launcher.Fires.Where(f => f.Duty.Id == "half").ToList();
        Assert.InRange(runs.Count, (int)(hours * 2) - 1, (int)(hours * 2) + 1);
        Assert.DoesNotContain(launcher.Fires, f => f.Duty.Id != "half");
    }
}
