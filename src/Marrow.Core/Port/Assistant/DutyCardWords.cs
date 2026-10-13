// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.1, 7.2, 7.3; canvas 5; unit AS-10; ED-NEW-dextra-as10-9). No Swift twin.
// The plain words around a duty: its trigger ("Every day at 08:00", "New files matching *.pdf in D:\Store\Invoices"), its next
// run ("Tomorrow 08:00", "in 41 min", "On change"), its report style, the inputs of the card the owner sees before it is
// saved, and the warning for a trigger that fires too often. Pure.
using System.Globalization;
using System.Text;

namespace Marrow.Core.Port.Assistant;

public static class DutyCardWords
{
    public static string Trigger(DutyTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger switch
        {
            TimeTrigger time => ScheduleText.Describe(time.Schedule),
            FolderChangeTrigger folder => $"{FolderEventWords(folder.Events)} {(folder.Pattern == "*" ? "files" : "files matching " + folder.Pattern)} in {folder.Path}{(folder.Subfolders ? " and below" : "")}",
            PageWatchTrigger page => PageWords(page),
            AppEventTrigger app => AppEventWords(app),
            _ => "Run now only",
        };
    }

    private static string FolderEventWords(FolderEvents events)
    {
        var names = new List<string>();
        if (events.HasFlag(FolderEvents.New)) names.Add("new");
        if (events.HasFlag(FolderEvents.Changed)) names.Add("changed");
        if (events.HasFlag(FolderEvents.Removed)) names.Add("removed");
        if (names.Count == 0) return "No";
        var list = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];
        return char.ToUpperInvariant(list[0]) + list[1..];
    }

    private static string PageWords(PageWatchTrigger page)
    {
        var host = Uri.TryCreate(page.Url, UriKind.Absolute, out var uri) ? uri.Host + uri.AbsolutePath.TrimEnd('/') : page.Url;
        var condition = page.Condition switch
        {
            PageCondition.PriceBelow => "price below " + Money(page.PriceBelow),
            PageCondition.ContainsText => $"contains “{page.Text}”",
            _ => "changed",
        };
        return $"{Every(page.EveryMinutes)} · {condition} · {host}";
    }

    private static string Money(decimal? amount) => amount is { } value ? "$" + value.ToString("0.##", CultureInfo.InvariantCulture) : "the amount";

    private static string Every(int minutes) => ScheduleText.Describe(TimeSchedule.Every(minutes));

    private static string AppEventWords(AppEventTrigger app)
    {
        var who = string.IsNullOrWhiteSpace(app.Account) ? "an account" : app.Account;
        var window = string.IsNullOrWhiteSpace(app.Window) ? "usage" : app.Window + " window";
        return app.Event switch
        {
            AppEventKind.ChatFinished => "When a chat finishes",
            AppEventKind.HeadFailed => "When a head fails",
            AppEventKind.WindowReset => $"When {who}'s {window} resets",
            AppEventKind.AccountAbove => $"When {who} goes above {(app.AbovePercent ?? 0).ToString("0.##", CultureInfo.InvariantCulture)}%",
            _ => "When a mission finishes",
        };
    }

    public static string Report(ReportStyle style) => style switch
    {
        ReportStyle.Always => "Always",
        ReportStyle.DigestOnly => "Digest only",
        ReportStyle.NotifyMe => "Notify me",
        _ => "Silent unless changed",
    };

    public static string Missed(MissedRunPolicy policy) => policy == MissedRunPolicy.Skip ? "Skip it" : "Catch up once";

    /// <summary>When it runs next, in the owner's reading: "in 41 min", "Today 14:00", "Tomorrow 08:00", "Mon 09:00", "On change".</summary>
    public static string NextRun(DutyTrigger trigger, DateTimeOffset? next, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (trigger is not (TimeTrigger or PageWatchTrigger)) return trigger is ManualTrigger ? "Run now only" : "On change";
        if (next is not { } due) return "Not scheduled";
        var wait = due - now;
        if (wait < TimeSpan.Zero) return "Due now";
        if (wait < TimeSpan.FromMinutes(1)) return "in under a minute";
        if (wait < TimeSpan.FromMinutes(60)) return $"in {((int)Math.Ceiling(wait.TotalMinutes)).ToString(CultureInfo.InvariantCulture)} min";
        var local = TimeZoneInfo.ConvertTime(due, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var days = (local.Date - today).Days;
        return days switch
        {
            0 => "Today " + clock,
            1 => "Tomorrow " + clock,
            < 7 => local.ToString("ddd", CultureInfo.InvariantCulture) + " " + clock,
            _ => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + clock,
        };
    }

    /// <summary>What a run may do, in words: "Everything Dextra may", or the classes it is narrowed to.</summary>
    public static string Grants(Duty duty, string assistantName)
    {
        if (duty.Scope is null || (duty.Scope.AllowedClasses is null && duty.Scope.GrantIds is null)) return $"Inherits {assistantName}'s grants and can only narrow them";
        var parts = new List<string>();
        if (duty.Scope.AllowedClasses is { } classes) parts.Add("only " + (classes.Count == 0 ? "reads nth's own state" : string.Join(", ", classes.Select(ClassWords))) + " alone");
        if (duty.Scope.GrantIds is { } ids) parts.Add($"{ids.Count.ToString(CultureInfo.InvariantCulture)} of {assistantName}'s grants");
        return "Narrowed to " + string.Join(" and ", parts);
    }

    public static string ClassWords(ActionClass actionClass) => actionClass switch
    {
        ActionClass.Agents => "agents",
        ActionClass.Work => "work in its worktree",
        ActionClass.Run => "build and test commands",
        ActionClass.ReadWeb => "web reads",
        ActionClass.ReadLocal => "file reads",
        ActionClass.ConnectorWrite => "connector drafts",
        ActionClass.WriteElsewhere => "writes elsewhere",
        _ => actionClass.ToString().ToLowerInvariant(),
    };

    public static string Budget(Duty duty) =>
        (duty.BudgetUsd is { } usd ? $"At most ${usd.ToString("0.00", CultureInfo.InvariantCulture)} a run" : "The plan's own limits")
        + (string.IsNullOrWhiteSpace(duty.ReserveText) ? "" : $" · {duty.ReserveText.Trim()}");

    // "Notify me" is the one style that makes a toast, so the card that confirms it says so in full (13.1 "Duty opt-in").
    private static string ReportOnCard(ReportStyle style) =>
        style == ReportStyle.NotifyMe ? Report(style) + ": a notification each time a run finds something" : Report(style);

    /// <summary>
    /// The inputs of the card the owner sees before a duty is saved (7.1: its trigger, what it does, its grants and its budget),
    /// and when it changes an existing duty, what changed.
    /// </summary>
    public static IReadOnlyList<DecisionInput> CardInputs(Duty duty, string assistantName, Duty? existing = null, DutyForecast? forecast = null, TimeZoneInfo? zone = null)
    {
        var inputs = new List<DecisionInput>();
        void Add(string name, string value, string? before = null)
        {
            inputs.Add(new DecisionInput(name, before is not null && before != value ? $"{value} (was: {before})" : value));
        }
        string WithZone(Duty d) => Trigger(d.Trigger) + (d.Trigger is TimeTrigger t && (zone ?? DutyZones.Find(t.TimeZoneId)) is { } tz ? " (" + tz.Id + ")" : "");
        Add("Duty", duty.Name, existing?.Name);
        Add("Trigger", WithZone(duty), existing is null ? null : WithZone(existing));
        Add("What it does", duty.Instructions, existing?.Instructions);
        Add("Assistant", assistantName);
        Add("Grants", Grants(duty, assistantName), existing is null ? null : Grants(existing, assistantName));
        Add("Budget", Budget(duty), existing is null ? null : Budget(existing));
        Add("Report", ReportOnCard(duty.Report), existing is null ? null : ReportOnCard(existing.Report));
        if (duty.Hours is { } hours) Add("Active hours", hours.ToString());
        Add("If nth was closed", Missed(duty.Missed));
        if (duty.NeedsDesktop) Add("Needs the desktop", "Yes: on a locked PC it does what needs no desktop, then waits for you to unlock");
        if (duty.WakePc) Add("Wakes the PC", "Yes, through Windows Task Scheduler");
        if (forecast is { } f) Add("How often", f.Words);
        return inputs;
    }

    /// <summary>A sentence for a duty's last runs: "3 runs, last Done".</summary>
    public static string HistoryLine(DutyHistoryEntry run, TimeZoneInfo zone)
    {
        var at = TimeZoneInfo.ConvertTime(run.At, zone).ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
        var text = new StringBuilder(at).Append(' ').Append(run.Outcome switch
        {
            DutyRunOutcome.NothingChanged => "Nothing changed (silent)",
            DutyRunOutcome.Done => "Done",
            DutyRunOutcome.Failed => "Failed",
            DutyRunOutcome.Skipped => "Skipped",
            DutyRunOutcome.Stopped => "Stopped",
            DutyRunOutcome.Refused => "Refused",
            _ => "Started",
        });
        if (!string.IsNullOrWhiteSpace(run.Note)) text.Append(": ").Append(run.Note);
        return text.ToString();
    }
}
