// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.6; unit AS-14; ED-NEW-dextra-as14-1). No Swift twin.
// The router's rules as functions of their inputs and nothing else (no clock, no store, no app): an option is an account with a
// model; it is left out for a reason, or scored. The score is the room under the account's tightest floor after the step's projection
// (11.5's own, handed in as windows), where allowance that resets within 12 hours counts double ("use it or lose it"), plus
// 20 points for a perfect fit of model to work. The uncalibrated and the unread come last (11.2), a model to avoid comes after the
// others, the owner's pill order breaks a tie, and a model the owner pinned is the only one ever chosen.
using Marrow.Core.Models;

namespace Marrow.Core.Port.Assistant;

/// <summary>What a step of a mission is, for model fit (11.6): search and reading go to cheap models, reasoning and edits to strong ones.</summary>
public enum StepWork { Search, Read, Reason, Edit, Review }

/// <summary>How strong a model is among its provider's: Light (cheap), Standard, Strong.</summary>
public enum ModelTier { Light, Standard, Strong }

/// <summary>A model of a provider, with its tier.</summary>
public sealed record RouteModel(string Id, ModelTier Tier, bool IsDefault = false, decimal? OutputPrice = null);

/// <summary>A step to route: its kind of work, the models the owner chose for it (never replaced), and a model it should avoid.</summary>
public sealed record RouteStep(StepWork Work)
{
    /// <summary>The models the owner chose for this stage (spec 8). Empty: the router chooses. Never replaced by another model.</summary>
    public ValueList<string> Pinned { get; init; } = ValueList<string>.Empty;

    /// <summary>A model to use last (the reviewer avoids the workers' model, 3.4).</summary>
    public string? AvoidModel { get; init; }
}

/// <summary>One window of an option's account, as a turn of the step's size would find it.</summary>
public sealed record RouteWindow(string Id, ReserveWindowKind Kind, double Estimate, double Floor, double Projected, DateTimeOffset? ResetsAt)
{
    /// <summary>Points under the floor after the projection; negative when the projection crosses it. The floor is 100 where no reserve applies.</summary>
    public double Room => Floor - Projected;

    public bool Crosses => ReserveMath.Crosses(Projected, Floor);

    /// <summary>The window is still to reset, and within 12 hours: the allowance left in it would be lost (11.6 "use it or lose it").</summary>
    public bool Expiring(DateTimeOffset now) => ResetsAt is { } reset && reset > now && reset - now <= RouteScoring.UseItOrLose;

    /// <summary>The room that counts for scoring: none when the projection is over the floor, and double when the window is about to reset.</summary>
    public double Weighted(DateTimeOffset now) => Math.Max(0, Room) * (Expiring(now) ? RouteScoring.ExpiringWeight : 1);
}

/// <summary>An account and a model it could run the step on.</summary>
public sealed record RouteOption(string AccountKey, string AccountName, ProviderKind Provider, string? Model, ModelTier Tier)
{
    public IReadOnlyList<RouteWindow> Windows { get; init; } = [];

    /// <summary>The place in the owner's pill order; 0 comes first.</summary>
    public int Rank { get; init; }

    /// <summary>The account passes CanUseAccount (enabled, connected, not out).</summary>
    public bool Usable { get; init; } = true;

    /// <summary>Why nth cannot run mission work on the provider (SealedProviderConfig.Refusal); null: it can.</summary>
    public string? Refusal { get; init; }

    /// <summary>The account's rate is still the conservative default (11.2): used last.</summary>
    public bool Rough { get; init; }

    /// <summary>Nothing has read the account yet: used last.</summary>
    public bool Unread => Windows.Count == 0;
}

/// <summary>Why an option was left out.</summary>
public enum RouteWhy { Unusable, Unsealable, NotPinned, ModelCannotDoStep, NoRoom }

/// <summary>An option that can run the step, with how it scored: its headroom, its fit (0 to 1), the total, and whether a window of it is about to reset.</summary>
public sealed record RouteRanked(RouteOption Option, double Headroom, double Fit, double Score, bool Expiring);

public sealed record RouteExcluded(RouteOption Option, RouteWhy Why);

/// <summary>The router's answer: the options that can run the step, best first, and those that cannot, with why (in the order they came).</summary>
public sealed record RouteDecision(IReadOnlyList<RouteRanked> Ranked, IReadOnlyList<RouteExcluded> Excluded)
{
    public RouteRanked? Best => Ranked.Count > 0 ? Ranked[0] : null;
}

public static class RouteScoring
{
    /// <summary>"The remaining percent of a window that resets within 12 hours counts double" (11.6).</summary>
    public static readonly TimeSpan UseItOrLose = TimeSpan.FromHours(12);

    public const double ExpiringWeight = 2;

    /// <summary>What a perfect fit of model to work is worth, in points of room (ED-NEW-dextra-as14-1).</summary>
    public const double FitPoints = 20;

    /// <summary>
    /// Whether a model of this tier can do this work at all: a light model searches and reads but does not reason, edit or judge. (A
    /// model the owner pinned is his choice and is not asked.)
    /// </summary>
    public static bool CanDo(StepWork work, ModelTier tier) => work is StepWork.Search or StepWork.Read || tier != ModelTier.Light;

    /// <summary>
    /// How well the tier suits the work, 0 to 1 (11.6 "search and reading go to cheap models, reasoning and edits to strong ones"). A
    /// reviewer wants the cheapest model that can judge: a standard one, a strong one half as well.
    /// </summary>
    public static double Fit(StepWork work, ModelTier tier) => work switch
    {
        StepWork.Search or StepWork.Read => tier switch { ModelTier.Light => 1, ModelTier.Standard => 0.5, _ => 0 },
        StepWork.Reason or StepWork.Edit => tier switch { ModelTier.Strong => 1, ModelTier.Standard => 0.5, _ => 0 },
        StepWork.Review => tier switch { ModelTier.Standard => 1, ModelTier.Strong => 0.5, _ => 0 },
        _ => 0,
    };

    /// <summary>
    /// Ranks the options that can run the step and says why the others cannot. Left out, by the first reason that applies: the account
    /// cannot be used now, nth cannot seal the provider, the model is not one the owner pinned (when he pinned any), the model cannot do
    /// the work (not asked of a pinned one), or a window's projection would cross its floor. The rest are ordered: the proven before
    /// the uncalibrated and the unread; the model to avoid last; by score, the highest first; by the owner's pill order; then by account
    /// and model, so the same options give the same order whatever order they arrive in.
    /// </summary>
    public static RouteDecision Decide(IEnumerable<RouteOption> options, RouteStep step, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(step);
        var ranked = new List<RouteRanked>();
        var excluded = new List<RouteExcluded>();
        foreach (var option in options)
        {
            if (Exclusion(option, step) is { } why)
            {
                excluded.Add(new RouteExcluded(option, why));
                continue;
            }
            var counted = option.Windows.Where(w => w.Kind != ReserveWindowKind.Other).ToList();
            var headroom = counted.Count == 0 ? 0 : counted.Min(w => w.Weighted(now));
            var fit = Fit(step.Work, option.Tier);
            // Rounded, so two scores that differ by arithmetic noise are a tie, and the pill order decides.
            var score = Math.Round(headroom + FitPoints * fit, 6);
            ranked.Add(new RouteRanked(option, headroom, fit, score, counted.Any(w => w.Expiring(now))));
        }
        var ordered = ranked
            .OrderBy(r => r.Option.Rough || r.Option.Unread)
            .ThenBy(r => Avoided(step, r.Option))
            .ThenByDescending(r => r.Score)
            .ThenBy(r => r.Option.Rank)
            .ThenBy(r => r.Option.AccountKey, StringComparer.Ordinal)
            .ThenBy(r => r.Option.Model ?? "", StringComparer.Ordinal)
            .ToList();
        return new RouteDecision(ordered, excluded);
    }

    private static RouteWhy? Exclusion(RouteOption option, RouteStep step)
    {
        if (!option.Usable) return RouteWhy.Unusable;
        if (option.Refusal is not null) return RouteWhy.Unsealable;
        if (step.Pinned.Count > 0)
        {
            if (option.Model is null || !step.Pinned.Any(p => string.Equals(p, option.Model, StringComparison.OrdinalIgnoreCase))) return RouteWhy.NotPinned;
        }
        else if (!CanDo(step.Work, option.Tier)) return RouteWhy.ModelCannotDoStep;
        return option.Windows.Any(w => w.Crosses) ? RouteWhy.NoRoom : null;
    }

    private static bool Avoided(RouteStep step, RouteOption option) =>
        step.AvoidModel is { } avoid && option.Model is { } model && string.Equals(avoid, model, StringComparison.OrdinalIgnoreCase);
}
