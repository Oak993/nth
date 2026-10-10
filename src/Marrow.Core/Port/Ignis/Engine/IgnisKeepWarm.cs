// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1). The keep-warm decision: whether, and when, to send a
// keep-warm ping before a chat's cache goes cold.
//
// The rule (PLAN U1): ping when P(the user returns within the next TTL) × (rebuild − read) > ping cost. P comes from the
// chat's own idle-gap history blended with a global prior and raised by draft text or the chat being on screen (PLAN lead
// decision "Likely-to-continue"); it falls as the idle time grows, so pings stop by themselves on a long break. Never:
// mode Off, Auto off (manual "Keep warm now" only), the model has keep-warm off in the points, any pause (PC locked, chat
// archived, a plan window at 95 % or more, a usage-limit error), or the cache already cold (a ping would rebuild it at
// full price). Not between tasks: wait and ask again. Every time is passed in, so a test runs on a manual clock.
namespace Marrow.Core.Port.Ignis.Engine;

/// <summary>What to do about a chat's cache now.</summary>
public enum IgnisKeepWarmAction
{
    /// <summary>Send the ping now.</summary>
    Ping,
    /// <summary>Nothing yet: ask again at <see cref="IgnisKeepWarmDecision.At"/>.</summary>
    Wait,
    /// <summary>No ping for this idle stretch (the reason says why).</summary>
    Skip,
}

/// <summary>Why no ping is sent.</summary>
public enum IgnisKeepWarmSkip
{
    None,
    Off,
    AutoOff,
    NotSupported,
    Paused,
    NoCache,
    AlreadyCold,
    NotWorthIt,
}

public sealed record IgnisKeepWarmDecision(IgnisKeepWarmAction Action, DateTimeOffset? At, IgnisKeepWarmSkip Skip, double Odds, decimal Benefit, decimal Cost)
{
    public static IgnisKeepWarmDecision Skipped(IgnisKeepWarmSkip reason, double odds = 0, decimal benefit = 0m, decimal cost = 0m) =>
        new(IgnisKeepWarmAction.Skip, null, reason, odds, benefit, cost);
}

/// <summary>Everything one decision needs; the controller fills it from the chat's state.</summary>
public sealed record IgnisKeepWarmInput
{
    public required DateTimeOffset Now { get; init; }
    public required IgnisMode Mode { get; init; }
    public required bool Auto { get; init; }
    public IgnisPause Pause { get; init; } = IgnisPause.None;
    public required bool BetweenTasks { get; init; }
    /// <summary>The model's points; null: an unknown model (no keep-warm).</summary>
    public IgnisModelPoints? Points { get; init; }
    /// <summary>When the cache's clock started (the chat's last request); null: never sent.</summary>
    public DateTimeOffset? LastRequestAt { get; init; }
    /// <summary>The cache lifetime in use (from the stream's write fields, else the points).</summary>
    public TimeSpan? Ttl { get; init; }
    /// <summary>When the user's idle stretch began (the last real turn's end); null: <see cref="LastRequestAt"/>. Pings move the cache clock, not this.</summary>
    public DateTimeOffset? IdleSince { get; init; }
    /// <summary>The cached prefix in tokens (the last request's whole prompt); null: not reported (time-based only).</summary>
    public long? PrefixTokens { get; init; }
    /// <summary>The chat's own past idle gaps (between a turn's end and the user's next message).</summary>
    public IReadOnlyList<TimeSpan> OwnGaps { get; init; } = [];
    /// <summary>All chats' past idle gaps (the global history; empty: the built-in prior alone).</summary>
    public IReadOnlyList<TimeSpan> GlobalGaps { get; init; } = [];
    public bool HasDraft { get; init; }
    public bool OnScreen { get; init; }
}

public static class IgnisKeepWarm
{
    /// <summary>ENGINEERING DECISION: the ping's own prompt (IgnisWords.PingText plus framing) and its reply ("ok"), in tokens.</summary>
    public const long PingInputTokens = 60;
    public const long PingOutputTokens = 5;

    /// <summary>
    /// How long before the cache goes cold the ping is sent: a tenth of the TTL, at least 15 s and at most 2 min
    /// (ENGINEERING DECISION: 30 s of a 5-minute cache, 2 min of a 1-hour one).
    /// </summary>
    public static TimeSpan Lead(TimeSpan ttl) =>
        TimeSpan.FromTicks(Math.Clamp(ttl.Ticks / 10, TimeSpan.FromSeconds(15).Ticks, TimeSpan.FromMinutes(2).Ticks));

    /// <summary>How long a "Wait" for between-tasks waits before asking again.</summary>
    public static TimeSpan BusyRetry { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The cache lifetime from the points: the main chat's, or a sub-agent's (falls back to the main one).</summary>
    public static TimeSpan? TtlFromPoints(IgnisModelPoints? points, bool subagent = false)
    {
        if (points is null) return null;
        var seconds = subagent ? points.Cache.TtlSubagentSeconds ?? points.Cache.TtlMainSeconds : points.Cache.TtlMainSeconds;
        return seconds is { } s ? TimeSpan.FromSeconds(s) : null;
    }

    /// <summary>The cache lifetime a turn's usage proves: a 1-hour write says 1 h, a 5-minute write 5 min; else null.</summary>
    public static TimeSpan? TtlFromUsage(IgnisTurnUsage usage) =>
        usage.CacheWrite1h > 0 ? TimeSpan.FromHours(1) : usage.CacheWrite5m > 0 ? TimeSpan.FromMinutes(5) : null;

    public static IgnisKeepWarmDecision Decide(IgnisKeepWarmInput input)
    {
        if (input.Mode == IgnisMode.Off) return IgnisKeepWarmDecision.Skipped(IgnisKeepWarmSkip.Off);
        if (!input.Auto) return IgnisKeepWarmDecision.Skipped(IgnisKeepWarmSkip.AutoOff);
        if (input.Points is not { } points || !points.Cache.KeepWarm) return IgnisKeepWarmDecision.Skipped(IgnisKeepWarmSkip.NotSupported);
        if (input.Pause != IgnisPause.None) return IgnisKeepWarmDecision.Skipped(IgnisKeepWarmSkip.Paused);
        if (input.LastRequestAt is not { } last || (input.Ttl ?? TtlFromPoints(points)) is not { } ttl)
            return IgnisKeepWarmDecision.Skipped(IgnisKeepWarmSkip.NoCache);

        var expires = last + ttl;
        if (input.Now >= expires) return IgnisKeepWarmDecision.Skipped(IgnisKeepWarmSkip.AlreadyCold);
        var due = expires - Lead(ttl);
        if (input.Now < due) return new IgnisKeepWarmDecision(IgnisKeepWarmAction.Wait, due, IgnisKeepWarmSkip.None, 0, 0m, 0m);

        var odds = ReturnOdds(input.OwnGaps, input.GlobalGaps, idle: input.Now - (input.IdleSince ?? last), horizon: ttl, input.HasDraft, input.OnScreen);
        var (benefit, cost) = Worth(points, input.PrefixTokens, ttl, odds);
        if (benefit <= cost) return IgnisKeepWarmDecision.Skipped(IgnisKeepWarmSkip.NotWorthIt, odds, benefit, cost);
        if (!input.BetweenTasks)
        {
            var retry = input.Now + BusyRetry;
            return retry < expires
                ? new IgnisKeepWarmDecision(IgnisKeepWarmAction.Wait, retry, IgnisKeepWarmSkip.None, odds, benefit, cost)
                : IgnisKeepWarmDecision.Skipped(IgnisKeepWarmSkip.AlreadyCold, odds, benefit, cost);
        }
        return new IgnisKeepWarmDecision(IgnisKeepWarmAction.Ping, input.Now, IgnisKeepWarmSkip.None, odds, benefit, cost);
    }

    /// <summary>
    /// The expected benefit (odds × (rebuild − read)) and the ping's cost. With the prefix not reported, both are
    /// per token of prefix (the ping's own small message left out), so the comparison is the price ratio alone.
    /// </summary>
    public static (decimal Benefit, decimal Cost) Worth(IgnisModelPoints points, long? prefixTokens, TimeSpan ttl, double odds)
    {
        var p = (decimal)Math.Clamp(odds, 0, 1);
        if (prefixTokens is { } prefix && prefix > 0)
            return (p * IgnisMoney.RebuildPremium(points, prefix, ttl), IgnisMoney.PingCost(points, prefix));
        var tier = IgnisMoney.Tier(points.Prices, 0);
        return (p * (tier.CacheWriteFor(ttl) - tier.CacheRead), tier.CacheRead);
    }

    /// <summary>ENGINEERING DECISION: the global prior's weight in pseudo-gaps, and its shape (0.8 at once, halving by 30 min idle).</summary>
    public const double PriorWeight = 3;

    public static double Prior(TimeSpan idle) => 0.8 / (1 + Math.Max(0, idle.TotalMinutes) / 30);

    /// <summary>
    /// P(the user's next message comes within <paramref name="horizon"/>, given <paramref name="idle"/> has passed):
    /// among past gaps longer than the idle time, the share that ended within the horizon; the chat's own gaps first,
    /// smoothed toward the global history (or the built-in prior when there is none) with <see cref="PriorWeight"/>
    /// pseudo-gaps. Draft text raises it to at least 0.9; the chat on screen removes 40 % of the remaining doubt.
    /// </summary>
    public static double ReturnOdds(IReadOnlyList<TimeSpan> own, IReadOnlyList<TimeSpan> global, TimeSpan idle, TimeSpan horizon, bool hasDraft = false, bool onScreen = false)
    {
        var prior = Survival(global, idle, horizon) is { } fromGlobal ? fromGlobal.Odds : Prior(idle);
        var longer = own.Where(gap => gap > idle).ToList();
        var hits = longer.Count(gap => gap <= idle + horizon);
        var odds = (hits + PriorWeight * prior) / (longer.Count + PriorWeight);
        if (onScreen) odds = 1 - (1 - odds) * 0.6;
        if (hasDraft) odds = Math.Max(odds, 0.9);
        return Math.Clamp(odds, 0, 1);
    }

    private static (double Odds, int Count)? Survival(IReadOnlyList<TimeSpan> gaps, TimeSpan idle, TimeSpan horizon)
    {
        var longer = gaps.Where(gap => gap > idle).ToList();
        if (longer.Count < 5) return null;
        return ((double)longer.Count(gap => gap <= idle + horizon) / longer.Count, longer.Count);
    }

    /// <summary>
    /// "Kept warm" saving when the user came back to a cache a ping kept warm: the rebuild premium avoided minus what
    /// the pings since the last message cost; never below 0.
    /// </summary>
    public static decimal KeptWarmSaving(IgnisModelPoints points, long prefixTokens, TimeSpan? ttl, decimal pingsCost) =>
        Math.Max(0m, IgnisMoney.RebuildPremium(points, prefixTokens, ttl) - pingsCost);
}
