// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3, 15.3; unit AS-22; ED-NEW-dextra-as22-8). No Swift twin.
// Beside the other documents at the store's root, the delegate's answers: what it answered for the owner and what he corrected
// (delegate-answers.json), written the same temp-then-rename way.
using System.Text.Json.Nodes;

namespace Marrow.Services.Assistant;

public sealed partial class AssistantStore
{
    public const string DelegateAnswersFile = "delegate-answers.json";

    /// <summary>What the delegate answered for the owner (<c>delegate-answers.json</c>, AS-22), or null when there is none or it does not parse.</summary>
    public JsonObject? DelegateAnswers() => ReadDocument(Path.Combine(Root, DelegateAnswersFile)) as JsonObject;

    public void SaveDelegateAnswers(JsonObject document) => SaveDocument(Path.Combine(Root, DelegateAnswersFile), document);
}
