using System.Xml.Linq;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4 "Wake the PC", 16.1 "Task Scheduler"; ED-NEW-dextra-as10-15): the Task Scheduler entry for a
// duty that wakes the PC. WakeToRun, nth --duty <id>, the signed-in user at the lowest run level, a trigger that matches the
// duty's schedule on the PC's own clock; the single next time for anything it cannot express. A duty that needs the desktop, or
// is off, or did not ask, gets none. The real Task Scheduler takes this XML in the Native test.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-15")]
public sealed class WakeTaskXmlTests
{
    private static readonly TimeZoneInfo Pc = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero); // 08:00 on the PC
    private const string Exe = @"C:\Users\me\AppData\Local\nth\current\nth.exe";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static Duty Wake(TimeSchedule schedule, string zone = "Eastern Standard Time", string id = "d1") =>
        new(id, "Morning digest", "dextra", new TimeTrigger(schedule, zone), "Compose it.") { WakePc = true, Since = Now.AddDays(-3) };

    private static XElement Task(Duty duty)
    {
        var entry = WakeTaskXml.For(duty, Exe, Now, Pc);
        Assert.NotNull(entry);
        Assert.Equal("duty-" + duty.Id, entry.Name);
        Assert.Equal(duty.Id, entry.DutyId);
        return XDocument.Parse(entry.Xml).Root!;
    }

    [Fact]
    public void The_entry_wakes_the_pc_and_starts_nth_hidden_for_that_one_duty_as_the_signed_in_user()
    {
        var task = Task(Wake(TimeSchedule.Daily(new TimeOnly(8, 30))));
        var settings = task.Element(Ns + "Settings")!;
        Assert.Equal("true", settings.Element(Ns + "WakeToRun")!.Value);
        Assert.Equal("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        var principal = task.Element(Ns + "Principals")!.Element(Ns + "Principal")!;
        Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType")!.Value);
        Assert.Equal("LeastPrivilege", principal.Element(Ns + "RunLevel")!.Value);
        var exec = task.Element(Ns + "Actions")!.Element(Ns + "Exec")!;
        Assert.Equal(Exe, exec.Element(Ns + "Command")!.Value);
        Assert.Equal("--duty d1", exec.Element(Ns + "Arguments")!.Value);
        Assert.Equal(@"\nth\duty-d1", task.Element(Ns + "RegistrationInfo")!.Element(Ns + "URI")!.Value);
    }

    [Fact]
    public void A_daily_duty_on_the_pc_clock_is_a_repeating_daily_trigger_starting_at_its_next_time()
    {
        var trigger = Task(Wake(TimeSchedule.Daily(new TimeOnly(8, 30)))).Element(Ns + "Triggers")!.Element(Ns + "CalendarTrigger")!;
        Assert.Equal("2026-10-13T08:30:00", trigger.Element(Ns + "StartBoundary")!.Value);
        Assert.Equal("1", trigger.Element(Ns + "ScheduleByDay")!.Element(Ns + "DaysInterval")!.Value);
    }

    [Fact]
    public void Weekdays_and_weekly_duties_name_their_days()
    {
        var weekdays = Task(Wake(TimeSchedule.OnWeekdays(new TimeOnly(9, 0)))).Descendants(Ns + "DaysOfWeek").Single();
        Assert.Equal(["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"], weekdays.Elements().Select(e => e.Name.LocalName));
        var weekly = Task(Wake(TimeSchedule.Weekly(WeekDays.Monday | WeekDays.Sunday, new TimeOnly(18, 0)))).Descendants(Ns + "ScheduleByWeek").Single();
        Assert.Equal(["Sunday", "Monday"], weekly.Element(Ns + "DaysOfWeek")!.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("1", weekly.Element(Ns + "WeeksInterval")!.Value);
    }

    [Fact]
    public void A_monthly_duty_on_the_31st_also_runs_on_the_last_day_of_a_shorter_month()
    {
        var days = Task(Wake(TimeSchedule.Monthly(31, new TimeOnly(9, 0)))).Descendants(Ns + "DaysOfMonth").Single();
        Assert.Equal(["31", "Last"], days.Elements().Select(e => e.Value));
        var fifteenth = Task(Wake(TimeSchedule.Monthly(15, new TimeOnly(9, 0)))).Descendants(Ns + "DaysOfMonth").Single();
        Assert.Equal(["15"], fifteenth.Elements().Select(e => e.Value));
    }

    [Fact]
    public void An_every_schedule_repeats_from_its_next_time()
    {
        var trigger = Task(Wake(TimeSchedule.Every(90))).Element(Ns + "Triggers")!.Element(Ns + "TimeTrigger")!;
        Assert.Equal("PT90M", trigger.Element(Ns + "Repetition")!.Element(Ns + "Interval")!.Value);
        Assert.True(DateTime.Parse(trigger.Element(Ns + "StartBoundary")!.Value, System.Globalization.CultureInfo.InvariantCulture) > new DateTime(2026, 10, 13, 8, 0, 0));
    }

    [Fact]
    public void A_cron_line_and_a_duty_in_another_zone_are_the_single_next_time_on_the_pcs_clock()
    {
        var cron = Task(Wake(TimeSchedule.FromCron("0 9 * * 1-5"))).Element(Ns + "Triggers")!.Element(Ns + "TimeTrigger")!;
        Assert.Equal("2026-10-13T09:00:00", cron.Element(Ns + "StartBoundary")!.Value);
        Assert.Null(cron.Element(Ns + "Repetition"));
        // 08:30 in Karachi is 03:30 UTC, 23:30 the evening before on the PC (EDT, UTC-4): the next one is the 14th at 08:30 Karachi = 23:30 on the 13th.
        var karachi = Task(Wake(TimeSchedule.Daily(new TimeOnly(8, 30)), zone: "Pakistan Standard Time")).Element(Ns + "Triggers")!;
        Assert.Null(karachi.Element(Ns + "CalendarTrigger"));
        Assert.Equal("2026-10-13T23:30:00", karachi.Element(Ns + "TimeTrigger")!.Element(Ns + "StartBoundary")!.Value);
    }

    [Fact]
    public void A_once_duty_is_its_one_time_and_none_once_it_has_passed()
    {
        var once = Task(Wake(TimeSchedule.Once(new DateTime(2026, 11, 1, 7, 15, 0)))).Element(Ns + "Triggers")!.Element(Ns + "TimeTrigger")!;
        Assert.Equal("2026-11-01T07:15:00", once.Element(Ns + "StartBoundary")!.Value);
        Assert.Null(WakeTaskXml.For(Wake(TimeSchedule.Once(new DateTime(2026, 9, 1, 7, 15, 0))), Exe, Now, Pc));
    }

    [Fact]
    public void A_duty_name_with_markup_or_control_characters_is_text_in_the_description_only()
    {
        var duty = Wake(TimeSchedule.Daily(new TimeOnly(8, 0))) with { Name = "<b>Me & \"you\"</b>\u0007\r\nnext" };
        var description = Task(duty).Element(Ns + "RegistrationInfo")!.Element(Ns + "Description")!.Value;
        Assert.Contains("<b>Me & \"you\"</b>next", description, StringComparison.Ordinal);
        Assert.DoesNotContain('\u0007', description);
        var entry = WakeTaskXml.For(duty, Exe, Now, Pc)!;
        Assert.DoesNotContain("<b>", entry.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duty_that_cannot_or_did_not_ask_for_a_wake_gets_no_entry()
    {
        var daily = TimeSchedule.Daily(new TimeOnly(8, 0));
        Assert.Null(WakeTaskXml.For(Wake(daily) with { WakePc = false }, Exe, Now, Pc));
        Assert.Null(WakeTaskXml.For(Wake(daily) with { Enabled = false }, Exe, Now, Pc));
        Assert.Null(WakeTaskXml.For(Wake(daily) with { NeedsDesktop = true }, Exe, Now, Pc)); // 7.4: greyed out for these
        Assert.Null(WakeTaskXml.For(Wake(daily) with { Trigger = new FolderChangeTrigger(@"C:\Inbox") }, Exe, Now, Pc));
        Assert.Null(WakeTaskXml.For(Wake(daily) with { Name = "" }, Exe, Now, Pc));
        Assert.Null(WakeTaskXml.For(Wake(daily, zone: "No/Such/Zone"), Exe, Now, Pc));
        Assert.Null(WakeTaskXml.For(Wake(daily), "nth.exe", Now, Pc)); // a relative path is not a place Windows can start
        Assert.Null(WakeTaskXml.For(Wake(daily), "", Now, Pc));
    }

    [Theory]
    [InlineData("duty-d1", true)]
    [InlineData("duty-d0123456789ab", true)]
    [InlineData("duty-", false)]
    [InlineData("duty-Has Space", false)]
    [InlineData("duty-..", false)]
    [InlineData(@"duty-a\b", false)]
    [InlineData("other", false)]
    [InlineData("", false)]
    public void Only_names_nth_makes_are_nth_to_change(string name, bool ours) => Assert.Equal(ours, WakeTaskXml.IsOurs(name));
}
