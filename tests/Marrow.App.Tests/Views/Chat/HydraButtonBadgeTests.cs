using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The badge above the Hydra mark (App/Views/Chat/HydraButton.swift:52-73, :620-646): the sending pulse for 0.9 s when the
/// running count rises, then the count, in one capsule that never jumps.
/// </summary>
[Collection("Headless")]
public sealed class HydraButtonBadgeTests(HeadlessSession headless)
{
    private void Run(Action<HydraButtonRig> body, bool attach = true) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var rig = new HydraButtonRig(attach: attach);
        body(rig);
    });

    private static bool Pulses(HydraSendWorkingBadge badge) => LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(badge).Any();

    private static string? Count(HydraSendWorkingBadge badge) => LogicalQuery.SelfAndDescendants<TextBlock>(badge).SingleOrDefault()?.Text;

    private static Border Capsule(HydraSendWorkingBadge badge) => Assert.IsType<Border>(badge.Child);

    private static Color Tint(HydraSendWorkingBadge badge) => Assert.IsAssignableFrom<ISolidColorBrush>(Capsule(badge).Background).Color;

    [Fact]
    [Trait("Req", "PR-HYDRA-161")] // HydraButton.swift:64-73, :626-629 a head goes out: the pulse for 0.9 s, then the count
    [Trait("Req", "UI-HYDRA-002")] // HydraButton.swift:626-633
    public void A_head_going_out_shows_the_mini_spinner_for_point_nine_seconds_and_then_the_count()
    {
        Run(rig =>
        {
            Assert.Null(rig.Badge);
            rig.AddHead(HydraHeadInfo.Status.Running);

            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            Assert.True(Pulses(badge));
            Assert.Null(Count(badge));
            rig.Tick(0.5);
            Assert.Same(badge, rig.Badge); // the same capsule throughout: it morphs in place
            Assert.True(Pulses(badge));
            rig.Tick(0.35);
            Assert.True(Pulses(badge)); // 0.85 s: not yet
            rig.Tick(0.1);
            Assert.Same(badge, rig.Badge);
            Assert.False(Pulses(badge));
            Assert.Equal("1", Count(badge));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-161")] // HydraButton.swift:64-67 isSending is set only when the running count RISES
    public void The_pulse_follows_a_rising_count_only_and_a_falling_count_shows_at_once()
    {
        Run(rig =>
        {
            var first = rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(1);
            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            Assert.Equal("1", Count(badge));

            var second = rig.AddHead(HydraHeadInfo.Status.Running); // 1 -> 2: rises
            Assert.True(Pulses(badge));
            rig.Tick(1);
            Assert.Equal("2", Count(badge));

            rig.SetHeadStatus(second, HydraHeadInfo.Status.Completed); // 2 -> 1: falls, no pulse
            Assert.False(Pulses(badge));
            Assert.Equal("1", Count(badge));

            rig.SetHeadStatus(first, HydraHeadInfo.Status.Completed); // the last one back: the badge goes
            rig.Tick(1);
            Assert.Null(rig.Badge);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-161")] // HydraButton.swift:64-67 .onChange(of:) does not fire for the value the view appeared with
    public void Heads_already_working_when_the_mark_appears_show_the_count_without_a_pulse()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Show();

            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            Assert.False(Pulses(badge));
            Assert.Equal("1", Count(badge));
        }, attach: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-161")] // HydraButton.swift:68-73 .task(id: isSending): a second rise during the pulse does not restart it
    public void A_second_head_during_the_pulse_does_not_extend_it()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(0.5);
            rig.AddHead(HydraHeadInfo.Status.Running);
            Assert.True(Pulses(Assert.IsType<HydraSendWorkingBadge>(rig.Badge)));

            rig.Tick(0.45); // 0.95 s after the first
            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            Assert.False(Pulses(badge));
            Assert.Equal("2", Count(badge));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-161")] // HydraButton.swift:640 the tint is glass while sending and accent while counting
    [Trait("Req", "UI-HYDRA-002")] // HydraButton.swift:640
    public void The_capsule_is_the_glass_tint_while_sending_and_the_accent_while_counting_and_crossfades()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(0.3);
            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            var theme = badge.ActualThemeVariant;
            var glass = Chrome.GlassTint.Resolve(theme);
            var accent = Chrome.Accent.Resolve(theme);
            Assert.NotEqual(glass, accent);
            Assert.Equal(glass, Tint(badge));

            rig.Tick(0.6 + 0.05); // the pulse ends at 0.9 s; 0.05 s into the panel-slide crossfade
            var middle = Tint(badge);
            Assert.NotEqual(glass, middle);
            Assert.NotEqual(accent, middle);

            rig.Tick(2);
            Assert.Equal(accent, Tint(badge));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-002")] // HydraButton.swift:629-633 the count: caption2 bold, monospaced digits, white
    public void The_count_is_bold_caption2_with_monospaced_digits_in_white()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(1);
            var count = LogicalQuery.SelfAndDescendants<TextBlock>(Assert.IsType<HydraSendWorkingBadge>(rig.Badge)).Single();

            var expected = SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Bold).MonospacedDigit().Resolve();
            Assert.Equal(10, count.FontSize); // caption2 is 10 points
            Assert.Equal(FontWeight.Bold, count.FontWeight);
            Assert.Equal(expected.FontFamily, count.FontFamily);
            Assert.Contains(count.FontFeatures!, feature => feature.Tag == "tnum" && feature.Value == 1);
            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(count.Foreground).Color);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-002")] // HydraButton.swift:55, :635-637 offset(3, -3); at least 14 by 14 of content, padded 4 and 1; the same size in both states
    public void The_capsule_sits_three_points_out_of_the_top_right_corner_at_one_size_in_both_states()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(0.8); // the arrival has settled, the pulse has not ended
            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            Assert.True(Pulses(badge));
            var sending = Capsule(badge).Bounds.Size;
            var origin = badge.TranslatePoint(default, rig.Mark)!.Value;

            // the content's 14 by 14 minimum, plus 4 either side and 1 above and below
            Assert.Equal(new Size(22, 16), sending);
            Assert.Equal(rig.Mark.Bounds.Width + 3, origin.X + sending.Width, 0.01); // trailing edge 3 points past the mark's
            Assert.Equal(-3, origin.Y, 0.01); // top edge 3 points above it

            rig.Tick(1);
            Assert.Equal(sending, Capsule(badge).Bounds.Size);
            Assert.Equal(origin, badge.TranslatePoint(default, rig.Mark)!.Value);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-002")] // HydraButton.swift:643-644 the tooltip, and hidden from accessibility
    public void The_badge_has_its_own_tooltip_and_is_hidden_from_accessibility()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(1);
            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            Assert.Equal("1 head working · Show or hide the Hydra panel", ToolTip.GetTip(badge));
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(badge));

            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(1);
            Assert.Equal("2 heads working · Show or hide the Hydra panel", ToolTip.GetTip(Assert.IsType<HydraSendWorkingBadge>(rig.Badge)));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-002")] // HydraButton.swift:643 the sending pulse of a delegating lead has no head yet: "0 heads working"
    public void The_delegation_pulse_reads_no_count_but_the_zero_form_of_the_tooltip()
    {
        Run(rig =>
        {
            var turn = Guid.NewGuid();
            rig.SetRunning(true);
            rig.SetEntries(HydraButtonRig.Tool("tool-" + Guid.NewGuid().ToString("N"), turn, ToolCall.Kind.Agent, ToolCall.Status.Running));

            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            Assert.True(Pulses(badge));
            Assert.Equal("0 heads working · Show or hide the Hydra panel", ToolTip.GetTip(badge));
            rig.Tick(5); // delegating, not sending: the pulse is not the 0.9 s one and does not end by itself
            Assert.True(Pulses(Assert.IsType<HydraSendWorkingBadge>(rig.Badge)));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-002")] // HydraButton.swift:59 the count change runs on the spring response 0.3, damping 0.7
    [Trait("Req", "PR-HYDRA-161")] // HydraButton.swift:56, :59 the badge arrives scaled and faded on that spring
    public void The_badge_arrives_scaled_and_faded_on_the_count_spring()
    {
        Run(rig =>
        {
            var spring = Animation.Spring(response: 0.3, dampingFraction: 0.7).Progress(0.05);
            var slide = Animation.Spring(response: 0.32, dampingFraction: 0.9).Progress(0.05);
            Assert.True(Math.Abs(spring - slide) > 0.02);

            rig.AddHead(HydraHeadInfo.Status.Running);
            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            Assert.Equal(0, HydraTransitionProbe.Opacity(badge, rig.Mark), 0.01); // arrives from nothing: .scale.combined(with: .opacity)
            rig.Tick(0.05);

            Assert.Equal(spring, HydraTransitionProbe.Opacity(badge, rig.Mark), 0.01);
            Assert.Equal(spring, HydraTransitionProbe.Scale(Capsule(badge), rig.Mark), 0.01); // default .scale grows from nothing
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-161")] // HydraButton.swift:627 the sending pulse; Calculi WU6 (ED-NEW-calculi-wu6-1) replaces MiniSpinner(cellSize: 2.4) with the quiet Tripus beads
    public void The_sending_pulse_is_the_quiet_Tripus_beads_at_12_DIP_inside_the_badges_14_point_content()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(0.3);
            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            var spinner = LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(badge).Single();
            Assert.Equal(new Size(12, 12), spinner.DesiredSize);
            Assert.True(spinner.Beads.Quiet);
            Assert.True(spinner.Beads.Ambient);
            Assert.Equal(Marrow.UI.Port.Nth.Beads.NthBeadsState.Hydra, spinner.Beads.Current.State);
            Assert.True(spinner.IsRunning);
            var content = LogicalQuery.SelfAndDescendants<Border>(badge).Single(border => ReferenceEquals(border.Child, spinner));
            Assert.True(content.DesiredSize.Width >= 14 && content.DesiredSize.Height >= 14); // :635 minWidth/minHeight 14, never grown by the beads' overhang
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-161")] // HydraButton.swift:53 the badge is there while sending, though no head is running any more
    public void The_pulse_outlasts_a_head_that_finishes_inside_it_and_then_the_badge_goes()
    {
        Run(rig =>
        {
            var head = rig.AddHead(HydraHeadInfo.Status.Running);
            rig.SetHeadStatus(head, HydraHeadInfo.Status.Completed); // running 1 -> 0 inside the 0.9 s
            rig.Tick(0.5);
            var badge = Assert.IsType<HydraSendWorkingBadge>(rig.Badge);
            Assert.True(Pulses(badge));

            rig.Tick(2);
            Assert.Null(rig.Badge);
        });
    }
}
