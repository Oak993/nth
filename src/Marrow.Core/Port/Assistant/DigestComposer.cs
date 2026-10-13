// PORT-ONLY: Dextra (DESIGN.md rev 5, 6, 7.1, 11.5, 12.4; canvas "Morning digest"; unit AS-11; ED-NEW-dextra-as11-1, -2, -3). No Swift twin.
// The morning digest, composed without a model from the facts nth already holds: what needs the owner, what finished (with the reviewer's
// verdict), what is running, the usage per account with every number naming its window, what standing duties found (a silent duty that
// found nothing is not there), what runs today, anything unusual, what the delegate answered, each Astur's paragraph, the Suggestions lane,
// and the line it ends on. The same facts always give the same digest. Pure: no clock, no store, no model.
using System.Globalization;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

public static class DigestComposer
{
    /// <summary>The most items an inline section names before it says how many more there are.</summary>
    public const int MaxListed = 5;

    /// <summary>The longest a title, a duty's finding or a paragraph runs in a line (masked first, then cut).</summary>
    public const int TitleChars = 80;

    public const int FindingChars = 160;

    public const int ParagraphChars = 400;

    /// <summary>A weekly window that resets within the use-it-or-lose-it span with at least this many points unused is said to be used first (11.6).</summary>
    public const double UseFirstUnused = 20;

    /// <summary>Use outside nth of at least this many points of a window in the last day is worth a line (11.5).</summary>
    public const double OutsideWorthSaying = 2;

    private const string Quote = "“";
    private const string Unquote = "”";

    public static Digest Compose(DigestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var now = TimeZoneInfo.ConvertTime(facts.Now, facts.Zone);
        var sections = new List<DigestSection>();
        var taint = Taint.Clean;

        sections.Add(NeedsYou(facts));
        if (Finished(facts) is { } finished) sections.Add(finished);
        if (Running(facts, now) is { } running) sections.Add(running);
        var usage = Usage(facts, now);
        if (usage.Section is { } usageSection) sections.Add(usageSection);
        var duties = Duties(facts, ref taint);
        if (duties is not null) sections.Add(duties);
        if (Today(facts, now) is { } today) sections.Add(today);
        var unusual = Unusual(facts, now);
        if (unusual is not null) sections.Add(unusual);
        if (Answered(facts) is { } answered) sections.Add(answered);
        if (Asturs(facts, ref taint) is { } asturs) sections.Add(asturs);

        var header = (facts.Kind == DigestKind.Scheduled ? "Morning digest" : "Digest") + " · " + now.ToString("HH:mm", CultureInfo.InvariantCulture);
        return new Digest(facts.Id, facts.Kind, facts.Now, header, sections)
        {
            Suggestions = facts.Suggestions,
            LaneTitle = facts.Suggestions.Count > 0 ? SuggestionWords.LaneTitle(facts.OfferedToday) : "",
            Seal = facts.Seal,
            Taint = taint,
            Since = facts.Since,
            Spoken = Speak(facts, usage.Breached, unusual?.Items.Count ?? 0),
        };
    }

    // ---- 1. What needs you ----

    private static DigestSection NeedsYou(DigestFacts facts)
    {
        var asks = facts.NeedsYou;
        var items = asks.Take(MaxListed).Select(a => new DigestItem(Line(a.Title, TitleChars), a.Link)).ToList();
        if (asks.Count > MaxListed) items.Add(new DigestItem("and " + N(asks.Count - MaxListed) + " more", DigestLink.None));
        return new DigestSection(DigestPart.NeedsYou, "Needs you:", asks.Count == 0 ? "nothing." : N(asks.Count) + ".", items, true);
    }

    // ---- 2. What finished, with the reviewer's verdict ----

    private static DigestSection? Finished(DigestFacts facts)
    {
        if (facts.Finished.Count == 0) return null;
        var done = facts.Finished.OrderBy(d => d.At).ToList();
        var items = done.Take(MaxListed).Select(d => new DigestItem(Quoted(d.Title), d.Link) { Note = Verdict(d.Review) }).ToList();
        if (done.Count > MaxListed) items.Add(new DigestItem("and " + N(done.Count - MaxListed) + " more", DigestLink.None));
        return new DigestSection(DigestPart.Finished, "Finished:", "", items, true);
    }

    private static string Verdict(DigestReview review) => review switch
    {
        DigestReview.Passed => "(reviewed: passed)",
        DigestReview.Failed => "(reviewed: failed)",
        _ => "(not reviewed)",
    };

    // ---- 3. What is running, and the next checkpoint ----

    private static DigestSection? Running(DigestFacts facts, DateTimeOffset now)
    {
        if (facts.Running.Count == 0) return null;
        var items = facts.Running.Take(MaxListed).Select(r =>
        {
            var state = r.State.ToLowerInvariant();
            var note = r.NextCheckpoint is { } at && at > facts.Now
                ? $"({state}; next checkpoint {CheckpointWords(TimeZoneInfo.ConvertTime(at, facts.Zone), now)})"
                : $"({state})";
            return new DigestItem(Quoted(r.Title), r.Link) { Note = note };
        }).ToList();
        if (facts.Running.Count > MaxListed) items.Add(new DigestItem("and " + N(facts.Running.Count - MaxListed) + " more", DigestLink.None));
        return new DigestSection(DigestPart.Running, "Running:", "", items, true);
    }

    private static string CheckpointWords(DateTimeOffset at, DateTimeOffset now) =>
        at.Date == now.Date ? "about " + Clock(at) : (at.Date == now.Date.AddDays(1) ? "tomorrow about " + Clock(at) : "on " + at.ToString("d MMM", CultureInfo.InvariantCulture) + " about " + Clock(at));

    // ---- 4. Usage: every number names its window ----

    private sealed record UsageResult(DigestSection? Section, IReadOnlyList<string> Breached);

    private static UsageResult Usage(DigestFacts facts, DateTimeOffset now)
    {
        var accounts = facts.Accounts.Where(a => a.Windows.Count > 0).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (accounts.Count == 0) return new UsageResult(null, []);
        var items = new List<DigestItem>();
        var breached = new List<string>();
        var anyFloor = false;
        foreach (var account in accounts)
        {
            var clauses = new List<string>();
            foreach (var window in account.Windows.OrderBy(w => w.Kind).ThenBy(w => w.Title, StringComparer.Ordinal))
            {
                clauses.Add(UsageClause(window, facts.Now, now, facts.Zone));
                if (window.Floor is { } floor && floor < 100)
                {
                    anyFloor = true;
                    if (window.Used > floor + 0.5) breached.Add($"{Line(account.Name, TitleChars)} is past your reserve on the {WindowWords(window)}.");
                }
            }
            items.Add(new DigestItem($"{Line(account.Name, TitleChars)}: {string.Join("; ", clauses)}.", new DigestLink(DigestLinkKind.Account, account.Name)));
        }
        // The reserve sentence carries no number: the one number a reserve has is a window's, and the line above already names each window.
        if (anyFloor)
        {
            if (breached.Count == 0) items.Add(new DigestItem("Your reserves hold.", DigestLink.None));
            else items.AddRange(breached.Select(b => new DigestItem(b, DigestLink.None)));
        }
        return new UsageResult(new DigestSection(DigestPart.Usage, "Usage:", "", items, false), breached);
    }

    // One window, one figure, and the figure names the window: "22% of the 5-hour window used"; an allowance about to reset unused is
    // said the other way round ("31% of the weekly window is unused and resets at 14:00, so I'm using it first").
    private static string UsageClause(DigestWindow window, DateTimeOffset nowUtc, DateTimeOffset local, TimeZoneInfo zone)
    {
        var used = Math.Round(Math.Clamp(double.IsFinite(window.Used) ? window.Used : 0, 0, 100), MidpointRounding.AwayFromZero);   // a reading that is not a number reads as none
        var unused = 100 - used;
        var words = WindowWords(window);
        DateTimeOffset? resets = window.ResetsAt is { } at && at > nowUtc ? TimeZoneInfo.ConvertTime(at, zone) : null;
        var expiring = window.Kind == ReserveWindowKind.Weekly && window.ResetsAt is { } reset && reset > nowUtc
            && reset - nowUtc <= RouteScoring.UseItOrLose && unused >= UseFirstUnused;
        if (expiring && resets is { } soon)
            return $"{N(unused)}% of the {words} is unused and resets at {ResetWords(soon, local)}, so I'm using it first";
        var clause = $"{N(used)}% of the {words} used";
        return resets is { } later && later.Date == local.Date ? $"{clause}, resets at {Clock(later)}" : clause;
    }

    private static string ResetWords(DateTimeOffset at, DateTimeOffset now) => at.Date == now.Date ? Clock(at) : Clock(at) + " tomorrow";

    /// <summary>"5-hour window", "weekly window", "daily budget", "monthly budget"; another window by the name its provider gave it.</summary>
    public static string WindowWords(DigestWindow window) =>
        window.Kind == ReserveWindowKind.Other && !string.IsNullOrWhiteSpace(window.Title) ? Line(window.Title, TitleChars) : ReserveWindowKinds.Words(window.Kind);

    // ---- 5. What standing duties found ----

    private static DigestSection? Duties(DigestFacts facts, ref Taint taint)
    {
        var shown = new List<(DigestDutyRun Latest, int Runs)>();
        foreach (var group in facts.DutyRuns.Where(ShowsAsFinding).GroupBy(r => r.DutyId, StringComparer.Ordinal).OrderBy(g => g.Min(r => r.At)))
        {
            var latest = group.OrderByDescending(r => r.At).First();
            shown.Add((latest, group.Count()));
        }
        if (shown.Count == 0) return null;
        var items = new List<DigestItem>();
        foreach (var (run, runs) in shown)
        {
            var text = run.Outcome == DutyRunOutcome.NothingChanged ? "nothing changed." : FindingText(run);
            var label = runs > 1 ? $"{Line(run.Duty, TitleChars)} ({N(runs)} runs)" : Line(run.Duty, TitleChars);
            items.Add(new DigestItem($"{label}: {text}", run.Mission is { } mission ? new DigestLink(DigestLinkKind.Mission, mission.ToString("D")) : new DigestLink(DigestLinkKind.Duty, run.DutyId)));
            taint = taint.Union(run.Taint);
        }
        return new DigestSection(DigestPart.Duties, "Duties:", "", items, false);
    }

    // 7.1: a run that found something shows for every report style; a quiet run shows only where the duty asked to be heard every time (Always,
    // Digest only), and never for Silent unless something changed or Notify me; a failed run is not a finding, it is unusual (7).
    private static bool ShowsAsFinding(DigestDutyRun run) => run.Outcome switch
    {
        DutyRunOutcome.Done => true,
        DutyRunOutcome.NothingChanged => run.Style is ReportStyle.Always or ReportStyle.DigestOnly,
        _ => false,
    };

    private static string FindingText(DigestDutyRun run)
    {
        var text = Line(run.Text, FindingChars);
        return text.Length == 0 ? "finished." : (text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') || text.EndsWith('…') ? text : text + ".");
    }

    // ---- 6. What runs today ----

    private static DigestSection? Today(DigestFacts facts, DateTimeOffset now)
    {
        var today = facts.Today
            .Select(s => (Duty: s, Local: TimeZoneInfo.ConvertTime(s.At, facts.Zone)))
            .Where(s => s.Local.Date == now.Date && s.Duty.At >= facts.Now)
            .OrderBy(s => s.Duty.At)
            .ToList();
        if (today.Count == 0) return null;
        var items = today.Take(MaxListed).Select(s => new DigestItem($"{Line(s.Duty.Duty, TitleChars)} at {Clock(s.Local)}", new DigestLink(DigestLinkKind.Duty, s.Duty.DutyId))).ToList();
        if (today.Count > MaxListed) items.Add(new DigestItem("and " + N(today.Count - MaxListed) + " more", DigestLink.None));
        return new DigestSection(DigestPart.Today, "Today:", "", items, true);
    }

    // ---- 7. Anything unusual ----

    private static DigestSection? Unusual(DigestFacts facts, DateTimeOffset now)
    {
        var items = new List<DigestItem>();
        foreach (var group in facts.DutyRuns.Where(r => r.Outcome is DutyRunOutcome.Failed or DutyRunOutcome.Refused).GroupBy(r => (r.DutyId, r.Outcome)).OrderBy(g => g.Min(r => r.At)))
        {
            var latest = group.OrderByDescending(r => r.At).First();
            var times = group.Count();
            var link = new DigestLink(DigestLinkKind.Duty, latest.DutyId);
            var name = Quoted(latest.Duty);
            if (latest.Outcome == DutyRunOutcome.Refused)
                items.Add(new DigestItem($"{name} could not start{(times > 1 ? $" ({N(times)} times)" : "")}.", link) { Note = Reason(latest.Text) });
            else
                items.Add(new DigestItem(times > 1 ? $"{name} failed {N(times)} times; the last at {Clock(TimeZoneInfo.ConvertTime(latest.At, facts.Zone))}." : $"{name} failed at {Clock(TimeZoneInfo.ConvertTime(latest.At, facts.Zone))}.", link));
        }
        foreach (var login in facts.ExpiredLogins.Order(StringComparer.OrdinalIgnoreCase))
            items.Add(new DigestItem($"Your sign-in for {Line(login, TitleChars)} expired.", new DigestLink(DigestLinkKind.Account, login)));
        foreach (var account in facts.Accounts.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var outside in account.Outside.Where(o => o.Points >= OutsideWorthSaying).OrderBy(o => o.Kind))
            {
                var words = outside.Kind == ReserveWindowKind.Other && !string.IsNullOrWhiteSpace(outside.Title) ? Line(outside.Title, TitleChars) : ReserveWindowKinds.Words(outside.Kind);
                items.Add(new DigestItem($"{N(Math.Round(outside.Points, MidpointRounding.AwayFromZero))}% of the {words} on {Line(account.Name, TitleChars)} went on use outside nth in the last day.", new DigestLink(DigestLinkKind.Account, account.Name)));
            }
        }
        return items.Count == 0 ? null : new DigestSection(DigestPart.Unusual, "Unusual:", "", items, false);
    }

    private static string Reason(string? text) => Line(text, TitleChars) is { Length: > 0 } reason ? "(" + reason.TrimEnd('.') + ")" : "";

    // ---- 8. What the delegate answered ----

    private static DigestSection? Answered(DigestFacts facts)
    {
        if (facts.Answered.Count == 0) return null;
        var items = facts.Answered.Select(a => new DigestItem($"{N(a.Count)} {(a.Count == 1 ? "question" : "questions")} in {Quoted(a.Thread)}", a.Link) { Note = "(each with Override)" }).ToList();
        return new DigestSection(DigestPart.Answered, "Answered for you:", "", items, true);
    }

    // ---- the Asturs' paragraphs ----

    private static DigestSection? Asturs(DigestFacts facts, ref Taint taint)
    {
        var paragraphs = facts.Asturs.Where(a => Line(a.Paragraph, ParagraphChars).Length > 0).ToList();
        if (paragraphs.Count == 0) return null;
        var items = new List<DigestItem>();
        foreach (var astur in paragraphs)
        {
            items.Add(new DigestItem($"{Line(astur.Name, TitleChars)}: {Line(astur.Paragraph, ParagraphChars)}", DigestLink.None));
            taint = taint.Union(astur.Taint);
        }
        return new DigestSection(DigestPart.Asturs, "From your " + AssistantNames.Asturs + ":", "", items, false);
    }

    // ---- read aloud ----

    private static string Speak(DigestFacts facts, IReadOnlyList<string> breached, int unusual)
    {
        var parts = new List<string>();
        parts.Add(facts.NeedsYou.Count switch
        {
            0 => "Nothing needs you.",
            1 => "One thing needs you.",
            var n => N(n) + " things need you.",
        });
        if (facts.Finished.Count > 0) parts.Add("Finished: " + Names(facts.Finished.OrderBy(d => d.At).Select(d => d.Title).ToList()) + ".");
        if (facts.Running.Count > 0) parts.Add("Running: " + Names(facts.Running.Select(r => r.Title).ToList()) + ".");
        var usage = facts.Accounts.Where(a => a.Windows.Count > 0).ToList();
        if (usage.Any(a => a.Windows.Any(w => w.Floor is < 100)))
            parts.Add(breached.Count == 0 ? "Your reserves hold." : breached.Count == 1 ? "One reserve is crossed." : N(breached.Count) + " reserves are crossed.");
        if (unusual > 0) parts.Add(unusual == 1 ? "One thing looks unusual." : N(unusual) + " things look unusual.");
        parts.Add(Digest.EndLine);
        return string.Join(" ", parts);
    }

    // At most two names, then how many more: "“A” and “B”", "“A”, “B” and 3 more".
    private static string Names(IReadOnlyList<string> titles)
    {
        var shown = titles.Take(2).Select(Quoted).ToList();
        var rest = titles.Count - shown.Count;
        return rest > 0 ? string.Join(", ", shown) + " and " + N(rest) + " more" : string.Join(" and ", shown);
    }

    // ---- words ----

    private static string Quoted(string title) => Quote + Line(title, TitleChars) + Unquote;

    // One line, secrets masked, cut to a length: a title or a finding is data, and it is shown, never obeyed.
    private static string Line(string? text, int chars) => FeedShield.Excerpt(text, chars);

    private static string Clock(DateTimeOffset local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string N(double n) => n.ToString("0", CultureInfo.InvariantCulture);
}
