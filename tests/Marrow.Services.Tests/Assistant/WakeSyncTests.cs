using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4, 16.1; ED-NEW-dextra-as10-15): the entries under \nth\ follow the duties that asked to wake the
// PC. One is made when a duty asks, renewed when its schedule changes, removed when the duty is off, gone, or stops asking; an entry
// whose XML has not changed is not asked of Windows again; a task nth did not make is never touched; a refusal is a line for the
// duty's card and is tried again at the next sync.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-15")]
public sealed class WakeSyncTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\nth\current\nth.exe";
    private static readonly TimeZoneInfo Pc = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private readonly FakeTaskSchedulerHost host = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    private WakeSync Sync() => new(host, Exe, clock, Pc);

    private static Duty Wake(string id, int hour = 8) =>
        new(id, "Duty " + id, "dextra", new TimeTrigger(TimeSchedule.Daily(new TimeOnly(hour, 0)), "Eastern Standard Time"), "Do it.") { WakePc = true, Since = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) };

    [Fact]
    public async Task A_duty_that_asks_gets_an_entry_and_one_that_did_not_ask_gets_none()
    {
        var sync = Sync();
        var problems = await sync.Sync([Wake("d1"), Wake("d2") with { WakePc = false }, Wake("d3") with { NeedsDesktop = true }], CancellationToken.None);
        Assert.Empty(problems);
        Assert.Equal(["duty-d1"], host.Tasks.Keys);
        Assert.Contains("--duty d1", host.Tasks["duty-d1"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_entry_that_has_not_changed_is_not_asked_of_windows_again_and_a_changed_one_is_renewed()
    {
        var sync = Sync();
        await sync.Sync([Wake("d1")], CancellationToken.None);
        host.Calls.Clear();
        await sync.Sync([Wake("d1")], CancellationToken.None);
        Assert.Equal(["list"], host.Calls);
        await sync.Sync([Wake("d1", hour: 9)], CancellationToken.None);
        Assert.Contains("register duty-d1", host.Calls);
        Assert.Contains("09:00:00", host.Tasks["duty-d1"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_entry_that_vanished_from_windows_is_made_again()
    {
        var sync = Sync();
        await sync.Sync([Wake("d1")], CancellationToken.None);
        host.Tasks.Clear(); // the owner deleted it in Task Scheduler
        await sync.Sync([Wake("d1")], CancellationToken.None);
        Assert.Contains("duty-d1", host.Tasks.Keys);
    }

    [Fact]
    public async Task An_entry_goes_when_its_duty_is_off_deleted_or_stops_asking_and_only_nths_own_tasks_are_ever_removed()
    {
        host.Tasks["someone-elses-task"] = "<x/>";
        host.Tasks["duty-Bad Name"] = "<x/>";
        var sync = Sync();
        await sync.Sync([Wake("d1"), Wake("d2"), Wake("d3")], CancellationToken.None);
        Assert.Equal(3, host.Tasks.Keys.Count(WakeTaskXml.IsOurs));
        await sync.Sync([Wake("d1") with { Enabled = false }, Wake("d2") with { WakePc = false }], CancellationToken.None); // d3 deleted
        Assert.DoesNotContain(host.Tasks.Keys, WakeTaskXml.IsOurs);
        Assert.Contains("someone-elses-task", host.Tasks.Keys);
        Assert.Contains("duty-Bad Name", host.Tasks.Keys);
    }

    [Fact]
    public async Task Remove_all_takes_every_entry_nth_made_away()
    {
        var sync = Sync();
        await sync.Sync([Wake("d1"), Wake("d2")], CancellationToken.None);
        await sync.RemoveAll(CancellationToken.None);
        Assert.Empty(host.Tasks);
    }

    [Fact]
    public async Task A_refusal_is_a_line_for_the_duty_and_is_tried_again_at_the_next_sync()
    {
        host.RefuseRegister = name => name == "duty-d1" ? "Access is denied." : null;
        var sync = Sync();
        var problems = await sync.Sync([Wake("d1"), Wake("d2")], CancellationToken.None);
        Assert.Equal("Windows would not make the wake entry: Access is denied.", problems["d1"]);
        Assert.DoesNotContain("d2", problems.Keys);
        Assert.Contains("duty-d2", host.Tasks.Keys);
        Assert.Equal("Windows would not make the wake entry: Access is denied.", sync.Problems["d1"]);
        host.RefuseRegister = null;
        Assert.Empty(await sync.Sync([Wake("d1"), Wake("d2")], CancellationToken.None));
        Assert.Contains("duty-d1", host.Tasks.Keys);
    }

    [Fact]
    public async Task A_task_scheduler_that_cannot_be_read_gives_a_problem_for_each_wanted_entry_and_never_throws()
    {
        host.RefuseList = "The service is not running.";
        var problems = await Sync().Sync([Wake("d1"), Wake("d2")], CancellationToken.None);
        Assert.Equal(2, problems.Count);
        Assert.StartsWith("Windows Task Scheduler could not be read", problems["d1"], StringComparison.Ordinal);
        Assert.Empty(host.Tasks);
    }

    [Fact]
    public async Task A_removal_windows_refuses_is_a_problem_too()
    {
        var sync = Sync();
        await sync.Sync([Wake("d1")], CancellationToken.None);
        host.RefuseDelete = "Access is denied.";
        var problems = await sync.Sync([], CancellationToken.None);
        Assert.Equal("Windows would not remove the wake entry: Access is denied.", problems["d1"]);
    }
}
