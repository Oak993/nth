using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the Native gate of DESIGN.md 16.1 for AS-10, "Task Scheduler wake" (the lead runs it alone, as the signed-in user; every unit filter
// excludes Native). An entry for a duty that asks to wake the PC is made in the REAL Task Scheduler, under \nth\duty-native<random>, read back
// with schtasks (it wakes the PC, runs as the signed-in user with limited rights, and runs "nth --duty <id>"), seen in the list, and removed
// again; power-plan words come from the real powercfg. The program the entry names does not exist and its first time is a year away, so
// nothing can start from it. WHAT THIS CANNOT PROVE is the wake itself from sleep, which the owner checks once by hand: make a duty with "Wake
// the PC" on, put the PC to sleep with a time two minutes ahead, and look for the run in the activity log.
[Trait("Decision", "ED-NEW-dextra-as10-15")]
[Trait("Feature", "AS-10")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class TaskSchedulerNativeTests
{
    private const string Absent = @"C:\nth-native-test\absent\nth.exe";

    private static Duty Wakes(string id) =>
        new(id, "Native wake check", "dextra", new TimeTrigger(TimeSchedule.Daily(new TimeOnly(3, 15)), TimeZoneInfo.Local.Id), "Nothing.")
        {
            WakePc = true,
            Since = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task A_wake_entry_is_made_listed_read_back_with_its_flags_and_removed_in_the_real_task_scheduler()
    {
        var id = "native" + Guid.NewGuid().ToString("N")[..8];
        var name = WakeTaskXml.NameOf(id);
        var host = new SchtasksHost();
        var entry = WakeTaskXml.For(Wakes(id), Absent, DateTimeOffset.UtcNow.AddDays(400), TimeZoneInfo.Local);
        Assert.NotNull(entry);
        try
        {
            await host.Register(entry.Name, entry.Xml, CancellationToken.None);
            Assert.Contains(name, await host.List(CancellationToken.None));

            var read = await SystemProcessRunner.Instance.Run(Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                ["/Query", "/TN", @"\" + WakeTaskXml.Folder + @"\" + name, "/XML"], TimeSpan.FromSeconds(30), CancellationToken.None);
            Assert.Equal(0, read.ExitCode);
            Assert.Contains("<WakeToRun>true</WakeToRun>", read.Output, StringComparison.Ordinal);
            Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", read.Output, StringComparison.Ordinal);
            Assert.Contains("<LogonType>InteractiveToken</LogonType>", read.Output, StringComparison.Ordinal);
            Assert.Contains("--duty " + id, read.Output, StringComparison.Ordinal);
            Assert.Contains(Absent, read.Output, StringComparison.Ordinal);

            // The same call again replaces the entry (the owner changed the duty): still one.
            await host.Register(entry.Name, entry.Xml, CancellationToken.None);
            Assert.Single(await host.List(CancellationToken.None), n => n == name);
        }
        finally
        {
            await host.Delete(name, CancellationToken.None);
        }

        Assert.DoesNotContain(name, await host.List(CancellationToken.None));
        await host.Delete(name, CancellationToken.None); // a task that is already gone is not an error
    }

    [Fact]
    public async Task The_sync_keeps_only_the_entries_nth_made_and_leaves_the_owners_own_tasks_alone()
    {
        var id = "native" + Guid.NewGuid().ToString("N")[..8];
        var host = new SchtasksHost();
        var sync = new WakeSync(host, Absent, pcZone: TimeZoneInfo.Local);
        try
        {
            var before = await host.List(CancellationToken.None);
            var problems = await sync.Sync([Wakes(id) with { Since = DateTimeOffset.UtcNow }], CancellationToken.None);
            Assert.Empty(problems);
            Assert.Contains(WakeTaskXml.NameOf(id), await host.List(CancellationToken.None));
            problems = await sync.Sync([], CancellationToken.None);
            Assert.Empty(problems);
            Assert.DoesNotContain(WakeTaskXml.NameOf(id), await host.List(CancellationToken.None));
            Assert.Equal(before.Where(n => n != WakeTaskXml.NameOf(id)).Order(StringComparer.Ordinal), (await host.List(CancellationToken.None)).Order(StringComparer.Ordinal));
        }
        finally
        {
            await host.Delete(WakeTaskXml.NameOf(id), CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_real_powercfg_answers_about_wake_timers_in_words_the_parser_reads()
    {
        var state = await WakeTimerPolicy.Read(SystemProcessRunner.Instance, null, CancellationToken.None);
        Assert.NotNull(state.Allowed); // a desktop with a power plan answers; a null here means powercfg's words changed
    }
}
