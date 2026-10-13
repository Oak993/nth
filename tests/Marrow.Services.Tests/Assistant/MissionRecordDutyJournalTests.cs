using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1, 7.3, 15.3; ED-NEW-dextra-as10-8, -18): what a duty's mission adds to the mission journal. The two keys are
// optional and written only when set, so a journal written before them loads as it did and a record without them writes what it wrote.
// A dry run nth ended halfway comes back as a dry run (never as a real one), and a duty's mission keeps the duty it belongs to, so its
// narrowing of the assistant's grants still applies after a restart.
[Trait("Decision", "ED-NEW-dextra-as10-8")]
[Trait("Feature", "AS-10")]
public sealed class MissionRecordDutyJournalTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 13, 6, 0, 0, TimeSpan.Zero);

    private static MissionRecord Plain() => new(new Mission(Guid.Parse("20000000-0000-4000-8000-000000000003"), "Weekly note", "dextra", MissionOrigin.Duty, MissionState.Working))
    {
        Brief = "Write the weekly note.",
        ProjectId = Guid.Parse("50000000-0000-4000-8000-000000000005"),
        ProjectPath = @"C:\code\app",
        Provider = ProviderKind.Claude,
        Created = At,
    };

    [Fact]
    public void A_record_without_the_keys_writes_neither_and_loads_as_a_real_run_of_no_duty()
    {
        var json = MissionJournal.ToJson(Plain());
        Assert.False(json.ContainsKey("dryRun"));
        Assert.False(json.ContainsKey("dutyId"));
        var back = MissionJournal.FromJson(json)!;
        Assert.False(back.DryRun);
        Assert.Null(back.DutyId);
        Assert.Equal(json.ToJsonString(), MissionJournal.ToJson(back).ToJsonString());
    }

    [Fact]
    public void A_dry_run_and_its_duty_come_back_whole_and_stay_so_after_an_interrupted_open()
    {
        var record = Plain() with { DryRun = true, DutyId = "d1" };
        var json = MissionJournal.ToJson(record);
        Assert.True(json["dryRun"]!.GetValue<bool>());
        Assert.Equal("d1", json["dutyId"]!.GetValue<string>());
        var back = MissionJournal.FromJson(json)!;
        Assert.True(back.DryRun);
        Assert.Equal("d1", back.DutyId);

        // nth ended while it ran: it comes back Paused and asking "Resume?", still a dry run of the same duty.
        var interrupted = MissionJournal.FromJson(MissionJournal.Interrupted(json))!;
        Assert.True(interrupted.Interrupted);
        Assert.True(interrupted.DryRun);
        Assert.Equal("d1", interrupted.DutyId);
    }
}
