using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B3 52bc792), :374-396 UsageBar and :169-177 the row's use of it.
[Collection("Headless")]
public sealed class UsageViewsBarTests(HeadlessSession headless)
{
    private static (Border Track, Border Fill) Parts(UsageBar bar)
    {
        var borders = All<Border>(bar);
        Assert.Equal(2, borders.Length); // the track and the fill, nothing else is drawn
        return (borders[0], borders[1]);
    }

    private static Color Solid(Border border) => Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;

    // ------------------------------------------------------------------ PR-USAGE-032, UI-PROVREG-017, UI-USAGE-015

    [Fact]
    [Trait("Req", "PR-USAGE-032")] // :381-391 .frame(height: 5); Capsule(style: .continuous).fill(Chrome.overlay(0.1)) over the full width
    [Trait("Req", "UI-PROVREG-017")] // :373-395
    [Trait("Req", "UI-USAGE-015")] // "Full content width, 5 points tall"; track Chrome.overlay(0.1)
    public void The_bar_is_5_points_tall_and_full_width_over_a_10_percent_label_colour_track() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var bar = rig.Show(new UsageBar(0.4, Chrome.Accent), width: 300);
        var (track, _) = Parts(bar);

        Assert.Equal(5, bar.Bounds.Height);
        Assert.Equal(300, bar.Bounds.Width);
        Assert.Equal(5, track.Bounds.Height);
        Assert.Equal(300, track.Bounds.Width);
        Assert.Equal(0, BoundsIn(track, bar).X);
        Assert.Equal(Resolved(Chrome.Overlay(0.1)), Solid(track));
        Assert.Equal(Resolved(Chrome.PrimaryText.Opacity(0.1)), Solid(track)); // Chrome.overlay(o) is primaryText.opacity(o)
    });

    [Fact]
    [Trait("Req", "PR-USAGE-032")] // :382-387 two continuous capsules: the corner radius is half the 5-point height
    [Trait("Req", "UI-PROVREG-017")] // "Two continuous-style Capsules in a ZStack aligned leading"
    public void Track_and_fill_are_capsules() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var bar = rig.Show(new UsageBar(0.4, Chrome.Accent), width: 300);
        var (track, fill) = Parts(bar);

        Assert.Equal(new CornerRadius(2.5), track.CornerRadius);
        Assert.Equal(new CornerRadius(2.5), fill.CornerRadius);
        Assert.Equal(5, fill.Bounds.Height);
    });

    [Theory]
    [InlineData(0.5, 150)] // the proportional width
    [InlineData(0.02, 6)]
    [InlineData(1, 300)]
    [InlineData(0, 0)] // exactly zero: nothing drawn over the track
    [InlineData(0.001, 5)] // a tiny non-zero fraction still shows a 5-point nub
    [InlineData(0.01, 5)] // 3 points proportionally, lifted to the minimum
    [InlineData(1.7, 300)] // over-full is clamped into the track
    [InlineData(-0.5, 0)] // below zero is clamped to zero, which draws nothing
    [Trait("Req", "PR-USAGE-032")] // :386 .frame(width: max(fraction > 0 ? 5 : 0, proxy.size.width * min(max(fraction, 0), 1)))
    [Trait("Req", "UI-PROVREG-017")] // "at least 5 points whenever the fraction is greater than zero, and exactly 0 when the fraction is zero"
    [Trait("Req", "UI-USAGE-015")] // "0 percent: no fill; tiny non-zero percent: a 5 point nub; 100 percent or more: fill spans the track"
    public void The_fill_width_is_the_clamped_fraction_of_the_track_with_a_5_point_minimum(double fraction, double expected) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var bar = rig.Show(new UsageBar(fraction, Chrome.Accent), width: 300);
        var (track, fill) = Parts(bar);

        Assert.Equal(expected, fill.Bounds.Width, 0.01);
        Assert.Equal(300, track.Bounds.Width); // the track is always the whole width
        Assert.Equal(0, BoundsIn(fill, bar).X); // left aligned
    });

    [Fact]
    [Trait("Req", "PR-USAGE-032")] // :385 .fill(tint): the second capsule is the given tint
    [Trait("Req", "UI-PROVREG-017")]
    public void The_fill_is_the_given_tint_over_the_track() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        foreach (var tint in new[] { Chrome.Accent, Chrome.Warning, Chrome.Danger })
        {
            var bar = rig.Show(new UsageBar(0.4, tint), width: 300);
            var (track, fill) = Parts(bar);
            Assert.Equal(Resolved(tint), Solid(fill));
            Assert.NotEqual(Solid(track), Solid(fill));
        }
    });

    [Fact]
    [Trait("Req", "PR-USAGE-032")] // :381-391 the width follows the available width, not a fixed size
    public void The_fill_follows_the_width_the_bar_is_given() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var narrow = rig.Show(new UsageBar(0.25, Chrome.Accent), width: 200);
        var wide = rig.Show(new UsageBar(0.25, Chrome.Accent), width: 480);

        Assert.Equal(50, Parts(narrow).Fill.Bounds.Width, 0.01);
        Assert.Equal(120, Parts(wide).Fill.Bounds.Width, 0.01);
    });

    // ------------------------------------------------------------------ the row's bar (:169-177)

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.4, 5)] // tiny non-zero percent: the nub
    [InlineData(50, 150)] // the fraction is percent / 100
    [InlineData(80, 240)]
    [InlineData(100, 300)]
    [InlineData(150, 300)] // 100 percent or more: the fill spans the track
    [Trait("Req", "UI-USAGE-015")] // :169 UsageBar(fraction: window.percent / 100, ...)
    [Trait("Req", "UI-PROVREG-017")] // "The view takes a FRACTION, and the caller divides the percent by 100"
    public void The_row_passes_the_percent_over_100_as_the_fraction(double percent, double expected) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = rig.Show(new LimitRow(Meter("w", "Weekly", percent)), width: 300);
        var (_, fill) = Parts(Assert.Single(All<UsageBar>(row)));

        Assert.Equal(expected, fill.Bounds.Width, 0.01);
    });

    [Theory]
    [InlineData(0, "accent")]
    [InlineData(74.9, "accent")]
    [InlineData(75, "warning")]
    [InlineData(89.9, "warning")]
    [InlineData(90, "danger")]
    [InlineData(100, "danger")]
    [InlineData(250, "danger")]
    [InlineData(-5, "accent")]
    [Trait("Req", "UI-USAGE-015")] // :173-177 danger from 90, warning from 75 up to 90, accent below 75
    [Trait("Req", "UI-PROVREG-017")] // "The thresholds are compared against the PERCENT, not the fraction"
    public void The_fill_is_accent_below_75_warning_from_75_and_danger_from_90(double percent, string colour) => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = rig.Show(new LimitRow(Meter("w", "Weekly", percent)), width: 300);
        var (track, fill) = Parts(Assert.Single(All<UsageBar>(row)));

        var expected = colour switch { "danger" => Chrome.Danger, "warning" => Chrome.Warning, _ => Chrome.Accent };
        Assert.Equal(Resolved(expected), Solid(fill));
        Assert.Equal(Resolved(Chrome.Overlay(0.1)), Solid(track)); // the track never changes colour
    });

    [Fact]
    [Trait("Req", "UI-USAGE-015")] // "Its accessibility label is the window title."
    [Trait("Req", "UI-PROVREG-017")] // "the element is labelled with the window title"
    public void The_row_bar_is_labelled_with_the_window_title_and_a_bare_bar_with_Used() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var row = rig.Show(new LimitRow(Meter("w", "Weekly · Fable", 30)), width: 300);
        var bar = Assert.Single(All<UsageBar>(row));
        Assert.Equal("Weekly · Fable", ControlAutomationPeer.CreatePeerForElement(bar).GetName());

        var bare = rig.Show(new UsageBar(0.3, Chrome.Accent), width: 300);
        Assert.Equal("Used", ControlAutomationPeer.CreatePeerForElement(bare).GetName());
    });
}
