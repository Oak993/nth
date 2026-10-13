// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.2 "Proactive suggestions", 6; unit AS-11; ED-NEW-dextra-as11-5). No Swift twin.
// A suggestion: what cheap rules spotted (a candidate), what is offered to the owner (a suggestion, with a line, a cost and what Accept
// does), and the plan Accept follows. Pure data. The Spotter finds candidates, the SuggestionLedger decides which are offered today.
namespace Marrow.Core.Port.Assistant;

/// <summary>
/// The four things the spotter looks for (5.2), and the one line the brain adds with its <c>suggest</c> tool (15.5). Three dismissals of a kind turn that kind off.
/// </summary>
public enum SuggestionKind { AllowanceUnused, DoneByHand, FailingCheck, IdleDuty, FromBrain }

public enum SuggestionState { Open, Accepted, Dismissed }

/// <summary>What Accept does: start a mission, raise the creation card of a new duty, or raise the card that changes a duty.</summary>
public enum SuggestionDo { StartMission, NewDuty, ChangeDuty }

/// <summary>
/// What Accept follows, as plain fields so it survives a restart. A mission starts with <see cref="Brief"/> in <see cref="ProjectId"/>; a new duty
/// runs weekly on <see cref="Day"/> at <see cref="At"/> doing <see cref="Instructions"/>; a changed duty looks every <see cref="EveryMinutes"/> minutes.
/// Accept never skips a card: a duty is made or changed only through the usual creation card (7.1), a mission only inside the usual rules.
/// </summary>
public sealed record SuggestionPlan(SuggestionDo Do, string Title)
{
    public string Brief { get; init; } = "";

    public Guid? ProjectId { get; init; }

    public string? DutyId { get; init; }

    public int? EveryMinutes { get; init; }

    public DayOfWeek? Day { get; init; }

    public TimeOnly? At { get; init; }

    public string? Instructions { get; init; }
}

/// <summary>
/// What a rule spotted, before it is offered. <see cref="Key"/> names the event (so the same thing is never offered twice); <see cref="Line"/> and
/// <see cref="Cost"/> are the words nth wrote without a model, which a model may replace. <see cref="LocalOnly"/>: the facts come from a chat that may not be
/// summarised to a cloud provider (5.4), so only a model on this PC may rewrite the line.
/// </summary>
public sealed record SuggestionCandidate(SuggestionKind Kind, string Key, string Line, string Cost, SuggestionPlan Plan)
{
    public bool LocalOnly { get; init; }

    /// <summary>How soon it matters, larger first, for the day's three places: an allowance about to reset before a duty that could be slower.</summary>
    public double Urgency { get; init; }
}

/// <summary>A suggestion offered to the owner.</summary>
public sealed record Suggestion(string Key, SuggestionKind Kind, string Line, string Cost, SuggestionPlan Plan, DateTimeOffset OfferedAt)
{
    public SuggestionState State { get; init; } = SuggestionState.Open;

    public bool LocalOnly { get; init; }

    public DateTimeOffset? Settled { get; init; }
}

/// <summary>The words of the Suggestions lane and its buttons (canvas "Morning digest").</summary>
public static class SuggestionWords
{
    public const string AcceptTitle = "Accept";

    public const string DismissTitle = "Dismiss";

    /// <summary>"Suggestions · 2 of at most 3 today".</summary>
    public static string LaneTitle(int offered) => FormattableString.Invariant($"Suggestions · {offered} of at most {SuggestionLedger.MaxPerDay} today");

    /// <summary>What a dismissed suggestion says in its place; the third dismissal of a kind says the kind is off.</summary>
    public static string Dismissed(bool kindOff) => kindOff ? "Dismissed. I won't offer these any more." : "Dismissed. I'll offer fewer like this.";
}
