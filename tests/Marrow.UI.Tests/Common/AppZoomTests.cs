using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Common;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

// Source under test: DroppyCode/UI/Common/AppZoom.swift (B2 1fbf81e), :1-51.
// Every expected value below is transcribed from that file, never from the port:
//   :6      levels = [75, 90, 100, 110, 125, 150, 175, 200]
//   :8-10   normalized(p) = levels.contains(p) ? p : 100
//   :12-15  next(p, increasing:) = normalized first, then levels.first { $0 > current } ?? current, or
//           levels.last { $0 < current } ?? current
//   :22     scale = CGFloat(normalized(percent)) / 100
//   :24     content.scaleEffect(scale, anchor: .topLeading)
//   :34-38  sizeThatFits: zero with no child; else the child's size for unscaled(proposal), times scale
//   :40-46  placeSubviews: the child at bounds.origin, anchored top-leading, proposed unscaled(bounds.size)
//   :48-50  unscaled: each proposed dimension divided by scale, a nil (unspecified) dimension staying nil
// Conventions: a ViewModifier is a sealed record with `Control Body(Control content)` and a Layout is a Panel
// (ED-095); a SwiftUI unspecified proposal is an Avalonia infinite constraint; one point is one DIP (ED-071).
// A CGFloat is a double, so the scale of each level is the double (double)percent / 100 and every quotient and
// product below is that double arithmetic, compared exactly.
[Collection("Headless")]
public sealed class AppZoomTests(HeadlessSession headless)
{
    private const double HostWidth = 1000;
    private const double HostHeight = 800;
    private static readonly Size ContentWants = new(240, 180);

    public static TheoryData<int> Levels() => new() { 75, 90, 100, 110, 125, 150, 175, 200 };

    public static TheoryData<int> OffTableValues() => new()
    {
        95, 130, 60, 250, 74, 76, 89, 91, 101, 109, 199, 201, 0, 1, -1, -75, -100, int.MinValue, int.MaxValue,
    };

    // :22, the scale Swift computes for a percent that is already one of the levels.
    private static double ScaleOf(int level) => (double)level / 100;

    // --- AppZoom (:5-16) ---

    [Fact]
    [Trait("Req", "PR-WIN-216")] // UI/Common/AppZoom.swift:6
    public void Levels_are_exactly_75_90_100_110_125_150_175_200_in_that_order()
    {
        Assert.Equal(new[] { 75, 90, 100, 110, 125, 150, 175, 200 }, AppZoom.Levels.ToArray());
    }

    [Fact]
    [Trait("Req", "PR-WIN-216")] // UI/Common/AppZoom.swift:6; App/Views/Settings/SettingsView.swift:481,497 read first and last
    public void The_first_level_is_75_the_last_is_200_and_100_is_one_of_them()
    {
        Assert.Equal(8, AppZoom.Levels.Count());
        Assert.Equal(75, AppZoom.Levels.First());
        Assert.Equal(200, AppZoom.Levels.Last());
        Assert.Contains(100, AppZoom.Levels);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    [Trait("Req", "PR-WIN-217")] // UI/Common/AppZoom.swift:8-10
    public void Normalized_returns_a_level_unchanged(int level)
    {
        Assert.Equal(level, AppZoom.Normalized(level));
    }

    [Theory]
    [MemberData(nameof(OffTableValues))]
    [Trait("Req", "PR-WIN-217")] // UI/Common/AppZoom.swift:8-10
    public void Normalized_returns_100_for_every_other_value_never_the_nearest_level(int percent)
    {
        Assert.Equal(100, AppZoom.Normalized(percent));
    }

    [Fact]
    [Trait("Req", "PR-WIN-217")] // UI/Common/AppZoom.swift:8-10; the record's verification examples
    public void Normalized_does_not_clamp_or_round_the_record_examples()
    {
        Assert.Equal(100, AppZoom.Normalized(95));
        Assert.Equal(100, AppZoom.Normalized(130));
        Assert.Equal(100, AppZoom.Normalized(60));
        Assert.Equal(100, AppZoom.Normalized(250));
        Assert.Equal(125, AppZoom.Normalized(125));
    }

    [Theory]
    [InlineData(75, true, 90)]
    [InlineData(90, true, 100)]
    [InlineData(100, true, 110)]
    [InlineData(110, true, 125)]
    [InlineData(125, true, 150)]
    [InlineData(150, true, 175)]
    [InlineData(175, true, 200)]
    [InlineData(200, true, 200)]
    [InlineData(75, false, 75)]
    [InlineData(90, false, 75)]
    [InlineData(100, false, 90)]
    [InlineData(110, false, 100)]
    [InlineData(125, false, 110)]
    [InlineData(150, false, 125)]
    [InlineData(175, false, 150)]
    [InlineData(200, false, 175)]
    [Trait("Req", "PR-WIN-218")] // UI/Common/AppZoom.swift:12-15
    public void Next_moves_to_the_neighbouring_level_and_stays_put_at_either_end(int percent, bool increasing, int expected)
    {
        Assert.Equal(expected, AppZoom.Next(percent, increasing: increasing));
    }

    [Theory]
    [InlineData(95, true, 110)]
    [InlineData(95, false, 90)]
    [InlineData(130, true, 110)]
    [InlineData(130, false, 90)]
    [InlineData(60, true, 110)]
    [InlineData(60, false, 90)]
    [InlineData(250, true, 110)]
    [InlineData(250, false, 90)]
    [InlineData(0, true, 110)]
    [InlineData(0, false, 90)]
    [InlineData(int.MinValue, true, 110)]
    [InlineData(int.MaxValue, false, 90)]
    [Trait("Req", "PR-WIN-218")] // UI/Common/AppZoom.swift:12-15 (normalized first, :13)
    [Trait("Req", "PR-WIN-217")] // UI/Common/AppZoom.swift:8-10 (the step goes through normalized)
    public void Next_counts_an_off_table_value_as_100_before_stepping(int percent, bool increasing, int expected)
    {
        Assert.Equal(expected, AppZoom.Next(percent, increasing: increasing));
    }

    [Fact]
    [Trait("Req", "PR-WIN-218")] // UI/Common/AppZoom.swift:12-15; the record's examples
    public void Next_matches_the_record_examples()
    {
        Assert.Equal(110, AppZoom.Next(100, increasing: true));
        Assert.Equal(100, AppZoom.Next(110, increasing: false));
        Assert.Equal(110, AppZoom.Next(95, increasing: true));
        Assert.Equal(200, AppZoom.Next(200, increasing: true));
        Assert.Equal(75, AppZoom.Next(75, increasing: false));
    }

    // --- AppZoomModifier (:18-27) and ZoomLayout (:29-51) ---

    [Theory]
    [MemberData(nameof(Levels))]
    [Trait("Req", "PR-WIN-219")] // UI/Common/AppZoom.swift:22-24
    [Trait("Req", "UI-WIN-078")] // UI/Common/AppZoom.swift:18-51
    public void The_content_is_drawn_scaled_by_percent_over_100_about_its_top_leading_corner(int level) => headless.OnUiThread(() =>
    {
        var scale = ScaleOf(level);
        var content = new LayoutProbe(ContentWants);
        var zoomed = Layout(new AppZoomModifier(level).Body(content));

        var toHost = content.TransformToVisual(zoomed);
        Assert.NotNull(toHost);
        var arranged = content.Bounds.Size;
        // The top-leading corner stays at the layout's origin (:24 anchor .topLeading, :42 bounds.origin).
        Assert.Equal(new Point(0, 0), toHost.Value.Transform(new Point(0, 0)));
        // Every other point moves away from it by the factor, in both dimensions (:24 one scale for both).
        Assert.Equal(new Point(arranged.Width * scale, arranged.Height * scale),
            toHost.Value.Transform(new Point(arranged.Width, arranged.Height)));
        Assert.Equal(new Point(100 * scale, 0), toHost.Value.Transform(new Point(100, 0)));
        Assert.Equal(new Point(0, 100 * scale), toHost.Value.Transform(new Point(0, 100)));
    });

    [Fact]
    [Trait("Req", "PR-WIN-219")] // UI/Common/AppZoom.swift:22 (100 / 100 is exactly 1)
    [Trait("Req", "UI-WIN-078")] // UI/Common/AppZoom.swift:8-10, :22; state "100: drawn at the stock size"
    public void At_100_the_content_is_laid_out_and_drawn_at_the_stock_size() => headless.OnUiThread(() =>
    {
        var content = new LayoutProbe(ContentWants);
        var zoomed = Layout(new AppZoomModifier(100).Body(content));

        Assert.Equal(new Size(HostWidth, HostHeight), content.Measured[^1]);
        Assert.Equal(new Size(HostWidth, HostHeight), content.Arranged[^1]);
        Assert.Equal(ContentWants, zoomed.DesiredSize);
        var toHost = content.TransformToVisual(zoomed);
        Assert.NotNull(toHost);
        Assert.Equal(new Point(HostWidth, HostHeight), toHost.Value.Transform(new Point(HostWidth, HostHeight)));
        Assert.Equal(new Point(123, 45), toHost.Value.Transform(new Point(123, 45)));
    });

    [Theory]
    [InlineData(95)]
    [InlineData(130)]
    [InlineData(60)]
    [InlineData(250)]
    [InlineData(0)]
    [InlineData(-100)]
    [Trait("Req", "PR-WIN-219")] // UI/Common/AppZoom.swift:22 (the scale goes through normalized)
    [Trait("Req", "PR-WIN-217")] // UI/Common/AppZoom.swift:8-10
    [Trait("Req", "UI-WIN-078")] // UI/Common/AppZoom.swift:8-10, :22; state "any other stored value draws at 100"
    public void An_off_table_percent_draws_at_the_stock_size(int percent) => headless.OnUiThread(() =>
    {
        var content = new LayoutProbe(ContentWants);
        var zoomed = Layout(new AppZoomModifier(percent).Body(content));

        Assert.Equal(new Size(HostWidth, HostHeight), content.Measured[^1]);
        Assert.Equal(new Size(HostWidth, HostHeight), content.Arranged[^1]);
        Assert.Equal(ContentWants, zoomed.DesiredSize);
        var toHost = content.TransformToVisual(zoomed);
        Assert.NotNull(toHost);
        Assert.Equal(new Point(HostWidth, HostHeight), toHost.Value.Transform(new Point(HostWidth, HostHeight)));
    });

    [Theory]
    [MemberData(nameof(Levels))]
    [Trait("Req", "PR-WIN-220")] // UI/Common/AppZoom.swift:34-38, :48-50
    [Trait("Req", "UI-WIN-078")] // UI/Common/AppZoom.swift:34-38, :48-50; laid out in the host's size divided by the scale
    public void Measuring_proposes_the_host_size_divided_by_the_scale_and_reports_the_content_size_times_the_scale(int level) => headless.OnUiThread(() =>
    {
        var scale = ScaleOf(level);
        var content = new LayoutProbe(ContentWants);
        var zoomed = new AppZoomModifier(level).Body(content);
        zoomed.UseLayoutRounding = false;

        zoomed.Measure(new Size(HostWidth, HostHeight));

        Assert.Equal(new Size(HostWidth / scale, HostHeight / scale), content.Measured[^1]);
        Assert.Equal(new Size(ContentWants.Width * scale, ContentWants.Height * scale), zoomed.DesiredSize);
    });

    [Theory]
    [InlineData(200, 700, 500)]
    [InlineData(75, 1500, 1200)]
    // DEVIATION ED-150: AppZoom.swift:36-37 would report 1400x1000 at 200 and 1125x900 at 75; Avalonia clamps DesiredSize to the constraint.
    [Trait("Req", "PR-WIN-220")] // UI/Common/AppZoom.swift:36-37, :40-46, :48-50
    public void Content_wanting_more_than_the_scaled_host_is_clamped_to_the_host_and_placement_still_keeps_parity(
        int level, double wantsWidth, double wantsHeight) => headless.OnUiThread(() =>
    {
        var scale = ScaleOf(level);
        var content = new LayoutProbe(new Size(wantsWidth, wantsHeight));
        var zoomed = new AppZoomModifier(level).Body(content);
        zoomed.UseLayoutRounding = false;

        zoomed.Measure(new Size(HostWidth, HostHeight));

        var unscaledHost = new Size(HostWidth / scale, HostHeight / scale);
        Assert.Equal(unscaledHost, content.Measured[^1]);
        Assert.Equal(unscaledHost, content.DesiredSize);
        Assert.Equal(new Size(HostWidth, HostHeight), zoomed.DesiredSize);

        zoomed.Arrange(new Rect(0, 0, HostWidth, HostHeight));

        Assert.Equal(unscaledHost, content.Arranged[^1]);
        var toHost = content.TransformToVisual(zoomed);
        Assert.NotNull(toHost);
        Assert.Equal(new Point(0, 0), toHost.Value.Transform(new Point(0, 0)));
        var farCorner = toHost.Value.Transform(new Point(unscaledHost.Width, unscaledHost.Height));
        Assert.Equal(HostWidth, farCorner.X, 9);
        Assert.Equal(HostHeight, farCorner.Y, 9);
    });

    [Fact]
    [Trait("Req", "PR-WIN-220")] // UI/Common/AppZoom.swift:48-50 (proposal.width.map / proposal.height.map)
    public void An_unspecified_dimension_stays_unspecified_when_measuring() => headless.OnUiThread(() =>
    {
        var scale = ScaleOf(150);
        var content = new LayoutProbe(ContentWants);
        var zoomed = new AppZoomModifier(150).Body(content);
        zoomed.UseLayoutRounding = false;

        zoomed.Measure(new Size(double.PositiveInfinity, HostHeight));

        Assert.Equal(new Size(double.PositiveInfinity, HostHeight / scale), content.Measured[^1]);
        Assert.Equal(new Size(ContentWants.Width * scale, ContentWants.Height * scale), zoomed.DesiredSize);

        var unbounded = new LayoutProbe(ContentWants);
        var zoomedUnbounded = new AppZoomModifier(150).Body(unbounded);
        zoomedUnbounded.UseLayoutRounding = false;

        zoomedUnbounded.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(new Size(double.PositiveInfinity, double.PositiveInfinity), unbounded.Measured[^1]);
        Assert.Equal(new Size(ContentWants.Width * scale, ContentWants.Height * scale), zoomedUnbounded.DesiredSize);
    });

    [Theory]
    [MemberData(nameof(Levels))]
    [Trait("Req", "PR-WIN-220")] // UI/Common/AppZoom.swift:40-46, :48-50
    [Trait("Req", "UI-WIN-078")] // UI/Common/AppZoom.swift:24, :40-46; drawn scaled back up, filling the host
    public void Placing_puts_the_content_at_the_origin_with_the_host_size_divided_by_the_scale_so_it_fills_the_host(int level) => headless.OnUiThread(() =>
    {
        var scale = ScaleOf(level);
        var content = new LayoutProbe(ContentWants);
        var zoomed = Layout(new AppZoomModifier(level).Body(content));

        var placed = new Size(HostWidth / scale, HostHeight / scale);
        Assert.Equal(placed, content.Arranged[^1]);
        var toHost = content.TransformToVisual(zoomed);
        Assert.NotNull(toHost);
        Assert.Equal(new Point(0, 0), toHost.Value.Transform(new Point(0, 0)));
        // The scaled drawing ends where the host ends: (host / scale) * scale in each dimension.
        var farCorner = toHost.Value.Transform(new Point(placed.Width, placed.Height));
        Assert.Equal(new Point(placed.Width * scale, placed.Height * scale), farCorner);
        Assert.Equal(HostWidth, farCorner.X, 9);
        Assert.Equal(HostHeight, farCorner.Y, 9);
    });

    [Theory]
    [InlineData(150, 600, 300, 400, 200)]
    [InlineData(200, 1200, 900, 600, 450)]
    [Trait("Req", "PR-WIN-220")] // UI/Common/AppZoom.swift:40-46 (:44 unscaled(ProposedViewSize(bounds.size))), :48-50
    public void Placing_divides_the_bounds_it_is_arranged_in_not_the_size_it_was_measured_with(
        int level, double boundsWidth, double boundsHeight, double placedWidth, double placedHeight) => headless.OnUiThread(() =>
    {
        // :36 proposes unscaled(proposal) when measuring; :44 proposes unscaled(bounds.size) when placing, a
        // separate value. Measured under the 1000x800 host and then arranged in other bounds, the content is
        // placed with those bounds divided by the scale, not with the measure proposal divided by it.
        var scale = ScaleOf(level);
        var content = new LayoutProbe(ContentWants);
        var zoomed = new AppZoomModifier(level).Body(content);
        zoomed.UseLayoutRounding = false;

        zoomed.Measure(new Size(HostWidth, HostHeight));
        zoomed.Arrange(new Rect(0, 0, boundsWidth, boundsHeight));

        Assert.Equal(new Size(HostWidth / scale, HostHeight / scale), content.Measured[^1]);
        Assert.Equal(new Size(boundsWidth / scale, boundsHeight / scale), content.Arranged[^1]);
        Assert.Equal(new Size(placedWidth, placedHeight), content.Arranged[^1]);
        var toHost = content.TransformToVisual(zoomed);
        Assert.NotNull(toHost);
        // :42-43 at bounds.origin, anchored top-leading, drawn back up by :24: the far corner is the bounds' far corner.
        Assert.Equal(new Point(0, 0), toHost.Value.Transform(new Point(0, 0)));
        Assert.Equal(new Point(boundsWidth, boundsHeight), toHost.Value.Transform(new Point(placedWidth, placedHeight)));
    });

    [Fact]
    [Trait("Req", "PR-WIN-220")] // UI/Common/AppZoom.swift:29-51; the record's 200% example
    [Trait("Req", "UI-WIN-078")] // UI/Common/AppZoom.swift:3-4, :29-51; a bigger zoom shows less at once
    public void At_200_a_host_1000_points_wide_lays_the_content_out_500_wide_and_draws_it_twice_as_large() => headless.OnUiThread(() =>
    {
        var content = new LayoutProbe(ContentWants);
        var zoomed = Layout(new AppZoomModifier(200).Body(content));

        Assert.Equal(new Size(500, 400), content.Measured[^1]);
        Assert.Equal(new Size(500, 400), content.Arranged[^1]);
        Assert.Equal(new Size(480, 360), zoomed.DesiredSize);
        var toHost = content.TransformToVisual(zoomed);
        Assert.NotNull(toHost);
        Assert.Equal(new Point(1000, 800), toHost.Value.Transform(new Point(500, 400)));
        Assert.Equal(new Point(500, 400), toHost.Value.Transform(new Point(250, 200)));
    });

    [Fact]
    [Trait("Req", "PR-WIN-220")] // UI/Common/AppZoom.swift:35 (guard let content = subviews.first else { return .zero }), :40-46
    public void With_no_child_the_zoom_layout_is_zero_by_zero_and_placing_does_nothing() => headless.OnUiThread(() =>
    {
        // ED-095: the modifier's body is the ZoomLayout itself (:23), and a Layout is a Panel.
        var layout = Assert.IsAssignableFrom<Panel>(new AppZoomModifier(150).Body(new LayoutProbe(ContentWants)));
        layout.UseLayoutRounding = false;
        layout.Children.Clear();

        layout.Measure(new Size(HostWidth, HostHeight));

        Assert.Equal(new Size(0, 0), layout.DesiredSize);
        // :41 `subviews.first?.place(...)` is optional chaining: with no subview nothing is placed and nothing traps.
        Assert.Null(Record.Exception(() => layout.Arrange(new Rect(0, 0, HostWidth, HostHeight))));
    });

    [Fact]
    [Trait("Req", "PR-WIN-220")] // UI/Common/AppZoom.swift:35 (.zero whatever the proposal), :40-46
    public void With_no_child_an_unspecified_proposal_also_gives_zero_by_zero() => headless.OnUiThread(() =>
    {
        var layout = Assert.IsAssignableFrom<Panel>(new AppZoomModifier(75).Body(new LayoutProbe(ContentWants)));
        layout.UseLayoutRounding = false;
        layout.Children.Clear();

        layout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(new Size(0, 0), layout.DesiredSize);
        Assert.Null(Record.Exception(() => layout.Arrange(new Rect(0, 0, HostWidth, HostHeight))));
    });

    [Fact]
    [Trait("Req", "PR-WIN-219")] // UI/Common/AppZoom.swift:22-25 (scaleEffect is an outer effect: the content's own transform stays)
    public void Zoom_composes_with_a_transform_the_content_already_has() => headless.OnUiThread(() =>
    {
        var content = new LayoutProbe(ContentWants);
        var own = new Avalonia.Media.TranslateTransform(7, 3);
        var ownOrigin = new RelativePoint(0.25, 0.75, RelativeUnit.Relative);
        content.RenderTransform = own;
        content.RenderTransformOrigin = ownOrigin;
        var zoomed = Layout(new AppZoomModifier(150).Body(content));

        Assert.Same(own, content.RenderTransform);
        Assert.Equal(ownOrigin, content.RenderTransformOrigin);
        var toHost = content.TransformToVisual(zoomed);
        Assert.NotNull(toHost);
        Assert.Equal(new Point(7 * 1.5, 3 * 1.5), toHost.Value.Transform(new Point(0, 0)));
    });

    // Measures and arranges the modifier's body as a host of HostWidth by HostHeight would, with layout
    // rounding off so the probe records the exact double quotients (:48-50).
    private static Control Layout(Control zoomed)
    {
        zoomed.UseLayoutRounding = false;
        zoomed.Measure(new Size(HostWidth, HostHeight));
        zoomed.Arrange(new Rect(0, 0, HostWidth, HostHeight));
        return zoomed;
    }
}
