// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-guards). The keep-warm guards' pure maths, asked by the controller: the idle
// limit (owner 2026-10-10: "Not every chat stays active; I have lots of old chats" -> a learned return time and/or the
// "Cool idle chats after" setting), the three-chat limit, the daily keep-warm allowance, the model-switch cost and the
// "cache never reused" check. No I/O and no clock reads: every time is passed in, so a test runs on a manual clock.
namespace Marrow.Core.Port.Ignis.Engine;

/// <summary>One main-chat turn's cache numbers, as the never-reused check reads them.</summary>
public sealed record IgnisCacheTurn(long Prompt, long Read, long Written, string? Model, string? Effort);

public static class IgnisKeepWarmGuards
{
    /// <summary>ENGINEERING DECISION (lead brief): the learned return time is this share of the chat's past breaks.</summary>
    public const double ReturnPercentile = 0.9;

    /// <summary>ENGINEERING DECISION: a gap history needs this many breaks before its percentile is trusted.</summary>
    public const int MinimumGaps = 5;

    /// <summary>The learned return time with no history at all (lead brief).</summary>
    public static readonly TimeSpan FallbackReturn = TimeSpan.FromHours(3);

    /// <summary>The learned limit alone ("Cool idle chats after" is Never) never goes past this (owner 2026-10-10, safety).</summary>
    public static readonly TimeSpan LearnedCeiling = TimeSpan.FromHours(24);

    /// <summary>At most this many chats are kept warm at the same time, the most recently used (owner 2026-10-10).</summary>
    public const int WarmChats = 3;

    /// <summary>ENGINEERING DECISION: the daily allowance pings always have, on top of what keep-warm saved this week.</summary>
    public const decimal StartAllowance = 0.50m;

    /// <summary>The switch-cost notice shows for a warm cache at least this big (lead brief).</summary>
    public const long SwitchNoticeTokens = 50_000;

    /// <summary>The never-reused check needs this many turns in a row (lead brief).</summary>
    public const int MissTurns = 4;

    /// <summary>
    /// ENGINEERING DECISION: a prompt below this is never flagged. Providers cache from 1,024 tokens (OpenAI, most Claude
    /// models) up to 4,096 (some Claude and Gemini models); the largest keeps the check from blaming a prompt too short to cache.
    /// </summary>
    public const long MinimumCacheable = 4_096;

    /// <summary>ENGINEERING DECISION: a turn that read under 2 % of its prompt from the cache counts as reading "near 0".</summary>
    public const double NearZeroShare = 0.02;

    // ---- the idle limit ----

    /// <summary>
    /// The learned return time: the 90th percentile of the chat's own breaks (at least <see cref="MinimumGaps"/>), else of
    /// every chat's breaks for the provider, else <see cref="FallbackReturn"/>. ENGINEERING DECISION: never under one cache
    /// lifetime, so the first keep-warm of a break (the one that covers the likeliest late returns) always stays possible.
    /// </summary>
    public static TimeSpan LearnedReturn(IReadOnlyList<TimeSpan> own, IReadOnlyList<TimeSpan> global, TimeSpan? ttl)
    {
        var learned = Percentile(own, ReturnPercentile) ?? Percentile(global, ReturnPercentile) ?? FallbackReturn;
        return ttl is { } life && learned < life ? life : learned;
    }

    /// <summary>
    /// How long a chat may sit idle and still be kept warm, from Settings › Ignis (owner 2026-10-10): both on, the sooner of
    /// the learned time and "Cool idle chats after"; learning off, the fixed limit; the fixed limit "Never", the learned time
    /// capped at <see cref="LearnedCeiling"/>; both off, no limit (null). A draft in the composer sets the learned part aside
    /// (the user is coming back), so only the fixed limit (or the 24-hour ceiling) applies.
    /// </summary>
    public static TimeSpan? IdleLimit(int? coolAfterHours, bool learn, TimeSpan learned, bool hasDraft = false)
    {
        TimeSpan? fixedLimit = coolAfterHours is { } hours ? TimeSpan.FromHours(hours) : null;
        if (!learn) return fixedLimit;
        if (hasDraft) return fixedLimit ?? LearnedCeiling;
        var capped = learned < LearnedCeiling ? learned : LearnedCeiling;
        return fixedLimit is { } limit && limit < capped ? limit : capped;
    }

    /// <summary>The value below which <paramref name="share"/> of the gaps fall (nearest rank); null with fewer than <see cref="MinimumGaps"/>.</summary>
    public static TimeSpan? Percentile(IReadOnlyList<TimeSpan> gaps, double share)
    {
        if (gaps.Count < MinimumGaps) return null;
        var sorted = gaps.Order().ToArray();
        var rank = (int)Math.Ceiling(share * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    // ---- three chats ----

    /// <summary>
    /// The chats that may be kept warm now: the <see cref="WarmChats"/> most recently used, a chat on screen or with a draft
    /// first. Ties keep the given order.
    /// </summary>
    public static IReadOnlySet<Guid> WarmSlots(IEnumerable<(Guid Thread, DateTimeOffset LastUsed, bool InFront)> candidates) =>
        candidates.OrderByDescending(c => c.InFront).ThenByDescending(c => c.LastUsed).Take(WarmChats).Select(c => c.Thread).ToHashSet();

    // ---- the daily allowance ----

    /// <summary>What a day's keep-warm pings cost (its spending without the longer-lifetime writes).</summary>
    public static decimal PingSpent(IgnisDayTotals day) => Math.Max(0m, day.Spent - day.LongerMemorySpent);

    /// <summary>
    /// Today's keep-warm allowance (owner brief: pings never cost more than keep-warm saved over the last 7 days plus a small
    /// start): <see cref="StartAllowance"/> plus what keep-warm saved over the 7 days ending today, less what pings cost on the
    /// 6 days before today, never below the start. <paramref name="week"/> is the 7 days oldest first, today last.
    /// </summary>
    public static decimal DailyAllowance(IReadOnlyList<IgnisDayTotals> week)
    {
        if (week.Count == 0) return StartAllowance;
        var saved = week.Sum(day => day.KeptWarm);
        var spentBefore = week.Take(week.Count - 1).Sum(PingSpent);
        return StartAllowance + Math.Max(0m, saved - spentBefore);
    }

    /// <summary>Today's pings reached today's allowance.</summary>
    public static bool OverDailyLimit(IReadOnlyList<IgnisDayTotals> week) => week.Count != 0 && PingSpent(week[^1]) >= DailyAllowance(week);

    // ---- the model switch ----

    /// <summary>
    /// What switching a warm chat of <paramref name="prefixTokens"/> to <paramref name="next"/> costs on its next message: the
    /// prefix written at the new model's write price for its lifetime (lead brief), in the tier of the prefix's size.
    /// </summary>
    public static decimal SwitchCost(IgnisModelPoints next, long prefixTokens, TimeSpan? ttl)
    {
        if (prefixTokens <= 0) return 0m;
        var life = ttl ?? IgnisKeepWarm.TtlFromPoints(next);
        var price = (life is { } t ? next.Prices.WriteFor(t, prefixTokens) : null) ?? IgnisMoney.Tier(next.Prices, prefixTokens).CacheWriteFor(life);
        return prefixTokens * price / 1_000_000m;
    }

    /// <summary>Whether a switch gets the notice: the cache is warm and holds at least <see cref="SwitchNoticeTokens"/>.</summary>
    public static bool ShowsSwitchNotice(bool warm, long? prefixTokens) => warm && prefixTokens >= SwitchNoticeTokens;

    // ---- the never-reused check ----

    /// <summary>
    /// Null while the cache is reused. When each of the last <see cref="MissTurns"/> (or more) turns read nearly nothing from
    /// a prompt big enough to cache, the likely cause: the model or the effort changed between them, nothing was written either
    /// (no caching), every turn wrote it again (the start of the chat changes), else unknown. Report-only.
    /// </summary>
    public static IgnisCacheMissCause? NotReused(IReadOnlyList<IgnisCacheTurn> turns)
    {
        if (turns.Count < MissTurns) return null;
        var last = turns.Skip(turns.Count - MissTurns).ToArray();
        if (!last.All(turn => turn.Prompt >= MinimumCacheable && turn.Read <= turn.Prompt * NearZeroShare)) return null;
        if (last.Select(turn => turn.Model).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) return IgnisCacheMissCause.ModelSwitches;
        if (last.Select(turn => turn.Effort).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) return IgnisCacheMissCause.EffortSwitches;
        if (last.All(turn => turn.Read == 0 && turn.Written == 0)) return IgnisCacheMissCause.NoCaching;
        if (last.All(turn => turn.Written > 0)) return IgnisCacheMissCause.PrefixChanges;
        return IgnisCacheMissCause.Unknown;
    }
}
