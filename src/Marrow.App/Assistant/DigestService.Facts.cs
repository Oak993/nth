// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1 "built from the feed, not from transcripts", 6, 7.1, 11.5; unit AS-11; ED-NEW-dextra-as11-1, -3, -4). No Swift twin.
// Where the digest's facts come from: the decision queue, the mission runner's records, the scheduler's duty list and their histories, the reserve's bars and the
// use it found outside nth, and the seams the setup gives. All of it is nth's own state read on the main thread; no chat is read and no model is asked. What a
// duty's run reported is taken as one line of data, and the taint of what it was made from goes with it.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class DigestService
{
    private sealed record Gathered(DigestFacts Facts, IReadOnlyList<SuggestionCandidate> Candidates);

    /// <summary>The runs of a duty's history the spotter is told of at least (newest first), however short its quiet streak is.</summary>
    private const int RecentRuns = 12;

    /// <summary>The use outside nth the digest mentions: the last day (11.5).</summary>
    private static readonly TimeSpan OutsideSpan = TimeSpan.FromHours(24);

    /// <summary>A project counts as one the owner works in when he used it this lately (5.2: "a failing check in a project the owner works on").</summary>
    private static readonly TimeSpan WorksIn = TimeSpan.FromDays(3);

    // On the main thread.
    private Gathered Gather(DigestKind kind, DateTimeOffset now, TimeZoneInfo zone)
    {
        var controller = o.Controller;
        var missions = controller.Runner.Missions.Where(m => !m.DryRun && m.Mission.ParentMissionId is null).ToList();
        var views = o.Scheduler.Duties;
        var since = Since(now);

        var facts = new DigestFacts("d" + Guid.NewGuid().ToString("N")[..12], now, zone, kind)
        {
            Since = since,
            NeedsYou = NeedsYou(missions),
            Finished = Finished(missions, since),
            Running = Running(missions),
            Accounts = Accounts(),
            DutyRuns = DutyRuns(views, since),
            Today = Today(views),
            ExpiredLogins = setup.ExpiredLogins?.Invoke() ?? [],
            Answered = setup.AnsweredSince?.Invoke(since) ?? setup.Answered?.Invoke() ?? [],
            Asturs = setup.AstursSince?.Invoke(since) ?? setup.Asturs?.Invoke() ?? [],
        };
        return new Gathered(facts, Spotter.Spot(SpotInput(now, zone, missions, views, facts.Accounts)));
    }

    // The last morning digest; with none, a day back; never more than three days.
    private DateTimeOffset Since(DateTimeOffset now)
    {
        var last = Book.Digests.FirstOrDefault(d => d.Kind == DigestKind.Scheduled)?.At;
        var since = last is { } at && at < now ? at : now - FirstLookback;
        return since < now - MaxLookback ? now - MaxLookback : since;
    }

    // ---- 1 to 3 ----

    private List<DigestAsk> NeedsYou(IReadOnlyList<MissionRecord> missions)
    {
        var waiting = o.Controller.Queue.Waiting;
        var asks = waiting.Select(card => new DigestAsk(card.Title, new DigestLink(DigestLinkKind.Decision, card.Id.ToString("D")))).ToList();
        // A task that failed or is blocked has no card but lands under Needs you on the board (3.2).
        var asked = waiting.Where(c => c.MissionId is not null).Select(c => c.MissionId!.Value).ToHashSet();
        foreach (var record in missions.Where(m => m.Mission.State is MissionState.Failed or MissionState.Blocked && !asked.Contains(m.Id)))
        {
            var words = record.Mission.State == MissionState.Failed ? "failed" : "is blocked";
            asks.Add(new DigestAsk($"“{record.Mission.Title}” {words}", new DigestLink(DigestLinkKind.Mission, record.Id.ToString("D"))));
        }
        return asks;
    }

    // Owner and delegated work that finished after the last digest (one that ended in the same instant was told in it). A duty's run is told in the duties section, not here.
    private static List<DigestDone> Finished(IReadOnlyList<MissionRecord> missions, DateTimeOffset since) =>
        [.. missions.Where(m => m.Mission.State == MissionState.Done && m.DutyId is null && m.Ended is { } ended && ended > since)
            .Select(m => new DigestDone(m.Mission.Title, new DigestLink(DigestLinkKind.Mission, m.Id.ToString("D")), m.Ended!.Value,
                m.Reviews.Count == 0 ? DigestReview.NotReviewed : m.Reviews[^1].Passed ? DigestReview.Passed : DigestReview.Failed))];

    private static List<DigestRunning> Running(IReadOnlyList<MissionRecord> missions) =>
        [.. missions.Where(m => MissionStates.IsRunning(m.Mission.State) || m.Mission.State == MissionState.Pausing)
            .Select(m => new DigestRunning(m.Mission.Title, new DigestLink(DigestLinkKind.Mission, m.Id.ToString("D")), MissionStates.Label(m.Mission.State)))];

    // ---- 4: usage ----

    private List<DigestAccount> Accounts()
    {
        if (o.Controller.Reserve is not { } reserve) return [];
        var outside = reserve.OutsideUses(OutsideSpan);
        return [.. reserve.Bars().Select(bar => new DigestAccount(bar.Name, [.. bar.Windows.Select(w =>
            new DigestWindow(w.Kind, w.Kind == ReserveWindowKind.Other ? w.Title : "", w.Used, w.Floor, w.ResetsAt))])
        {
            Outside = [.. outside.Where(r => r.AccountKey == bar.Key).Select(r => new DigestOutside(r.Kind, r.Kind == ReserveWindowKind.Other ? r.Title : "", r.Points))],
        })];
    }

    // ---- 5 to 7: the duties ----

    private List<DigestDutyRun> DutyRuns(IReadOnlyList<DutyView> views, DateTimeOffset since)
    {
        var runs = new List<DigestDutyRun>();
        foreach (var view in views.Where(v => !DigestDuty.Is(v.Duty)))
        {
            foreach (var entry in view.Duty.History.Where(h => h.At >= since && !IsDryRun(h) && h.Outcome is DutyRunOutcome.Done or DutyRunOutcome.NothingChanged or DutyRunOutcome.Failed or DutyRunOutcome.Refused))
            {
                var run = new DigestDutyRun(view.Duty.Id, view.Duty.Name, view.Duty.Report, entry.Outcome, entry.At) { Mission = entry.MissionId };
                if (entry.Outcome == DutyRunOutcome.Refused) run = run with { Text = entry.Note };
                else if (entry.Outcome == DutyRunOutcome.Done && entry.MissionId is { } id && o.Controller.Runner.Find(id) is { } record)
                    run = run with { Text = FirstLine(record.Report), Taint = record.ReportTaint };
                runs.Add(run);
            }
        }
        return runs;
    }

    private static bool IsDryRun(DutyHistoryEntry entry) => entry.Note?.StartsWith("Dry run", StringComparison.Ordinal) == true;

    /// <summary>
    /// One line of what a run reported: the first line with words in it, without Markdown marks and without the nothing-changed line. It is data (it may be
    /// made from a page or a file), so the composer shows it and obeys none of it.
    /// </summary>
    public static string? FirstLine(string? report)
    {
        if (string.IsNullOrWhiteSpace(report)) return null;
        foreach (var raw in report.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#', '*', '-', '>', ' ', '\t').TrimEnd('*', ' ', '\r');
            if (line.Length == 0 || line.StartsWith("RESULT:", StringComparison.OrdinalIgnoreCase)) continue;
            return line;
        }
        return null;
    }

    private static List<DigestScheduled> Today(IReadOnlyList<DutyView> views) =>
        [.. views.Where(v => !DigestDuty.Is(v.Duty) && v.Duty.Enabled && !v.ReadOnly && v.Duty.Trigger is TimeTrigger && v.NextRun is not null)
            .Select(v => new DigestScheduled(v.Duty.Id, v.Duty.Name, v.NextRun!.Value))];

    // ---- the spotter's input ----

    private SpotterInput SpotInput(DateTimeOffset now, TimeZoneInfo zone, IReadOnlyList<MissionRecord> missions, IReadOnlyList<DutyView> views, IReadOnlyList<DigestAccount> accounts)
    {
        var projects = SpotProjects();
        var works = projects.Where(p => now - p.LastUsed <= WorksIn).ToDictionary(p => p.Id, p => p.Name);
        var failing = missions
            .Where(m => m.Mission.State == MissionState.Failed && m.Reviews.Any(r => !r.Passed) && works.ContainsKey(m.ProjectId))
            .Select(m => new FailingCheck(m.Id.ToString("D"), m.Mission.Title, m.ProjectId, works[m.ProjectId], m.Ended ?? m.Created));
        var duties = views.Where(v => !DigestDuty.Is(v.Duty)).Select(SpotDutyOf);
        return new SpotterInput(now, zone)
        {
            Accounts = accounts,
            Projects = projects,
            Habits = setup.Habits?.Invoke() ?? [],
            Failing = [.. failing],
            Duties = [.. duties],
            DutyNames = [.. views.Select(v => v.Duty.Name)],
        };
    }

    private List<SpotProject> SpotProjects() =>
        setup.Projects?.Invoke() is { } given ? [.. given] : [.. o.Projects().Select(p => new SpotProject(p.Id, p.Name, DateTimeOffset.MinValue))];

    private static SpotDuty SpotDutyOf(DutyView view)
    {
        var duty = view.Duty;
        int? every = duty.Trigger switch
        {
            TimeTrigger { Schedule: { Kind: ScheduleKind.Every, EveryMinutes: { } minutes } } => minutes,
            PageWatchTrigger page => page.EveryMinutes,
            _ => null,
        };
        // The history holds the last 50 runs, newest first. What matters is the streak of quiet runs at its head and how long it has lasted.
        var real = duty.History.Where(h => !IsDryRun(h) && h.Outcome is DutyRunOutcome.Done or DutyRunOutcome.NothingChanged or DutyRunOutcome.Failed).ToList();
        var streak = real.TakeWhile(h => h.Outcome == DutyRunOutcome.NothingChanged).ToList();
        var span = streak.Count > 1 ? streak[0].At - streak[^1].At : TimeSpan.Zero;
        return new SpotDuty(duty.Id, duty.Name, every, [.. real.Take(Math.Max(RecentRuns, streak.Count)).Select(h => h.Outcome)], span, duty.Enabled && !view.ReadOnly);
    }
}
