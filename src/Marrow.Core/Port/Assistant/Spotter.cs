// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.2 "Proactive suggestions"; unit AS-11; ED-NEW-dextra-as11-4, -5). No Swift twin.
// The spotter: cheap rules, no model call, that find the four things worth a suggestion (5.2): an allowance about to reset unused, the owner doing
// the same thing by hand three times, a failing check in a project he works on, and a duty that keeps finding nothing. Each candidate carries
// the words nth wrote for it and the plan Accept follows; a model may reword it later, never change the plan. Pure: no clock, no store.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>A project the owner works in, with when he last did: where an unused allowance is put to work.</summary>
public sealed record SpotProject(Guid Id, string Name, DateTimeOffset LastUsed);

/// <summary>
/// A kind of chat the owner starts: the same label in the same project. <see cref="Starts"/> are when he started them. <see cref="LocalOnly"/>: a
/// chat on a local model or a private account (5.4), whose words never go to a cloud model.
/// </summary>
public sealed record ChatHabit(string Key, string Label, Guid ProjectId, string ProjectName, IReadOnlyList<DateTimeOffset> Starts)
{
    public bool LocalOnly { get; init; }
}

/// <summary>A mission of Dextra's that failed its review in a project the owner works in. <see cref="Key"/> names the mission.</summary>
public sealed record FailingCheck(string Key, string Title, Guid ProjectId, string ProjectName, DateTimeOffset Last);

/// <summary>A duty as the spotter reads it: how often it looks (null: not on a fixed interval), how its runs ended (newest first), and the span of its quiet streak (the newest runs that found nothing).</summary>
public sealed record SpotDuty(string Id, string Name, int? EveryMinutes, IReadOnlyList<DutyRunOutcome> Recent, TimeSpan Span, bool Enabled);

public sealed record SpotterInput(DateTimeOffset Now, TimeZoneInfo Zone)
{
    public IReadOnlyList<DigestAccount> Accounts { get; init; } = [];

    public IReadOnlyList<SpotProject> Projects { get; init; } = [];

    public IReadOnlyList<ChatHabit> Habits { get; init; } = [];

    public IReadOnlyList<FailingCheck> Failing { get; init; } = [];

    public IReadOnlyList<SpotDuty> Duties { get; init; } = [];

    /// <summary>Every duty's name, so a habit already made a duty is not suggested again.</summary>
    public IReadOnlyList<string> DutyNames { get; init; } = [];
}

public static class Spotter
{
    /// <summary>A weekly window with at least this many points unused, resetting within 12 hours, is worth an offer (5.2: "resets in 3 h with 31% unused").</summary>
    public const double UnusedMinimum = 20;

    /// <summary>The room under the owner's floor an offer needs: the least it can spend (the canvas offers "about 3%").</summary>
    public const double SpendMinimum = 3;

    /// <summary>The most one offer puts to work, in points of the window.</summary>
    public const double SpendMaximum = 5;

    /// <summary>The weeks in a row the owner did the same thing by hand on the same day (5.2: "three times"; the canvas: "three Fridays running").</summary>
    public const int HandWeeks = 3;

    /// <summary>A duty that finds nothing is slow after this many quiet runs in a row over at least a day, if it looks at least this often.</summary>
    public const int IdleRuns = 8;

    public static readonly TimeSpan IdleSpan = TimeSpan.FromHours(24);

    public const int IdleOftenMinutes = 240;

    /// <summary>The steps a slower interval is rounded up to, in minutes.</summary>
    public static readonly int[] Steps = [15, 30, 60, 120, 180, 240, 360, 480, 720, 1440];

    public const int MaxCandidates = 12;

    public static IReadOnlyList<SuggestionCandidate> Spot(SpotterInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var found = new List<SuggestionCandidate>();
        found.AddRange(UnusedAllowance(input));
        found.AddRange(DoneByHand(input));
        found.AddRange(FailingChecks(input));
        found.AddRange(IdleDuties(input));
        return [.. found.OrderByDescending(c => c.Urgency).ThenBy(c => c.Key, StringComparer.Ordinal).Take(MaxCandidates)];
    }

    // ---- an allowance about to reset unused ----

    private static IEnumerable<SuggestionCandidate> UnusedAllowance(SpotterInput input)
    {
        var project = input.Projects.OrderByDescending(p => p.LastUsed).ThenBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault();
        if (project is null) yield break;
        foreach (var account in input.Accounts)
        {
            foreach (var window in account.Windows.Where(w => w.Kind == ReserveWindowKind.Weekly))
            {
                if (window.ResetsAt is not { } reset || reset <= input.Now || reset - input.Now > RouteScoring.UseItOrLose) continue;
                var used = Math.Round(Math.Clamp(window.Used, 0, 100), MidpointRounding.AwayFromZero);
                var unused = 100 - used;
                var room = Math.Min(window.Floor ?? 100, 100) - window.Used;
                if (unused < UnusedMinimum || room < SpendMinimum) continue;
                var spend = Math.Min(SpendMaximum, Math.Floor(room));
                var name = account.Name;
                var words = ReserveWindowKinds.Words(window.Kind);
                var when = Clock(reset, input);
                var hours = (reset - input.Now).TotalHours;
                yield return new SuggestionCandidate(SuggestionKind.AllowanceUnused,
                    "allowance:" + name + ":" + window.Kind + ":" + reset.UtcDateTime.ToString("yyyyMMddTHHmm", CultureInfo.InvariantCulture),
                    $"{N(unused)}% of {name}'s {words} is unused and resets at {when}. Put up to {N(spend)}% of it to work on {project.Name}?",
                    $"Up to {N(spend)}% of {name}'s {words}",
                    new SuggestionPlan(SuggestionDo.StartMission, $"Use the spare allowance on {name}")
                    {
                        ProjectId = project.Id,
                        Brief = $"Dextra found {N(unused)}% of {name}'s {words} unused, resetting at {when}. Use up to {N(spend)}% of that window on useful, low-risk work in {project.Name}: "
                            + "a dependency audit, a test run, a check of stale branches, or a read-only review of recent changes. Pick the one that helps most, tell the owner what you chose "
                            + "in your first message, and report what you found. Change nothing outside the mission's own copy of the project.",
                    })
                {
                    Urgency = 1000 - hours * 10 + unused,
                };
            }
        }
    }

    // ---- the owner doing the same thing by hand three times ----

    private static IEnumerable<SuggestionCandidate> DoneByHand(SpotterInput input)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(input.Now, input.Zone).DateTime);
        var thisWeek = Week(today);
        foreach (var habit in input.Habits)
        {
            if (habit.Starts.Count < HandWeeks || string.IsNullOrWhiteSpace(habit.Label) || MadeADuty(input, habit.Label)) continue;
            var local = habit.Starts.Select(s => TimeZoneInfo.ConvertTime(s, input.Zone)).ToList();
            foreach (var day in local.Select(s => s.DayOfWeek).Distinct().Order())
            {
                var mine = local.Where(s => s.DayOfWeek == day).ToList();
                var weeks = mine.Select(s => Week(DateOnly.FromDateTime(s.DateTime))).Distinct().ToHashSet();
                // The latest of three weeks in a row, and it is this week or last: a habit the owner dropped a month ago is not one.
                var latest = weeks.Where(w => w >= thisWeek - 1 && weeks.Contains(w - 1) && weeks.Contains(w - 2)).OrderDescending().Select(w => (int?)w).FirstOrDefault();
                if (latest is not { } newest) continue;
                var runs = mine.Where(s => Week(DateOnly.FromDateTime(s.DateTime)) >= newest - 2).ToList();
                var typical = Typical(runs);
                var plural = day.ToString() + "s";
                yield return new SuggestionCandidate(SuggestionKind.DoneByHand, "hand:" + habit.Key + ":" + day,
                    $"You started “{habit.Label}” by hand three {plural} running. Make it a duty? You'll see the duty card first.",
                    "Its budget is on the duty card. Nothing runs until you create it.",
                    new SuggestionPlan(SuggestionDo.NewDuty, habit.Label)
                    {
                        ProjectId = habit.ProjectId,
                        Day = day,
                        At = typical,
                        Instructions = $"The owner started “{habit.Label}” by hand every {day} for three weeks running. Do it the way he does, in {habit.ProjectName}, "
                            + "and report what changed. Ask him before anything that sends, pays, deletes or installs.",
                    })
                {
                    LocalOnly = habit.LocalOnly,
                    Urgency = 100 + newest - thisWeek,
                };
            }
        }
    }

    private static bool MadeADuty(SpotterInput input, string label) =>
        input.DutyNames.Any(n => n.Contains(label, StringComparison.OrdinalIgnoreCase) || (n.Length >= 6 && label.Contains(n, StringComparison.OrdinalIgnoreCase)));

    // Weeks counted from Monday 0001-01-01, so a week is a whole Monday-to-Sunday and consecutive weeks differ by one.
    private static int Week(DateOnly day) => day.DayNumber / 7;

    // The middle of the times he started it, to the nearest quarter hour.
    private static TimeOnly Typical(IReadOnlyList<DateTimeOffset> runs)
    {
        var minutes = runs.Select(r => r.Hour * 60 + r.Minute).Order().ToList();
        var middle = minutes[minutes.Count / 2];
        var rounded = (int)Math.Round(middle / 15.0, MidpointRounding.AwayFromZero) * 15;
        return rounded >= 24 * 60 ? new TimeOnly(23, 45) : new TimeOnly(rounded / 60, rounded % 60);
    }

    // ---- a failing check in a project the owner works on ----

    private static IEnumerable<SuggestionCandidate> FailingChecks(SpotterInput input)
    {
        foreach (var failing in input.Failing)
        {
            yield return new SuggestionCandidate(SuggestionKind.FailingCheck, "check:" + failing.Key,
                $"“{failing.Title}” failed its review in {failing.ProjectName}. Want me to find out why?",
                "One mission. The plan card shows what it will use before it starts.",
                new SuggestionPlan(SuggestionDo.StartMission, $"Find out why “{failing.Title}” failed")
                {
                    ProjectId = failing.ProjectId,
                    Brief = $"The mission “{failing.Title}” in {failing.ProjectName} failed its review. Read its report and its branch, find out why the checks fail, "
                        + "and tell the owner what you found and how to fix it. Change nothing in the owner's files.",
                })
            {
                Urgency = 500 + Math.Max(0, 1 - (input.Now - failing.Last).TotalDays),
            };
        }
    }

    // ---- a duty that keeps finding nothing ----

    private static IEnumerable<SuggestionCandidate> IdleDuties(SpotterInput input)
    {
        foreach (var duty in input.Duties)
        {
            if (!duty.Enabled || duty.EveryMinutes is not { } every || every > IdleOftenMinutes || every <= 0) continue;
            // The quiet streak: runs in a row, newest first, that found nothing; it is what has to be long enough and wide enough.
            var quiet = duty.Recent.TakeWhile(o => o == DutyRunOutcome.NothingChanged).Count();
            if (quiet < IdleRuns || duty.Span < IdleSpan) continue;
            var slower = Steps.FirstOrDefault(s => s >= every * 3, Steps[^1]);
            if (slower <= every) continue;
            var saved = Math.Max(1, (int)Math.Round(1440.0 / every - 1440.0 / slower, MidpointRounding.AwayFromZero));
            yield return new SuggestionCandidate(SuggestionKind.IdleDuty, "idle:" + duty.Id,
                $"“{duty.Name}” found nothing in its last {N(quiet)} runs. Look every {Interval(slower)} instead of every {Interval(every)}?",
                $"Saves about {N(saved)} runs a day.",
                new SuggestionPlan(SuggestionDo.ChangeDuty, duty.Name) { DutyId = duty.Id, EveryMinutes = slower })
            {
                Urgency = 50,
            };
        }
    }

    /// <summary>"15 min", "2 h", "24 h".</summary>
    public static string Interval(int minutes) =>
        minutes % 60 == 0 ? N(minutes / 60) + " h" : N(minutes) + " min";

    private static string Clock(DateTimeOffset at, SpotterInput input)
    {
        var local = TimeZoneInfo.ConvertTime(at, input.Zone);
        var now = TimeZoneInfo.ConvertTime(input.Now, input.Zone);
        return (local.Date == now.Date ? "" : "tomorrow ") + local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static string N(double n) => n.ToString("0", CultureInfo.InvariantCulture);

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
