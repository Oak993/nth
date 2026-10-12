// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-16; DESIGN.md 10.4, 10.7, 10.9). How long each kind of ask lives,
// what answers it takes from which view, and the words around it: the card's title, the too-late line, the line when the
// model stopped waiting, and the timeline row an answer leaves in the chat.
//   Approval, confirm, show window: until answered, or their step ends (the call stopped waiting, its token ended, a stop),
//     and never past the chat turn that asked.
//   pc_ask question: answered, dismissed, 30 minutes, or the user writes in that chat first; it outlives its tool call.
//   Provider question or approval: as the provider sets it (its adapter withdraws it).
// A provider approval is never answered from the pill, and a question that asks for a secret only in the chat.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public static class AskLifetimes
{
    /// <summary>A pc_ask question's life (DESIGN.md 10.9).</summary>
    public const int QuestionMs = 30 * 60 * 1000;

    /// <summary>How long the pill shows the too-late line.</summary>
    public const int TooLateMs = 2_000;

    public const string StoppedWaiting = "The model stopped waiting for this.";
    public const string MovedOnLine = "The chat has moved on";
    public const string ChatOnlyLine = "Answer this in the chat";
    public const string ProviderApprovalLine = "Commands, file changes and plans are approved in the chat";

    /// <summary>Withdrawn when its step ends (the token the asker passes).</summary>
    public static bool EndsWithStep(PcAskKind kind) => kind is PcAskKind.Approval or PcAskKind.Confirm or PcAskKind.ShowWindow;

    /// <summary>Withdrawn at the end of the chat turn that asked.</summary>
    public static bool EndsWithTurn(PcAskKind kind) => EndsWithStep(kind);

    /// <summary>Withdrawn ("moved on") when the user writes in that chat first.</summary>
    public static bool EndsWhenUserWrites(PcAskKind kind) => kind == PcAskKind.Question;

    /// <summary>Withdrawn by a stop that covers it. A question outlives the stop: its answer still goes to the chat.</summary>
    public static bool EndsWithStop(PcAskKind kind) => EndsWithStep(kind);

    /// <summary>Its own clock: a question ends after 30 minutes.</summary>
    public static int? LifetimeMs(PcAskKind kind) => kind == PcAskKind.Question ? QuestionMs : null;

    public static bool ChatOnly(PcAskKind kind, object payload) => kind switch
    {
        PcAskKind.ProviderApproval => true,
        PcAskKind.Question => payload is PcAskRequest { AsksForSecret: true },
        PcAskKind.ProviderQuestion => payload is PcProviderAsk { Secret: true },
        _ => false,
    };

    /// <summary>Whether the pill's card answers it: "Answer approvals and questions in the pill" on, and not chat-only.</summary>
    public static bool AnsweredInPill(PcPendingAsk ask, bool answerInPill)
    {
        ArgumentNullException.ThrowIfNull(ask);
        return answerInPill && !ask.ChatOnly;
    }

    public static string Title(PcPendingAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        var app = ask.AppName is { Length: > 0 } name ? ". " + name : "";
        return ask.Kind switch
        {
            PcAskKind.Question => "Waiting for your answer" + app,
            PcAskKind.ProviderQuestion => "Waiting for your answer",
            PcAskKind.ProviderApproval => ask.Payload is PcProviderAsk provider
                ? provider.Title + (ask.Chat is { Length: > 0 } chat ? ". " + chat : "")
                : "Waiting in the chat",
            _ => "Needs your OK" + app,
        };
    }

    /// <summary>Whether <paramref name="answer"/> is one this ask takes from <paramref name="source"/>.</summary>
    public static bool Accepts(PcPendingAsk ask, object answer, PcAnswerSource source)
    {
        ArgumentNullException.ThrowIfNull(ask);
        if (source == PcAnswerSource.Pill && ask.ChatOnly) return false;
        return ask.Kind switch
        {
            PcAskKind.Approval => (answer is PcApprovalReply reply ? reply.Approval : answer) is PcApproval.Denied or PcApproval.Chat
                || ((answer is PcApprovalReply always ? always.Approval : answer) is PcApproval.Always && ask.Payload is PcApprovalAsk { AllowAlways: true }),
            PcAskKind.Confirm => answer is bool or PcConfirmReply,
            PcAskKind.ShowWindow => answer is PcShowAnswer,
            PcAskKind.Question => answer is PcAnswer reply && ask.Payload is PcAskRequest request && Fits(request, reply),
            _ => true,
        };
    }

    private static bool Fits(PcAskRequest request, PcAnswer reply)
    {
        if (reply.Choice is { } choice) return reply.Text is null && request.Choices.Contains(choice, StringComparer.Ordinal);
        return request.AllowText && !string.IsNullOrWhiteSpace(reply.Text);
    }

    /// <summary>What the pill says for 2 s when a click reaches the broker after its ask ended. Nothing was done.</summary>
    public static string TooLate(PcPendingAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        var app = ask.AppName ?? "the app";
        return ask.Kind switch
        {
            PcAskKind.Confirm when ask.Payload is PcConfirmContent content => content.Verb + " in " + app + " expired. Nothing was sent. Ask in chat to try again.",
            PcAskKind.Confirm => "This confirm in " + app + " expired. Nothing was sent. Ask in chat to try again.",
            PcAskKind.ShowWindow => "Showing " + app + " expired. Nothing was shown. Ask in chat to try again.",
            PcAskKind.Approval => "The request for " + app + " expired. Nothing was allowed. Ask in chat to try again.",
            PcAskKind.Question => "This question ended. Your answer was not sent.",
            _ => "This request ended in the chat. Nothing was sent.",
        };
    }

    /// <summary>The timeline row an answer leaves in the chat ("You allowed Outlook for this chat (from the pill)").</summary>
    public static string Logged(PcPendingAsk ask, object answer, PcAnswerSource source)
    {
        ArgumentNullException.ThrowIfNull(ask);
        var app = ask.AppName ?? "the app";
        var from = source == PcAnswerSource.Pill ? " (from the pill)" : "";
        var verb = ask.Payload is PcConfirmContent content ? content.Verb : "the action";
        // A See-only answer says so ("You let this chat see Outlook (from the pill)").
        if (ask.Kind == PcAskKind.Approval && answer is PcApprovalReply { Use: false, Approval: PcApproval.Chat or PcApproval.Always } seeOnly)
            return (seeOnly.Approval == PcApproval.Always ? "You let every chat see " : "You let this chat see ") + app + from;
        if (answer is PcApprovalReply plain) answer = plain.Approval;
        if (answer is PcConfirmReply confirm) answer = confirm.Confirmed;
        return (ask.Kind, answer) switch
        {
            (PcAskKind.Approval, PcApproval.Chat) => "You allowed " + app + " for this chat" + from,
            (PcAskKind.Approval, PcApproval.Always) => "You always allowed " + app + from,
            (PcAskKind.Approval, _) => "You did not allow " + app + from,
            (PcAskKind.Confirm, true) => "You confirmed " + verb + " in " + app + from,
            (PcAskKind.Confirm, _) => "You cancelled " + verb + " in " + app + from,
            (PcAskKind.ShowWindow, PcShowAnswer.Show) => "You showed " + app + from,
            (PcAskKind.ShowWindow, _) => "You did not show " + app + from,
            (_, PcAnswer reply) => (source == PcAnswerSource.Pill ? "You answered from the pill: " : "You answered: ") + (reply.Choice ?? reply.Text),
            _ => source == PcAnswerSource.Pill ? "You answered from the pill" : "You answered",
        };
    }
}
