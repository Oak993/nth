// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine, ED-NEW-ignis-runtime; PLAN units U1 + U3, lead integration). The
// engine as the per-chat controller asks it (IIgnisEngine): a thin, pure adapter over the U1 maths (IgnisMoney,
// IgnisTidyPoints, IgnisKeepWarm). No I/O and no clock reads; the controller owns time and state.
namespace Marrow.Core.Port.Ignis.Engine;

public sealed class IgnisEngine : IIgnisEngine
{
    public static IgnisEngine Shared { get; } = new();

    public long? TidyPoint(IgnisModelPoints points) => IgnisTidyPoints.For(points, IgnisKeepWarm.TtlFromPoints(points)).Tokens is var at && at > 0 ? at : null;

    /// <summary>
    /// P(the user returns within the cache's lifetime) × (rebuild − read) &gt; the ping's cost. The odds start from the
    /// chat's own idle gaps (the built-in prior where it has few), raised by a draft or the chat on screen.
    /// </summary>
    public bool ShouldKeepWarm(IgnisKeepWarmQuestion question)
    {
        if (!question.Points.Cache.KeepWarm || question.Ttl <= TimeSpan.Zero || question.TimeLeft <= TimeSpan.Zero) return false;
        var odds = IgnisKeepWarm.ReturnOdds(question.IdleGaps, question.GlobalGaps, question.IdleFor, question.Ttl, question.HasDraft, question.OnScreen);
        var (benefit, cost) = IgnisKeepWarm.Worth(question.Points, question.ContextTokens > 0 ? question.ContextTokens : null, question.Ttl, odds);
        return benefit > cost;
    }

    public decimal ReuseSaved(IgnisModelPoints points, IgnisTurnUsage usage) => IgnisMoney.ReuseSaving(points, usage);

    public decimal RebuildPremium(IgnisModelPoints points, long contextTokens) =>
        contextTokens > 0 ? Math.Max(0m, IgnisMoney.RebuildPremium(points, contextTokens, IgnisKeepWarm.TtlFromPoints(points))) : 0m;

    public decimal RebuildPremium(IgnisModelPoints points, long contextTokens, TimeSpan? ttl) =>
        contextTokens > 0 ? Math.Max(0m, IgnisMoney.RebuildPremium(points, contextTokens, ttl ?? IgnisKeepWarm.TtlFromPoints(points))) : 0m;

    public decimal LongerMemorySaved(IgnisModelPoints points, long prefixTokens, long readTokens, TimeSpan gap, TimeSpan lifetime) =>
        IgnisCacheLifetime.AvoidedRebuild(points, prefixTokens, readTokens, gap, lifetime);

    public decimal LongerMemoryCost(IgnisModelPoints points, long oneHourWrites, long promptTokens) =>
        IgnisCacheLifetime.ExtraWriteCost(points, oneHourWrites, promptTokens);

    public decimal PingCost(IgnisModelPoints points, long contextTokens) => IgnisMoney.PingCost(points, Math.Max(0, contextTokens));

    /// <summary>
    /// ENGINEERING DECISION (ED-NEW-ignis-runtime): a tidy-up's saving is what each later message saves by carrying the
    /// shorter chat, over the payback window the tidy point's break-even assumes (<see cref="IgnisTidyPoints.PaybackMessages"/>).
    /// </summary>
    public decimal TidySaved(IgnisModelPoints points, long beforeTokens, long afterTokens) =>
        IgnisTidyPoints.PerMessageSaving(points, beforeTokens, afterTokens) * IgnisTidyPoints.PaybackMessages;

    /// <summary>Settings › Ignis "Rebuild points": the engine's tidy point in the U5 row shape.</summary>
    public static IgnisRebuildPoint RebuildPoint(IgnisModelPoints points)
    {
        var point = IgnisTidyPoints.For(points, IgnisKeepWarm.TtlFromPoints(points));
        return point.Reason switch
        {
            IgnisTidyReason.Override => new(point.Tokens, IgnisRebuildReason.Stated, StatedReason: point.OverrideText),
            IgnisTidyReason.PriceStep => new(point.Tokens, IgnisRebuildReason.PriceStep, StepTokens: point.StepTokens),
            IgnisTidyReason.LastQuarter => new(point.Tokens, IgnisRebuildReason.LastQuarter, Window: point.Window),
            _ => new(point.Tokens, IgnisRebuildReason.BreakEven),
        };
    }
}
