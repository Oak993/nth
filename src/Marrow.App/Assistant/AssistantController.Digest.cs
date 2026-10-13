// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.5; unit AS-11; ED-NEW-dextra-as11-9). No Swift twin.
// The activity line the morning digest writes for what it does (12.5: every action is written down): a digest composed, a suggestion accepted or dismissed, a duty made
// or changed from one. A line holds the kind and counts, never a title, a finding or a line of a suggestion: those are data from chats and pages.
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class AssistantController
{
    /// <summary>Writes one activity line for something the digest did. A line the log cannot take is reported on the board and the digest goes on.</summary>
    public void AuditDigest(string? assistantId, string action, string actionClass, string result) =>
        TryAudit(new AuditEntry { Assistant = assistantId ?? MainAssistantId ?? "dextra", Tool = "nth-dextra/digest-" + action, ActionClass = actionClass, Result = result });
}
