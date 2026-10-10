using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Tests.Port.Ignis.Engine;

// Ignis U1 (OD-0134, ED-NEW-ignis-engine): the tidy-up point. Just under a price step (95K for a 100K step), else the last
// quarter of the window, never below break-even; the points' tidyAt overrides.
public sealed class IgnisTidyPointTests
{
    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_100K_price_step_puts_the_point_at_95K()
    {
        var point = IgnisTidyPoints.For(IgnisMoneyTests.HaikuWithStep());
        Assert.Equal(95_000, point.Tokens);
        Assert.Equal(IgnisTidyReason.PriceStep, point.Reason);
        Assert.Equal(100_000, point.StepTokens);
        Assert.Equal("Price step at 100K", point.Why);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Without_a_step_the_point_is_before_the_last_quarter_of_the_window()
    {
        var model = IgnisMoneyTests.Model("codex", 400_000, new IgnisPrices(1.75m, 14m, 0.175m, null, null, new([])), new IgnisCachePoints(null, null, false));
        var point = IgnisTidyPoints.For(model);
        Assert.Equal(300_000, point.Tokens);
        Assert.Equal(IgnisTidyReason.LastQuarter, point.Reason);
        Assert.Equal("Rebuild is cheapest before the last quarter of its 400K window", point.Why);
        // A step at or beyond the window is not a step inside it.
        var opus = IgnisMoneyTests.Opus();
        Assert.Equal(750_000, IgnisTidyPoints.For(opus).Tokens);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_points_tidyAt_overrides_with_its_own_reason()
    {
        var model = IgnisMoneyTests.Model("codex", 400_000, new IgnisPrices(1.75m, 14m, 0.175m, null, null, new([])), new IgnisCachePoints(null, null, false), tidyAt: 250_000, tidyReason: "Quality drops after 250K");
        var point = IgnisTidyPoints.For(model);
        Assert.Equal(250_000, point.Tokens);
        Assert.Equal(IgnisTidyReason.Override, point.Reason);
        Assert.Equal("Quality drops after 250K", point.Why);
        var noReason = IgnisTidyPoints.For(model with { TidyReason = null });
        Assert.Equal(IgnisWords.WhyBreakEven, noReason.Why);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_point_never_sits_below_break_even()
    {
        // Read 0.1, output 100, 5-minute write 1.25, window 600K: break-even C = (4000·100 + 20000·(1.25 + 10·0.1)) / (0.1·9)
        // = (400000 + 45000) / 0.9 = 494444.4… → 494445, above the last-quarter 450000.
        var model = IgnisMoneyTests.Model("claude", 600_000, new IgnisPrices(1m, 100m, 0.1m, 1.25m, 2m, new([])), new IgnisCachePoints(3600, 300, true));
        Assert.Equal(494_445, IgnisTidyPoints.BreakEven(model));
        var point = IgnisTidyPoints.For(model);
        Assert.Equal(494_445, point.Tokens);
        Assert.Equal(IgnisTidyReason.BreakEven, point.Reason);
        Assert.Equal("Replaying the history costs more than a rebuild", point.Why);
        // Opus 5.5 (1 h cache): (4000·20 + 20000·(8 + 2)) / (0.2·9) = 280000 / 1.8 = 155555.5… → 155556, below 750K.
        Assert.Equal(155_556, IgnisTidyPoints.BreakEven(IgnisMoneyTests.Opus(), TimeSpan.FromHours(1)));
        // A model with free cache reads never breaks even.
        Assert.Null(IgnisTidyPoints.BreakEven(IgnisMoneyTests.Model("claude", 600_000, new IgnisPrices(1m, 1m, 0m, null, null, new([])), new IgnisCachePoints(null, null, true))));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_tidy_up_saves_the_dropped_tokens_on_every_later_message()
    {
        // Opus: (300000 − 40000)·0.2 = 52000 → $0.052 a message.
        Assert.Equal(0.052m, IgnisTidyPoints.PerMessageSaving(IgnisMoneyTests.Opus(), 300_000, 40_000));
        // Haiku across its step: 120000·0.05 − 40000·0.01 = 6000 − 400 = 5600 → $0.0056.
        Assert.Equal(0.0056m, IgnisTidyPoints.PerMessageSaving(IgnisMoneyTests.HaikuWithStep(), 120_000, 40_000));
        Assert.Equal(0m, IgnisTidyPoints.PerMessageSaving(IgnisMoneyTests.Opus(), 40_000, 40_000));
    }
}
