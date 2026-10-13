using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-11): digests composed by the real composer from small facts, for the tests of what the store keeps. Helper code only (R-TEST-3).</summary>
public static class DigestFixtures
{
    public static readonly DateTimeOffset Morning = new(2026, 10, 13, 7, 0, 0, TimeSpan.Zero);

    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test+1", TimeSpan.FromHours(1), "test+1", "test+1");

    public static Suggestion Offer(string key, SuggestionKind kind = SuggestionKind.AllowanceUnused) =>
        new(key, kind, "A line for " + key + ".", "Up to 5% of Codex A's weekly window", new SuggestionPlan(SuggestionDo.StartMission, "Title " + key)
        {
            Brief = "Do the thing.",
            ProjectId = new Guid("11111111-1111-1111-1111-111111111111"),
        }, Morning);

    /// <summary>A digest that uses every part of the format: sections of every part, a lane, a seal, a taint, a duty change plan.</summary>
    public static Digest Full(string id = "d1", DateTimeOffset? at = null)
    {
        var now = at ?? Morning;
        var facts = new DigestFacts(id, now, Zone, DigestKind.Scheduled)
        {
            Since = now.AddDays(-1),
            NeedsYou = [new DigestAsk("A send for Store", new DigestLink(DigestLinkKind.Decision, "c1"))],
            Finished = [new DigestDone("Invoice filed", new DigestLink(DigestLinkKind.Mission, "m1"), now.AddHours(-5), DigestReview.Passed)],
            Running = [new DigestRunning("Shop login fix", new DigestLink(DigestLinkKind.Mission, "m2"), "Working") { NextCheckpoint = now.AddHours(3) }],
            Accounts = [new DigestAccount("Codex A", [new DigestWindow(ReserveWindowKind.Weekly, "", 69, 90, now.AddHours(6))]) { Outside = [new DigestOutside(ReserveWindowKind.Weekly, "", 4)] }],
            DutyRuns = [new DigestDutyRun("w1", "Channel numbers", ReportStyle.Always, DutyRunOutcome.Done, now.AddHours(-2)) { Text = "Views are up 8%", Taint = Taint.From(ContentOrigin.Web, "https://example.test/a") }],
            Today = [new DigestScheduled("w2", "File invoices", now.AddHours(2))],
            ExpiredLogins = ["Channel B"],
            Answered = [new DigestAnswered("Pitch deck v3", 2, new DigestLink(DigestLinkKind.Thread, "t1"))],
            Asturs = [new DigestAstur("Store", "Two listings need prices.")],
            Suggestions =
            [
                Offer("s1"),
                new Suggestion("s2", SuggestionKind.IdleDuty, "Slow it down?", "Saves about 8 runs a day.", new SuggestionPlan(SuggestionDo.ChangeDuty, "Espresso") { DutyId = "d9", EveryMinutes = 360 }, now) { LocalOnly = true },
                new Suggestion("s3", SuggestionKind.DoneByHand, "A duty?", "On the card.", new SuggestionPlan(SuggestionDo.NewDuty, "Re-price") { Day = DayOfWeek.Friday, At = new TimeOnly(16, 0), Instructions = "Do it." }, now),
            ],
            OfferedToday = 3,
            Seal = new DigestSeal(new DateOnly(2026, 10, 12), new string('a', 64)),
        };
        return DigestComposer.Compose(facts);
    }

    public static Digest Plain(string id, DateTimeOffset at) => DigestComposer.Compose(new DigestFacts(id, at, Zone, DigestKind.Scheduled) { Since = at.AddDays(-1) });
}
