// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.2, 8.3, 15.5; unit AS-12; ED-NEW-dextra-as12-17). No Swift twin.
// The three memory tools of nth-dextra (remember, recall, forget) on the memory store. The policy engine has already decided the
// call (AssistantToolRunner); what is left here is the work and the words the model reads back. A note written from a tainted
// chat is quarantined by the store, a note about a third party is kept only in words the owner used, and recall never returns a
// note that waits for the owner.
using System.Text;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>What the owner said recently in each home chat, kept in memory only: the evidence the third-party rule checks a note against.</summary>
public sealed class OwnerWordsLog
{
    public const int MessagesPerChat = 6;
    public const int MaxMessageChars = 2000;
    public const int MaxChats = 64;

    private readonly Dictionary<Guid, Queue<string>> chats = [];
    private readonly Queue<Guid> order = new();
    private readonly object gate = new();

    /// <summary>The owner said something in this chat (his own typed or spoken words, never a tool result or a pasted page).</summary>
    public void Said(Guid chat, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (gate)
        {
            if (!chats.TryGetValue(chat, out var messages))
            {
                chats[chat] = messages = new Queue<string>();
                order.Enqueue(chat);
                while (order.Count > MaxChats) chats.Remove(order.Dequeue());
            }
            messages.Enqueue(text.Length > MaxMessageChars ? text[..MaxMessageChars] : text);
            while (messages.Count > MessagesPerChat) messages.Dequeue();
        }
    }

    /// <summary>His recent words in the chat, oldest first, or null when there are none.</summary>
    public string? Recent(Guid chat)
    {
        lock (gate) return chats.TryGetValue(chat, out var messages) && messages.Count > 0 ? string.Join('\n', messages) : null;
    }
}

public sealed class MemoryTools(MemoryStore store, OwnerWordsLog? owner = null)
{
    public const int DefaultRecall = 10;

    public AssistantToolResult Remember(AssistantToolCall call, TaintContext taint)
    {
        ArgumentNullException.ThrowIfNull(call);
        var args = call.Arguments;
        var text = args["note"]?.JSONValueString;
        if (string.IsNullOrWhiteSpace(text)) return AssistantToolResult.Failed("Not kept: the note is empty.");
        var chat = call.Chat;
        if (!AssistantStore.IsId(chat.AssistantId)) return AssistantToolResult.Failed("Not kept: this chat has no memory of its own.");
        // The engine's decision is the rule (12.4): a call it marked for quarantine is tainted, whatever this chat's own record says.
        var context = call.Quarantine && !taint.IsTainted ? new TaintContext(Taint.From(ContentOrigin.ToolOutput, "this chat read untrusted content")) : taint;
        var kind = MemoryRules.GuessKind(text, NoteKind.Fact);
        var detail = args["source"]?.JSONValueString;
        var source = chat.MissionId is { } mission
            ? new NoteSource(NoteSourceKind.Mission, "mission:" + mission.ToString("N"), Detail: detail)
            : new NoteSource(NoteSourceKind.Chat, "chat:" + chat.Thread.ToString("N"), Detail: detail);
        var outcome = store.Propose(new NoteProposal(text, kind, kind == NoteKind.Preference ? MemoryScope.Shared : chat.AssistantId, source)
        {
            Topic = args["about"]?.JSONValueString,
            Author = NoteAuthor.Assistant,
            AuthorName = chat.AssistantId,
            Context = context,
            OwnerWords = owner?.Recent(chat.Thread),
        });
        var id = outcome.Note is { } note ? $" (note {note.Id})" : "";
        return outcome.Written ? AssistantToolResult.Done(outcome.Message + id) : AssistantToolResult.Failed(outcome.Message);
    }

    public AssistantToolResult Recall(AssistantToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var args = call.Arguments;
        var limit = args["limit"] is { } v ? (int)Math.Clamp(v.JSONValueInt ?? (long?)v.JSONValueDouble ?? DefaultRecall, 1, 50) : DefaultRecall;
        var found = store.Recall(call.Chat.AssistantId, args["query"]?.JSONValueString, limit);
        if (found.Count == 0) return AssistantToolResult.Done("Nothing is remembered about that.");
        var text = new StringBuilder();
        text.Append(found.Count == 1 ? "1 note:" : $"{found.Count} notes:");
        foreach (var hit in found)
        {
            var note = hit.Note;
            text.Append("\n- [").Append(note.Id).Append("] ").Append(note.Text).Append(" (").Append(MemoryRules.KindWord(note.Kind));
            if (note.Kind == NoteKind.Fact && note.MayChange) text.Append(" · may have changed: check before relying on it");
            text.Append(" · ").Append(MemoryWords.Source(note)).Append(')');
        }
        text.Append("\nWhat the owner says now beats a note.");
        return AssistantToolResult.Done(text.ToString());
    }

    public AssistantToolResult Forget(AssistantToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var ids = call.Arguments["notes"]?.JSONValueArray is { } items ? items.Select(i => i.JSONValueString).OfType<string>().ToList() : [];
        if (ids.Count == 0) return AssistantToolResult.Failed("Nothing to forget: give the notes' ids from recall.");
        var report = store.Delete(ids, call.Chat.AssistantId);
        if (report.Failed) return AssistantToolResult.Failed("Not forgotten: the memory files could not be written. Nothing changed.");
        var missing = ids.Distinct(StringComparer.Ordinal).Count() - report.Removed;
        return AssistantToolResult.Done(report.Removed == 0 ? "No note with those ids is one you can read."
            : $"Forgot {report.Removed} {(report.Removed == 1 ? "note" : "notes")}, for real." + (missing > 0 ? $" {missing} ids matched no note you can read." : ""));
    }
}
