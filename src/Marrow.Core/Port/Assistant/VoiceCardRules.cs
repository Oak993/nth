// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "What voice may answer", 4.2, 12.1; 16 "done when" for AS-18: voice never answers Send, Pay, Delete,
// Install or Commitment cards; ED-NEW-dextra-as18-5). No Swift twin.
// Which decision cards a spoken answer may close. A spoken "yes" can come from the room, a video or the speakers, so voice answers only
// cards that are reversible AND say what they are: a reserve, cap or paused-duty choice, a question that asks nothing irreversible, and
// "Allow once" on a read-only action. Everything else, and every card of an irreversible class however it is worded, is opened on the
// screen with a line that says which button to tap. A decline is refused too: the safe spoken move on such a card is "Stop everything".
// Pure: a card and the words in, a move out. The queue is never touched here; the caller acts on an Answer move.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

public enum VoiceCardMoveKind
{
    /// <summary>The words answer the card: <see cref="VoiceCardMove.Answer"/>.</summary>
    Answer,

    /// <summary>Voice may not answer this card: open it on the screen and say <see cref="VoiceCardMove.Line"/>.</summary>
    OnScreen,

    /// <summary>The words are not one of the card's answers: ask again with <see cref="VoiceCardMove.Line"/>.</summary>
    Unclear,
}

public sealed record VoiceCardMove(VoiceCardMoveKind Kind, string Line)
{
    public DecisionAnswer? Answer { get; init; }
}

public static partial class VoiceCardRules
{
    // Words that make a question about something irreversible, whatever kind the card is. Checked over the card's title, reason, inputs
    // and options: "Send it now or later?" is a send, and is answered on the screen.
    [GeneratedRegex(@"\b(?:send|sends|sent|email|e-?mail|post|publish|tweet|share|reply|pay|paying|payment|purchase|buy|order|checkout|subscribe|subscription|transfer|wire|delete|deletes|remove|erase|wipe|uninstall|format|install|installs|book|accept|agree|sign|commit|push|merge|deploy|release|upload|submit|confirm|invoice|refund|cancel the|overwrite|drop)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Irreversible();

    /// <summary>The input a "Which one?" card holds the owner's own message in.</summary>
    public const string OwnWords = "Your message";

    /// <summary>Whether the card is of a kind or class no spoken word may answer: Send, Pay, Delete, Install, Commitment, a merge, personal data, a refused action.</summary>
    public static bool IsIrreversible(DecisionItem card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Weight == DecisionWeight.Irreversible || card.Kind == DecisionKind.Merge) return true;
        return card.Class is ActionClass.Outward or ActionClass.Commitment or ActionClass.Destructive or ActionClass.Install or ActionClass.Money
            or ActionClass.PersonalDataOut or ActionClass.HardDeny;
    }

    /// <summary>Whether any spoken answer may close this card at all (it is open, reversible, and one of the three kinds above).</summary>
    public static bool VoiceMayAnswer(DecisionItem card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.State != DecisionState.Open || IsIrreversible(card)) return false;
        return card.Kind switch
        {
            DecisionKind.Reserve or DecisionKind.Cap or DecisionKind.Duty => card.Options.Count > 0,
            DecisionKind.Question => !SaysIrreversible(card),
            DecisionKind.Approval or DecisionKind.ToolCall => card.Weight == DecisionWeight.Reversible && card.Class is ActionClass.ReadWeb or ActionClass.ReadLocal && !SaysIrreversible(card),
            _ => false,
        };
    }

    private static bool SaysIrreversible(DecisionItem card)
    {
        if (Irreversible().IsMatch(card.Title) || Irreversible().IsMatch(card.Why)) return true;
        foreach (var input in card.Inputs)
        {
            // "Your message" is the owner's own words held while he picks who gets them ("Which one?"): they are not what the card asks.
            if (input.Name == OwnWords) continue;
            if (Irreversible().IsMatch(input.Name) || Irreversible().IsMatch(input.Value)) return true;
        }
        return card.Options.Any(option => Irreversible().IsMatch(option));
    }

    /// <summary>"Tap Send on the screen to send it.": what voice says to a card it may not answer (2.4).</summary>
    public static string OnScreenLine(DecisionItem card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Kind == DecisionKind.Merge) return "Tap Merge on the screen to merge it.";
        return card.Class switch
        {
            ActionClass.Outward or ActionClass.PersonalDataOut => "Tap Send on the screen to send it.",
            ActionClass.Money => "Tap Pay on the screen to pay.",
            ActionClass.Destructive => "Tap Delete on the screen to delete it.",
            ActionClass.Install => "Tap Install on the screen to install it.",
            ActionClass.Commitment => "Tap Commit on the screen to commit.",
            _ => IsIrreversible(card) ? "Tap the button on the screen to go ahead." : "That one needs your answer on the screen.",
        };
    }

    /// <summary>
    /// One sentence that tells the owner about a waiting card, and how to answer it when voice may: "Channel A wants to read the
    /// vendor site; say allow once or deny". A card voice may not answer says it needs a tap instead.
    /// </summary>
    public static string Offer(DecisionItem card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var title = SpokenText.Phrase(card.Title, 70);
        if (title.Length == 0) title = "a decision";
        if (!VoiceMayAnswer(card)) return $"{title} needs your tap on the screen.";
        if (card.Options.Count > 0) return $"{title}; say {SpokenOptions(card)}.";
        return card.Kind == DecisionKind.Question ? $"{title}; say your answer." : $"{title}; say allow once or deny.";
    }

    private static string SpokenOptions(DecisionItem card)
    {
        var options = card.Options.Take(3).Select(o => SpokenText.Phrase(o, 40)).Where(o => o.Length > 0).ToList();
        var more = card.Options.Count > options.Count ? ", or the screen" : "";
        return options.Count switch
        {
            0 => "your answer",
            1 => options[0] + more,
            _ => string.Join(", ", options.Take(options.Count - 1)) + (more.Length > 0 ? ", " + options[^1] + more : " or " + options[^1]),
        };
    }

    // Words that aim a request at the card Dextra has just read out, even when no yes, no or place was said: the card's own verbs and the answers to it.
    private static readonly HashSet<string> AimedAt = new(StringComparer.Ordinal)
    {
        "send", "pay", "delete", "install", "commit", "merge", "approve", "confirm", "allow", "deny", "decline", "accept", "reject", "discard", "publish", "buy", "book", "go", "ahead",
    };

    /// <summary>
    /// Whether the words are aimed at <paramref name="card"/> (the one Dextra has just read out): a yes, a no or a place, a word of the action itself
    /// ("send it"), a name for one of its options, or any answer to a free-text question. Words that are not go to Dextra as an ordinary request, and the
    /// card is left to the screen. Aimed words are still only answered by <see cref="Decide"/>.
    /// </summary>
    public static bool Targets(DecisionItem card, VoiceCommand command, string? spoken)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(command);
        if (command.Kind is VoiceCommandKind.Yes or VoiceCommandKind.No or VoiceCommandKind.Ordinal) return true;
        var words = VoiceCommands.Normalise(spoken);
        if (words.Count > 0 && words.Count <= 6 && words.Any(AimedAt.Contains)) return true;
        if (!VoiceMayAnswer(card)) return false;
        if (card.Options.Count > 0) return Pick(card, command, spoken) is not null;
        return card.Kind == DecisionKind.Question && words.Count > 0 && command.Kind == VoiceCommandKind.None;
    }

    /// <summary>What a spoken command does to a card (the card is the one Dextra has just read out).</summary>
    public static VoiceCardMove Decide(DecisionItem card, VoiceCommand command, string? spoken)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(command);
        if (IsIrreversible(card)) return new VoiceCardMove(VoiceCardMoveKind.OnScreen, OnScreenLine(card));
        if (!VoiceMayAnswer(card)) return new VoiceCardMove(VoiceCardMoveKind.OnScreen, OnScreenLine(card));

        if (card.Options.Count > 0 || card.Kind is DecisionKind.Reserve or DecisionKind.Cap or DecisionKind.Duty)
        {
            var option = Pick(card, command, spoken);
            if (option is not null && DecisionRules.Refusal(card, DecisionAnswer.Pick(option)) is null)
                return new VoiceCardMove(VoiceCardMoveKind.Answer, $"Done: {SpokenText.Phrase(option, 50)}.") { Answer = DecisionAnswer.Pick(option) };
            return new VoiceCardMove(VoiceCardMoveKind.Unclear, $"Say {SpokenOptions(card)}.");
        }
        if (card.Kind == DecisionKind.Question)
        {
            var text = (spoken ?? "").Trim();
            return text.Length == 0 || command.Kind != VoiceCommandKind.None
                ? new VoiceCardMove(VoiceCardMoveKind.Unclear, "Say your answer.")
                : new VoiceCardMove(VoiceCardMoveKind.Answer, "Sent your answer.") { Answer = DecisionAnswer.Discuss(text) };
        }
        // An approval for a read-only action: Allow once or Deny, nothing wider (a grant needs the card on the screen).
        return command.Kind switch
        {
            VoiceCommandKind.Yes => new VoiceCardMove(VoiceCardMoveKind.Answer, "Allowed once.") { Answer = DecisionAnswer.AllowOnce },
            VoiceCommandKind.No => new VoiceCardMove(VoiceCardMoveKind.Answer, "Denied.") { Answer = DecisionAnswer.Deny("Denied by voice.") },
            _ => new VoiceCardMove(VoiceCardMoveKind.Unclear, "Say allow once, or deny."),
        };
    }

    // The option the words name: by place ("the second one"), by its own words, or "yes"/"no" when the card's options are those words.
    private static string? Pick(DecisionItem card, VoiceCommand command, string? spoken)
    {
        var options = card.Options;
        if (command.Kind == VoiceCommandKind.Ordinal)
        {
            var index = command.Place == -1 ? options.Count - 1 : command.Place - 1;
            return index >= 0 && index < options.Count ? options[index] : null;
        }
        var flat = VoiceCommands.Flat(spoken);
        if (flat.Length == 0) return null;
        var named = options.Where(o => Names(flat, VoiceCommands.Flat(o))).ToList();
        if (named.Count == 1) return named[0];
        if (named.Count > 1)
        {
            // "Move it to Codex" fits "Move it to Codex A" and "Move it to Codex": the longest option the words contain whole wins, when it is the only one that long.
            var longest = named.Max(o => VoiceCommands.Flat(o).Length);
            var best = named.Where(o => VoiceCommands.Flat(o).Length == longest).ToList();
            return best.Count == 1 ? best[0] : null;
        }
        if (command.Kind is VoiceCommandKind.Yes or VoiceCommandKind.No)
        {
            var wanted = command.Kind == VoiceCommandKind.Yes ? "yes" : "no";
            var literal = options.Where(o => VoiceCommands.Flat(o) == wanted).ToList();
            return literal.Count == 1 ? literal[0] : null;
        }
        return null;
    }

    // The words of the option appear whole and in a row in what was said (an option "Wait" is named by "wait for the reset" only if nothing longer is).
    private static bool Names(string said, string option)
    {
        if (option.Length == 0) return false;
        return said == option || said.StartsWith(option + " ", StringComparison.Ordinal) || said.EndsWith(" " + option, StringComparison.Ordinal)
            || said.Contains(" " + option + " ", StringComparison.Ordinal);
    }
}
