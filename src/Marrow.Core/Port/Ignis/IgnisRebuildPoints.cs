// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-settings; PLAN unit U5). An ADDITIVE contract member: Settings › Ignis "Rebuild
// points" lists each model's cheapest rebuild point and why, and the U0 contract had no way to ask for it. The engine
// (unit U1) owns the real tidy-point function (just under a price step, else the break-even point, tidyAt overriding);
// it binds <see cref="IgnisRebuildPoints.For"/> at start-up. Until then the default reads only what the points file
// states outright: tidyAt (with its tidyReason), else just under the first price step, else the last quarter of the
// window. It never invents a break-even point; that needs the engine's maths.
namespace Marrow.Core.Port.Ignis;

/// <summary>Why a model's rebuild point sits where it does (the "Why" column; IgnisWords gives the text).</summary>
public enum IgnisRebuildReason
{
    /// <summary>"Price step at 100K": just under the first price step.</summary>
    PriceStep,
    /// <summary>"Replaying the history costs more than a rebuild".</summary>
    BreakEven,
    /// <summary>"Rebuild is cheapest before the last quarter of its 400K window".</summary>
    LastQuarter,
    /// <summary>The points file's own tidyAt, explained by its tidyReason.</summary>
    Stated,
}

/// <summary>One model's cheapest rebuild point in tokens and why.</summary>
public sealed record IgnisRebuildPoint(long AtTokens, IgnisRebuildReason Reason, long? StepTokens = null, long? Window = null, string? StatedReason = null)
{
    /// <summary>The "Why" column's words.</summary>
    public string Why => Reason switch
    {
        IgnisRebuildReason.PriceStep when StepTokens is { } step => IgnisWords.WhyPriceStep(step),
        IgnisRebuildReason.LastQuarter when Window is { } window => IgnisWords.WhyLastQuarter(window),
        IgnisRebuildReason.Stated when !string.IsNullOrWhiteSpace(StatedReason) => StatedReason!,
        _ => IgnisWords.WhyBreakEven,
    };
}

public static class IgnisRebuildPoints
{
    /// <summary>The rebuild point of a model. Bound by the engine (U1/U3); unbound: <see cref="Stated"/>.</summary>
    public static Ambient<Func<IgnisModelPoints, IgnisRebuildPoint>> For { get; } = new(new Func<IgnisModelPoints, IgnisRebuildPoint>(Stated));

    /// <summary>
    /// What the points file states outright: tidyAt (and its tidyReason); else 95 % of the first price step ("95K for a
    /// 100K step"); else three quarters of the context window.
    /// </summary>
    public static IgnisRebuildPoint Stated(IgnisModelPoints model)
    {
        if (model.TidyAt is { } at) return new(at, IgnisRebuildReason.Stated, StatedReason: model.TidyReason);
        if (model.Prices.Steps.Count > 0)
        {
            var step = model.Prices.Steps.Min(s => s.AboveTokens);
            return new(step * 95 / 100, IgnisRebuildReason.PriceStep, StepTokens: step);
        }
        return new(model.ContextWindow * 3 / 4, IgnisRebuildReason.LastQuarter, Window: model.ContextWindow);
    }
}
