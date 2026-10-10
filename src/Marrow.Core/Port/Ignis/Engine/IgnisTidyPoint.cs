// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1). Where a chat is tidied up (rebuilt shorter): the notch on
// the Chat size track and the "Cheapest point" column of Settings › Ignis › Rebuild points.
//
// The rule (PLAN U1): the model points' tidyAt overrides; else just under the first price step that lies inside the
// window (95 % of it, so a 100K step gives 95K: the next message would otherwise pay the higher tier for the whole
// prompt); else the last quarter of the window (75 %). The point is never below the break-even size, where replaying
// the history from the cache starts to cost more than a rebuild over the next few messages; when the break-even size
// is the binding one the reason says so.
namespace Marrow.Core.Port.Ignis.Engine;

/// <summary>Why the tidy-up point sits where it does.</summary>
public enum IgnisTidyReason
{
    /// <summary>The model points name the point (tidyAt), with their own reason text.</summary>
    Override,
    /// <summary>Just under a price step ("Price step at 100K").</summary>
    PriceStep,
    /// <summary>"Rebuild is cheapest before the last quarter of its 400K window".</summary>
    LastQuarter,
    /// <summary>"Replaying the history costs more than a rebuild".</summary>
    BreakEven,
}

/// <summary>A model's tidy-up point in tokens and why.</summary>
public sealed record IgnisTidyPoint(long Tokens, IgnisTidyReason Reason, long? StepTokens, long Window, string? OverrideText)
{
    /// <summary>The "Why" column text (IgnisWords).</summary>
    public string Why => Reason switch
    {
        IgnisTidyReason.Override => OverrideText ?? IgnisWords.WhyBreakEven,
        IgnisTidyReason.PriceStep => IgnisWords.WhyPriceStep(StepTokens ?? Tokens),
        IgnisTidyReason.LastQuarter => IgnisWords.WhyLastQuarter(Window),
        IgnisTidyReason.BreakEven => IgnisWords.WhyBreakEven,
        _ => IgnisWords.WhyBreakEven,
    };
}

public static class IgnisTidyPoints
{
    /// <summary>How far under a price step the point sits (95 %: 95K for a 100K step).</summary>
    public const decimal UnderStep = 0.95m;

    /// <summary>The last-quarter rule: 75 % of the window.</summary>
    public const decimal LastQuarter = 0.75m;

    /// <summary>
    /// ENGINEERING DECISION: the break-even assumptions. A tidy-up keeps about this many tokens (system, tools and the
    /// kept turns), writes a summary of about <see cref="SummaryTokens"/>, and pays for itself over
    /// <see cref="PaybackMessages"/> messages.
    /// </summary>
    public const long KeptTokens = 20_000;
    public const long SummaryTokens = 4_000;
    public const int PaybackMessages = 10;

    public static IgnisTidyPoint For(IgnisModelPoints points, TimeSpan? ttl = null)
    {
        var window = points.ContextWindow;
        if (points.TidyAt is { } fixedPoint) return new IgnisTidyPoint(fixedPoint, IgnisTidyReason.Override, null, window, points.TidyReason);

        var step = points.Prices.Steps.Where(s => s.AboveTokens < window).OrderBy(s => s.AboveTokens).FirstOrDefault();
        var candidate = step is not null
            ? new IgnisTidyPoint((long)Math.Floor(step.AboveTokens * UnderStep), IgnisTidyReason.PriceStep, step.AboveTokens, window, null)
            : new IgnisTidyPoint((long)Math.Floor(window * LastQuarter), IgnisTidyReason.LastQuarter, null, window, null);

        var breakEven = BreakEven(points, ttl);
        return breakEven is { } even && even > candidate.Tokens && even < window
            ? new IgnisTidyPoint(even, IgnisTidyReason.BreakEven, null, window, null)
            : candidate;
    }

    /// <summary>
    /// The smallest chat size C where a tidy-up pays for itself within <see cref="PaybackMessages"/> messages:
    /// the rebuild (C read once, a summary written out, K written into the cache) is at most what the M messages
    /// save by carrying K instead of C, (C − K) read M times. Solved for C at base prices; null when it never pays.
    /// </summary>
    public static long? BreakEven(IgnisModelPoints points, TimeSpan? ttl = null)
    {
        var tier = IgnisMoney.Tier(points.Prices, 0);
        var read = tier.CacheRead;
        if (read <= 0m || PaybackMessages <= 1) return null;
        var write = tier.CacheWriteFor(ttl);
        // C·read + S·out + K·write ≤ M·(C − K)·read  ⇔  C ≥ (S·out + K·(write + M·read)) / (read·(M − 1))
        var size = (SummaryTokens * tier.Output + KeptTokens * (write + PaybackMessages * read)) / (read * (PaybackMessages - 1));
        return (long)Math.Ceiling(size);
    }

    /// <summary>What each later message saves after a tidy-up from <paramref name="before"/> to <paramref name="after"/> tokens (cache reads at each size's tier).</summary>
    public static decimal PerMessageSaving(IgnisModelPoints points, long before, long after)
    {
        if (after >= before) return 0m;
        var high = IgnisMoney.Tier(points.Prices, before).CacheRead * before;
        var low = IgnisMoney.Tier(points.Prices, after).CacheRead * after;
        return Math.Max(0m, (high - low) / 1_000_000m);
    }
}
