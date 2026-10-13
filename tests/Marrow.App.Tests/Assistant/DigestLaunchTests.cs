using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 6, 7.4; ED-NEW-dextra-as11-8): the launch brings back what is there. When the duties file holds the Morning digest duty the launch binds the
// digest, so the duty is composed (caught up once if nth was closed at 08:00) and never started as a mission; when it does not, the launch makes nothing: making the digest duty
// is turning Dextra on (AS-8), not opening nth.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-8")]
public sealed class DigestLaunchTests
{
    private static readonly DateTimeOffset Yesterday = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    private static void WriteDuties(AssistantTempRoot temp, params Duty[] duties)
    {
        using var store = AssistantStore.Open(temp.Roots);
        store.SaveDuties("dextra", DutyJson.Write(new DutyDocument(duties)));
    }

    private static Duty Weekly() => new("d1", "Weekly note", "dextra", new ManualTrigger(), "Write the weekly note.");

    [Fact]
    public void A_duties_file_with_the_digest_duty_is_found_and_one_without_it_or_one_that_cannot_be_read_is_not()
    {
        using var temp = new AssistantTempRoot();
        WriteDuties(temp, Weekly(), DigestDuty.Make("dextra", "UTC"));
        using var store = AssistantStore.Open(temp.Roots);
        Assert.True(BackgroundStart.HasDigestDuty(store, ["dextra"]));
        Assert.False(BackgroundStart.HasDigestDuty(store, ["scout"]));
        store.SaveDuties("dextra", DutyJson.Write(new DutyDocument([Weekly()])));
        Assert.False(BackgroundStart.HasDigestDuty(store, ["dextra"]));
        File.WriteAllText(Path.Combine(store.Root, "assistants", "dextra", "duties.json"), "not json");
        Assert.False(BackgroundStart.HasDigestDuty(store, ["dextra"]));
    }

    [Fact]
    public void A_launch_whose_duties_hold_no_digest_duty_binds_no_digest_and_makes_none()
    {
        using var temp = new AssistantTempRoot();
        WriteDuties(temp, Weekly());
        using var launch = new BackgroundLaunchRig(temp);
        var host = launch.Run(StartupMode.Normal, out var problem);
        Assert.Null(problem);
        Assert.Null(host!.Digests);
        Assert.Equal("d1", Assert.Single(host.Scheduler.Duties).Duty.Id);
    }

    [Fact]
    public void A_launch_whose_duties_hold_the_digest_duty_composes_the_digest_it_missed_and_starts_no_mission()
    {
        using var temp = new AssistantTempRoot();
        WriteDuties(temp, DigestDuty.Make("dextra", "UTC") with { Since = Yesterday });
        using var launch = new BackgroundLaunchRig(temp);
        var host = launch.Run(StartupMode.Normal, out var problem);
        Assert.Null(problem);
        Assert.NotNull(host!.Digests);
        MissionRig.Until(() => host.Digests!.Latest is not null, "the digest nth missed at 08:00 yesterday");
        Assert.Equal(DigestKind.Scheduled, host.Digests!.Latest!.Kind);
        MissionRig.Until(() => host.Scheduler.Find(DigestDuty.Id)!.History.Count > 0 && host.Scheduler.Find(DigestDuty.Id)!.History[0].Outcome == DutyRunOutcome.Done, "the run to end");
        Assert.Equal(DutyCause.CatchUp, host.Scheduler.Find(DigestDuty.Id)!.History[0].Cause);
        Assert.Empty(launch.Model.Dextra!.Runner.Missions);
        Assert.Single(host.Scheduler.Duties);
    }
}
