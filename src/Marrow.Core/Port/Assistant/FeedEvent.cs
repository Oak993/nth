// PORT-ONLY: Dextra, nth's always-on assistant (DESIGN.md rev 5, 5.1; unit AS-2; ED-NEW-dextra-as2-1). No Swift twin.
// What the live event feed carries. Pure data: no I/O, no clock, no model call.
namespace Marrow.Core.Port.Assistant;

/// <summary>How much the owner needs to look at an event (5.1). Needs-you and error events are never dropped.</summary>
public enum FeedSeverity
{
    Quiet,
    Info,
    NeedsYou,
    Error,
}

/// <summary>
/// What the privacy filter let through (5.4): the words of the chat as well (Full), the chat's metadata only
/// (MetadataOnly), nothing about the chat but that it happened (Hidden), or nothing to filter because the event is
/// not about a chat (None).
/// </summary>
public enum FeedTextAccess
{
    None,
    Full,
    MetadataOnly,
    Hidden,
}

/// <summary>Where an event came from (5.1 table). The kinds after the first block are published by later units.</summary>
public enum FeedEventKind
{
    // ThreadRuntime.Handle
    TurnStarted,
    ToolStarted,
    Approval,
    Question,
    UsageLimit,
    TokenSpend,
    AgentStarted,
    AgentProgress,
    AgentFinished,
    Error,
    // AppModel.TurnFinished and ThreadNeedsAttention
    TurnFinished,
    NeedsAttention,
    // ThreadRuntime.HydraHeadFinished
    HeadFinished,
    // MultiAccountRouting
    AccountWindow,
    AccountLimitReached,
    AccountWindowReset,
    // Tactus PcChangeFeed (published when Tactus lands)
    PcAction,
    PcApproval,
    PcStop,
    // Scheduler (AS-10)
    DutyFired,
    DutySkipped,
    WatcherChanged,
    // The Assistant itself (AS-5 and later)
    MissionState,
    DecisionRaised,
    DecisionAnswered,
    GrantUsed,
    // AS-7: instructions found in untrusted content an agent read (12.4); shown and ignored.
    InstructionFound,
}

/// <summary>
/// One line of the live feed (5.1): time, kind, thread id, mission id, assistant id, a one-line summary built
/// without a model, a cost delta and a severity. <see cref="Sequence"/> counts events in the order the bus delivered
/// them. <see cref="CostDelta"/> is tokens (the unit nth's ledger and the usage windows speak in). <see cref="Count"/>
/// is how many raw events were folded into this one by coalescing.
/// </summary>
public sealed record FeedEvent(
    long Sequence,
    DateTimeOffset Time,
    FeedEventKind Kind,
    FeedSeverity Severity,
    string Summary,
    Guid? ThreadId = null,
    Guid? MissionId = null,
    Guid? AssistantId = null,
    long CostDelta = 0,
    int Count = 1,
    FeedTextAccess Text = FeedTextAccess.None);
