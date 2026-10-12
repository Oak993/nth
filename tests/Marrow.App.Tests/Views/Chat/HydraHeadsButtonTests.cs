using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The count capsule at the strip's left end (App/Views/Chat/HydraPanel.swift:216-257, :290-296): the Hydra mark, the head count,
/// a mini spinner while any head runs and a chevron on a 32-point glass capsule; its tooltip and label; its foreground; and the
/// tap that toggles the heads popover, anchored below.
/// </summary>
[Collection("Headless")]
public sealed class HydraHeadsButtonTests(HeadlessSession headless)
{
    private void Run(Action<HydraPanelContext, ContentRecordingPresenter> test) => ChatHeadlessFixtures.Run(headless, () =>
    {
        var presenter = new ContentRecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        using var context = new HydraPanelContext(visible: true);
        context.Tick(1);
        test(context, presenter);
    });

    private static Control Capsule(HydraPanelContext c, string tip = "2 heads") => c.WithTip(tip);

    private static Color Resolve(SwiftColor color) => color.Resolve(ThemeVariant.Light);

    private static Color Foreground(Control control) => Assert.IsAssignableFrom<ISolidColorBrush>(
        Assert.IsAssignableFrom<TextBlock>(control).Foreground).Color;

    private static TextBlock Count(Control capsule) => Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(capsule));

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:232-249 HStack(spacing: 6), padded horizontally 12, framed to 28, padded vertically 2: a 32-point capsule
    [Trait("Req", "UI-HYDRA-013")] // HydraPanel.swift:232-249 mark, count, spinner, chevron, spaced 6 apart, 12 in from the capsule's ends and centred in its 32 points
    public void The_capsule_holds_the_mark_the_count_the_spinner_and_the_chevron_six_points_apart_and_twelve_in_from_its_ends() => Run((c, _) =>
    {
        var capsule = c.Where(Capsule(c));
        Assert.Equal(32, capsule.Height);
        var mark = c.Where(Assert.Single(LogicalQuery.SelfAndDescendants<HydraMarkImage>(Capsule(c))));
        var count = c.Where(Count(Capsule(c)));
        var spinner = c.Where(Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(Capsule(c))));
        var chevron = c.Where(Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(Capsule(c)), i => i.SymbolName == "chevron.down"));
        Assert.Equal(capsule.X + 12, mark.X, 0.01);
        Assert.Equal(mark.Right + 6, count.X, 0.01);
        Assert.Equal(count.Right + 6, spinner.X, 0.01);
        Assert.Equal(spinner.Right + 6, chevron.X, 0.01);
        Assert.Equal(capsule.Right - 12, chevron.Right, 0.01);
        // An HStack centres its parts: none is stretched to the capsule's 28 content points, and each sits on its midline.
        foreach (var part in new[] { mark, count, spinner, chevron })
        {
            Assert.Equal(capsule.Center.Y, part.Center.Y, 0.51);
            Assert.True(part.Height < 20, $"{part} is centred, not stretched");
        }
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:291-296 HydraSpokesMark: the Hydra mark at 15 by 15 in primaryText at 0.9
    [Trait("Req", "UI-HYDRA-013")] // HydraPanel.swift:294 .foregroundStyle(Chrome.primaryText.opacity(0.9)); :295 frame 15 by 15
    public void The_mark_is_fifteen_by_fifteen_in_primary_text_at_ninety_percent() => Run((c, _) =>
    {
        var mark = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMarkImage>(Capsule(c)));
        var rect = c.Where(mark);
        Assert.Equal(new Size(15, 15), rect.Size);
        var image = Assert.IsType<SwiftImage>(mark.Child);
        Assert.Equal(Resolve(Chrome.PrimaryText.Opacity(0.9)), Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:234-235 Text(verbatim: "\(heads.count)").font(.system(size: 12.5, weight: .semibold).monospacedDigit())
    [Trait("Req", "UI-HYDRA-013")] // HydraPanel.swift:235 the count is .system(size: 12.5, weight: .semibold) with monospaced digits
    public void The_count_is_the_head_count_in_twelve_and_a_half_point_semibold_monospaced_digits() => Run((c, _) =>
    {
        var count = Count(Capsule(c));
        Assert.Equal("2", count.Text);
        Assert.Equal(12.5, count.FontSize);
        Assert.Equal(FontWeight.SemiBold, count.FontWeight);
        Assert.Contains(count.FontFeatures!, f => f.Tag == "tnum" && f.Value == 1);
        c.InPanel(c.Second, false);
        c.Tick(1);
        Assert.Equal("1", Count(Capsule(c, "1 head")).Text);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:239-241 Image(systemName: "chevron.down").font(Chrome.chevronFont).foregroundStyle(Chrome.secondaryText)
    [Trait("Req", "UI-HYDRA-013")] // Chrome.swift:30 chevronFont .system(size: 8, weight: .bold); secondary text is primary text at 0.7
    public void The_chevron_is_an_eight_point_bold_down_arrow_in_secondary_text() => Run((c, _) =>
    {
        var chevron = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(Capsule(c)), i => i.SymbolName == "chevron.down");
        Assert.Equal(SwiftFont.System(8, SwiftFont.Weight.Bold), SwiftImage.GetFont(chevron));
        Assert.Equal(Resolve(Chrome.SecondaryText), Assert.IsAssignableFrom<ISolidColorBrush>(chevron.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:236-238 if running > 0 { MiniSpinner() }; Calculi WU6 (ED-NEW-calculi-wu6-1): the quiet Tripus beads
    [Trait("Req", "UI-HYDRA-013")] // UI-HYDRA-013 states: any head running, the working mark between the count and the chevron
    public void The_spinner_shows_while_any_head_runs_and_only_then() => Run((c, _) =>
    {
        var beads = Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(Capsule(c)));
        Assert.Equal(Marrow.UI.Port.Nth.Beads.NthBeadsPlace.HydraChip, beads.Place);
        Assert.True(beads.Beads.Quiet); Assert.True(beads.Beads.Ambient);
        Assert.Equal(Marrow.UI.Port.Nth.Beads.NthBeadsState.Hydra, beads.Beads.Current.State);
        Assert.Equal(1, beads.Beads.Current.Turn);
        c.Status(c.First, HydraHeadInfo.Status.Completed);
        // the capsule is rebuilt on each change, but the beads are the same ones and go on (no second entrance)
        Assert.Same(beads, Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(Capsule(c)))); // one still runs
        Assert.Equal(1, beads.Beads.Current.Turn); Assert.True(beads.IsRunning);
        c.Status(c.Second, HydraHeadInfo.Status.Failed);
        Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(Capsule(c)));
        c.Status(c.Second, HydraHeadInfo.Status.Running);
        Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(Capsule(c)));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:254-257 .help and .accessibilityLabel "1 head" or "N heads", .accessibilityAddTraits(.isButton), .accessibilityAction { isPresented.toggle() }
    [Trait("Req", "UI-HYDRA-013")] // UI-HYDRA-013 interactions: tooltip exactly "1 head" or "N heads"; labelled, button trait, a toggle action
    public void The_tooltip_and_label_are_one_head_or_n_heads_and_the_button_trait_and_toggle_action_open_the_popover() => Run((c, presenter) =>
    {
        var capsule = Capsule(c);
        Assert.Equal("2 heads", AutomationProperties.GetName(capsule));
        var peer = ControlAutomationPeer.CreatePeerForElement(capsule);
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        Assert.Equal("2 heads", peer.GetName());
        peer.GetProvider<IInvokeProvider>()!.Invoke();
        c.Drain();
        Assert.Single(presenter.Shown());
        peer.GetProvider<IInvokeProvider>()!.Invoke();
        c.Drain();
        Assert.Empty(presenter.Shown());

        c.InPanel(c.Second, false);
        c.Tick(1);
        Assert.DoesNotContain("2 heads", c.Tips());
        var single = Capsule(c, "1 head");
        Assert.Equal("1 head", AutomationProperties.GetName(single));
        Assert.Equal("1 head", ControlAutomationPeer.CreatePeerForElement(single).GetName());
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:243, :251-253 foreground primaryText at 1 when hovering or presented, 0.92 otherwise, animated with Chrome.hover
    [Trait("Req", "UI-HYDRA-013")] // UI-HYDRA-013 states: hover or popover open, the foreground rises to full, animated with Chrome.hover
    public void The_foreground_is_ninety_two_percent_and_rises_to_full_while_hovered_or_presented() => Run((c, presenter) =>
    {
        var resting = Resolve(Chrome.PrimaryText.Opacity(0.92));
        var full = Resolve(Chrome.PrimaryText.Opacity(1));
        var count = Count(Capsule(c));
        Assert.Equal(resting, Foreground(count));

        c.Hover(Capsule(c));
        c.Tick(Chrome.Hover.Duration / 2);
        var midway = Foreground(count);
        Assert.NotEqual(resting, midway); // animated, not a jump (and not yet there)
        Assert.NotEqual(full, midway);
        c.Tick(Chrome.Hover.Duration);
        Assert.Equal(full, Foreground(count));

        c.HoverAway();
        c.Tick(Chrome.Hover.Duration * 2);
        Assert.Equal(resting, Foreground(count));

        c.Tap(Capsule(c)); // presented: full even with the pointer over the popover rather than the capsule
        c.HoverAway();
        c.Tick(Chrome.Hover.Duration * 2);
        Assert.Single(presenter.Shown());
        Assert.Equal(full, Foreground(count));

        c.Tap(Capsule(c));
        c.HoverAway();
        c.Tick(Chrome.Hover.Duration * 2);
        Assert.Empty(presenter.Shown());
        Assert.Equal(resting, Foreground(count));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:250, :258 a tap toggles isPresented; .popover(isPresented:, arrowEdge: .bottom)
    [Trait("Req", "UI-HYDRA-013")] // UI-HYDRA-013 interactions: click toggles the heads popover, anchored below
    public void A_tap_toggles_the_heads_popover_anchored_below_the_capsule() => Run((c, presenter) =>
    {
        Assert.Empty(presenter.Shown());
        c.Tap(Capsule(c));
        var popup = Assert.Single(presenter.Shown()).Popup;
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        Assert.True(popup.PlacementTarget is { } target && (target == Capsule(c) || Capsule(c).GetLogicalAncestors().Contains(target)));
        Assert.IsType<PopoverMenu>(popup.Child);
        c.Tap(Capsule(c));
        Assert.Empty(presenter.Shown());
        c.Tap(Capsule(c));
        Assert.Single(presenter.Shown());
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:250 a tap is a press and a release on the capsule: one that began or ended elsewhere toggles nothing
    public void A_press_that_did_not_begin_and_end_on_the_capsule_does_not_toggle_the_popover() => Run((c, presenter) =>
    {
        var inside = c.Where(Capsule(c)).Center;
        var away = new Point(210, 230);
        // Pressed on the capsule, released elsewhere.
        c.Window.MouseMove(inside, RawInputModifiers.None);
        c.Window.MouseDown(inside, MouseButton.Left, RawInputModifiers.None);
        c.Window.MouseMove(away, RawInputModifiers.None);
        c.Window.MouseUp(away, MouseButton.Left, RawInputModifiers.None);
        c.Drain();
        Assert.Empty(presenter.Shown());
        // Pressed elsewhere, released on the capsule.
        c.Window.MouseDown(away, MouseButton.Left, RawInputModifiers.None);
        c.Window.MouseMove(inside, RawInputModifiers.None);
        c.Window.MouseUp(inside, MouseButton.Left, RawInputModifiers.None);
        c.Drain();
        Assert.Empty(presenter.Shown());
        // A right press toggles nothing either.
        c.Window.MouseDown(inside, MouseButton.Right, RawInputModifiers.None);
        c.Window.MouseUp(inside, MouseButton.Right, RawInputModifiers.None);
        c.Drain();
        Assert.Empty(presenter.Shown());
        c.Tap(Capsule(c));
        Assert.Single(presenter.Shown());
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-173")] // HydraPanel.swift:249 .chromeGlassCapsule(); Chrome.swift:179-181 .glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3)).interactive(), in: Capsule(style: .continuous))
    [Trait("Req", "UI-HYDRA-013")] // UI-HYDRA-013 geometry: hit-shaped as a continuous Capsule; drawn as a chrome glass capsule
    public void The_capsule_is_drawn_as_a_chrome_glass_capsule_that_presses_like_a_native_control() => Run((c, _) =>
    {
        var capsule = Capsule(c);
        var surface = Assert.IsAssignableFrom<Panel>(capsule.Parent);
        var glass = Assert.Single(surface.Children.Select(SwiftGlass.GetEffect).OfType<GlassEffect>());
        Assert.Equal(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.GlassTint, 0.3).Interactive(), glass);
        // The whole capsule takes the press: its own surface is hit-testable across its full 32 points.
        Assert.NotNull(Assert.IsAssignableFrom<Border>(capsule).Background);
        Assert.Equal(new CornerRadius(16), Assert.IsAssignableFrom<Border>(capsule).CornerRadius);
    });
}
