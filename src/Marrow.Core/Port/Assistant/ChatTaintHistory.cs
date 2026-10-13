// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.4 "Taint ... is cleared only by the owner's own message"; unit AS-22; ED-NEW-dextra-as22-7). No Swift twin.
// What an owner's chat has read since the owner last wrote in it, read from its timeline. A chat that is not Dextra's is not followed
// while it works (that would cost every tool call of every chat); when the delegate is about to answer in it, its timeline from the
// owner's last message on is placed with the same wiring a mission's chat is (TaintWiring.Of), and the union is the chat's taint. A
// sub-agent's own reads are not in a timeline, so a sub-agent counts as having read something untrusted; more calls than can be placed
// count as untrusted too. Pure: the timeline and the classifier's context come in.
using Marrow.Core.Models;

namespace Marrow.Core.Port.Assistant;

public static class ChatTaintHistory
{
    /// <summary>The most tool calls placed; a turn with more counts as having read something untrusted.</summary>
    public const int MaxCalls = 2_000;

    /// <summary>The source that stands for a sub-agent's reads, which a timeline does not show.</summary>
    public static readonly Taint SubAgent = Taint.From(ContentOrigin.ToolOutput, "a sub-agent whose reads are not in the chat's history");

    /// <summary>The source that stands for more calls than nth places.</summary>
    public static readonly Taint TooMany = Taint.From(ContentOrigin.ToolOutput, "more tool calls than nth checks");

    /// <summary>
    /// The taint of what the chat's agent has read since the owner's last message (all of the timeline when he never wrote in it).
    /// <paramref name="items"/> are oldest first.
    /// </summary>
    public static Taint Since(IReadOnlyList<TimelineItem> items, ClassifierContext context, ProviderKind provider)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(context);
        var start = 0;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].TimelineItemContent is TimelineItem.Content.User)
            {
                start = i + 1;
                break;
            }
        }
        var sources = new List<Taint>();
        var calls = 0;
        for (var i = start; i < items.Count; i++)
        {
            if (items[i].TimelineItemContent is not TimelineItem.Content.Tool { Value: var call }) continue;
            if (call.ToolCallStatus == ToolCall.Status.Declined) continue; // refused: nothing was read
            if (++calls > MaxCalls)
            {
                sources.Add(TooMany);
                break;
            }
            sources.Add(call.ToolCallKind == ToolCall.Kind.Agent ? SubAgent : TaintWiring.Of(call, context, provider));
        }
        return TaintWiring.Capped(Taint.UnionOf(sources));
    }
}
