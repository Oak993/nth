using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

using Capsule = (Avalonia.Controls.StackPanel Content, Avalonia.Controls.Border Padded, Avalonia.Controls.Panel GlassHost, Avalonia.Controls.Grid Stage);

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The stage capsule across the top of the head panel (App/Views/Chat/HydraPanel.swift:136-165, B3 52bc792): an HStack of the
/// glyph, the name, the provider's mark, the status word and the elapsed time, padded 12 by 2, framed to a content height of 28,
/// fixed size, drawn by chromeGlassCapsule with the drag handle laid over it.
/// </summary>
[Collection("Headless")]
public sealed class HydraPanelStageCapsuleTests(HeadlessSession headless)
{
    private void Run(Action<HydraPanelContext> test) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var context = new HydraPanelContext(visible: true);
        context.Tick(1); // the soft-appear of the stage capsule has settled
        test(context);
    });

    private static Capsule CapsuleOf(HydraPanelContext c)
    {
        return CapsulesOf(c).Single();
    }

    // More than one while a head change fades the old capsule out; the newest is last.
    private static Capsule[] CapsulesOf(HydraPanelContext c) => c.Find<HydraGlyph>().Where(g => g.Bounds.Width == 16).Select(glyph =>
    {
        var content = Assert.IsType<StackPanel>(glyph.Parent);
        var padded = Assert.IsType<Border>(content.Parent);
        var glassHost = Assert.IsType<Panel>(padded.Parent);
        var stage = Assert.IsType<Grid>(glassHost.Parent);
        return (content, padded, glassHost, stage);
    }).ToArray();

    private static Color Ink(Control on, SwiftColor expected) => expected.Resolve(on.ActualThemeVariant);

    private static Color Fill(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    // Source :158 `.chromeGlassCapsule()`: the capsule is the shared glass surface (a hit-test-free capsule fill behind the content, carrying
    // the glass effect off a glass panel), as the heads button beside it is, not a flat tint of its own.
    [Fact]
    [Trait("Req", "UI-HYDRA-019")] // HydraPanel.swift:154-163: spacing 6, padding 12/2, 28 content height (32 tall), chrome glass capsule, drag handle overlaid
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-HYDRA-019: geometry and the glass surface; type, colour, states and tooltip are the tests below
    public void The_stage_capsule_is_a_chrome_glass_capsule_32_tall_padded_12_by_2_with_the_drag_handle_over_it() => Run(c =>
    {
        var capsule = CapsuleOf(c);
        Assert.Equal(6, capsule.Content.Spacing);
        Assert.Equal(new Thickness(12, 2), capsule.Padded.Padding);
        Assert.Equal(32, capsule.Padded.Bounds.Height); // 28 of content plus 2 above and 2 below
        Assert.Equal(32, c.Where(capsule.GlassHost).Height);

        // The same surface as the heads button (chromeGlassCapsule on a padded, 32-tall capsule).
        Border Fill(Panel host) => Assert.IsType<Border>(host.Children[0]);
        var heads = Assert.IsType<Panel>(c.WithTip("2 heads").Parent);
        var headsFill = Fill(heads);
        var fill = Fill(capsule.GlassHost);
        Assert.False(fill.IsHitTestVisible); // the glass takes no presses; the drag handle over it does
        Assert.Equal(headsFill.CornerRadius, fill.CornerRadius);
        Assert.Equal(new CornerRadius(16), fill.CornerRadius); // a capsule: half of 32
        Assert.Equal(SwiftGlass.GetEffect(headsFill) is null, SwiftGlass.GetEffect(fill) is null);
        if (SwiftGlass.GetEffect(fill) is { } effect)
        {
            Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind);
            Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style);
        }
        Assert.Same(capsule.Padded, capsule.GlassHost.Children[1]); // the content sits on the glass

        // The drag handle is laid over the whole capsule.
        Assert.Equal(2, capsule.Stage.Children.Count);
        Assert.Same(capsule.GlassHost, capsule.Stage.Children[0]);
        var handle = Assert.IsAssignableFrom<Control>(capsule.Stage.Children[1]);
        Assert.Equal("PanelDragHandle", handle.GetType().Name);
        Assert.Equal(c.Where(capsule.GlassHost), c.Where(handle));
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-019")] // HydraPanel.swift:137-152: name 12.5 medium primary at 0.92, status callout 12 secondary, elapsed subheadline 11 monospaced digits secondary, glyph 16, order
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-HYDRA-019: type and colour of the running capsule
    public void The_running_capsule_reads_glyph_name_provider_mark_working_and_elapsed_in_the_source_type_and_colours() => Run(c =>
    {
        var capsule = CapsuleOf(c);
        var parts = capsule.Content.Children;
        Assert.Equal(5, parts.Count);
        var glyph = Assert.IsType<HydraGlyph>(parts[0]);
        var name = Assert.IsType<HydraNameText>(parts[1]);
        var mark = Assert.IsType<ProviderIcon>(parts[2]);
        var status = Assert.IsType<TextBlock>(parts[3]);
        var elapsed = Assert.IsType<HydraElapsedTime>(parts[4]);
        Assert.Equal(16, glyph.Bounds.Width);
        Assert.Equal(16, glyph.Bounds.Height);
        Assert.Equal(12, mark.Size);

        var nameBlock = Assert.IsType<TextBlock>(name.Child);
        var nameRun = Assert.IsType<Run>(Assert.Single(nameBlock.Inlines!)); // StyledText puts the type and colour on its run
        Assert.Equal(12.5, nameRun.FontSize);
        Assert.Equal(FontWeight.Medium, nameRun.FontWeight);
        Assert.Equal(Ink(nameBlock, Chrome.PrimaryText.Opacity(0.92)), Fill(nameRun.Foreground ?? nameBlock.Foreground));

        Assert.Equal("working", status.Text);
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout).PointSize, status.FontSize);
        Assert.Equal(12, status.FontSize);
        Assert.Equal(Ink(status, Chrome.SecondaryText), Fill(status.Foreground));

        var clock = Assert.IsType<TextBlock>(elapsed.Child);
        var expected = new TextBlock();
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).MonospacedDigit().Apply(expected);
        Assert.Equal(11, clock.FontSize);
        Assert.Equal(expected.FontSize, clock.FontSize);
        Assert.Equal(expected.FontFeatures, clock.FontFeatures); // monospaced digits
        Assert.Equal(Ink(clock, Chrome.SecondaryText), Fill(clock.Foreground));
    });

    [Theory]
    [InlineData(HydraHeadInfo.Status.Running)]
    [InlineData(HydraHeadInfo.Status.Completed)]
    [Trait("Req", "UI-HYDRA-019")] // HydraPanel.swift:132 `HydraGlyph(persona:, size: 16, status:)` is a 16 point frame in an HStack (centre alignment), not a view stretched to the row
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-HYDRA-019: the glyph sits at the capsule's vertical centre
    public void The_capsules_glyph_is_centred_in_the_row_at_its_own_16_points_not_stretched_to_it(HydraHeadInfo.Status status) => Run(c =>
    {
        c.Status(c.First, status);
        c.Tick(0.2);
        var capsule = CapsuleOf(c);
        var glyph = Assert.IsType<HydraGlyph>(capsule.Content.Children[0]);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, glyph.VerticalAlignment); // the source's HStack centres its children; Avalonia's default would stretch the glyph to the row
        Assert.Equal(16, glyph.Bounds.Height);
        Assert.Equal(16, glyph.Bounds.Width);
        var centre = glyph.TranslatePoint(new Point(8, 8), capsule.Padded)!.Value;
        Assert.Equal(capsule.Padded.Bounds.Height / 2, centre.Y, 3); // on the capsule's horizontal midline
    });

    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed, "done")]
    [InlineData(HydraHeadInfo.Status.Failed, "failed")]
    [InlineData(HydraHeadInfo.Status.Stopped, "stopped")]
    [Trait("Req", "UI-HYDRA-019")] // HydraPanel.swift:146-152: a finished head says done, failed or stopped and its elapsed time stops counting
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-HYDRA-019: the finished words
    public void A_finished_capsule_says_done_failed_or_stopped_and_freezes_its_elapsed_time(HydraHeadInfo.Status finished, string word) => Run(c =>
    {
        c.Status(c.First, finished);
        c.Tick(0.2);
        var content = CapsuleOf(c).Content;
        Assert.Equal(word, Assert.IsType<TextBlock>(content.Children[3]).Text);
        var clock = Assert.IsType<TextBlock>(Assert.IsType<HydraElapsedTime>(content.Children[4]).Child);
        var frozen = clock.Text;
        Assert.False(string.IsNullOrEmpty(frozen));
        c.Tick(5);
        Assert.Equal(frozen, Assert.IsType<TextBlock>(Assert.IsType<HydraElapsedTime>(CapsuleOf(c).Content.Children[4]).Child).Text);
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-019")] // HydraPanel.swift:141-145: the provider mark's tooltip is the model's short name, or the provider's display name
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-HYDRA-019: the hover tooltip
    public void The_provider_mark_names_the_provider_when_the_head_has_no_known_model() => Run(c =>
    {
        var mark = Assert.IsType<ProviderIcon>(CapsuleOf(c).Content.Children[2]);
        Assert.Equal(c.First.Provider.DisplayName, ToolTip.GetTip(mark));
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-019")] // HydraPanel.swift:136-165 (no .id: one capsule view), :199 (.animation(liveResize.isActive ? nil : Chrome.panelSlide, value: selected?.id)): a head change keeps the one capsule and its contents change on the panel slide; a live resize does it at once
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-HYDRA-019: the head-change state (the dip, the swap and the width glide are ENGINEERING DECISIONS under OD-0076, ED-273)
    public void A_head_change_keeps_the_one_capsule_and_changes_its_contents_on_the_panel_slide_and_a_live_resize_does_it_at_once() => Run(c =>
    {
        var before = CapsuleOf(c);
        var firstWidth = before.Padded.Bounds.Width;
        var motion = Chrome.PanelSlide;
        var half = motion.Duration / 2;
        Assert.Equal(c.First.Hydra!.Persona, Assert.IsType<HydraGlyph>(before.Content.Children[0]).Persona);

        c.Select(c.Second);

        // The same capsule view throughout, never a second one and never faded as a whole: its stage, glass and drag handle stand.
        var kept = CapsuleOf(c);
        Assert.Same(before.Stage, kept.Stage);
        Assert.Same(before.Content, kept.Content);
        Assert.Equal(1, kept.Stage.Opacity);
        Assert.Equal(1, kept.Content.Opacity);
        Assert.Equal(c.First.Hydra!.Persona, Assert.IsType<HydraGlyph>(kept.Content.Children[0]).Persona); // the old head's words still on show as it begins

        // The first half of the panel slide: the old contents fade out on its ease-in, and nothing of the new head has come yet.
        c.Tick(half / 2);
        kept = CapsuleOf(c);
        Assert.Same(before.Stage, kept.Stage);
        Assert.Equal(1 - Animation.EaseIn(half).Progress(half / 2), kept.Content.Opacity, 6);
        Assert.Equal(c.First.Hydra!.Persona, Assert.IsType<HydraGlyph>(kept.Content.Children[0]).Persona);
        Assert.Equal(1, kept.Stage.Opacity);

        // The second half: the new head's contents come in on its ease-out, and the glass has eased to the width they need.
        c.Tick(half);
        kept = CapsuleOf(c);
        Assert.Same(before.Stage, kept.Stage);
        Assert.Equal(c.Second.Hydra!.Persona, Assert.IsType<HydraGlyph>(kept.Content.Children[0]).Persona);
        Assert.Equal(Animation.EaseOut(half).Progress(half / 2), kept.Content.Opacity, 6);
        Assert.Equal(1, kept.Stage.Opacity);

        c.Tick(motion.Duration);
        kept = CapsuleOf(c);
        Assert.Same(before.Stage, kept.Stage);
        Assert.Equal(1, kept.Content.Opacity);
        Assert.True(double.IsNaN(kept.Padded.Width)); // the glide is over: the capsule sizes to its contents again
        Assert.Equal(Chrome.CapsuleHeight, kept.Padded.Bounds.Height);
        var secondWidth = kept.Padded.Bounds.Width;
        Assert.Equal(c.Second.Hydra!.Persona, Assert.IsType<HydraGlyph>(kept.Content.Children[0]).Persona);
        Assert.NotEqual(firstWidth, secondWidth); // the two heads' names differ in width, so the glide below has somewhere to go

        // The width glides over the second half of the motion from the old head's width to the new one's, never past either.
        c.Select(c.First);
        c.Tick(half * 1.5);
        var middle = CapsuleOf(c).Padded.Bounds.Width;
        Assert.InRange(middle, Math.Min(firstWidth, secondWidth), Math.Max(firstWidth, secondWidth));
        Assert.NotEqual(secondWidth, middle);
        Assert.NotEqual(firstWidth, middle);
        c.Tick(motion.Duration);
        Assert.Equal(firstWidth, CapsuleOf(c).Padded.Bounds.Width);

        // A live window resize does it at once (:199 `liveResize.isActive ? nil : ...`): the next head's contents are there with no motion.
        c.Resize(true);
        c.Select(c.Second);
        var resized = CapsuleOf(c);
        Assert.Same(before.Stage, resized.Stage);
        Assert.Equal(1, resized.Content.Opacity);
        Assert.Equal(c.Second.Hydra!.Persona, Assert.IsType<HydraGlyph>(resized.Content.Children[0]).Persona);
        Assert.Equal(secondWidth, resized.Padded.Bounds.Width);
        c.Resize(false);
    });
}
