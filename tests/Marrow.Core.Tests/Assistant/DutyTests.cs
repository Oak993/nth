using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1, 7.4; ED-NEW-dextra-as10-4): what a duty is and when it cannot be saved. The defaults are the
// design's (silent unless changed, catch up once, on). It keeps the last 50 runs, newest first. A duty that needs the desktop is
// never offered the Task Scheduler wake, and says why.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-4")]
public sealed class DutyTests
{
    private static Duty Sample(DutyTrigger? trigger = null) =>
        new("d1", "Morning digest", "dextra", trigger ?? new TimeTrigger(TimeSchedule.Daily(new TimeOnly(8, 0)), "UTC"), "Compose the digest.");

    [Fact]
    public void A_new_duty_has_the_designs_defaults()
    {
        var duty = Sample();
        Assert.Equal(ReportStyle.SilentUnlessChanged, duty.Report);
        Assert.Equal(MissedRunPolicy.CatchUpOnce, duty.Missed);
        Assert.True(duty.Enabled);
        Assert.False(duty.WakePc);
        Assert.False(duty.NeedsDesktop);
        Assert.Null(duty.Scope);
        Assert.Null(duty.Hours);
        Assert.Null(duty.Problem());
    }

    [Theory]
    [InlineData("", "Give the duty a name.")]
    [InlineData("   ", "Give the duty a name.")]
    public void A_duty_without_a_name_cannot_be_saved(string name, string problem) => Assert.Equal(problem, (Sample() with { Name = name }).Problem());

    [Fact]
    public void A_duty_with_nothing_to_do_or_a_bad_id_or_a_bad_trigger_cannot_be_saved()
    {
        Assert.NotNull((Sample() with { Instructions = "  " }).Problem());
        Assert.Null((Sample() with { Instructions = "", Skill = "digest" }).Problem());
        Assert.NotNull((Sample() with { Id = "Has Space" }).Problem());
        Assert.NotNull((Sample() with { Id = "../x" }).Problem());
        Assert.NotNull((Sample() with { Name = new string('x', Duty.NameLimit + 1) }).Problem());
        Assert.NotNull((Sample() with { BudgetUsd = -1 }).Problem());
        Assert.NotNull(Sample(new TimeTrigger(TimeSchedule.Every(1), "UTC")).Problem());
        Assert.NotNull(Sample(new TimeTrigger(TimeSchedule.Daily(new TimeOnly(8, 0)), "Nowhere/Standard")).Problem());
    }

    [Fact]
    public void A_duty_that_needs_the_desktop_is_not_offered_the_wake_and_cannot_be_saved_with_it()
    {
        var needs = Sample() with { NeedsDesktop = true };
        var offer = needs.WakeOffer();
        Assert.False(offer.Offered);
        Assert.Contains("needs the desktop", offer.Reason, StringComparison.Ordinal);
        Assert.NotNull((needs with { WakePc = true }).Problem());
        Assert.True(Sample().WakeOffer().Offered);
        Assert.Null((Sample() with { WakePc = true }).Problem());
        var folder = Sample(new FolderChangeTrigger(@"C:\Inbox"));
        Assert.False(folder.WakeOffer().Offered);
    }

    [Fact]
    public void The_history_keeps_the_last_fifty_runs_newest_first()
    {
        var duty = Sample();
        var start = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 60; i++) duty = duty.WithRun(new DutyRun(start.AddHours(i), DutyCause.Time, DutyRunOutcome.Done));
        Assert.Equal(Duty.HistoryLimit, duty.History.Count);
        Assert.Equal(start.AddHours(59), duty.History[0].At);
        Assert.Equal(start.AddHours(10), duty.History[^1].At);
        Assert.Equal(start.AddHours(59), duty.LastRun);
    }

    [Fact]
    public void A_run_is_updated_by_its_mission_when_the_mission_ends()
    {
        var mission = Guid.NewGuid();
        var other = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        var duty = Sample()
            .WithRun(new DutyRun(at, DutyCause.Time, DutyRunOutcome.Started) { MissionId = other })
            .WithRun(new DutyRun(at.AddHours(1), DutyCause.Time, DutyRunOutcome.Started) { MissionId = mission });
        var ended = duty.WithRunUpdated(mission, r => r with { Outcome = DutyRunOutcome.NothingChanged });
        Assert.Equal(DutyRunOutcome.NothingChanged, ended.History[0].Outcome);
        Assert.Equal(DutyRunOutcome.Started, ended.History[1].Outcome);
    }

    [Fact]
    public void Ids_are_short_safe_tokens()
    {
        var id = Duty.NewId();
        Assert.Matches(Duty.IdShape, id);
        Assert.NotEqual(id, Duty.NewId());
    }

    [Fact]
    public void Triggers_state_their_own_problems()
    {
        Assert.NotNull(new FolderChangeTrigger("relative").Problem());
        Assert.NotNull(new FolderChangeTrigger(@"C:\Inbox") { Events = FolderEvents.None }.Problem());
        Assert.Null(new FolderChangeTrigger(@"C:\Inbox").Problem());
        Assert.NotNull(new PageWatchTrigger("ftp://x.example/a", 60).Problem());
        Assert.NotNull(new PageWatchTrigger("https://user:pass@x.example/a", 60).Problem());
        Assert.NotNull(new PageWatchTrigger("https://x.example/a", 4).Problem());
        Assert.NotNull(new PageWatchTrigger("https://x.example/a", 60) { Condition = PageCondition.PriceBelow }.Problem());
        Assert.NotNull(new PageWatchTrigger("https://x.example/a", 60) { Condition = PageCondition.ContainsText }.Problem());
        Assert.Null(new PageWatchTrigger("https://x.example/a", 60) { Condition = PageCondition.PriceBelow, PriceBelow = 650 }.Problem());
        Assert.NotNull(new AppEventTrigger(AppEventKind.AccountAbove).Problem());
        Assert.Null(new AppEventTrigger(AppEventKind.AccountAbove) { AbovePercent = 80 }.Problem());
        Assert.Null(new AppEventTrigger(AppEventKind.WindowReset).Problem());
        Assert.Null(new ManualTrigger().Problem());
    }

    [Fact]
    public void Unknown_fields_compare_by_content()
    {
        var a = Sample() with { Unknown = new ValueMap<string, string>([new("future", "{\"x\":1}")]) };
        var b = Sample() with { Unknown = new ValueMap<string, string>([new("future", "{\"x\":1}")]) };
        Assert.Equal(a, b);
    }
}
