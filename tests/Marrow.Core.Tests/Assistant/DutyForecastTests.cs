using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.3 "Broad-trigger warning", 7.1 "Silent unless something changed"; ED-NEW-dextra-as10-9, -10):
// a trigger likely to fire more than 20 times a day warns, with an estimate of its daily cost; a duty's report ends with the
// nothing-changed line when there was nothing to say.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-9")]
public sealed class DutyForecastTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static Duty With(DutyTrigger trigger) => new("d1", "Watch", "dextra", trigger, "Do it.") { Since = Now.AddDays(-3) };

    [Fact]
    public void A_daily_duty_runs_once_a_day_and_is_not_broad()
    {
        var forecast = DutyForecast.Of(With(new TimeTrigger(TimeSchedule.Daily(new TimeOnly(8, 0)), "UTC")), Now);
        Assert.Equal(1, forecast.RunsPerDay);
        Assert.False(forecast.Broad);
        Assert.Equal("About 1 a day", forecast.Words);
    }

    [Fact]
    public void An_every_five_minutes_duty_is_broad_and_says_what_it_costs_a_day()
    {
        var duty = With(new TimeTrigger(TimeSchedule.Every(5), "UTC")) with { BudgetUsd = 0.5m };
        var forecast = DutyForecast.Of(duty, Now);
        Assert.Equal(288, forecast.RunsPerDay);
        Assert.True(forecast.Broad);
        Assert.Contains("About 288 a day", forecast.Words, StringComparison.Ordinal);
        Assert.Contains("up to $144.00 a day at $0.50 a run", forecast.Words, StringComparison.Ordinal);
        Assert.Contains("more than 20 a day", forecast.Words, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cron_line_is_counted_on_the_schedule_itself()
    {
        var forecast = DutyForecast.Of(With(new TimeTrigger(TimeSchedule.FromCron("0 9-17 * * *"), "UTC")), Now);
        Assert.Equal(9, forecast.RunsPerDay);
        Assert.False(forecast.Broad);
    }

    [Fact]
    public void A_watch_on_every_file_is_broad_unless_nth_measured_it_quiet()
    {
        var all = With(new FolderChangeTrigger(@"C:\Users\me\Downloads"));
        Assert.True(DutyForecast.Of(all, Now).Broad);
        Assert.False(DutyForecast.Of(all, Now, observedPerDay: 3).Broad);
        Assert.True(DutyForecast.Of(all, Now, observedPerDay: 60).Broad);
        Assert.False(DutyForecast.Of(With(new FolderChangeTrigger(@"C:\Inbox") { Pattern = "*.pdf" }), Now).Broad);
    }

    [Fact]
    public void A_page_that_changes_can_run_at_every_look_but_a_condition_fires_rarely()
    {
        var changes = With(new PageWatchTrigger("https://shop.example/a", 5));
        Assert.True(DutyForecast.Of(changes, Now).Broad);
        var price = With(new PageWatchTrigger("https://shop.example/a", 5) { Condition = PageCondition.PriceBelow, PriceBelow = 650 });
        Assert.False(DutyForecast.Of(price, Now).Broad);
    }

    [Fact]
    public void A_manual_duty_runs_only_when_the_owner_says()
    {
        var forecast = DutyForecast.Of(With(new ManualTrigger()), Now);
        Assert.Equal(0, forecast.RunsPerDay);
        Assert.Equal("Only when you run it", forecast.Words);
    }

    [Fact]
    public void The_nothing_changed_line_must_be_the_last_line_of_the_report()
    {
        Assert.True(DutyResult.NothingChanged("Checked the page.\nRESULT: nothing_changed"));
        Assert.True(DutyResult.NothingChanged("Checked.\n\n  result: NOTHING_CHANGED  \n\n"));
        Assert.False(DutyResult.NothingChanged("RESULT: nothing_changed\nBut then the price fell to $640."));
        Assert.False(DutyResult.NothingChanged("The price fell."));
        Assert.False(DutyResult.NothingChanged(null));
        Assert.False(DutyResult.NothingChanged("   "));
        Assert.Contains(DutyResult.NothingChangedLine, DutyResult.Instruction, StringComparison.Ordinal);
    }
}
