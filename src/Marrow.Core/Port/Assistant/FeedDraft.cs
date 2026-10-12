// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1; unit AS-2; ED-NEW-dextra-as2-1, -5). No Swift twin.
// What a tap hands the bus: the facts of the event before the privacy filter has had its say. A draft is cheap to make
// (a few references and numbers), so a tap on the main thread does no formatting; the bus builds the line later, off
// that thread, with FeedBuilder.
namespace Marrow.Core.Port.Assistant;

/// <summary>An event as a tap saw it, before the privacy filter and the one-line summary.</summary>
public sealed record FeedDraft(FeedEventKind Kind, FeedSeverity Severity, DateTimeOffset Time, string Lead)
{
    /// <summary>The chat the event is about. Null for an event that is not about a chat (an account, a duty, a mission).</summary>
    public Guid? ThreadId { get; init; }
    public Guid? MissionId { get; init; }
    public Guid? AssistantId { get; init; }

    /// <summary>Facts about that chat for the privacy filter. Null: nothing to filter, <see cref="Lead"/> is the whole line.</summary>
    public FeedChatFacts? Chat { get; init; }

    /// <summary>Words that came from a chat (a reply, an error, a head's summary, a question). Shown only when the filter allows it.</summary>
    public string? Detail { get; init; }

    /// <summary>Tokens spent (the cost delta of 5.1).</summary>
    public long CostDelta { get; init; }

    /// <summary>How many raw events this draft stands for (coalescing adds).</summary>
    public int Count { get; init; } = 1;

    /// <summary>Which stream of a thread this draft belongs to when the thread has several (a head's id for its progress).</summary>
    public string? Stream { get; init; }

    /// <summary>
    /// A draft for an event that is not about a chat (the Assistant's own missions and decisions, a duty, a watcher, a PC
    /// action from Tactus): <paramref name="line"/> is the whole summary and nothing is filtered but the shield.
    /// </summary>
    public static FeedDraft About(FeedEventKind kind, FeedSeverity severity, DateTimeOffset time, string line,
        Guid? mission = null, Guid? assistant = null, Guid? thread = null) =>
        new(kind, severity, time, line) { MissionId = mission, AssistantId = assistant, ThreadId = thread };

    /// <summary>Token and progress events fold into one per thread and stream for 2 seconds (5.1).</summary>
    public bool Coalesces => Kind is FeedEventKind.TokenSpend or FeedEventKind.AgentProgress;

    /// <summary>Whether this kind of event is kept when a chat is hidden: only that a turn ended or the owner is needed.</summary>
    public bool SurvivesHiding => Kind is FeedEventKind.TurnFinished or FeedEventKind.Approval or FeedEventKind.Question or FeedEventKind.NeedsAttention;

    /// <summary>
    /// The draft that stands for this one and a later one of the same thread, kind and stream. Tokens add up. Progress
    /// keeps the newest note, because it reports where a head is now (its own counters are running totals).
    /// </summary>
    public FeedDraft Merge(FeedDraft later) => Kind switch
    {
        FeedEventKind.TokenSpend => this with { CostDelta = checked(CostDelta + later.CostDelta), Count = checked(Count + later.Count) },
        FeedEventKind.AgentProgress => later with { Time = Time, Count = checked(Count + later.Count) },
        _ => this,
    };
}
