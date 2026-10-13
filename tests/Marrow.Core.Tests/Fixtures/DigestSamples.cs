using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-11): small builders for the facts a digest is composed from, on the canvas's own example. Helper code only (R-TEST-3).</summary>
internal static class DigestSamples
{
    /// <summary>Tuesday 13 October 2026, 08:00 in a zone one hour ahead of UTC (07:00 UTC).</summary>
    public static readonly DateTimeOffset Morning = new(2026, 10, 13, 7, 0, 0, TimeSpan.Zero);

    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test+1", TimeSpan.FromHours(1), "test+1", "test+1");

    public static DigestFacts Facts(DigestKind kind = DigestKind.Scheduled) => new("d1", Morning, Zone, kind) { Since = Morning.AddHours(-24) };

    public static DigestAsk Ask(string title, string id = "c1") => new(title, new DigestLink(DigestLinkKind.Decision, id));

    public static DigestDone Done(string title, DigestReview review = DigestReview.Passed, int hoursAgo = 6) =>
        new(title, new DigestLink(DigestLinkKind.Mission, title), Morning.AddHours(-hoursAgo), review);

    public static DigestRunning Running(string title, string state = "Working") => new(title, new DigestLink(DigestLinkKind.Mission, title), state);

    public static DigestWindow Window(ReserveWindowKind kind, double used, double? floor = null, DateTimeOffset? resets = null, string title = "") =>
        new(kind, title, used, floor, resets);

    public static DigestAccount Account(string name, params DigestWindow[] windows) => new(name, windows);

    public static DigestDutyRun Run(string duty, ReportStyle style, DutyRunOutcome outcome, string? text = null, int hoursAgo = 3) =>
        new(duty.ToLowerInvariant().Replace(' ', '-'), duty, style, outcome, Morning.AddHours(-hoursAgo)) { Text = text };

    public static Suggestion Offer(string key, SuggestionKind kind = SuggestionKind.AllowanceUnused) =>
        new(key, kind, "A line for " + key + ".", "A cost.", new SuggestionPlan(SuggestionDo.StartMission, "Title " + key), Morning);

    public static SuggestionCandidate Candidate(string key, SuggestionKind kind = SuggestionKind.AllowanceUnused, double urgency = 0) =>
        new(kind, key, "A line for " + key + ".", "A cost.", new SuggestionPlan(SuggestionDo.StartMission, "Title " + key)) { Urgency = urgency };
}
