using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "Limits", 17 Q4, 22 row 6; ED-NEW-dextra-as20-2): the defaults are $50, $150 and $300,
// every Astur starts at $0 and hands the payment over, a purchase over any limit cannot be paid and names the limit, and what
// is left is never shown below 0.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-2")]
public sealed class PurchaseLimitsTests
{
    private const string Astur = "store-2";

    [Fact]
    public void The_defaults_are_50_150_and_300_and_every_astur_starts_at_zero()
    {
        var limits = PurchaseLimits.Default;
        Assert.Equal((50m, 150m, 300m), (limits.PerPurchase, limits.PerDay, limits.PerMonth));
        Assert.Equal(0m, limits.AsturLimit(Astur));
        Assert.Null(limits.Problem());
    }

    [Theory]
    [InlineData(46.80, 0, 0, null)]
    [InlineData(50.00, 0, 0, null)]
    [InlineData(50.01, 0, 0, PurchaseLimitKind.PerPurchase)]
    [InlineData(46.80, 103.20, 0, null)]
    [InlineData(46.80, 103.21, 103.21, PurchaseLimitKind.PerDay)]
    [InlineData(46.80, 0, 253.21, PurchaseLimitKind.PerMonth)]
    [InlineData(46.80, 0, 253.20, null)]
    public void Dextra_is_held_by_the_purchase_day_and_month_limits_in_that_order(double total, double today, double month, PurchaseLimitKind? stopped)
    {
        var check = PurchaseLimitRules.Check((decimal)total, PurchaseLimits.Default, new PurchaseSpend((decimal)today, (decimal)month), astur: null);
        Assert.Equal(stopped, check.StoppedBy);
        Assert.Equal(stopped is null, check.Allowed);
        Assert.Null(check.LeftForAstur);
        Assert.True(check.LeftToday >= 0 && check.LeftThisMonth >= 0);
    }

    [Fact]
    public void An_astur_at_zero_is_stopped_by_its_own_limit_and_one_with_a_limit_spends_within_it()
    {
        var spend = new PurchaseSpend(0m, 0m) { ThisMonthBy = new ValueMap<string, decimal>([new(Astur, 10m)]) };
        Assert.Equal(PurchaseLimitKind.PerAstur, PurchaseLimitRules.Check(1m, PurchaseLimits.Default, spend, Astur).StoppedBy);

        var limits = PurchaseLimits.Default.With(PurchaseLimitKind.PerAstur, 60m, Astur);
        var within = PurchaseLimitRules.Check(46.80m, limits, spend, Astur);
        Assert.True(within.Allowed);
        Assert.Equal(3.20m, within.LeftForAstur);
        Assert.Equal(PurchaseLimitKind.PerAstur, PurchaseLimitRules.Check(46.80m, limits.With(PurchaseLimitKind.PerAstur, 55m, Astur), spend, Astur).StoppedBy);
        // the purchase limit still comes first for an Astur
        Assert.Equal(PurchaseLimitKind.PerPurchase, PurchaseLimitRules.Check(51m, limits.With(PurchaseLimitKind.PerAstur, 500m, Astur), spend, Astur).StoppedBy);
    }

    [Fact]
    public void What_is_left_is_never_below_zero_and_reads_today_then_this_month()
    {
        var check = PurchaseLimitRules.Check(46.80m, PurchaseLimits.Default, new PurchaseSpend(140m, 290m), null);
        Assert.Equal((0m, 0m), (check.LeftToday, check.LeftThisMonth));
        var ok = PurchaseLimitRules.Check(46.80m, PurchaseLimits.Default, PurchaseSpend.None, null);
        Assert.Equal("$103.20 of $150.00 today · $253.20 of $300.00 this month", PurchaseWords.Left(ok, PurchaseLimits.Default, "USD"));
    }

    [Theory]
    [InlineData(-1, "A limit cannot be below 0.")]
    [InlineData(100000.01, "A limit can be at most 100000.")]
    [InlineData(12.345, "A limit is in whole cents.")]
    [InlineData(0, null)]
    [InlineData(100000, null)]
    public void A_limit_is_whole_cents_from_zero_to_the_most(double value, string? problem) =>
        Assert.Equal(problem, PurchaseLimits.Invalid((decimal)value));

    [Fact]
    public void The_over_line_names_the_limit_that_stops_it()
    {
        var limits = PurchaseLimits.Default;
        Assert.Equal("Over your $50.00 limit per purchase", PurchaseWords.Over(PurchaseLimitKind.PerPurchase, limits, "USD", null, null));
        Assert.Equal("Over your $150.00 limit per day", PurchaseWords.Over(PurchaseLimitKind.PerDay, limits, "USD", null, null));
        Assert.Equal("Store 2 has a $0.00 limit", PurchaseWords.Over(PurchaseLimitKind.PerAstur, limits, "USD", Astur, "Store 2"));
    }
}
