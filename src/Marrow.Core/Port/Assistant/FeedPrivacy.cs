// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.4 Privacy across providers; unit AS-2; ED-NEW-dextra-as2-2, -3). No Swift twin.
// Decides how much of a chat's words may reach the Assistant's brain. Pure: facts in, decision out.
using Marrow.Core.Models;

namespace Marrow.Core.Port.Assistant;

/// <summary>
/// What the filter needs to know about one chat (5.4), read on the main thread when an event is tapped. The title is
/// metadata the brain may always see; everything else here only steers the decision.
/// </summary>
public sealed record FeedChatFacts(
    string Title,
    ProviderKind Provider,
    bool Hidden,
    bool IsLocalModel,
    bool IsPrivateAccount)
{
    /// <summary>How deep a chain of heads is followed to find a hidden ancestor (a loop in the data cannot hang it).</summary>
    public const int MaxAncestors = 16;

    /// <summary>
    /// The facts of a chat. Rules of ED-NEW-dextra-as3a-1 item 5: a chat with valid MissionID/AssistantID marks is
    /// the Assistant's own work and is readable whatever HiddenFromAssistant says ("mission chats cannot be hidden");
    /// any other chat is hidden when its own mark is set or when an ancestor in its ParentThreadID chain is hidden, so
    /// a chat hidden after its heads were made hides them as well. A chat on the Local provider keeps its words on
    /// this PC, so it counts as a local model. <paramref name="isPrivateAccount"/> is the owner's "Private account"
    /// mark on the account a chat runs on (not in the app yet; the default says no).
    /// </summary>
    public static FeedChatFacts From(ChatThread thread, Func<Guid, ChatThread?> lookup, Func<ChatThread, bool>? isPrivateAccount = null)
    {
        var hidden = !IsAssistantWork(thread) && (thread.HiddenFromAssistant || AncestorHidden(thread, lookup));
        return new FeedChatFacts(thread.Title, thread.Provider, hidden, thread.Provider == ProviderKind.Local,
            isPrivateAccount?.Invoke(thread) == true);
    }

    // Guid.Empty is the decoder's damaged-key sentinel, never proof that the Assistant owns a chat.
    // Any damaged mark fails closed, even beside a valid mark (ED-NEW-dextra-cx-2).
    private static bool IsAssistantWork(ChatThread thread) =>
        thread.MissionID != Guid.Empty && thread.AssistantID != Guid.Empty
        && (thread.MissionID is not null || thread.AssistantID is not null);

    private static bool AncestorHidden(ChatThread thread, Func<Guid, ChatThread?> lookup)
    {
        var seen = new HashSet<Guid> { thread.Id };
        var current = thread;
        for (var depth = 0; depth < MaxAncestors; depth++)
        {
            if (current.ParentThreadID is not { } parentId || !seen.Add(parentId)) return false;
            if (lookup(parentId) is not { } parent) return false;
            if (!IsAssistantWork(parent) && parent.HiddenFromAssistant) return true;
            current = parent;
        }
        return false;
    }
}

/// <summary>
/// The brain's side of the decision: the provider it runs on (null until the Assistant has a brain, which fails
/// closed), whether it runs on this PC, and the owner's two switches from the first-run card (2.6): "Summaries of
/// chats on the same provider" (on by default) and "Also chats on other providers" (off by default).
/// </summary>
public sealed record FeedBrain(
    ProviderKind? Provider,
    bool RunsLocally = false,
    bool SameProviderSummaries = true,
    bool AlsoOtherProviders = false)
{
    /// <summary>No brain is set up yet: no chat's words are passed on.</summary>
    public static FeedBrain Unknown { get; } = new((ProviderKind?)null);
}

/// <summary>The rules of 5.4 as one function.</summary>
public static class FeedPrivacy
{
    /// <summary>
    /// How much of a chat the brain may see.
    /// 1. A hidden chat sends nothing.
    /// 2. With the owner's summaries switch off, nothing but metadata (title, project, status, cost) is passed on.
    /// 3. A chat on a local model or a private account is passed on only to a brain that runs on this PC, whatever
    ///    the cross-provider switch says.
    /// 4. A brain that is not set up yet sees metadata only.
    /// 5. A chat on the brain's own provider is passed on; one on another provider only when the owner allowed it.
    /// </summary>
    public static FeedTextAccess Decide(FeedChatFacts chat, FeedBrain brain)
    {
        if (chat.Hidden) return FeedTextAccess.Hidden;
        if (!brain.SameProviderSummaries) return FeedTextAccess.MetadataOnly;
        if (chat.IsLocalModel || chat.IsPrivateAccount) return brain.RunsLocally ? FeedTextAccess.Full : FeedTextAccess.MetadataOnly;
        if (brain.Provider is not { } brainProvider) return FeedTextAccess.MetadataOnly;
        if (chat.Provider == brainProvider) return FeedTextAccess.Full;
        return brain.AlsoOtherProviders ? FeedTextAccess.Full : FeedTextAccess.MetadataOnly;
    }
}
