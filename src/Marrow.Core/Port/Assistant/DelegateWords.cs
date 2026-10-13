// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3 "The delegate responder", 4.2 the card's rule line; unit AS-22; ED-NEW-dextra-as22-1). No Swift twin.
// Why the delegate leaves a question or an approval to the owner, and every word the delegate shows: the label in the chat
// ("answered by ⟨head⟩ for you"), the card's rule line, the correction an Override sends, the feed line and the digest's line.
// Pure: no I/O, no clock, no model.
using System.Globalization;
using System.Text;

namespace Marrow.Core.Port.Assistant;

/// <summary>Why the delegate does not answer (4.3: "what it never answers").</summary>
public enum DelegateReason
{
    /// <summary>Money: a purchase, a subscription, a transfer.</summary>
    Money,
    /// <summary>A password, a key, a token, a sign-in, a field that asks for a secret.</summary>
    Credentials,
    /// <summary>Sending, posting, sharing, publishing, pushing anything out.</summary>
    Outward,
    /// <summary>Deleting, overwriting, wiping.</summary>
    Destructive,
    /// <summary>Installing software, packages or extensions.</summary>
    Install,
    /// <summary>Booking, accepting, agreeing, promising.</summary>
    Commitment,
    /// <summary>A true design or taste pick that no recorded decision settles.</summary>
    Taste,
    /// <summary>The delegate was not sure enough (below the threshold, or it said so).</summary>
    LowConfidence,
    /// <summary>The owner's rules ask first, or the engine did not allow it.</summary>
    Rules,
    /// <summary>The chat read untrusted content (12.4): a tainted delegate answers nothing.</summary>
    Tainted,
    /// <summary>The model's answer was missing, unreadable or not one of the choices.</summary>
    Unclear,
    /// <summary>No model could be asked, or it did not answer in time.</summary>
    NoModel,
    /// <summary>The delegate may not read this chat (5.4).</summary>
    Private,
    /// <summary>More questions, or longer ones, than the delegate takes.</summary>
    TooMuch,
}

public static class DelegateWords
{
    /// <summary>The longest answer the label and the feed repeat.</summary>
    public const int ShownChars = 160;

    /// <summary>The label of an answer in the chat (4.3): "answered by ⟨head⟩ for you".</summary>
    public static string AnsweredBy(string who) => $"Answered by {Name(who)} for you";

    /// <summary>The note the chat shows for a question the delegate answered.</summary>
    public static string AnswerNote(string who, string answer) => $"{AnsweredBy(who)}: {Clip(answer)}";

    /// <summary>The note the chat shows for an approval the delegate answered (the engine allowed it).</summary>
    public static string ApprovalNote(string who, string title, string rule) =>
        $"{AnsweredBy(who)}: allowed {Clip(title)}. {rule}";

    /// <summary>The note the chat shows for a question or approval the delegate left to the owner.</summary>
    public static string LeftNote(string who, DelegateReason reason, string? detail = null) =>
        $"{Name(who)} left this to you: {Reason(reason, detail)}";

    /// <summary>Why, as the card says it ("Always confirmed: sending" is the rule line; this is the why).</summary>
    public static string Reason(DelegateReason reason, string? detail = null) => reason switch
    {
        DelegateReason.Money => "Money is always yours to decide.",
        DelegateReason.Credentials => "Passwords, keys and sign-ins are always yours.",
        DelegateReason.Outward => "Anything sent out is always confirmed by you.",
        DelegateReason.Destructive => "Deleting is always confirmed by you.",
        DelegateReason.Install => "Installing is always confirmed by you.",
        DelegateReason.Commitment => "Booking, agreeing and promising are always confirmed by you.",
        DelegateReason.Taste => "This is a design or taste pick, and nothing you decided settles it.",
        DelegateReason.LowConfidence => "It was not sure enough to answer for you.",
        DelegateReason.Rules => string.IsNullOrWhiteSpace(detail) ? "Your rules ask you first." : Sentence(detail),
        DelegateReason.Tainted => "This chat read untrusted content, so a delegate answers nothing in it.",
        DelegateReason.Unclear => "Its answer was not one of the choices.",
        DelegateReason.NoModel => "No model could be asked in time.",
        DelegateReason.Private => "It may not read this chat.",
        DelegateReason.TooMuch => "There is more here than it answers for you.",
        _ => "It is yours to answer.",
    };

    /// <summary>The rule line of a card the delegate raised (4.2: "which rule sent it to the owner").</summary>
    public static string RuleLine(DelegateReason reason) => reason switch
    {
        DelegateReason.Money => "Always confirmed: money",
        DelegateReason.Credentials => "Never answered for you: credentials",
        DelegateReason.Outward => "Always confirmed: sending",
        DelegateReason.Destructive => "Always confirmed: deleting",
        DelegateReason.Install => "Always confirmed: installing",
        DelegateReason.Commitment => "Always confirmed: promises",
        DelegateReason.Taste => "Design and taste picks are yours",
        DelegateReason.LowConfidence => "Below the delegate's confidence",
        DelegateReason.Rules => "Your rules ask first",
        DelegateReason.Tainted => "Untrusted content: no delegate answers",
        _ => "Left to you by your delegate",
    };

    /// <summary>The correction an Override sends the asking session (4.3: "sent to the asking session as a correction").</summary>
    public static string Correction(string question, string answered, string correction) =>
        "A correction from the owner. The question you asked was answered for them with " + Quote(answered) + " (your question: " + Quote(question)
        + "). The owner says that was not right: " + correction.Trim() + "\nGo by this from now on.";

    /// <summary>The feed's lead for an answer: "answered a question for you" (the chat's title leads the line).</summary>
    public static string FeedLead(bool approval) => approval ? "an approval was answered for you" : "a question was answered for you";

    /// <summary>The digest's line, one chat's count (6: "Answered for you: 2 questions in “Pitch deck v3”").</summary>
    public static string DigestItem(string chat, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? "question" : "questions")} in “{chat}”");

    public const string DigestHead = "Answered for you:";

    public const string DigestNote = "(each with Override)";

    public const string OverrideButton = "Override";

    public const string CorrectionPrompt = "What should it have been?";

    public const string SendCorrection = "Send correction";

    public const string Corrected = "Corrected";

    public const string AnsweredHeading = "Answered for you";

    public const string AnsweredEmpty = "Nothing answered for you yet.";

    /// <summary>The longest correction the owner can send.</summary>
    public const int CorrectionChars = 1_000;

    /// <summary>The chat menu's toggle (4.3: "the chat's menu").</summary>
    public static string MenuTitle(bool on) => on ? "Stop answering for me" : "Answer questions for me";

    /// <summary>The name shown for a delegate: Dextra itself, or the head it is.</summary>
    public static string Name(string who) => string.IsNullOrWhiteSpace(who) ? AssistantNames.Assistant : who.Trim();

    /// <summary>One line of at most <see cref="ShownChars"/> characters: line breaks folded to spaces, cut with an ellipsis.</summary>
    public static string Clip(string? text, int chars = ShownChars)
    {
        var line = (text ?? "").ReplaceLineEndings(" ").Trim();
        if (line.Length <= chars) return line;
        var cut = line[..chars];
        return cut.TrimEnd() + "…";
    }

    private static string Quote(string text) => "“" + Clip(text, 300) + "”";

    private static string Sentence(string text)
    {
        var line = Clip(text, 240);
        return line.EndsWith('.') ? line : line + ".";
    }

    /// <summary>Appends a clipped answer to a builder as a quoted fragment (shared by the card and the log).</summary>
    public static void AppendQuoted(StringBuilder builder, string text) => builder.Append(Quote(text));
}
