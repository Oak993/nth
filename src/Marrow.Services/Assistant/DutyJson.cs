// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.1, 15.3; unit AS-10; ED-NEW-dextra-as10-5). No Swift twin.
// The duties of one assistant as assistants/<id>/duties.json: a versioned document read strictly. A field this build does not
// know survives a load and a save (on the duty and on the document); a duty that cannot be read is kept as written and listed
// as a problem, never dropped; a document written by a newer build (a higher version) is read for display and left untouched.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>An assistant's duties as the file holds them.</summary>
public sealed record DutyDocument(IReadOnlyList<Duty> Duties)
{
    public static DutyDocument Empty { get; } = new([]);

    /// <summary>The version the file was written with.</summary>
    public int Version { get; init; } = DutyJson.CurrentVersion;

    /// <summary>The file was written by a newer nth: it is shown, not run, and never written back.</summary>
    public bool Newer => Version > DutyJson.CurrentVersion;

    /// <summary>Document-level fields this build does not know, as JSON text by name.</summary>
    public ValueMap<string, string> Unknown { get; init; } = ValueMap<string, string>.Empty;

    /// <summary>Entries that could not be read, as written (JSON text); they are written back as they were.</summary>
    public IReadOnlyList<string> Unreadable { get; init; } = [];

    /// <summary>What was wrong, in words for the owner.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];
}

public static class DutyJson
{
    public const int CurrentVersion = 1;

    /// <summary>The most duties one assistant keeps; a file with more is read up to this many and the rest is reported.</summary>
    public const int DutyLimit = 200;

    private static readonly HashSet<string> KnownDocument = new(StringComparer.Ordinal) { "version", "duties" };

    private static readonly HashSet<string> KnownDuty = new(StringComparer.Ordinal)
    {
        "id", "name", "assistant", "trigger", "instructions", "skill", "workflow", "project", "budgetUsd", "reserve", "scope", "report",
        "hours", "missed", "enabled", "needsDesktop", "wakePc", "createdBy", "since", "handled", "lastRun", "history",
    };

    // ---- reading ----

    /// <summary>Reads a duties document. A null document (no file) is an empty one.</summary>
    public static DutyDocument Read(JsonNode? node)
    {
        if (node is null) return DutyDocument.Empty;
        if (node is not JsonObject root) return DutyDocument.Empty with { Problems = ["The duties file is not a JSON object."], Unreadable = [node.ToJsonString()] };
        var version = root["version"] is { } v && Whole(v) is { } whole ? (int)Math.Clamp(whole, 0, int.MaxValue) : 0;
        var unknown = Unknown(root, KnownDocument);
        var problems = new List<string>();
        var unreadable = new List<string>();
        var duties = new List<Duty>();
        if (root["duties"] is JsonArray array)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in array)
            {
                if (duties.Count >= DutyLimit)
                {
                    problems.Add($"Only the first {DutyLimit.ToString(CultureInfo.InvariantCulture)} duties are used.");
                    if (entry is not null) unreadable.Add(entry.ToJsonString());
                    continue;
                }
                var duty = entry is JsonObject body ? ReadDuty(body, out _) : null;
                if (duty is null || !seen.Add(duty.Id))
                {
                    problems.Add(duty is null ? "A duty could not be read: " + (entry is JsonObject ? "its fields do not fit." : "it is not an object.") : $"Two duties share the id {duty.Id}; the second is left alone.");
                    if (entry is not null) unreadable.Add(entry.ToJsonString());
                    continue;
                }
                duties.Add(duty);
            }
        }
        else if (root["duties"] is not null)
        {
            problems.Add("The duties file's list of duties is not a list.");
        }
        if (version > CurrentVersion) problems.Add("The duties file was written by a newer nth; its duties are shown but not run, and the file is left as it is.");
        return new DutyDocument(duties) { Version = version == 0 ? CurrentVersion : version, Unknown = unknown, Unreadable = unreadable, Problems = problems };
    }

    /// <summary>One duty, or null when its fields do not fit; <paramref name="problem"/> says what.</summary>
    public static Duty? ReadDuty(JsonObject body, out string? problem)
    {
        problem = null;
        try
        {
            var trigger = ReadTrigger(Object(body, "trigger") ?? throw new FormatException("trigger"));
            var duty = new Duty(Text(body, "id") ?? throw new FormatException("id"), Text(body, "name") ?? throw new FormatException("name"),
                Text(body, "assistant") ?? throw new FormatException("assistant"), trigger, Text(body, "instructions") ?? "")
            {
                Skill = Text(body, "skill"),
                Workflow = Text(body, "workflow"),
                ProjectId = Text(body, "project") is { } project ? Guid.Parse(project) : null,
                BudgetUsd = body["budgetUsd"] is { } budget && AssistantJson.Number(budget) is { } usd ? (decimal)usd : null,
                ReserveText = Text(body, "reserve"),
                Scope = body["scope"] is JsonObject scope ? ReadScope(scope) : null,
                Report = EnumOf(body, "report", ReportStyle.SilentUnlessChanged),
                Hours = body["hours"] is JsonObject hours ? ReadHours(hours) : null,
                Missed = EnumOf(body, "missed", MissedRunPolicy.CatchUpOnce),
                Enabled = Bool(body, "enabled", true),
                NeedsDesktop = Bool(body, "needsDesktop", false),
                WakePc = Bool(body, "wakePc", false),
                CreatedBy = Text(body, "createdBy") ?? "owner",
                Since = Time(Text(body, "since") ?? throw new FormatException("since")),
                Handled = Text(body, "handled") is { } handled ? Time(handled) : null,
                LastRun = Text(body, "lastRun") is { } last ? Time(last) : null,
                History = new ValueList<DutyRun>((body["history"] as JsonArray ?? []).Select(h => ReadRun((JsonObject)h!)).Take(Marrow.Core.Port.Assistant.Duty.HistoryLimit)),
                Unknown = Unknown(body, KnownDuty),
            };
            return duty;
        }
        catch (Exception error) when (error is FormatException or InvalidOperationException or InvalidCastException or ArgumentException or OverflowException or KeyNotFoundException)
        {
            problem = error.Message;
            return null;
        }
    }

    private static DutyTrigger ReadTrigger(JsonObject o) => Text(o, "kind") switch
    {
        "time" => new TimeTrigger(ReadSchedule(Object(o, "schedule") ?? throw new FormatException("schedule")), Text(o, "zone") ?? throw new FormatException("zone")),
        "folder" => new FolderChangeTrigger(Text(o, "path") ?? throw new FormatException("path"))
        {
            Pattern = Text(o, "pattern") ?? "*",
            Events = (FolderEvents)Math.Clamp(Whole(o["events"]) ?? (long)FolderEvents.New, 0, (long)FolderEvents.All),
            Subfolders = Bool(o, "subfolders", false),
        },
        "page" => new PageWatchTrigger(Text(o, "url") ?? throw new FormatException("url"), (int)Math.Clamp(Whole(o["everyMinutes"]) ?? 0, 0, TimeSchedule.MaximumEveryMinutes))
        {
            Condition = EnumOf(o, "condition", PageCondition.Changed),
            Text = Text(o, "text"),
            PriceBelow = o["priceBelow"] is { } price && AssistantJson.Number(price) is { } below ? (decimal)below : null,
            StartMarker = Text(o, "start"),
            EndMarker = Text(o, "end"),
        },
        "app" => new AppEventTrigger(EnumOf(o, "event", AppEventKind.ChatFinished, required: true))
        {
            Account = Text(o, "account"),
            Window = Text(o, "window"),
            AbovePercent = o["above"] is { } above ? AssistantJson.Number(above) : null,
        },
        "manual" => new ManualTrigger(),
        _ => throw new FormatException("trigger kind"),
    };

    private static TimeSchedule ReadSchedule(JsonObject o)
    {
        var kind = EnumOf(o, "kind", ScheduleKind.Daily, required: true);
        var schedule = new TimeSchedule(kind);
        return kind switch
        {
            ScheduleKind.Once => schedule with { At = DateTime.SpecifyKind(DateTime.ParseExact(Text(o, "at") ?? throw new FormatException("at"), "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), DateTimeKind.Unspecified) },
            ScheduleKind.Every => schedule with { EveryMinutes = (int)Math.Clamp(Whole(o["every"]) ?? 0, 0, int.MaxValue) },
            ScheduleKind.Weekly => schedule with { Days = (WeekDays)Math.Clamp(Whole(o["days"]) ?? 0, 0, (long)WeekDays.All), TimeOfDay = Clock(o) },
            ScheduleKind.Monthly => schedule with { DayOfMonth = (int)Math.Clamp(Whole(o["day"]) ?? 1, 0, 99), TimeOfDay = Clock(o) },
            ScheduleKind.Cron => schedule with { Cron = Text(o, "cron") ?? throw new FormatException("cron") },
            _ => schedule with { TimeOfDay = Clock(o) },
        };
    }

    private static TimeOnly Clock(JsonObject o) => TimeOnly.ParseExact(Text(o, "time") ?? throw new FormatException("time"), "HH:mm", CultureInfo.InvariantCulture);

    private static DutyScope ReadScope(JsonObject o) => new()
    {
        AllowedClasses = o["classes"] is JsonArray classes ? new ValueList<ActionClass>(classes.Select(c => ParseClass(c!.GetValue<string>()))) : null,
        GrantIds = o["grants"] is JsonArray grants ? new ValueList<Guid>(grants.Select(g => Guid.Parse(g!.GetValue<string>()))) : null,
    };

    private static ActionClass ParseClass(string text) =>
        System.Enum.TryParse<ActionClass>(text, ignoreCase: false, out var value) && System.Enum.IsDefined(value) ? value : throw new FormatException("class");

    private static ActiveHours ReadHours(JsonObject o) => new(TimeOnly.ParseExact(Text(o, "from") ?? throw new FormatException("from"), "HH:mm", CultureInfo.InvariantCulture),
        TimeOnly.ParseExact(Text(o, "to") ?? throw new FormatException("to"), "HH:mm", CultureInfo.InvariantCulture),
        (WeekDays)Math.Clamp(Whole(o["days"]) ?? (long)WeekDays.All, 0, (long)WeekDays.All));

    private static DutyRun ReadRun(JsonObject o) => new(Time(Text(o, "at") ?? throw new FormatException("at")), EnumOf(o, "cause", DutyCause.Time, required: true),
        EnumOf(o, "outcome", DutyRunOutcome.Started, required: true))
    {
        MissionId = Text(o, "mission") is { } mission ? Guid.Parse(mission) : null,
        Note = Text(o, "note"),
        Count = (int)Math.Clamp(Whole(o["count"]) ?? 1, 1, int.MaxValue),
    };

    // ---- writing ----

    public static JsonObject Write(DutyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = new JsonObject { ["version"] = document.Version };
        foreach (var (name, json) in document.Unknown) root[name] = JsonNode.Parse(json);
        var array = new JsonArray();
        foreach (var duty in document.Duties) array.Add(ToJson(duty));
        foreach (var raw in document.Unreadable) array.Add(JsonNode.Parse(raw));
        root["duties"] = array;
        return root;
    }

    public static JsonObject ToJson(Duty duty)
    {
        var o = new JsonObject
        {
            ["id"] = duty.Id,
            ["name"] = duty.Name,
            ["assistant"] = duty.AssistantId,
            ["trigger"] = WriteTrigger(duty.Trigger),
            ["instructions"] = duty.Instructions,
        };
        if (duty.Skill is not null) o["skill"] = duty.Skill;
        if (duty.Workflow is not null) o["workflow"] = duty.Workflow;
        if (duty.ProjectId is { } project) o["project"] = project.ToString("D");
        if (duty.BudgetUsd is { } budget) o["budgetUsd"] = budget;
        if (duty.ReserveText is not null) o["reserve"] = duty.ReserveText;
        if (duty.Scope is { } scope)
        {
            var s = new JsonObject();
            if (scope.AllowedClasses is { } classes) s["classes"] = new JsonArray([.. classes.Select(c => (JsonNode)c.ToString())]);
            if (scope.GrantIds is { } grants) s["grants"] = new JsonArray([.. grants.Select(g => (JsonNode)g.ToString("D"))]);
            o["scope"] = s;
        }
        o["report"] = duty.Report.ToString();
        if (duty.Hours is { } hours)
            o["hours"] = new JsonObject { ["from"] = Clock(hours.From), ["to"] = Clock(hours.To), ["days"] = (int)hours.Days };
        o["missed"] = duty.Missed.ToString();
        o["enabled"] = duty.Enabled;
        o["needsDesktop"] = duty.NeedsDesktop;
        o["wakePc"] = duty.WakePc;
        o["createdBy"] = duty.CreatedBy;
        o["since"] = Time(duty.Since);
        if (duty.Handled is { } handled) o["handled"] = Time(handled);
        if (duty.LastRun is { } last) o["lastRun"] = Time(last);
        o["history"] = new JsonArray([.. duty.History.Select(r => (JsonNode)WriteRun(r))]);
        foreach (var (name, json) in duty.Unknown) o[name] = JsonNode.Parse(json);
        return o;
    }

    private static JsonObject WriteRun(DutyRun run)
    {
        var o = new JsonObject { ["at"] = Time(run.At), ["cause"] = run.Cause.ToString(), ["outcome"] = run.Outcome.ToString() };
        if (run.MissionId is { } mission) o["mission"] = mission.ToString("D");
        if (run.Note is not null) o["note"] = run.Note;
        if (run.Count > 1) o["count"] = run.Count;
        return o;
    }

    private static JsonObject WriteTrigger(DutyTrigger trigger) => trigger switch
    {
        TimeTrigger time => new JsonObject { ["kind"] = "time", ["schedule"] = WriteSchedule(time.Schedule), ["zone"] = time.TimeZoneId },
        FolderChangeTrigger folder => new JsonObject
        {
            ["kind"] = "folder", ["path"] = folder.Path, ["pattern"] = folder.Pattern, ["events"] = (int)folder.Events, ["subfolders"] = folder.Subfolders,
        },
        PageWatchTrigger page => WritePage(page),
        AppEventTrigger app => WriteApp(app),
        _ => new JsonObject { ["kind"] = "manual" },
    };

    private static JsonObject WritePage(PageWatchTrigger page)
    {
        var o = new JsonObject { ["kind"] = "page", ["url"] = page.Url, ["everyMinutes"] = page.EveryMinutes, ["condition"] = page.Condition.ToString() };
        if (page.Text is not null) o["text"] = page.Text;
        if (page.PriceBelow is { } below) o["priceBelow"] = below;
        if (page.StartMarker is not null) o["start"] = page.StartMarker;
        if (page.EndMarker is not null) o["end"] = page.EndMarker;
        return o;
    }

    private static JsonObject WriteApp(AppEventTrigger app)
    {
        var o = new JsonObject { ["kind"] = "app", ["event"] = app.Event.ToString() };
        if (app.Account is not null) o["account"] = app.Account;
        if (app.Window is not null) o["window"] = app.Window;
        if (app.AbovePercent is { } above) o["above"] = above;
        return o;
    }

    private static JsonObject WriteSchedule(TimeSchedule s)
    {
        var o = new JsonObject { ["kind"] = s.Kind.ToString() };
        switch (s.Kind)
        {
            case ScheduleKind.Once:
                o["at"] = (s.At ?? default).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                break;
            case ScheduleKind.Every:
                o["every"] = s.EveryMinutes;
                break;
            case ScheduleKind.Cron:
                o["cron"] = s.Cron;
                break;
            case ScheduleKind.Weekly:
                o["days"] = (int)s.Days;
                o["time"] = Clock(s.TimeOfDay);
                break;
            case ScheduleKind.Monthly:
                o["day"] = s.DayOfMonth;
                o["time"] = Clock(s.TimeOfDay);
                break;
            default:
                o["time"] = Clock(s.TimeOfDay);
                break;
        }
        return o;
    }

    // ---- small readers ----

    private static string Clock(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string? Text(JsonObject o, string name) => o[name] is { } node ? node.GetValue<string>() : null;

    private static bool Bool(JsonObject o, string name, bool fallback) => o[name] is { } node ? node.GetValue<bool>() : fallback;

    private static JsonObject? Object(JsonObject o, string name) => o[name] as JsonObject;

    private static long? Whole(JsonNode? node) => AssistantJson.Number(node) is { } number && number == Math.Floor(number) && Math.Abs(number) < 1e12 ? (long)number : null;

    private static T EnumOf<T>(JsonObject o, string name, T fallback, bool required = false) where T : struct, System.Enum
    {
        if (Text(o, name) is not { } text) return required ? throw new FormatException(name) : fallback;
        return System.Enum.TryParse<T>(text, ignoreCase: false, out var value) && System.Enum.IsDefined(value) ? value : throw new FormatException(name);
    }

    private static ValueMap<string, string> Unknown(JsonObject o, HashSet<string> known) =>
        new(o.Where(p => !known.Contains(p.Key) && p.Value is not null).Select(p => new KeyValuePair<string, string>(p.Key, p.Value!.ToJsonString())));
}
