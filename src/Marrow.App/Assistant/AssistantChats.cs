// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.1: mission chats "are not listed in the day groups, so the sidebar does not
// flood"; the home thread lives behind the Assistant row; ED-NEW-dextra-as8-8). No Swift twin.
// Which chats are the Assistant's own (its home thread, its missions' lead chats and heads), and the sidebar's list without them.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;

namespace Marrow.App.Assistant;

public static class AssistantChats
{
    /// <summary>
    /// Whether a chat is the Assistant's own: it carries a valid AssistantID or MissionID. Guid.Empty is the decoder's damaged-key
    /// sentinel and never counts, so a damaged chat stays visible in the day groups.
    /// </summary>
    public static bool IsAssistants(ChatThread thread) =>
        thread.AssistantID is { } assistant && assistant != Guid.Empty || thread.MissionID is { } mission && mission != Guid.Empty;

    /// <summary>The chats the day groups list: the same list when none is the Assistant's (no copy), else the others.</summary>
    public static IReadOnlyList<ChatThread> Listed(IReadOnlyList<ChatThread> threads)
    {
        ArgumentNullException.ThrowIfNull(threads);
        for (var i = 0; i < threads.Count; i++)
        {
            if (IsAssistants(threads[i])) return threads.Where(t => !IsAssistants(t)).ToArray();
        }
        return threads;
    }

    /// <summary>
    /// A chat's "Hide from the Assistant" (5.4), or "Show to the Assistant" once hidden: offered while Dextra runs (on, first run
    /// done), never on the Assistant's own chats. Null when it is not offered, so the menu is the upstream one otherwise.
    /// </summary>
    internal static RowAction? HideAction(AppModel model, ChatThread thread)
    {
        if (IsAssistants(thread) || !model.AssistantHost.MayRun) return null;
        var id = thread.Id;
        var hidden = thread.HiddenFromAssistant;
        var weak = new WeakReference<AppModel>(model);
        return new RowAction
        {
            Title = hidden ? "Show to the Assistant" : "Hide from the Assistant",
            Symbol = hidden ? "eye" : "eye.slash",
            Action = () => { if (weak.TryGetTarget(out var live)) live.SetHiddenFromAssistant(id, !hidden); },
        };
    }

    /// <summary>
    /// A chat's "Answer questions for me" (4.3: "the chat's menu"), or "Stop answering for me" once the delegate answers in it: offered while Dextra runs, never
    /// on the Assistant's own chats, on a chat the owner hid from it, or on a head (it follows the chat it belongs to). Choosing it sets the chat's own choice
    /// (which beats the scope); choosing what the scope already says clears the choice again. Null when it is not offered.
    /// </summary>
    internal static RowAction? DelegateAction(AppModel model, ChatThread thread)
    {
        if (IsAssistants(thread) || thread.HiddenFromAssistant || thread.ParentThreadID is not null || !model.AssistantHost.MayRun) return null;
        var settings = model.AssistantHost.Settings;
        var home = model.DextraHomeProject();
        var on = DelegateReach.IsOn(settings.Value, thread.Id, thread.ProjectID, home);
        var id = thread.Id;
        var project = thread.ProjectID;
        return new RowAction
        {
            Title = DelegateWords.MenuTitle(on),
            Symbol = on ? "hand.raised.slash" : "questionmark.bubble",
            Action = () =>
            {
                var scopeSays = DelegateReach.IsOn(settings.Value with { DelegateChats = ValueList<DelegateChatChoice>.Empty }, id, project, home);
                settings.SetDelegateChat(id, on == scopeSays ? !on : null);
            },
        };
    }
}
