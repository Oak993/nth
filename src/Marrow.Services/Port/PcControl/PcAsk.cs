// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12 item 4, ED-NEW-pcctl-16; DESIGN.md 10.4, 10.7). One pending ask
// as the broker holds it: its kind, the chat, app and head that asked, what it is about, when it came, and its revision
// (a confirm whose content was read again at the click gets a new one, so an answer to the old content cannot land). The
// pill's card and the chat are two views of the same entry.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public enum PcAskKind
{
    /// <summary>An app approval (DESIGN.md 4.1).</summary>
    Approval,

    /// <summary>A guarded action's confirm (DESIGN.md 4.3).</summary>
    Confirm,

    /// <summary>"Show this window?" for pc_focus_window or a visible quiet peek (DESIGN.md 2.10).</summary>
    ShowWindow,

    /// <summary>The model's pc_ask question (DESIGN.md 10.9).</summary>
    Question,

    /// <summary>The provider's own question in a chat with a Tactus stream.</summary>
    ProviderQuestion,

    /// <summary>The provider's own approval (command, file change, tool, permissions, plan): answered only in the chat.</summary>
    ProviderApproval,
}

public enum PcAskStatus
{
    Pending,
    Answered,
    Withdrawn,
}

/// <summary>Where an answer came from: the pill's card or the chat.</summary>
public enum PcAnswerSource
{
    Pill,
    Chat,
}

/// <summary>Why an ask left the queue unanswered.</summary>
public enum PcWithdrawal
{
    /// <summary>The user stopped Tactus (a stop that covers the ask).</summary>
    Stopped,

    /// <summary>The step it belongs to ended: its token expired, was cancelled, or its call stopped waiting.</summary>
    StepEnded,

    /// <summary>The chat turn that asked ended.</summary>
    TurnEnded,

    /// <summary>The provider cancelled its own request.</summary>
    ProviderCancelled,

    /// <summary>A question passed its 30 minutes.</summary>
    Expired,

    /// <summary>The user wrote in that chat before answering the question.</summary>
    MovedOn,

    /// <summary>The user dismissed the question.</summary>
    Dismissed,
}

/// <summary>What an answer attempt did: it won, came too late (answered or withdrawn already), answered an older revision, or is not an answer this ask takes from that view.</summary>
public enum PcResolve
{
    Done,
    TooLate,
    Changed,
    Refused,
}

/// <summary>An app approval's subject.</summary>
public sealed record PcApprovalAsk(PcCapability Capability, bool Risky, bool AllowAlways);

/// <summary>
/// The approval card's answer (DESIGN.md 10.4): Don't allow, Allow for this chat or Always allow, with the See and Use
/// chips' choice (Use implies See) and, for "Reply instead", the user's reason that goes to the model with Don't allow.
/// </summary>
public sealed record PcApprovalReply(PcApproval Approval, bool Use, string? Reason = null);

/// <summary>
/// The confirm card's answer from the pill (ED-NEW-pcctl-26): Confirm, or Cancel with what the user typed in "Reply
/// instead" ("Cancel, and tell the model what to change"). The chat's own answer stays a bare bool.
/// </summary>
public sealed record PcConfirmReply(bool Confirmed, string? Note = null);

/// <summary>A show-window ask's subject: the window and why it would show.</summary>
public sealed record PcShowAsk(string WindowTitle, string? Why = null);

/// <summary>A provider's own question or approval, as its adapter describes it.</summary>
public sealed record PcProviderAsk(string Title, string FirstLine, bool Secret = false)
{
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>The provider asks several questions at once: its sheet in the chat answers them (the pill points there).</summary>
    public bool Several { get; init; }
}

/// <summary>A new ask before the broker numbers it.</summary>
public sealed record PcAskDraft(PcAskKind Kind, string Thread, object Payload)
{
    /// <summary>The app's key (DESIGN.md 2.1 identity), when the ask is about one app.</summary>
    public string? App { get; init; }

    /// <summary>The app's name as the card shows it.</summary>
    public string? AppName { get; init; }

    public string? Head { get; init; }

    /// <summary>The model's name ("GPT-6.1 Sol asks"); never nth's.</summary>
    public string? Model { get; init; }

    /// <summary>The chat's title, for the card's second line.</summary>
    public string? Chat { get; init; }
}

/// <summary>How an ask ended for whoever waits on it.</summary>
public sealed record PcAskOutcome(PcAskStatus Status, object? Answer = null, PcAnswerSource? Source = null, PcWithdrawal? Reason = null);

/// <summary>One pending ask (immutable; a revision is a new copy with the same id and outcome).</summary>
public sealed record PcPendingAsk(string Id, PcAskKind Kind, string Thread, object Payload, long CreatedMs, Task<PcAskOutcome> Outcome)
{
    public string? App { get; init; }

    public string? AppName { get; init; }

    public string? Head { get; init; }

    public string? Model { get; init; }

    public string? Chat { get; init; }

    public int Revision { get; init; }

    /// <summary>Shown only in the chat: a provider approval, or a question that asks for a secret (DESIGN.md 10.4).</summary>
    public bool ChatOnly => AskLifetimes.ChatOnly(Kind, Payload);

    /// <summary>The card's title ("Needs your OK. Outlook").</summary>
    public string Title => AskLifetimes.Title(this);
}
