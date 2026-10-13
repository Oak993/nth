// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3, 5.4, 12.4; unit AS-22; ED-NEW-dextra-as22-10). No Swift twin.
// What the delegate responder is given by the app: the model that reads a question (a seam, so the tests ask a fake and nothing here ever calls a
// provider), the context it reads about the project and the chat, and the engagement that says where it answers and as whom.
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>
/// One ready call to the delegate's model: the provider it really runs on (for the privacy rules of 5.4: another provider reads a chat's words only
/// when the owner allowed it; a local-model chat never reaches a cloud one), and the call itself, which may run on any thread.
/// </summary>
public sealed record DelegateCall(ProviderKind Provider, Func<string, CancellationToken, Task<string?>> Ask);

/// <summary>
/// The model behind the delegate. It sees only the prompt nth builds (data fenced, bounded) and has no tools; what it answers is read by
/// <see cref="DelegateReply.Parse"/> and judged by <see cref="DelegatePolicy"/>, so a model that is wrong or hostile can only pick among the
/// choices the asking agent offered.
/// </summary>
public interface IDelegateModel
{
    /// <summary>
    /// Readies a call on the main thread (what the app's settings and sign-ins say is read here, once), or null when no model can be asked. The
    /// call throws only for cancellation or a fault; the caller treats both as "no answer".
    /// </summary>
    DelegateCall? Prepare(DelegateWho? who);
}

/// <summary>What the delegate reads of a chat: its title, project and folder, and its last turns (words only; the owner's are marked).</summary>
public sealed record DelegateChatView(Guid Thread, string Title, Guid Project, string? Folder, IReadOnlyList<DelegateTurn> Recent);

/// <summary>Where the delegate gets its context (4.3: "the owner's decisions log, briefs, past answers and the code").</summary>
public interface IDelegateContext
{
    /// <summary>The chat as it is now. Main thread only, cheap; null when the chat is gone.</summary>
    DelegateChatView? View(Guid thread);

    /// <summary>The project's decisions, the past answers and the chat's turns for one question. Reads files, so never on the main thread.</summary>
    Task<DelegatePack> Pack(DelegateChatView view, QuestionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Where the delegate answers and as whom: a chat that is not Dextra's own, not hidden from it, whose project (or whose own menu) has the delegate
/// on. <see cref="Who"/> is the name the label shows ("answered by ⟨head⟩ for you").
/// </summary>
/// <param name="Chat">The chat that asked.</param>
/// <param name="Title">Its title, as the card and the label say it.</param>
/// <param name="Who">The delegate's name: Dextra, or the provider whose model answers.</param>
/// <param name="WhoModel">Which model answers; null is Dextra's own brain.</param>
/// <param name="IsMission">The chat belongs to one of Dextra's missions: its questions go back to the mission runner when the delegate leaves them, and its approvals are the runner's.</param>
/// <param name="Facts">What the privacy filter needs to know about the chat.</param>
public sealed record DelegateEngagement(ChatThread Chat, string Title, string Who, DelegateWho? WhoModel, bool IsMission, FeedChatFacts Facts)
{
    public Guid Thread => Chat.Id;
}
