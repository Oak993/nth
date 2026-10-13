// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1, 5.4; unit AS-2; ED-NEW-dextra-as2-1 to -4). No Swift twin.
// Turns a draft into the feed's one line: the privacy filter first, then the shield, then the words. Deterministic:
// no model call, no clock, no I/O.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>Builds <see cref="FeedEvent"/> lines (5.1) under the privacy rules (5.4).</summary>
public static class FeedBuilder
{
    /// <summary>The first words of a reply the feed keeps (5.2: "the last reply's first 300 characters").</summary>
    public const int DetailChars = 300;

    /// <summary>The longest title the feed repeats.</summary>
    public const int TitleChars = 80;

    public const string HiddenTurnFinished = "A hidden chat finished a turn";
    public const string HiddenNeedsYou = "A hidden chat needs you";

    /// <summary>
    /// The event for a draft, or null when the filter drops it: a hidden chat keeps only that a turn ended and that it
    /// needs the owner, and nothing else about it is passed on, not even its id.
    /// </summary>
    public static FeedEvent? Build(FeedDraft draft, FeedBrain brain, long sequence)
    {
        if (draft.Chat is not { } chat)
            return new FeedEvent(sequence, draft.Time, draft.Kind, draft.Severity, Line(null, draft.Lead, draft.Detail),
                draft.ThreadId, draft.MissionId, draft.AssistantId, draft.CostDelta, draft.Count, FeedTextAccess.None);

        var access = FeedPrivacy.Decide(chat, brain);
        if (access == FeedTextAccess.Hidden)
        {
            if (!draft.SurvivesHiding) return null;
            return draft.Kind == FeedEventKind.TurnFinished
                ? new FeedEvent(sequence, draft.Time, draft.Kind, FeedSeverity.Info, HiddenTurnFinished, Text: FeedTextAccess.Hidden)
                : new FeedEvent(sequence, draft.Time, draft.Kind, FeedSeverity.NeedsYou, HiddenNeedsYou, Text: FeedTextAccess.Hidden);
        }

        var title = FeedShield.Excerpt(chat.Title, TitleChars);
        var lead = draft.Kind == FeedEventKind.TokenSpend
            ? string.Create(CultureInfo.InvariantCulture, $"used {draft.CostDelta:N0} tokens")
            : draft.Lead;
        var detail = access == FeedTextAccess.Full ? FeedShield.Excerpt(draft.Detail, DetailChars) : null;
        return new FeedEvent(sequence, draft.Time, draft.Kind, draft.Severity,
            Line(title.Length > 0 ? title : "A chat", lead, detail),
            draft.ThreadId, draft.MissionId, draft.AssistantId, draft.CostDelta, draft.Count, access);
    }

    // "Title: lead" and, when there are words from the chat, ". words". Everything is masked and on one line.
    private static string Line(string? title, string lead, string? detail)
    {
        var head = FeedShield.Fold(FeedShield.Mask(lead));
        var text = title is null ? head : title + ": " + head;
        if (string.IsNullOrWhiteSpace(detail)) return text;
        var tail = title is null ? FeedShield.Excerpt(detail, DetailChars) : detail;
        if (tail.Length == 0) return text;
        return (text.EndsWith('.') ? text : text + ".") + " " + tail;
    }
}
