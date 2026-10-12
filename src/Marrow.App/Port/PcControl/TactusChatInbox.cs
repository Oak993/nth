// PORT-ONLY: Tactus, the smallest seam into a chat (ED-NEW-pcctl-39; ED-NEW-pcctl-32 item 9; DESIGN.md 10.4 "'Open chat'
// opens it at the ask with the draft carried over", 10.7 "Answered in the chat. Your draft is still here." and "The model
// stopped waiting for this." with "Send to chat"). No Swift twin. Open chat puts the card's draft in the chat's composer,
// under what the user already typed there (never over it, and not twice); Send to chat sends it as the user's next message
// through the chat's own follow-up queue (ThreadRuntime.EnqueueFollowUp, ThreadRuntime.swift:991-1008): at once when the
// chat is idle, after the running turn otherwise, and the user's composer is left as it was. On the main actor, where the
// chat's runtime lives (the pill's card answers there).
using Marrow.App.Runtime;

namespace Marrow.App.Port.PcControl;

/// <summary>What Tactus may do in a chat: put the card's draft in its composer, or send it as the user's message.</summary>
public interface ITactusChatInbox
{
    /// <summary>Open chat: <paramref name="draft"/> into the chat's composer. False with no such chat or nothing to carry.</summary>
    bool Carry(string thread, string draft);

    /// <summary>Send to chat: <paramref name="text"/> as the user's next message. False when it did not go (no such chat,
    /// the chat reverting, nothing to send).</summary>
    bool Send(string thread, string text);
}

/// <summary>The chat inbox over the app's chats (<paramref name="chats"/>: a chat's runtime by id, null when it is gone).</summary>
public sealed class TactusChatInbox(Func<Guid, ThreadRuntime?> chats) : ITactusChatInbox
{
    public bool Carry(string thread, string draft)
    {
        var text = draft.Trim();
        if (text.Length == 0 || Chat(thread) is not { } chat) return false;
        chat.Draft = chat.Draft with { Text = Carried(chat.Draft.Text, text) };
        return true;
    }

    public bool Send(string thread, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Chat(thread) is not { IsReverting: false } chat) return false;
        chat.EnqueueFollowUp(text, []);
        return true;
    }

    /// <summary>
    /// The composer's text with the draft under it: the draft alone in an empty composer, after a blank line under the
    /// user's own text, and the text unchanged when it already ends with the draft (Open chat pressed again).
    /// </summary>
    public static string Carried(string composer, string draft)
    {
        var mine = composer.TrimEnd();
        if (mine.Trim().Length == 0) return draft;
        return mine.EndsWith(draft, StringComparison.Ordinal) ? composer : mine + "\n\n" + draft;
    }

    private ThreadRuntime? Chat(string thread) => Guid.TryParse(thread, out var id) ? chats(id) : null;
}
