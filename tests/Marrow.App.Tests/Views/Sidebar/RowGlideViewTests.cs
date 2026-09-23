using Marrow.App.Views.Sidebar;

namespace Marrow.App.Tests.Views.Sidebar;

// DroppyCode/App/Views/Sidebar/RowGlide.swift (B2 1fbf81e), the flight math RowGlideView.body computes every frame
// (:174-224, :239-242). These are pure functions of progress; they are proven directly, without a live keyframe
// animation running (there is no frame-tick foundation in the tree yet for a continuously re-rendering headless
// pixel test of the ghost in flight - see the unit report). Smoothstep itself, and the two window functions built
// from it (Lift, Fill), are exercised here across the whole 0-1 domain.
public sealed class RowGlideViewTests
{
    [Theory]
    [Trait("Req", "PR-SIDEBAR-139")] // App/Views/Sidebar/RowGlide.swift:239-242
    [InlineData(0.0, 0.2, 0.5, 1.0)] // above edge1: clamped to 1
    [InlineData(0.0, 0.2, -1.0, 0.0)] // below edge0: clamped to 0
    [InlineData(0.0, 0.2, 0.2, 1.0)] // at or past edge1: clamped to 1
    [InlineData(0.0, 0.2, 1.0, 1.0)]
    [InlineData(0.0, 1.0, 0.5, 0.5)] // the midpoint of a smoothstep is exactly 0.5 (t=0.5, t*t*(3-2t)=0.5)
    public void Smoothstep_clamps_to_0_1_and_eases_with_the_cubic_formula(double edge0, double edge1, double x, double expected)
    {
        Assert.Equal(expected, RowGlideView.Smoothstep(edge0, edge1, x), precision: 12);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-139")] // App/Views/Sidebar/RowGlide.swift:239-241, formula t*t*(3-2t)
    public void Smoothstep_matches_the_formula_at_an_interior_point()
    {
        // t = (0.3 - 0.05) / (0.95 - 0.05) = 0.25 / 0.9 = 0.2777..; t*t*(3-2t)
        var t = (0.3 - 0.05) / (0.95 - 0.05);
        var expected = t * t * (3 - 2 * t);
        Assert.Equal(expected, RowGlideView.Smoothstep(0.05, 0.95, 0.3), precision: 12);
    }

    [Theory]
    [Trait("Req", "PR-SIDEBAR-139")] // App/Views/Sidebar/RowGlide.swift:183, "rises over the first 12 percent and falls away over the last 30 percent"
    [InlineData(0.0, 0.0)]
    [InlineData(0.12, 1.0)] // fully risen by 12%
    [InlineData(0.4, 1.0)] // plateau
    [InlineData(0.7, 1.0)] // still full right up to 70%
    [InlineData(1.0, 0.0)] // fully gone by 100%
    public void Lift_rises_over_the_first_12_percent_and_falls_over_the_last_30(double p, double expected)
    {
        Assert.Equal(expected, RowGlideView.Lift(p), precision: 12);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-139")] // App/Views/Sidebar/RowGlide.swift:184, "a floor of 0.12 times the lift"
    public void Fill_interpolates_linearly_but_never_drops_below_012_times_the_lift()
    {
        // Linear interpolation dominates while it exceeds the lift floor.
        Assert.Equal(0.05, RowGlideView.Fill(0.1, 0.0, 0.5, lift: 0.0), precision: 12);
        Assert.Equal(0.0, RowGlideView.Fill(0.1, 0.0, 1.0, lift: 0.0), precision: 12);
        // At full lift (1.0) with a linear value of 0 (arrivalFill 0, p=1), the floor 0.12*1 wins.
        Assert.Equal(0.12, RowGlideView.Fill(0.1, 0.0, 1.0, lift: 1.0), precision: 12);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-140")] // App/Views/Sidebar/RowGlide.swift:166-167, "separate constants from the row's own check insets"
    public void The_check_insets_are_36_and_16_never_the_rows_own_26_and_6()
    {
        Assert.Equal(36, RowGlideView.TrailingCheckInset);
        Assert.Equal(16, RowGlideView.LeadingCheckInset);
    }
}
