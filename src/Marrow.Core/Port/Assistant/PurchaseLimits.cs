// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "Limits", 17 Q4, 22 row 6; ED-NEW-dextra-as20-2). No Swift twin.
// The owner's purchase limits and what they leave: per purchase, per day, per month, and per Astur per month. The defaults
// are $50, $150 and $300 in the card's currency, and every Astur starts at $0 (it may find and fill a cart, then hands the
// payment to the owner). A purchase over any limit cannot be paid; the check names the limit that stops it. Pure: the
// caller passes what was spent and when "today" and "this month" are.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>Which limit a value is.</summary>
public enum PurchaseLimitKind { PerPurchase, PerDay, PerMonth, PerAstur }

/// <summary>The owner's limits (12.7). Each is in the card's currency; an Astur not listed has $0.</summary>
public sealed record PurchaseLimits
{
    public const decimal DefaultPerPurchase = 50m;
    public const decimal DefaultPerDay = 150m;
    public const decimal DefaultPerMonth = 300m;

    /// <summary>The largest limit nth takes: past it a typo is likelier than a wish (the bank's own limit stays the true cap).</summary>
    public const decimal Most = 100_000m;

    public decimal PerPurchase { get; init; } = DefaultPerPurchase;

    public decimal PerDay { get; init; } = DefaultPerDay;

    public decimal PerMonth { get; init; } = DefaultPerMonth;

    /// <summary>Each Astur's monthly limit, by assistant id. Missing: $0.</summary>
    public ValueMap<string, decimal> PerAstur { get; init; } = ValueMap<string, decimal>.Empty;

    public static PurchaseLimits Default { get; } = new();

    /// <summary>An Astur's monthly limit: $0 until the owner sets one.</summary>
    public decimal AsturLimit(string assistantId) => PerAstur.TryGetValue(assistantId, out var limit) ? limit : 0m;

    /// <summary>The value of one limit (an Astur's needs its id).</summary>
    public decimal Of(PurchaseLimitKind kind, string? astur = null) => kind switch
    {
        PurchaseLimitKind.PerPurchase => PerPurchase,
        PurchaseLimitKind.PerDay => PerDay,
        PurchaseLimitKind.PerMonth => PerMonth,
        PurchaseLimitKind.PerAstur => AsturLimit(astur ?? throw new ArgumentNullException(nameof(astur))),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>These limits with one changed. The caller has checked the value with <see cref="Invalid"/>.</summary>
    public PurchaseLimits With(PurchaseLimitKind kind, decimal value, string? astur = null) => kind switch
    {
        PurchaseLimitKind.PerPurchase => this with { PerPurchase = value },
        PurchaseLimitKind.PerDay => this with { PerDay = value },
        PurchaseLimitKind.PerMonth => this with { PerMonth = value },
        PurchaseLimitKind.PerAstur => WithAstur(astur ?? throw new ArgumentNullException(nameof(astur)), value),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private PurchaseLimits WithAstur(string astur, decimal value) => this with
    {
        PerAstur = new ValueMap<string, decimal>(PerAstur.Where(p => p.Key != astur)
            .Append(new KeyValuePair<string, decimal>(astur, value)).OrderBy(p => p.Key, StringComparer.Ordinal)),
    };

    /// <summary>Null when the value can be a limit (0 to <see cref="Most"/>, whole cents), else why not.</summary>
    public static string? Invalid(decimal value)
    {
        if (value < 0) return "A limit cannot be below 0.";
        if (value > Most) return "A limit can be at most " + Most.ToString("0", CultureInfo.InvariantCulture) + ".";
        if (decimal.Round(value, 2) != value) return "A limit is in whole cents.";
        return null;
    }

    /// <summary>Null when every limit is one nth takes, else the first problem.</summary>
    public string? Problem() =>
        Invalid(PerPurchase) ?? Invalid(PerDay) ?? Invalid(PerMonth) ?? PerAstur.Values.Select(Invalid).FirstOrDefault(p => p is not null);
}

/// <summary>What was spent already, in the card's currency: today, this month, and this month by each assistant.</summary>
public sealed record PurchaseSpend(decimal Today, decimal ThisMonth)
{
    public ValueMap<string, decimal> ThisMonthBy { get; init; } = ValueMap<string, decimal>.Empty;

    public static PurchaseSpend None { get; } = new(0m, 0m);

    public decimal Of(string assistantId) => ThisMonthBy.TryGetValue(assistantId, out var spent) ? spent : 0m;
}

/// <summary>What the limits say about one purchase: whether it may be paid, the limit that stops it, and what is left after.</summary>
public sealed record PurchaseLimitCheck(PurchaseLimitKind? StoppedBy, decimal LeftToday, decimal LeftThisMonth, decimal? LeftForAstur)
{
    public bool Allowed => StoppedBy is null;
}

public static class PurchaseLimitRules
{
    /// <summary>
    /// Checks a total against the limits. <paramref name="astur"/> is the asking Astur's id, or null when Dextra itself asks
    /// (Dextra has no Astur limit). What is left is never below 0.
    /// </summary>
    public static PurchaseLimitCheck Check(decimal total, PurchaseLimits limits, PurchaseSpend spent, string? astur)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(spent);
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
        var leftToday = limits.PerDay - spent.Today - total;
        var leftMonth = limits.PerMonth - spent.ThisMonth - total;
        decimal? leftAstur = astur is null ? null : limits.AsturLimit(astur) - spent.Of(astur) - total;
        PurchaseLimitKind? stopped =
            total > limits.PerPurchase ? PurchaseLimitKind.PerPurchase
            : leftToday < 0 ? PurchaseLimitKind.PerDay
            : leftMonth < 0 ? PurchaseLimitKind.PerMonth
            : leftAstur < 0 || (astur is not null && limits.AsturLimit(astur) == 0m) ? PurchaseLimitKind.PerAstur
            : null;
        return new PurchaseLimitCheck(stopped, Math.Max(0m, leftToday), Math.Max(0m, leftMonth), leftAstur is { } a ? Math.Max(0m, a) : null);
    }
}
