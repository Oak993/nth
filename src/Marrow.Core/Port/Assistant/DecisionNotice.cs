// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.1 "Windows toasts only when a decision is needed", "Toast buttons", "Phone push later"; 4.2 "Keyboard";
// 2.5 size ladder "Notifications"; the canvas's 48 px notification; unit AS-9; ED-NEW-dextra-as9-1, -4). No Swift twin.
// What a decision toast says, which buttons it carries, and the channel that shows it. Pure: no I/O, no clock. A card that needs the owner
// is the only trigger; a reversible provider approval shows Allow once, Deny and Open; every other kind shows only Review, which opens the
// card (an irreversible step is always seen in full first, 13.1). The text is the card's title, masked for secrets and cut to one line; no
// input of the card ever goes into a toast.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>A button on a decision toast.</summary>
public enum NoticeAction
{
    /// <summary>Answers the card Allow once, without opening nth.</summary>
    AllowOnce,
    /// <summary>Answers the card Deny, without opening nth.</summary>
    Deny,
    /// <summary>Opens the card (a reversible decision's third button).</summary>
    Open,
    /// <summary>Opens the card in full (every other decision's only button).</summary>
    Review,
}

/// <summary>
/// One toast about decisions: its words, its buttons and the cards it is about. <see cref="Target"/> is the card a click opens (the first
/// of <see cref="Decisions"/>); a toast about several cards carries Review only. <see cref="Nonce"/> is set by the sender, once per toast.
/// </summary>
public sealed record DecisionNotice(Guid Target, ValueList<Guid> Decisions, string Title, string Body, ValueList<NoticeAction> Actions)
{
    /// <summary>A secret the sender made for this toast; an answer from a button counts only with it, and only once.</summary>
    public string Nonce { get; init; } = "";

    /// <summary>The toast speaks for several cards.</summary>
    public bool IsSummary => Decisions.Count > 1;
}

/// <summary>
/// A place a decision toast can go (13.1 "Phone push later"). Windows is the one this release has; the phone relay later is another
/// subclass, taking the same notices.
/// </summary>
public abstract class NotificationChannel
{
    /// <summary>The channel's name ("Windows"), for faults and tests.</summary>
    public abstract string Name { get; }

    /// <summary>Shows the notice. True when the channel took it; never throws for a channel that cannot show (it says false).</summary>
    public abstract Task<bool> Send(DecisionNotice notice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows a duty's finding (13.1 "Duty opt-in": a duty the owner set to "Notify me"). It answers nothing and stays up until he dismisses it. A
    /// channel that cannot say it (the default) returns false.
    /// </summary>
    public virtual Task<bool> Send(DutyNotice notice, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>Takes a notice back from wherever it sits (the card was answered, held or withdrawn). Best effort; never throws.</summary>
    public virtual Task Withdraw(Guid target) => Task.CompletedTask;
}

public static class DecisionNoticeWords
{
    public static string Label(NoticeAction action) => action switch
    {
        NoticeAction.AllowOnce => "Allow once",
        NoticeAction.Deny => "Deny",
        NoticeAction.Open => "Open",
        _ => "Review",
    };

    /// <summary>"Store needs you" (the canvas's notification).</summary>
    public static string Title(string who) => who + " needs you";

    /// <summary>"3 decisions need you" (the same words the Decisions tab uses for a batch).</summary>
    public static string Many(int count) => count.ToString(CultureInfo.InvariantCulture) + " decisions need you";

    /// <summary>"3 duties have news" (the summary of "Notify me" duties that ended together, 13.1 "Duty opt-in").</summary>
    public static string DutyMany(int count) => count.ToString(CultureInfo.InvariantCulture) + " duties have news";

    /// <summary>What an activation carries for a button; Parse reads it back.</summary>
    public static string Key(NoticeAction action) => action switch
    {
        NoticeAction.AllowOnce => "allowOnce",
        NoticeAction.Deny => "deny",
        NoticeAction.Open => "open",
        _ => "review",
    };

    /// <summary>The button a payload names, or null for anything else (a payload from outside nth is never trusted).</summary>
    public static NoticeAction? Parse(string? key) => key switch
    {
        "allowOnce" => NoticeAction.AllowOnce,
        "deny" => NoticeAction.Deny,
        "open" => NoticeAction.Open,
        "review" => NoticeAction.Review,
        _ => null,
    };
}

public static class DecisionNotices
{
    /// <summary>The longest name in a title: the banner is one line, and a name is the owner's own word.</summary>
    public const int WhoChars = 40;

    /// <summary>The longest line of a toast's body.</summary>
    public const int BodyChars = 160;

    /// <summary>The names a summary lists before it says "and n more".</summary>
    public const int NamesListed = 3;

    private const string Fallback = "Waiting for your decision.";

    /// <summary>The card needs the owner now (13.1): it waits open, and it is not one he may raise himself.</summary>
    public static bool NeedsYou(DecisionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Priority == DecisionPriority.NeedsYou && item.State == DecisionState.Open;
    }

    /// <summary>
    /// Whether the toast may answer the card (Allow once, Deny): a reversible provider approval or a limit card whose class the classifier
    /// placed and that shows no personal data. A tool call of Dextra's own (a new duty or Astur), a command nobody could place and any
    /// irreversible, picked or paid card is reviewed in the app first.
    /// </summary>
    public static bool AnswersInNotice(DecisionItem item)
    {
        if (!NeedsYou(item)) return false;
        if (item.Weight != DecisionWeight.Reversible || item.Kind is not (DecisionKind.Approval or DecisionKind.Limit)) return false;
        if (item.Class == ActionClass.Unknown || item.Highlights.Count > 0) return false;
        var answers = item.Answers;
        return answers.Contains(DecisionAnswerKind.AllowOnce) && answers.Contains(DecisionAnswerKind.Deny);
    }

    /// <summary>The toast for one card, or null when the card does not need the owner.</summary>
    public static DecisionNotice? For(DecisionItem item, string who)
    {
        if (!NeedsYou(item)) return null;
        var body = FeedShield.Excerpt(item.Title, BodyChars);
        return new DecisionNotice(item.Id, new ValueList<Guid>([item.Id]), DecisionNoticeWords.Title(Name(who)), body.Length > 0 ? body : Fallback,
            new ValueList<NoticeAction>(AnswersInNotice(item)
                ? [NoticeAction.AllowOnce, NoticeAction.Deny, NoticeAction.Open]
                : [NoticeAction.Review]));
    }

    /// <summary>
    /// The toast for cards that arrived together: one card is its own toast; several are one summary with Review only (each is still
    /// answered on its own, 4.2 "Batching"). Cards that do not need the owner are left out; null when none does.
    /// </summary>
    public static DecisionNotice? ForMany(IReadOnlyList<DecisionItem> items, Func<string, string> whoOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(whoOf);
        var waiting = items.Where(NeedsYou).ToList();
        if (waiting.Count == 0) return null;
        if (waiting.Count == 1) return For(waiting[0], whoOf(waiting[0].AssistantId));
        var names = waiting.Select(card => Name(whoOf(card.AssistantId))).Distinct(StringComparer.Ordinal).ToList();
        var listed = names.Count <= NamesListed
            ? names
            : [.. names.Take(NamesListed), (names.Count - NamesListed).ToString(CultureInfo.InvariantCulture) + " more"];
        return new DecisionNotice(waiting[0].Id, new ValueList<Guid>(waiting.Select(card => card.Id)), DecisionNoticeWords.Many(waiting.Count),
            $"{ControlWords.AndList(listed)} {(names.Count == 1 ? "is" : "are")} waiting on you.", new ValueList<NoticeAction>([NoticeAction.Review]));
    }

    /// <summary>The answer a button gives; null for Open and Review, which answer nothing.</summary>
    public static DecisionAnswer? Answer(NoticeAction action) => action switch
    {
        NoticeAction.AllowOnce => DecisionAnswer.AllowOnce,
        NoticeAction.Deny => DecisionAnswer.Deny(),
        _ => null,
    };

    private static string Name(string who)
    {
        var name = FeedShield.Excerpt(who, WhoChars);
        return name.Length > 0 ? name : AssistantNames.Assistant;
    }
}
