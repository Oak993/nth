using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B3 52bc792), the animations of PlanLimitsView (:78) and BankedResetsView
// (:240-256, :274-282, :346-365). A change is observed as the animation transaction current while the view applies it.
[Collection("Headless")]
public sealed class UsageViewsAnimationTests(HeadlessSession headless)
{
    private const ProviderKind Provider = ProviderKind.Zai;

    private static readonly Animation Spring = Animation.Spring(response: 0.32, dampingFraction: 0.9);
    private static readonly Animation ReducedMotion = Animation.EaseOut(duration: 0.18);

    private static PlanLimits.ResetCredits Cards(long available) =>
        Resets(available, Credit("FIVE_HOUR:1", title: "5-hour reset"), Credit("WEEK:3", title: "Weekly reset"));

    /// <summary>The animation current at each change of <paramref name="panel"/>'s children.</summary>
    private static List<Animation?> Watch(StackPanel panel)
    {
        var seen = new List<Animation?>();
        panel.Children.CollectionChanged += (_, _) => seen.Add(Transaction.Current);
        return seen;
    }

    /// <summary>The animation current whenever the decorator is given a new child.</summary>
    private static List<Animation?> Watch(Decorator view)
    {
        var seen = new List<Animation?>();
        view.PropertyChanged += (_, e) => { if (e.Property == Decorator.ChildProperty) seen.Add(Transaction.Current); };
        return seen;
    }

    // ------------------------------------------------------------------ PR-USAGE-048 limits

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-USAGE-048")] // :78 .animation(Chrome.panelSlide, value: limits); Chrome.swift:100-104 spring 0.32 / 0.9, or ease-out 0.18 with reduce motion
    public void A_different_limits_value_is_applied_with_the_panel_slide(bool reduceMotion) => headless.OnUiThread(() =>
    {
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(planName: "Coding Plan Pro", windows: [Meter("w", "5-hour limit", 25)]));
        var view = rig.Show(new PlanLimitsView(Provider));
        var seen = Watch(Assert.IsType<StackPanel>(view.Child));

        rig.PublishLimits(Provider, Limits(planName: "Coding Plan Pro", windows: [Meter("w", "5-hour limit", 60)]));

        Assert.NotEmpty(seen);
        Assert.All(seen, animation => Assert.Equal(reduceMotion ? ReducedMotion : Spring, animation));
        Assert.Contains("60%", Texts(view));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-048")] // "Equality is structural over the whole PlanLimits value, so an identical re-read animates nothing."
    public void An_update_that_leaves_the_limits_equal_animates_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(windows: [Meter("w", "5-hour limit", 25)]));
        var view = rig.Show(new PlanLimitsView(Provider));
        var seen = Watch(Assert.IsType<StackPanel>(view.Child));

        rig.SetLoadingLimits(Provider, true); // the view rebuilds (the spinner), the limits are the same value
        rig.SetLoadingLimits(Provider, false);
        rig.PublishLimits(Provider, Limits(windows: [Meter("w", "5-hour limit", 25)])); // an identical re-read

        Assert.NotEmpty(seen); // it did rebuild
        Assert.All(seen, animation => Assert.Null(animation));
    });

    // ------------------------------------------------------------------ PR-USAGE-045 the block

    [Fact]
    [Trait("Req", "PR-USAGE-045")] // :254 .animation(Chrome.panelSlide, value: phase): a new phase is applied with the panel slide
    public void A_phase_change_is_applied_with_the_panel_slide() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = rig.Show(new BankedResetsView(ProviderKind.Codex, Cards(2)));
        var seen = Watch(block);

        Click(ButtonTitled(All<BankedResetRow>(block)[0], "Use reset"));
        Assert.Equal([Spring], seen);

        seen.Clear();
        Click(ButtonTitled(All<BankedResetRow>(block)[0], "Cancel"));
        Assert.Equal([Spring], seen);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-045")] // :254 the same with Reduce Motion on: Chrome.panelSlide is then a 0.18-second ease-out
    public void A_phase_change_with_reduce_motion_is_a_0_18_second_ease_out() => headless.OnUiThread(() =>
    {
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        using var rig = new UsageViewsRig();
        var block = rig.Show(new BankedResetsView(ProviderKind.Codex, Cards(2)));
        var seen = Watch(block);

        Click(ButtonTitled(All<BankedResetRow>(block)[0], "Use reset"));

        Assert.Equal([ReducedMotion], seen);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-045")] // :255 .animation(Chrome.panelSlide, value: credits): "a count change after a spend animates the rows and the header count together"
    public void A_credits_change_is_applied_with_the_panel_slide_to_the_rows_and_the_header_count_together() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(windows: [Meter("w", "5-hour limit", 25)], resets: Cards(2)));
        var view = rig.Show(new PlanLimitsView(Provider));
        var block = All<BankedResetsView>(view).Single();
        var seen = Watch(block);

        rig.PublishLimits(Provider, Limits(windows: [Meter("w", "5-hour limit", 25)], resets: Resets(1, Credit("WEEK:3", title: "Weekly reset"))));

        Assert.Same(block, All<BankedResetsView>(view).Single()); // the same block, its input changed
        Assert.Equal([Spring], seen); // one rebuild: the header count and the rows change in the same pass
        Assert.Contains("· 1 available", Texts(block));
        Assert.Single(All<BankedResetRow>(block));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-045")] // :251 .transition(.opacity.combined(with: .move(edge: .top))): a row fades in or out while moving from or towards the top edge
    public void A_row_enters_and_leaves_with_opacity_combined_with_a_move_from_the_top_edge() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = rig.Show(new BankedResetsView(ProviderKind.Codex, Cards(3))); // two listed, one placeholder

        var rows = All<BankedResetRow>(block);
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.Equal(SwiftTransition.Opacity.Combined(SwiftTransition.Move(Edge.Top)), TransitionOf(row)));
    });

    // ------------------------------------------------------------------ the blur-replace swaps

    [Fact]
    [Trait("Req", "UI-PROVREG-025")] // :273 .transition(.blurReplace) on the outcome line
    [Trait("Req", "UI-PROVREG-026")] // :279 .transition(.blurReplace) on the failure line
    public void The_outcome_and_failure_lines_swap_with_blur_replace() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = rig.Show(new BankedResetsView(ProviderKind.Codex, Cards(2)));

        SetPhase(block, new BankedResetsView.Phase.Done(PlanLimits.ResetOutcome.Reset));
        rig.Settle();
        Assert.Equal(SwiftTransition.BlurReplace, TransitionOf(Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(block.Child).Children[1])));

        SetPhase(block, new BankedResetsView.Phase.Failed("No."));
        rig.Settle();
        Assert.Equal(SwiftTransition.BlurReplace, TransitionOf(Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(block.Child).Children[1])));
    });

    [Fact]
    [Trait("Req", "UI-USAGE-026")] // :364 Use reset .transition(.blurReplace)
    [Trait("Req", "UI-PROVREG-029")] // "tra..." the button swaps in and out
    [Trait("Req", "UI-USAGE-029")] // :351, :359 "blur-replace transition in and out" for the confirm pair and the Resetting pair
    [Trait("Req", "PR-USAGE-040")] // :352 "The swap animates with a blur-replace transition."
    public void The_controls_of_a_row_swap_with_blur_replace() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = rig.Show(new BankedResetsView(ProviderKind.Codex, Cards(2)));

        var use = (Control)ButtonTitled(All<BankedResetRow>(block)[0], "Use reset").Parent!;
        Assert.Equal(SwiftTransition.BlurReplace, TransitionOf(use));

        Click(ButtonTitled(All<BankedResetRow>(block)[0], "Use reset"));
        var pair = (Control)((Control)ButtonTitled(All<BankedResetRow>(block)[0], "Cancel").Parent!).Parent!;
        Assert.Equal(SwiftTransition.BlurReplace, TransitionOf(pair));

        SetPhase(block, new BankedResetsView.Phase.Applying("FIVE_HOUR:1"));
        rig.Settle();
        var progress = (Control)Assert.Single(Spinners(All<BankedResetRow>(block)[0])).Parent!;
        Assert.Equal(SwiftTransition.BlurReplace, TransitionOf(progress));
    });
}
