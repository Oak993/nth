// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3 "Shown and reversible: ... has an Override button. An override is sent to the asking session as a correction and is
// saved as a past answer, so the delegate learns from it"; unit AS-22; ED-NEW-dextra-as22-11). No Swift twin.
// The owner's correction of an answer given for him. It is checked (something to say, not too long, one correction for an answer), logged first (his words as
// their length only, 12.5), sent to the chat that asked as a message of his own (now when the chat is idle, else when its turn ends), and kept with the answer so
// the next question of the project is given it before anything else.
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>How an Override ended: kept with the answer, and whether it also reached the chat. <see cref="Problem"/> says why not when it was not kept.</summary>
public sealed record DelegateOverrideResult(bool Saved, bool Sent, string? Problem)
{
    public static DelegateOverrideResult Refused(string problem) => new(false, false, problem);
}

public sealed partial class DelegateResponder
{
    public const string NothingToSay = "Say what it should have been.";
    public const string AlreadyCorrected = "You already corrected this answer.";
    public const string NotOnRecord = "That answer is not on record any more.";
    public const string NotLoggedOverride = "The activity log could not be written, so the correction was not sent.";

    public static string TooLong => $"Keep it under {DelegateWords.CorrectionChars} characters.";

    /// <summary>Sends the owner's correction of the answer <paramref name="answer"/> to the chat that asked, and saves it as a past answer.</summary>
    public DelegateOverrideResult Override(Guid answer, string? correction)
    {
        var text = correction?.Trim() ?? "";
        if (text.Length == 0) return DelegateOverrideResult.Refused(NothingToSay);
        if (text.Length > DelegateWords.CorrectionChars) return DelegateOverrideResult.Refused(TooLong);
        if (o.Book.Find(answer) is not { } record) return DelegateOverrideResult.Refused(NotOnRecord);
        if (record.Corrected) return DelegateOverrideResult.Refused(AlreadyCorrected);
        if (!LogOverride(record, text)) return DelegateOverrideResult.Refused(NotLoggedOverride);
        var chat = o.ChatExists(record.Chat);
        if (chat) o.Deliver(record.Chat, DelegateWords.Correction(record.Question, record.Answer, text));
        // saved either way: a chat that is gone cannot be told, but the next question of the project still learns from it
        return o.Book.Correct(answer, text, o.Time.GetUtcNow(), chat) is null
            ? DelegateOverrideResult.Refused(AlreadyCorrected)
            : new DelegateOverrideResult(true, chat, null);
    }

    // His words are logged as their length only (12.5 "Typed text is logged as its length only").
    private bool LogOverride(DelegateRecord record, string text)
    {
        try
        {
            o.Audit(new AuditEntry
            {
                Assistant = o.AssistantId(),
                Agent = record.Chat.ToString("D"),
                Tool = "delegate_override",
                ActionClass = ActionClass.Agents.ToString(),
                Target = Masked(record.ChatTitle, 120),
                Inputs = [AuditInput.Typed("correction", text), Plain("answer", record.Answer, 300)],
                Result = "the owner corrected an answer given for him",
                AllowedBy = "delegate:" + record.Id.ToString("D"),
            });
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Fault("The activity log could not be written: " + error.Message);
            return false;
        }
    }
}
