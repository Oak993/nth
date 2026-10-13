// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4 "Wake the PC", 16.1 "Task Scheduler"; unit AS-10; ED-NEW-dextra-as10-15). No Swift twin.
// The Windows Task Scheduler entry that wakes the PC for a duty: a task under \nth\ named duty-<id> whose trigger matches the
// duty's schedule, with WakeToRun, that starts "nth --duty <id>" for the signed-in user at the lowest run level. Calendar
// schedules that read the same on the PC's own clock become a repeating trigger (so the wake still comes when nth is not
// running to renew it); a duty in another zone, a cron line and a monthly day nth cannot express become the single next time,
// which the app renews after each run and at each start. Pure: the XML is built here and registered by WakeSync.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>One entry to register: the task's name under \nth\, its XML and the duty it wakes the PC for.</summary>
public sealed record WakeEntry(string Name, string Xml, string DutyId);

public static partial class WakeTaskXml
{
    /// <summary>The Task Scheduler folder every nth entry lives in.</summary>
    public const string Folder = "nth";

    public const string NamePrefix = "duty-";

    private const string Namespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [GeneratedRegex(@"\Aduty-[a-z0-9][a-z0-9-]{0,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NameShape();

    public static string NameOf(string dutyId) => NamePrefix + dutyId;

    /// <summary>Whether a task name is one nth made (and so one nth may change or remove).</summary>
    public static bool IsOurs(string name) => NameShape().IsMatch(name);

    /// <summary>
    /// The entry for a duty, or null when it gets none (not on, not a time duty, needs the desktop, no wake asked, or the schedule has
    /// no next time). <paramref name="exePath"/> is nth's own executable.
    /// </summary>
    public static WakeEntry? For(Duty duty, string exePath, DateTimeOffset now, TimeZoneInfo pcZone)
    {
        ArgumentNullException.ThrowIfNull(duty);
        if (!duty.Enabled || !duty.WakePc || duty.NeedsDesktop || duty.Trigger is not TimeTrigger trigger || duty.Problem() is not null) return null;
        if (DutyZones.Find(trigger.TimeZoneId) is not { } zone) return null;
        if (string.IsNullOrWhiteSpace(exePath) || !Path.IsPathFullyQualified(exePath)) return null;
        var schedule = trigger.Schedule;
        var next = ScheduleMath.Next(schedule, zone, now, duty.Since);
        if (next is null) return null;
        var onPcClock = zone.Id == pcZone.Id;
        var local = TimeZoneInfo.ConvertTime(next.Value, pcZone).DateTime;
        var xml = Build(duty, exePath, schedule, local, onPcClock);
        return new WakeEntry(NameOf(duty.Id), xml, duty.Id);
    }

    private static string Build(Duty duty, string exePath, TimeSchedule schedule, DateTime firstLocal, bool onPcClock)
    {
        var builder = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false, Encoding = Encoding.Unicode };
        using (var writer = new StringWriterWithEncoding(builder, Encoding.Unicode))
        using (var xml = XmlWriter.Create(writer, settings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("Task", Namespace);
            xml.WriteAttributeString("version", "1.4");
            xml.WriteStartElement("RegistrationInfo");
            xml.WriteElementString("Description", $"nth wakes this PC for the duty “{Clean(duty.Name)}”. Turn it off in nth, on the duty's card.");
            xml.WriteElementString("URI", @"\" + Folder + @"\" + NameOf(duty.Id));
            xml.WriteEndElement();
            xml.WriteStartElement("Triggers");
            Trigger(xml, schedule, firstLocal, onPcClock);
            xml.WriteEndElement();
            xml.WriteStartElement("Principals");
            xml.WriteStartElement("Principal");
            xml.WriteAttributeString("id", "Author");
            xml.WriteElementString("LogonType", "InteractiveToken");
            xml.WriteElementString("RunLevel", "LeastPrivilege");
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteStartElement("Settings");
            xml.WriteElementString("MultipleInstancesPolicy", "IgnoreNew");
            xml.WriteElementString("DisallowStartIfOnBatteries", "false");
            xml.WriteElementString("StopIfGoingOnBatteries", "false");
            xml.WriteElementString("AllowHardTerminate", "true");
            xml.WriteElementString("StartWhenAvailable", "false");
            xml.WriteElementString("RunOnlyIfNetworkAvailable", "false");
            xml.WriteElementString("AllowStartOnDemand", "true");
            xml.WriteElementString("Enabled", "true");
            xml.WriteElementString("Hidden", "false");
            xml.WriteElementString("WakeToRun", "true");
            xml.WriteElementString("ExecutionTimeLimit", "PT4H");
            xml.WriteElementString("Priority", "7");
            xml.WriteEndElement();
            xml.WriteStartElement("Actions");
            xml.WriteAttributeString("Context", "Author");
            xml.WriteStartElement("Exec");
            xml.WriteElementString("Command", exePath);
            xml.WriteElementString("Arguments", "--duty " + duty.Id);
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }
        return builder.ToString();
    }

    // A duty's name is the owner's (or an agent's) text: it goes into an XML text node, and control characters are dropped.
    private static string Clean(string text) => new(text.Where(c => !char.IsControl(c)).Take(120).ToArray());

    private static void Trigger(XmlWriter xml, TimeSchedule schedule, DateTime firstLocal, bool onPcClock)
    {
        var start = firstLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        if (onPcClock && schedule.Kind is ScheduleKind.Daily or ScheduleKind.Weekdays or ScheduleKind.Weekly or ScheduleKind.Monthly)
        {
            xml.WriteStartElement("CalendarTrigger");
            xml.WriteElementString("StartBoundary", start);
            xml.WriteElementString("Enabled", "true");
            switch (schedule.Kind)
            {
                case ScheduleKind.Daily:
                    xml.WriteStartElement("ScheduleByDay");
                    xml.WriteElementString("DaysInterval", "1");
                    xml.WriteEndElement();
                    break;
                case ScheduleKind.Monthly:
                    xml.WriteStartElement("ScheduleByMonth");
                    xml.WriteStartElement("DaysOfMonth");
                    xml.WriteElementString("Day", schedule.DayOfMonth.ToString(CultureInfo.InvariantCulture));
                    if (schedule.DayOfMonth == 31) xml.WriteElementString("Day", "Last");
                    xml.WriteEndElement();
                    xml.WriteStartElement("Months");
                    foreach (var month in new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" })
                        xml.WriteElementString(month, "");
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    break;
                default:
                    xml.WriteStartElement("ScheduleByWeek");
                    xml.WriteStartElement("DaysOfWeek");
                    var days = schedule.Kind == ScheduleKind.Weekdays ? WeekDays.Weekdays : schedule.Days;
                    foreach (var (name, day) in new[] { ("Sunday", DayOfWeek.Sunday), ("Monday", DayOfWeek.Monday), ("Tuesday", DayOfWeek.Tuesday), ("Wednesday", DayOfWeek.Wednesday),
                                 ("Thursday", DayOfWeek.Thursday), ("Friday", DayOfWeek.Friday), ("Saturday", DayOfWeek.Saturday) })
                        if (days.Has(day)) xml.WriteElementString(name, "");
                    xml.WriteEndElement();
                    xml.WriteElementString("WeeksInterval", "1");
                    xml.WriteEndElement();
                    break;
            }
            xml.WriteEndElement();
            return;
        }
        xml.WriteStartElement("TimeTrigger");
        if (schedule.Kind == ScheduleKind.Every)
        {
            xml.WriteStartElement("Repetition");
            xml.WriteElementString("Interval", "PT" + schedule.EveryMinutes.ToString(CultureInfo.InvariantCulture) + "M");
            xml.WriteEndElement();
        }
        xml.WriteElementString("StartBoundary", start);
        xml.WriteElementString("Enabled", "true");
        xml.WriteEndElement();
    }

    // A StringWriter that reports the encoding the document declares (UTF-16), which schtasks wants in the file.
    private sealed class StringWriterWithEncoding(StringBuilder builder, Encoding encoding) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => encoding;
    }
}
