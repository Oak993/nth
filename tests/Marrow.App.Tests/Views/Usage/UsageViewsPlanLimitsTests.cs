using Avalonia;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B3 52bc792), :7-80 PlanLimitsView: the header, its spinner and the
// window rows. The limit row itself is in UsageViewsLimitRowTests, the foot notes in UsageViewsProblemNoteTests.
[Collection("Headless")]
public sealed class UsageViewsPlanLimitsTests(HeadlessSession headless)
{
    private const ProviderKind Provider = ProviderKind.Claude;

    private static Control Header(PlanLimitsView view) => Assert.IsType<StackPanel>(view.Child).Children[0];

    private static TextBlock HeaderText(PlanLimitsView view) => Assert.Single(All<TextBlock>(Header(view)));

    // ------------------------------------------------------------------ PR-USAGE-023, UI-PROVREG-011, UI-USAGE-009

    [Fact]
    [Trait("Req", "PR-USAGE-023")] // :16-19 the plan name follows after a space, U+00B7 and a space
    [Trait("Req", "UI-PROVREG-011")] // :15-20
    [Trait("Req", "UI-USAGE-009")] // :16-24
    public void The_header_names_the_plan_after_a_spaced_middle_dot() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(planName: "Max (20x)"));
        var view = rig.Show(new PlanLimitsView(Provider));

        Assert.Equal("Plan usage limits · Max (20x)", HeaderText(view).Text);

        rig.PublishLimits(Provider, Limits(planName: "Pro 5x"));
        Assert.Equal("Plan usage limits · Pro 5x", HeaderText(view).Text);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-023")] // :16-19 no limits object, or no plan name: exactly the bare header
    [Trait("Req", "UI-PROVREG-011")]
    [Trait("Req", "UI-USAGE-009")]
    public void The_header_is_the_bare_title_without_a_plan_name_and_is_always_present() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();

        // No limits object at all, not loading.
        var empty = rig.Show(new PlanLimitsView(Provider));
        Assert.Equal("Plan usage limits", HeaderText(empty).Text);

        // Limits with a nil plan name.
        rig.PublishLimits(Provider, Limits(planName: null, windows: [Meter("a", "Daily", 10)]));
        Assert.Equal("Plan usage limits", HeaderText(empty).Text);

        // A read that failed: the header stays above the provider's problem.
        rig.PublishLimits(Provider, Limits(planName: null, problem: "Please log in again."));
        Assert.Equal("Plan usage limits", HeaderText(empty).Text);

        // Loading, with nothing stored yet.
        rig.PublishLimits(Provider, null);
        rig.SetLoadingLimits(Provider, true);
        Assert.Equal("Plan usage limits", HeaderText(empty).Text);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-023")] // :17-18 .font(.system(.body)) .foregroundStyle(Chrome.secondaryText)
    [Trait("Req", "UI-PROVREG-011")]
    [Trait("Req", "UI-USAGE-009")] // ED-059 .body is 13, ED-062 secondaryText
    public void The_header_is_body_text_in_the_secondary_colour_at_the_leading_edge() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(planName: "Max (20x)"));
        var view = rig.Show(new PlanLimitsView(Provider));

        var text = HeaderText(view);
        AssertStyle(text, Body);
        AssertColor(text, Chrome.SecondaryText);
        Assert.Equal(0, BoundsIn(text, view).X); // VStack(alignment: .leading): flush with the block's leading edge
        Assert.Equal(0, BoundsIn(text, view).Y); // the first row of the block
    });

    // ------------------------------------------------------------------ PR-USAGE-024, UI-PROVREG-012, UI-USAGE-010

    [Fact]
    [Trait("Req", "PR-USAGE-024")] // :14, :21-23 the spinner follows loadingLimits.contains(provider)
    [Trait("Req", "UI-PROVREG-012")] // :21-23
    [Trait("Req", "UI-USAGE-010")] // :21-23
    public void A_spinner_shows_while_the_provider_is_loading_and_goes_when_it_stops() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(planName: "Max (20x)", windows: [Meter("a", "Daily", 10)]));
        var view = rig.Show(new PlanLimitsView(Provider));
        Assert.Empty(Spinners(view));

        rig.SetLoadingLimits(Provider, true);
        var spinner = Assert.Single(Spinners(view));
        Assert.True(spinner.IsIndeterminate);

        // Another provider loading is not this provider loading.
        rig.SetLoadingLimits(Provider, false);
        Assert.Empty(Spinners(view));
        rig.SetLoadingLimits(ProviderKind.Codex, true);
        Assert.Empty(Spinners(view));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-024")] // :20-23 trailing edge of the header row, after Spacer(minLength: 8)
    [Trait("Req", "UI-PROVREG-012")] // :16-23 HStack(spacing: 8): label, 8, Spacer(minLength: 8), 8, spinner
    [Trait("Req", "UI-USAGE-009")] // :16-23 "Spacer(minLength: 8) before the spinner slot"
    [Trait("Req", "UI-USAGE-010")] // "trailing end of the header row"
    public void The_spinner_sits_at_the_trailing_edge_with_three_8_point_gaps_of_room_after_the_label() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(planName: "Max (20x)"));
        rig.SetLoadingLimits(Provider, true);
        var view = rig.Show(new PlanLimitsView(Provider), width: 400);

        var spinner = Assert.Single(Spinners(view));
        var text = HeaderText(view);
        Assert.Equal(400, BoundsIn(spinner, view).Right); // trailing edge
        Assert.True(BoundsIn(spinner, view).Left - BoundsIn(text, view).Right >= 24); // 8 + Spacer(min 8) + 8
        Assert.Equal(BoundsIn(text, view).Center.Y, BoundsIn(spinner, view).Center.Y, 1); // centre-aligned HStack

        // The minimum: with the label taking all it needs, the gap is exactly the three 8-point steps.
        view.Measure(Size.Infinity);
        var textWidth = text.DesiredSize.Width;
        Assert.Equal(textWidth + 24 + spinner.Bounds.Width, Header(view).DesiredSize.Width, 1);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-024")] // "Previously read windows remain visible underneath; the panel does not blank."
    [Trait("Req", "UI-USAGE-010")] // "the block never blanks while refreshing"
    public void Windows_already_read_stay_visible_under_the_spinner() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(planName: "Max (20x)", windows: [Meter("a", "5-hour limit", 42)]));
        var view = rig.Show(new PlanLimitsView(Provider));

        rig.SetLoadingLimits(Provider, true);

        Assert.True(HasText(view, "5-hour limit"));
        Assert.True(HasText(view, "42%"));
        Assert.Single(Spinners(view));
    });

    // ------------------------------------------------------------------ PR-USAGE-025

    [Fact]
    [Trait("Req", "PR-USAGE-025")] // :25-29 ForEach(limits.windows) { LimitRow(...).padding(.top, 14) }
    public void Each_window_is_a_row_in_array_order_14_points_below_what_precedes_it() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        // Not sorted by percent or by title; two windows with the same title (different ids) are both kept.
        rig.PublishLimits(Provider, Limits(planName: "Max", windows:
        [
            Meter("w1", "Weekly", 90),
            Meter("w2", "Daily", 10),
            Meter("w3", "Weekly", 50),
            Meter("w4", "Monthly", 70),
        ]));
        var view = rig.Show(new PlanLimitsView(Provider));

        var rows = All<LimitRow>(view);
        Assert.Equal(["Weekly", "Daily", "Weekly", "Monthly"], rows.Select(row => All<TextBlock>(row).First().Text));
        Assert.Equal(["90%", "10%", "50%", "70%"], rows.Select(row => All<TextBlock>(row).Last().Text));

        var header = BoundsIn(Header(view), view);
        var first = BoundsIn(rows[0], view);
        Assert.Equal(14, first.Top - header.Bottom);
        for (var index = 1; index < rows.Length; index++)
            Assert.Equal(14, BoundsIn(rows[index], view).Top - BoundsIn(rows[index - 1], view).Bottom);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-025")] // "An empty windows array renders no rows at all, but the header still shows"
    public void An_empty_window_list_renders_no_rows_but_keeps_the_header() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(planName: "Max", windows: []));
        var view = rig.Show(new PlanLimitsView(Provider));

        Assert.Empty(All<LimitRow>(view));
        Assert.Equal("Plan usage limits · Max", HeaderText(view).Text);
        Assert.Single(Assert.IsType<StackPanel>(view.Child).Children); // nothing but the header
    });
}
