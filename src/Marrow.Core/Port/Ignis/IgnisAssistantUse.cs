// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Ignis"; unit AS-15; ED-NEW-dextra-as15-7). No Swift twin.
// What Dextra makes of a chat, as Ignis needs to know it: the home thread is the owner's own conversation with the assistant, and a
// mission's chat belongs to a task that is still going. Ignis compacts only between tasks, never keeps the home thread warm, and leaves
// a mission's chats alone mid-task.
namespace Marrow.Core.Port.Ignis;

public enum IgnisAssistantUse
{
    /// <summary>An ordinary chat (every chat there was before Dextra): Ignis works as it always has.</summary>
    None,

    /// <summary>Dextra's or an Astur's home thread: it may be tidied between tasks, and is never kept warm, by a ping or silently.</summary>
    HomeThread,

    /// <summary>A chat of a mission that is not done (a lead, a head, a helper, the reviewer): no keep-warm and no tidy-up.</summary>
    MissionMidTask,
}
