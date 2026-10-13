// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.2, 8.3, 15.5; unit AS-12; ED-NEW-dextra-as12-19). No Swift twin.
// Dextra's memory on the controller: the notes a home chat is told at its start, the owner's words kept for the third-party rule,
// the capture after a home turn (the cheap model, in the background), the day's folding, and the card a tainted chat's forget raises
// (it shows which notes, not only their ids). The tools themselves are MemoryTools; this is only what ties them to the app.
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class AssistantController
{
    private readonly CancellationTokenSource memoryStop = new();
    private MemoryTools? memoryTools;

    /// <summary>Dextra's memory (null when the controller was started without it).</summary>
    public MemorySetup? Notes => o.Notes;

    private MemoryTools Tools
    {
        get
        {
            lock (gate) return memoryTools ??= new MemoryTools(o.Notes!.Store, o.Notes.Owner);
        }
    }

    /// <summary>The notes a chat is told at its start (IAssistantToolHost): the home chat's, never a mission agent's.</summary>
    public string? MemoryBlock(AssistantChat chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        return o.Notes is { } notes && chat.Role == AssistantChatRole.Home ? notes.Store.PromptBlock(chat.AssistantId) : null;
    }

    /// <summary>
    /// The owner said something in a home chat (typed or spoken, never a tool result or a pasted page): kept in memory only, as the
    /// evidence for a note about someone else (8.2). The home calls this with each message the owner sends.
    /// </summary>
    public void OwnerSaid(Guid thread, string? text) => o.Notes?.Owner.Said(thread, text);

    /// <summary>
    /// A turn of a home chat finished: what the owner said is kept for the third-party rule, and when a capture is bound the cheap model
    /// proposes notes from the turn in the background, with the chat's taint (a turn that read the web writes quarantined notes) and
    /// the provider the chat ran on. The day's folding runs after the first capture of a local day. Nothing here waits or throws into
    /// the caller; a failure is audited. A chat that is hidden from Dextra, or is a mission's, is never captured.
    /// </summary>
    public void HomeTurnFinished(Guid thread, string ownerText, string replyText, string turnId)
    {
        if (o.Notes is not { } notes) return;
        notes.Owner.Said(thread, ownerText);
        if (notes.Capture is not { Active: true } capture) return;
        if (o.Thread(thread) is not { AssistantID: { } assistant, MissionID: null, HiddenFromAssistant: false } chat) return;
        var turn = new CapturedTurn(assistant.ToString("N"), thread, chat.Title, turnId, ownerText, replyText, HomeTaint(thread), notes.Owner.Recent(thread))
        {
            Provider = chat.Provider,
            Model = chat.Model,
        };
        _ = Task.Run(async () =>
        {
            try
            {
                await capture.CaptureAsync(turn, memoryStop.Token).ConfigureAwait(false);
                notes.Store.FoldIfDue();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException or JsonException)
            {
                TryAudit(new AuditEntry { Assistant = turn.AssistantId, Tool = "nth-dextra/memory-capture", ActionClass = "Work", Result = "failed: " + error.GetType().Name });
            }
        });
    }

    // The card of a forget the owner is asked about: the notes it would delete, by an excerpt and the id, so he sees what is going.
    private IEnumerable<DecisionInput>? MemoryCardInputs(AssistantToolCall call)
    {
        if (o.Notes is not { } notes || call.Tool.Name != "forget") return null;
        var ids = call.Arguments["notes"]?.JSONValueArray is { } items ? items.Select(i => i.JSONValueString).OfType<string>().ToList() : [];
        var lines = ids.Take(50).Select(id => notes.Store.Find(id) is { } note
            ? "“" + (note.Text.Length > 60 ? note.Text[..60] + "…" : note.Text) + "” (" + id + ")"
            : id + " (no such note)");
        return [new DecisionInput("notes", string.Join('\n', lines))];
    }

    private void DisposeMemory()
    {
        memoryStop.Cancel();
        o.Notes?.Owned?.Dispose();
        memoryStop.Dispose();
    }
}
