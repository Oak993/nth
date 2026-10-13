// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.1 "Creating a duty always asks", 7.2, 15.5; unit AS-10; ED-NEW-dextra-as10-12). No Swift twin.
// The three nth-dextra tools that make a standing duty: schedule_duty, watch_folder and watch_page. A duty is permanent configuration
// that acts later with the owner away, so every call raises a card that shows what the duty will be (its trigger, what it does,
// its grants, its budget; and, for a changed duty, what changed), and nothing is made until he says yes. Only then is the duty
// made, and a page watch first reads the page once, so a page that needs the browser or a sign-in is refused right here with
// words and never becomes a duty that cannot work. The owner's own Save in the duty editor goes straight to the scheduler.
using System.Globalization;
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>What the controller asks of the duty tools.</summary>
public interface IDutyTools
{
    /// <summary>The inputs of the card for a duty tool's call; null for any other tool.</summary>
    IReadOnlyList<DecisionInput>? Describe(AssistantToolCall call);

    /// <summary>The owner said yes: makes the duty. The words of how it went also go to <paramref name="deliver"/> (a page read finishes later).</summary>
    AssistantToolResult Create(AssistantToolCall call, Action<string> deliver);

    /// <summary>What a duty's run may do (null: nothing narrows it). A duty that is gone narrows to nothing alone, so every action asks.</summary>
    DutyScope? ScopeOf(string dutyId);
}

public sealed class DutyToolsOptions
{
    /// <summary>The project a chat belongs to, so a duty made from it works there.</summary>
    public Func<Guid, Guid?> ChatProject { get; init; } = _ => null;

    public Func<string, string> AssistantName { get; init; } = id => id;

    /// <summary>The owner's yes to a duty that watches this folder is the grant of it (7.2): called before the duty is saved. Returns why not, or null.</summary>
    public required Func<string, string?> ApproveFolder { get; init; }

    public WebWatcher? Web { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The PC's own time zone id, which a duty's schedule is read in.</summary>
    public Func<string> ZoneId { get; init; } = () => TimeZoneInfo.Local.Id;
}

public sealed partial class DutyTools(AssistantScheduler scheduler, DutyToolsOptions options) : IDutyTools
{
    private sealed record Proposal(Duty? Duty, Duty? Existing, string? Problem);

    [GeneratedRegex(@"^(?<n>\d{1,4})\s*(?<u>m|min|mins|minutes?|h|hr|hrs|hours?|d|days?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EveryPattern();

    public IReadOnlyList<DecisionInput>? Describe(AssistantToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (!IsDutyTool(call.Tool.Name)) return null;
        var proposal = Propose(call);
        if (proposal.Duty is not { } duty) return [new DecisionInput("Not possible", proposal.Problem ?? "nth could not read this duty.")];
        var now = options.Time.GetUtcNow();
        var inputs = new List<DecisionInput>(DutyCardWords.CardInputs(duty, options.AssistantName(duty.AssistantId), proposal.Existing, DutyForecast.Of(duty, now)));
        inputs.Add(new DecisionInput("What it reads", duty.Trigger switch
        {
            FolderChangeTrigger => "The names, sizes and times of the files in that folder. The agent that runs the duty may open them.",
            PageWatchTrigger => "The page's text, with plain HTTP: no sign-in, no cookies, nothing sent. A page that needs scripts or a sign-in is refused.",
            _ => "Nothing until it runs.",
        }));
        inputs.Add(new DecisionInput("Made by", proposal.Existing is null ? $"{options.AssistantName(call.Chat.AssistantId)} asks you to make it" : $"{options.AssistantName(call.Chat.AssistantId)} asks you to change it"));
        return inputs;
    }

    public AssistantToolResult Create(AssistantToolCall call, Action<string> deliver)
    {
        ArgumentNullException.ThrowIfNull(call);
        var proposal = Propose(call);
        if (proposal.Duty is not { } duty) return AssistantToolResult.Failed("Not created: " + proposal.Problem);
        if (duty.Trigger is PageWatchTrigger page)
        {
            if (options.Web is not { } web) return AssistantToolResult.Failed("Not created: watching pages needs the web reader, which is not available.");
            // The first read, before anything is saved: a page that cannot be watched is told so here, in the chat that asked.
            _ = Task.Run(async () =>
            {
                string text;
                try
                {
                    var reading = await web.Read(page.Url, page.StartMarker, page.EndMarker, CancellationToken.None).ConfigureAwait(false);
                    text = reading.Ok ? Save(duty, proposal.Existing) : "Not created: " + reading.Problem;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    text = "Not created: the page could not be read (" + error.Message + ").";
                }
                deliver(text);
            });
            return AssistantToolResult.Done("Reading the page once first; the answer comes back to this chat.");
        }
        var saved = Save(duty, proposal.Existing);
        return saved.StartsWith("Not created", StringComparison.Ordinal) ? AssistantToolResult.Failed(saved) : AssistantToolResult.Done(saved);
    }

    public DutyScope? ScopeOf(string dutyId) =>
        scheduler.Find(dutyId) is { } duty ? duty.Scope : new DutyScope { AllowedClasses = ValueList<ActionClass>.Empty };

    private string Save(Duty duty, Duty? existing)
    {
        // The card was answered yes: that is the owner granting the folder to this duty, and it is kept apart from the duties file.
        if (duty.Trigger is FolderChangeTrigger folder && options.ApproveFolder(folder.Path) is { } refused) return "Not created: " + refused;
        var (saved, problem) = existing is null ? scheduler.Add(duty) : scheduler.Update(duty with { Id = existing.Id });
        if (saved is null) return "Not created: " + problem;
        var zone = saved.Trigger is TimeTrigger t ? DutyZones.Find(t.TimeZoneId) : null;
        var next = scheduler.Duties.FirstOrDefault(v => v.Duty.Id == saved.Id)?.NextRun;
        var when = DutyCardWords.NextRun(saved.Trigger, next, options.Time.GetUtcNow(), zone ?? options.Time.LocalTimeZone);
        return $"The duty “{saved.Name}” is {(existing is null ? "set" : "changed")}: {DutyCardWords.Trigger(saved.Trigger)}. Next: {when}.";
    }

    // How a duty reports (7.1). "notify" is the one opt-in to a toast (13.1): true sets Notify me; false takes it off again; left out, the duty keeps what it had.
    // "silent" (schedule_duty only) picks between Silent unless something changed and Digest only.
    private static ReportStyle ReportOf(Marrow.Core.Support.JSONValue args, ReportStyle? existing, ReportStyle? silent)
    {
        if (args["notify"]?.JSONValueBool is { } notify)
        {
            if (notify) return ReportStyle.NotifyMe;
            if (silent is { } quiet) return quiet;
            return existing is null or ReportStyle.NotifyMe ? ReportStyle.SilentUnlessChanged : existing.Value;
        }
        return silent ?? existing ?? ReportStyle.SilentUnlessChanged;
    }

    private static bool IsDutyTool(string name) => name is "schedule_duty" or "watch_folder" or "watch_page";

    private Proposal Propose(AssistantToolCall call)
    {
        var args = call.Arguments;
        string? Text(string name) => args[name]?.JSONValueString?.Trim();
        var chat = call.Chat;
        var instructions = Text("instructions");
        if (string.IsNullOrWhiteSpace(instructions)) return new Proposal(null, null, "Say what the duty does.");
        var project = options.ChatProject(chat.Thread);
        var now = options.Time.GetUtcNow();
        switch (call.Tool.Name)
        {
            case "schedule_duty":
            {
                var title = Text("title");
                if (string.IsNullOrWhiteSpace(title)) return new Proposal(null, null, "Give the duty a title.");
                var zoneId = options.ZoneId();
                var zone = DutyZones.Find(zoneId) ?? options.Time.LocalTimeZone;
                var reading = ScheduleText.Read(Text("schedule"), now, zone);
                if (reading.Schedule is not { } schedule) return new Proposal(null, null, reading.Problem);
                var existing = scheduler.Duties.Select(v => v.Duty).FirstOrDefault(d =>
                    d.AssistantId == chat.AssistantId && string.Equals(d.Name, title, StringComparison.OrdinalIgnoreCase));
                var report = ReportOf(args, existing?.Report, args["silent"]?.JSONValueBool switch
                {
                    true => ReportStyle.SilentUnlessChanged,
                    false => ReportStyle.DigestOnly,
                    null => (ReportStyle?)null,
                });
                var trigger = new TimeTrigger(schedule, zoneId);
                var duty = existing is null
                    ? new Duty(Duty.NewId(), title, chat.AssistantId, trigger, instructions) { Report = report, ProjectId = project, CreatedBy = chat.AssistantId }
                    : existing with { Trigger = trigger, Instructions = instructions, Report = report };
                return Checked(duty, existing);
            }
            case "watch_folder":
            {
                var path = Text("path");
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return new Proposal(null, null, "Give the folder's full path.");
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                if (!Directory.Exists(full)) return new Proposal(null, null, $"The folder {full} is not there.");
                var trigger = new FolderChangeTrigger(full) { Pattern = Text("pattern") is { Length: > 0 } glob ? glob : "*" };
                var name = "Watch " + (Path.GetFileName(full) is { Length: > 0 } leaf ? leaf : full);
                var existing = scheduler.Duties.Select(v => v.Duty).FirstOrDefault(d => d.AssistantId == chat.AssistantId && d.Trigger is FolderChangeTrigger f && string.Equals(f.Path, full, StringComparison.OrdinalIgnoreCase));
                var duty = existing is null
                    ? new Duty(Duty.NewId(), name, chat.AssistantId, trigger, instructions) { ProjectId = project, CreatedBy = chat.AssistantId, Report = ReportOf(args, null, null) }
                    : existing with { Trigger = trigger, Instructions = instructions, Report = ReportOf(args, existing.Report, null) };
                return Checked(duty, existing);
            }
            default:
            {
                var url = Text("url");
                if (string.IsNullOrWhiteSpace(url)) return new Proposal(null, null, "Give the page's address.");
                if (!TryEvery(Text("every"), out var minutes, out var everyProblem)) return new Proposal(null, null, everyProblem);
                var trigger = new PageWatchTrigger(url, minutes);
                var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
                var existing = scheduler.Duties.Select(v => v.Duty).FirstOrDefault(d => d.AssistantId == chat.AssistantId && d.Trigger is PageWatchTrigger p && p.Url == url);
                var duty = existing is null
                    ? new Duty(Duty.NewId(), "Watch " + host, chat.AssistantId, trigger, instructions) { ProjectId = project, CreatedBy = chat.AssistantId, Report = ReportOf(args, null, null) }
                    : existing with { Trigger = trigger, Instructions = instructions, Report = ReportOf(args, existing.Report, null) };
                return Checked(duty, existing);
            }
        }
    }

    private static Proposal Checked(Duty duty, Duty? existing) => duty.Problem() is { } problem ? new Proposal(null, existing, problem) : new Proposal(duty, existing, null);

    // "1h", "30 minutes", "2 days"; nothing given is every hour.
    private static bool TryEvery(string? text, out int minutes, out string? problem)
    {
        problem = null;
        minutes = 60;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var match = EveryPattern().Match(text.Trim());
        if (!match.Success)
        {
            problem = "Say how often to look, like “30m”, “1h” or “1d”.";
            return false;
        }
        var count = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        minutes = char.ToLowerInvariant(match.Groups["u"].Value[0]) switch { 'h' => count * 60, 'd' => count * 1440, _ => count };
        if (minutes >= TimeSchedule.MinimumEveryMinutes && minutes <= TimeSchedule.MaximumEveryMinutes) return true;
        problem = $"Look no more often than every {TimeSchedule.MinimumEveryMinutes.ToString(CultureInfo.InvariantCulture)} minutes, and no less often than once a year.";
        return false;
    }
}
