// PORT-ONLY: T11 step 2. The example sheet behind "Show an example" under "What was handed over": what the next account is given, in the page's words and with the page's example figures left out (the page says "no token counts, bars not percentages").
// Which parts are kept word for word and which are shortened is the page's own list (its DIGEST); the sheet is an example, not a record of a real switch.
using Marrow.Core.Accounts;

namespace Marrow.Core.Port.MultiAccount.Settings;

public sealed record HandedOverItem(string Title, string Count, string Note, bool Kept);

public sealed record HandedOverExample(string Title, string Sub, string Intro, string? AiNote, IReadOnlyList<HandedOverItem> Items, string? File, string? FileLine, string? HandshakeLine)
{
    private static readonly IReadOnlyList<HandedOverItem> Digest =
    [
        new("Your messages", "14", "Kept word for word", true),
        new("Claude’s replies", "14", "Kept word for word", true),
        new("Task, plan and to-do", "4 of 6 done", "Kept", true),
        new("Files changed and the current diff", "3 files", "Kept", true),
        new("Commands and their results", "6 runs · last error", "Kept; long ones as head and tail", true),
        new("Approvals and web pages", "2", "Kept (they cannot be fetched again the same way)", true),
        new("File reads", "21", "Shortened to path and lines; it can read them again", false),
        new("Searches", "9", "Shortened to the query and hits; it can search again", false),
        new("Long build log", "1", "Head and tail kept", false),
    ];

    /// <param name="bring">The Settings value for "What the next account gets" (a <see cref="DecisionValues"/> string), or null when unset (the page's default, Marrow's summary).</param>
    public static HandedOverExample For(string? bring, string to, bool handshake)
    {
        var sub = "to " + to + (handshake ? " · with a Handshake" : "");
        var hs = handshake ? MultiAccountWords.HandedHandshakeLine(to) : null;
        if (bring == DecisionValues.Full) return new(MultiAccountWords.HandedTitle, sub, MultiAccountWords.HandedFullIntro, null, [], null, null, hs);
        return new(MultiAccountWords.HandedTitle, sub, MultiAccountWords.HandedSummaryIntro(to), bring == DecisionValues.AiSummary ? MultiAccountWords.HandedAiNote : null, Digest,
            "handoff-thread-0412.md", MultiAccountWords.HandedFileLine, hs);
    }
}
