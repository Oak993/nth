using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1, 7.2; canvas 5; ED-NEW-dextra-as10-9): the words the card and the duty list use. Each
// trigger says itself the way the canvas does; the next run reads "in 41 min", "Tomorrow 08:00", "Mon 09:00", "On change"; the
// card before a duty is saved shows its trigger, what it does, its grants and its budget, and what an edit changes.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-9")]
public sealed class DutyWordsTests
{
    private static readonly TimeZoneInfo Karachi = TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.FromHours(5)); // a Tuesday morning in Karachi

    private static Duty Daily(string name = "Morning digest") =>
        new("d1", name, "dextra", new TimeTrigger(TimeSchedule.Daily(new TimeOnly(8, 0)), "Pakistan Standard Time"), "Compose the digest.");

    [Fact]
    public void Each_trigger_says_itself_in_plain_words()
    {
        Assert.Equal("Every day at 08:00", DutyWords.Trigger(Daily().Trigger));
        Assert.Equal("New files matching *.pdf in D:\\Store\\Invoices", DutyWords.Trigger(new FolderChangeTrigger("D:\\Store\\Invoices") { Pattern = "*.pdf" }));
        Assert.Equal("New, changed or removed files in D:\\Inbox and below", DutyWords.Trigger(new FolderChangeTrigger("D:\\Inbox") { Events = FolderEvents.All, Subfolders = true }));
        Assert.Equal("Every 2 h · price below $650 · shop.example/espresso/pro",
            DutyWords.Trigger(new PageWatchTrigger("https://shop.example/espresso/pro/", 120) { Condition = PageCondition.PriceBelow, PriceBelow = 650 }));
        Assert.Equal("Every 30 min · contains “in stock” · shop.example/a",
            DutyWords.Trigger(new PageWatchTrigger("https://shop.example/a", 30) { Condition = PageCondition.ContainsText, Text = "in stock" }));
        Assert.Equal("When Codex A's weekly window resets", DutyWords.Trigger(new AppEventTrigger(AppEventKind.WindowReset) { Account = "Codex A", Window = "weekly" }));
        Assert.Equal("When Claude B goes above 80%", DutyWords.Trigger(new AppEventTrigger(AppEventKind.AccountAbove) { Account = "Claude B", AbovePercent = 80 }));
        Assert.Equal("When a chat finishes", DutyWords.Trigger(new AppEventTrigger(AppEventKind.ChatFinished)));
        Assert.Equal("Run now only", DutyWords.Trigger(new ManualTrigger()));
    }

    [Fact]
    public void The_next_run_reads_the_way_the_duty_list_shows_it()
    {
        var time = Daily().Trigger;
        Assert.Equal("in 41 min", DutyWords.NextRun(time, Now.AddMinutes(41), Now, Karachi));
        Assert.Equal("in under a minute", DutyWords.NextRun(time, Now.AddSeconds(20), Now, Karachi));
        Assert.Equal("Today 14:00", DutyWords.NextRun(time, new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.FromHours(5)), Now, Karachi));
        Assert.Equal("Tomorrow 08:00", DutyWords.NextRun(time, new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.FromHours(5)), Now, Karachi));
        Assert.Equal("Mon 09:00", DutyWords.NextRun(time, new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(5)), Now, Karachi));
        Assert.Equal("2026-11-02 09:00", DutyWords.NextRun(time, new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(5)), Now, Karachi));
        Assert.Equal("Not scheduled", DutyWords.NextRun(time, null, Now, Karachi));
        Assert.Equal("On change", DutyWords.NextRun(new FolderChangeTrigger("D:\\Inbox"), null, Now, Karachi));
        Assert.Equal("Run now only", DutyWords.NextRun(new ManualTrigger(), null, Now, Karachi));
    }

    [Fact]
    public void The_card_shows_the_trigger_what_it_does_its_grants_and_its_budget()
    {
        var duty = Daily() with { BudgetUsd = 2m, ReserveText = "keep 30%", Hours = ActiveHours.Weekdays(new TimeOnly(7, 0), new TimeOnly(19, 0)), WakePc = true };
        var inputs = DutyWords.CardInputs(duty, "Dextra").ToDictionary(i => i.Name, i => i.Value);
        Assert.Equal("Every day at 08:00 (Pakistan Standard Time)", inputs["Trigger"]);
        Assert.Equal("Compose the digest.", inputs["What it does"]);
        Assert.Equal("Inherits Dextra's grants and can only narrow them", inputs["Grants"]);
        Assert.Equal("At most $2.00 a run · keep 30%", inputs["Budget"]);
        Assert.Equal("Silent unless changed", inputs["Report"]);
        Assert.Equal("07:00 to 19:00", inputs["Active hours"]);
        Assert.Equal("Catch up once", inputs["If nth was closed"]);
        Assert.Equal("Yes, through Windows Task Scheduler", inputs["Wakes the PC"]);
    }

    [Fact]
    public void A_narrowed_duty_says_what_it_is_narrowed_to()
    {
        var scope = new DutyScope { AllowedClasses = new ValueList<ActionClass>([ActionClass.ReadWeb, ActionClass.ReadLocal]), GrantIds = new ValueList<Guid>([Guid.NewGuid()]) };
        var said = DutyWords.Grants(Daily() with { Scope = scope }, "Dextra");
        Assert.Equal("Narrowed to only web reads, file reads alone and 1 of Dextra's grants", said);
        Assert.Equal("Narrowed to only reads nth's own state alone",
            DutyWords.Grants(Daily() with { Scope = new DutyScope { AllowedClasses = ValueList<ActionClass>.Empty } }, "Dextra"));
    }

    [Fact]
    public void The_card_for_an_edit_says_what_it_changes()
    {
        var before = Daily() with { BudgetUsd = 2m };
        var after = before with { Trigger = new TimeTrigger(TimeSchedule.Daily(new TimeOnly(9, 30)), "Pakistan Standard Time"), BudgetUsd = 5m };
        var inputs = DutyWords.CardInputs(after, "Dextra", existing: before).ToDictionary(i => i.Name, i => i.Value);
        Assert.Equal("Every day at 09:30 (Pakistan Standard Time) (was: Every day at 08:00 (Pakistan Standard Time))", inputs["Trigger"]);
        Assert.Equal("At most $5.00 a run (was: At most $2.00 a run)", inputs["Budget"]);
        Assert.Equal("Compose the digest.", inputs["What it does"]);
    }

    [Fact]
    public void A_duty_that_needs_the_desktop_says_what_happens_on_a_locked_pc()
    {
        var inputs = DutyWords.CardInputs(Daily() with { NeedsDesktop = true }, "Dextra").ToDictionary(i => i.Name, i => i.Value);
        Assert.Contains("waits for you to unlock", inputs["Needs the desktop"], StringComparison.Ordinal);
        Assert.DoesNotContain("Wakes the PC", inputs.Keys);
    }

    [Fact]
    public void The_history_lines_name_the_outcome()
    {
        var run = new DutyRun(new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.FromHours(5)), DutyCause.Time, DutyRunOutcome.NothingChanged);
        Assert.Equal("Oct 13 06:00 Nothing changed (silent)", DutyWords.HistoryLine(run, Karachi));
        Assert.Equal("Oct 13 06:00 Skipped: nth was closed", DutyWords.HistoryLine(run with { Outcome = DutyRunOutcome.Skipped, Note = "nth was closed" }, Karachi));
    }
}
