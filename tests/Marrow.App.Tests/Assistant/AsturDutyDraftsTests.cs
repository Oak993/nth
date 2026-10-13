using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-4")]
public sealed class AsturDutyDraftsTests
{
    [Fact]
    public void An_idea_has_no_automatic_trigger_until_the_owner_names_a_schedule()
    {
        var profile = new AssistantProfile("shop", "Shop") { ParentId = "dextra" };
        var manual = AsturDutyDrafts.Build(profile, "Keep a backlog", "", DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        Assert.IsType<ManualTrigger>(manual.Duty!.Trigger);
        var timed = AsturDutyDrafts.Build(profile, "Watch numbers", "weekdays 09:00", DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        Assert.IsType<TimeTrigger>(timed.Duty!.Trigger);
        Assert.Equal("shop", timed.Duty.AssistantId);
        Assert.Equal(ReportStyle.SilentUnlessChanged, timed.Duty.Report);
        Assert.Null(AsturDutyDrafts.Build(profile, "Watch numbers", "whenever convenient", DateTimeOffset.UtcNow, TimeZoneInfo.Utc).Duty);
    }

    [Fact]
    public void A_review_does_not_create_a_duty_and_repeated_confirmation_has_one_identity()
    {
        using var rig = new DutyHostRig(tray: false);
        var profile = new AssistantProfile("dextra", "Dextra");
        var first = AsturDutyDrafts.Build(profile, "Watch numbers", "daily 09:00", rig.Clock.GetUtcNow(), TimeZoneInfo.Utc).Duty!;
        var again = AsturDutyDrafts.Build(profile, "Watch numbers", "daily 10:00", rig.Clock.GetUtcNow(), TimeZoneInfo.Utc).Duty!;
        Assert.Empty(rig.Host.Scheduler.Duties);
        Assert.Equal(first.Id, again.Id);
        Assert.NotNull(rig.Host.Save(first).Duty);
        Assert.NotNull(rig.Host.Save(again).Duty);
        Assert.Single(rig.Host.Scheduler.Duties);
    }
}
