// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime; work/ignis/PLAN.md unit U3). Additive to the U0 contract
// (IgnisContract.cs): the questions the per-chat controller asks the engine (unit U1 implements IIgnisEngine), and the
// seams the app reads the engine, the model points and the savings ledger from. Every seam is unbound (null) until the
// app binds the concrete U1 parts; while any of the three is unbound the controller is not created and Ignis is inert.
using Marrow.Core.Models;

namespace Marrow.Core.Port.Ignis;

/// <summary>
/// One keep-warm decision: a chat between tasks, its cache about to go cold. The engine pings when
/// P(the user returns within the cache's lifetime) × (rebuild − read) > the ping's cost (PLAN unit U1).
/// </summary>
/// <param name="IdleGaps">The chat's own past gaps between a turn's end and the next user turn, newest last (the history the probability starts from).</param>
/// <param name="HasDraft">The composer holds unsent text (raises the probability).</param>
/// <param name="OnScreen">The chat is on screen in the active app (raises the probability).</param>
public sealed record IgnisKeepWarmQuestion(
    Guid ThreadID,
    ProviderKind Provider,
    IgnisModelPoints Points,
    long ContextTokens,
    TimeSpan Ttl,
    TimeSpan IdleFor,
    TimeSpan TimeLeft,
    IReadOnlyList<TimeSpan> IdleGaps,
    bool HasDraft,
    bool OnScreen)
{
    /// <summary>Every chat's past idle gaps for the provider (ED-NEW-ignis-guards): the odds lean on them where the chat has few.</summary>
    public IReadOnlyList<TimeSpan> GlobalGaps { get; init; } = [];
}

/// <summary>The engine's maths and decisions the controller needs (unit U1). Pure: no I/O, no clock reads.</summary>
public interface IIgnisEngine
{
    /// <summary>The tidy-up point in tokens: just under a price step, else the break-even point; the points' tidyAt overrides. Null: none.</summary>
    long? TidyPoint(IgnisModelPoints points);

    /// <summary>Whether to keep the cache warm now.</summary>
    bool ShouldKeepWarm(IgnisKeepWarmQuestion question);

    /// <summary>Dollars this turn's cache reads saved over fresh input (API-equivalent dollars on plans); 0 when not reported.</summary>
    decimal ReuseSaved(IgnisModelPoints points, IgnisTurnUsage usage);

    /// <summary>What rebuilding <paramref name="contextTokens"/> cold costs more than reading them warm: the loss when the cache goes cold, and what a ping saves.</summary>
    decimal RebuildPremium(IgnisModelPoints points, long contextTokens);

    /// <summary>What one keep-warm request over <paramref name="contextTokens"/> costs.</summary>
    decimal PingCost(IgnisModelPoints points, long contextTokens);

    /// <summary>What a tidy-up from <paramref name="beforeTokens"/> to <paramref name="afterTokens"/> saves (the shorter chat's next rebuild and reads).</summary>
    decimal TidySaved(IgnisModelPoints points, long beforeTokens, long afterTokens);

    /// <summary>The rebuild premium at a known cache lifetime (a helper's 5-minute writes price differently from the chat's 1-hour ones).</summary>
    decimal RebuildPremium(IgnisModelPoints points, long contextTokens, TimeSpan? ttl) => RebuildPremium(points, contextTokens);

    /// <summary>
    /// What a request saved because nth lengthened its cache bucket to <paramref name="lifetime"/>: after a gap over 5 minutes
    /// (and within the lifetime) it read the prefix a 5-minute cache would have rebuilt (ED-NEW-ignis-runtime).
    /// </summary>
    decimal LongerMemorySaved(IgnisModelPoints points, long prefixTokens, long readTokens, TimeSpan gap, TimeSpan lifetime) =>
        gap > TimeSpan.FromMinutes(5) && gap <= lifetime && prefixTokens > 0 && readTokens > 0 ? RebuildPremium(points, Math.Min(prefixTokens, readTokens), TimeSpan.FromMinutes(5)) : 0m;

    /// <summary>What nth's longer lifetime added to a request: its 1-hour writes priced over the 5-minute ones.</summary>
    decimal LongerMemoryCost(IgnisModelPoints points, long oneHourWrites, long promptTokens) => 0m;
}

/// <summary>
/// What the controller needs from a chat's runtime beyond <see cref="IIgnisRuntimePort"/> (ThreadRuntime implements both):
/// whether a silent tidy-up is still running, and the quiet timeline event line.
/// </summary>
public interface IIgnisRuntimeTimeline
{
    /// <summary>A silent tidy-up was asked for and has not ended yet.</summary>
    bool IsTidying { get; }

    /// <summary>The last silent tidy-up ended without tidying anything (failed, or nothing to tidy): no "Tidied up" line.</summary>
    bool LastTidyFailed => false;

    /// <summary>Appends the event's quiet line: an info notice whose item id is <see cref="IgnisEvent.TimelineItemID"/>.</summary>
    void AppendEvent(IgnisEvent @event);
}

/// <summary>Where the app finds the U1 parts. Program binds them once (IgnisRuntimeStartup); tests override per flow.</summary>
public static class IgnisRuntimeSeams
{
    public static Ambient<Func<IIgnisEngine?>> Engine { get; } = new(new Func<IIgnisEngine?>(() => null));

    public static Ambient<Func<IIgnisPoints?>> Points { get; } = new(new Func<IIgnisPoints?>(() => null));

    public static Ambient<Func<ISavingsLedger?>> Ledger { get; } = new(new Func<ISavingsLedger?>(() => null));
}
