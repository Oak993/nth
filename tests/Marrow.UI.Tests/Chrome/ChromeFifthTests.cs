using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source under test: DroppyCode/UI/Chrome/Chrome.swift (B2 1fbf81e), part 2 of 4 (R-TEST-3: a new file for this part):
// PaneTopVeil :579-628, ChromeCompactTitle :630-647, SidebarSymbol :651-668, SidebarIconBadge :670-679,
// SidebarRow :681-757, RowAccessoryIcon :759-773, SoftAppearModifier/settleGlide/settleFlight :934-1002,
// ChromeCard :1006-1027 (B3 52bc792, PR-WIN-202/UI-WIN-052: gained `lazy: Bool = false`, was B2 :1006-1021),
// ChromeSection :1029-1053, ChromeRow :1055-1080, ChromeRowDivider :1082-1087 (B3 line numbers; +6 from B2's
// :1023-1047/:1049-1074/:1076-1081), ChromeQuietButtonStyle :275-280, ChromeRevealLayout :282-304 and the
// colour/motion/token block :36-120.
// Expected values are transcribed from the Swift and the records, never from the C# under test:
//   :41 veilHeight = 72; :48-67 the sidebar and card token literals; :76 primaryText = labelColor (ED-051,
//   SwiftColor.LabelColor); :79 secondaryText = primaryText.opacity(0.7) (ED-062); :98 hover 0.1s ease-out;
//   :100-104 panelSlide spring(0.32, 0.9) or 0.18s ease-out under Reduce Motion; :108 glideSpring
//   Spring(0.6, 0.86); :112-116 settleFlight = glideSpring or 0.2s ease-out under Reduce Motion;
//   :118-120 gray/blue/orange literals; :937-944 SoftAppearModifier opacity/offset; :958-961 softAppear
//   0.32s smooth; :963-988 SettleGlideModifier opacity/offset/scale/smoothstep.
// Port shape read (R-VIEW-1, R-VIEW-9, ED-095, ED-098): a ViewModifier is Body(content)/Update(body); a Layout is a
// Panel; the reduce-motion read is Chrome.ReduceMotionPlatform, overridden per test. `.accessibilityHidden(x)` is
// AccessibilityView.Raw when x, Content otherwise. Not asserted here (dependency not in the tree, see the unit
// report): SF Symbol glyphs as Lucide icons, glass, `.continuous` corner curvature and the in-flight frames of an
// animated change beyond the token-level curve each animation resolves to.
[Collection("Headless")]
public sealed class ChromeFifthTests(HeadlessSession headless)
{
    private static Window Show(Control content, ThemeVariant? theme = null, double width = 400, double height = 300)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width, Height = height, Content = content };
        if (theme is not null) window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IEnumerable<Control> SelfAndDescendants(Control root) =>
        new[] { root }.Concat(VisualQuery.Descendants<Control>(root));

    private static Color Effective(IBrush? brush)
    {
        if (brush is null) return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));
        Assert.InRange((int)actual.A, expected.A - 1, expected.A + 1);
    }

    private static Color Label(double opacity, ThemeVariant theme) => SwiftColor.LabelColor.Opacity(opacity).Resolve(theme);

    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - 1e-9, expected + 1e-9);

    /// <summary>Overrides Chrome.ReduceMotionPlatform for one call.</summary>
    private static void WithReduceMotion(bool enabled, Action body)
    {
        using var _ = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: enabled));
        body();
    }

    // ---- Tokens, :36-120 (PR-WIN-211) ----

    [Fact]
    [Trait("Req", "PR-WIN-211")] // Chrome.swift:48-67
    public void The_sidebar_and_card_tokens_match_the_source_literals()
    {
        Assert.Equal(28, Chrome.RowHeight);
        Assert.Equal(7, Chrome.RowCornerRadius);
        Assert.Equal(8, Chrome.RowHorizontalPadding);
        Assert.Equal(20, Chrome.IconSize);
        Assert.Equal(14, Chrome.SymbolSize);
        Assert.Equal(10, Chrome.GroupGap);
        Assert.Equal(10, Chrome.ListInset);
        Assert.Equal(16, Chrome.CardCornerRadius);
        Assert.Equal(20, Chrome.SectionSpacing);
        Assert.Equal(10, Chrome.SectionHeaderSpacing);
        Assert.Equal(16, Chrome.ContentHorizontalPadding);
        Assert.Equal(10, Chrome.RowControlTrailingPadding);
        Assert.Equal(16, Chrome.RowButtonLabelHeight);
    }

    // ---- PR-WIN-212: contentTopInset is 72 through an independent declaration from veilHeight ----

    [Fact]
    [Trait("Req", "PR-WIN-212")] // Chrome.swift:32, :38-41
    public void ContentTopInset_is_72_and_independent_of_VeilHeight()
    {
        Assert.Equal(72, Chrome.ContentTopInset);
        Assert.Equal(72, Chrome.VeilHeight);
        Assert.Equal(Chrome.ChromeTopPadding + Chrome.CapsuleHeight + Chrome.ContentBreathBelowChrome, Chrome.ContentTopInset);
    }

    // ---- PR-WIN-209, PR-WIN-230: derived colours ----

    [Fact]
    [Trait("Req", "PR-WIN-209")] // Chrome.swift:85-96
    public void Accent_falls_back_to_the_system_control_accent_when_the_theme_has_none() => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        try
        {
            ThemeManager.Current = AppTheme.System;
            Assert.Equal(SwiftColor.ControlAccentColor.Resolve(ThemeVariant.Light), Chrome.Accent.Resolve(ThemeVariant.Light));
            Assert.Equal(SwiftColor.ControlAccentColor.Resolve(ThemeVariant.Light), Chrome.AccentNSColor.Resolve(ThemeVariant.Light));
        }
        finally { ThemeManager.Current = original; }
    });

    [Fact]
    [Trait("Req", "PR-WIN-209")] // Chrome.swift:91-92, re-read from ThemeManager.Spec on every access
    public void Success_and_warning_re_read_the_theme_spec()
    {
        Assert.Equal(ThemeManager.Spec.Success, Chrome.Success);
        Assert.Equal(ThemeManager.Spec.Warning, Chrome.Warning);
    }

    [Fact]
    [Trait("Req", "PR-WIN-230")] // Chrome.swift:79, ED-062
    public void SecondaryText_is_primaryText_at_0_7_and_further_opacities_multiply()
    {
        AssertColor(Label(0.7, ThemeVariant.Light), Chrome.SecondaryText.Resolve(ThemeVariant.Light));
        AssertColor(Label(0.7 * 0.85, ThemeVariant.Light), Chrome.SecondaryText.Opacity(0.85).Resolve(ThemeVariant.Light));
        AssertColor(Label(0.7 * 0.75, ThemeVariant.Dark), Chrome.SecondaryText.Opacity(0.75).Resolve(ThemeVariant.Dark));
        AssertColor(Label(0.7 * 0.8, ThemeVariant.Dark), Chrome.SecondaryText.Opacity(0.8).Resolve(ThemeVariant.Dark));
    }

    [Fact]
    [Trait("Req", "PR-WIN-210")] // Chrome.swift:118-120
    public void The_three_fixed_hues_are_the_source_literals_and_do_not_vary_by_theme()
    {
        AssertColor(new Color(255, 142, 142, 147), Chrome.Gray.Resolve(ThemeVariant.Light));
        AssertColor(new Color(255, 142, 142, 147), Chrome.Gray.Resolve(ThemeVariant.Dark));
        AssertColor(new Color(255, 10, 122, 255), Chrome.Blue.Resolve(ThemeVariant.Light));
        AssertColor(new Color(255, 10, 122, 255), Chrome.Blue.Resolve(ThemeVariant.Dark));
        AssertColor(new Color(255, 255, 149, 0), Chrome.Orange.Resolve(ThemeVariant.Light));
        AssertColor(new Color(255, 255, 149, 0), Chrome.Orange.Resolve(ThemeVariant.Dark));
    }

    // ---- PR-WIN-208: the five motion tokens ----

    [Fact]
    [Trait("Req", "PR-WIN-208")] // Chrome.swift:98
    public void Hover_is_a_0_1_second_ease_out_with_no_branch()
    {
        Near(0.1, Chrome.Hover.Duration);
    }

    [Fact]
    [Trait("Req", "PR-WIN-208")] // Chrome.swift:100-104
    public void PanelSlide_is_a_spring_normally_and_a_0_18_second_ease_out_under_reduce_motion()
    {
        var expectedSpring = Marrow.UI.Port.Animation.Spring(response: 0.32, dampingFraction: 0.9);
        WithReduceMotion(false, () => Near(expectedSpring.Duration, Chrome.PanelSlide.Duration));
        WithReduceMotion(true, () => Near(0.18, Chrome.PanelSlide.Duration));
    }

    [Fact]
    [Trait("Req", "PR-WIN-208")] // Chrome.swift:108
    public void GlideSpring_is_response_0_6_damping_0_86()
    {
        Assert.Equal(0.6, Chrome.GlideSpring.Response);
        Assert.Equal(0.86, Chrome.GlideSpring.DampingRatio);
    }

    [Fact]
    [Trait("Req", "PR-WIN-208")] // Chrome.swift:112-116
    public void SettleFlight_is_the_glide_spring_normally_and_a_0_2_second_ease_out_under_reduce_motion()
    {
        WithReduceMotion(true, () => Near(0.2, Chrome.SettleFlight.Duration));
        WithReduceMotion(false, () => Near(Chrome.GlideSpring.SettlingDuration, Chrome.SettleFlight.Duration));
    }

    [Fact]
    [Trait("Req", "PR-WIN-208")] // Chrome.swift:958-961
    public void SoftAppear_is_a_0_32_second_smooth_curve_with_no_branch()
    {
        Near(Marrow.UI.Port.Animation.Smooth(duration: 0.32).Duration, Marrow.UI.Port.Animation.SoftAppear.Duration);
    }

    // ---- PR-WIN-206: SoftAppearModifier and the .softAppear transition ----

    [Fact]
    [Trait("Req", "PR-WIN-206")] // Chrome.swift:940-944
    public void SoftAppearModifier_sets_opacity_and_a_4_point_offset_by_visibility()
    {
        var probe = new LayoutProbe(new Size(10, 10));
        var visible = new SoftAppearModifier(true);
        visible.Update(probe);
        Assert.Equal(1, probe.Opacity);
        Assert.Equal(0, ((TranslateTransform)probe.RenderTransform!).Y);

        var hidden = new SoftAppearModifier(false);
        hidden.Update(probe);
        Assert.Equal(0, probe.Opacity);
        Assert.Equal(4, ((TranslateTransform)probe.RenderTransform!).Y);
    }

    [Fact]
    [Trait("Req", "PR-WIN-206")] // Chrome.swift:947-955: insertion animates on softAppear, removal is a plain 0.12s ease-out fade
    public void SoftAppear_transition_arrives_softly_and_leaves_with_a_quick_fade() => headless.OnUiThread(() =>
    {
        using var clocks = new AnimationClocks();
        var host = new Canvas();
        var window = Show(host);
        var child = new LayoutProbe(new Size(20, 20)).Transition(SwiftTransition.SoftAppear);
        SwiftTransition.Insert(host, 0, child);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(child, host.Children);
        window.Close();
    });

    // ---- PR-WIN-207: SettleGlideModifier's math, read through the public .settleGlide transition since the
    // modifier itself is a `file` type (ED-095/ED-098 skeleton decision, Chrome.swift:963). The spring's own
    // progress at each elapsed time (response 0.6, damping 0.86, GlideSpring) is the value
    // SwiftAnimationTransitionTests.cs's equivalent spring test already fixes: 0.1s -> 0.30155107726937647
    // (below the 0.45 smoothstep edge, so fade 0), 0.6s -> 1.0049814577061145 (above the 0.92 edge, so fade 1,
    // clamped), 0.8s -> settled at 1 and the control leaves the panel.
    [Fact]
    [Trait("Req", "PR-WIN-207")] // Chrome.swift:975-982: holds through the first half, 22pt drop, 4% shrink, gone by settling
    public void SettleGlide_holds_then_glides_down_and_shrinks_while_fading_out() => headless.OnUiThread(() => WithReduceMotion(false, () =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = Show(panel);
        var child = new LayoutProbe(new Size(20, 20)).Transition(SwiftTransition.SettleGlide);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        Assert.Contains(child, panel.Children);
        clocks.Frame(0.4); // the soft arrival (smooth 0.32 s) is over before the child leaves: a removal that cuts it short carries its opacity (SwiftTransitionInterruptionTests)

        SwiftTransition.Remove(panel, child); // outside withAnimation: the transition's own spring animates it
        clocks.Frame(0.1); // spring progress 0.30155107726937647, below the 0.45 smoothstep edge
        Assert.Equal(1, child.Opacity, 6); // holds through the first half
        var group1 = Assert.IsType<TransformGroup>(child.RenderTransform);
        Near(22 * 0.30155107726937647, ((TranslateTransform)group1.Children[0]).Y);
        Near(1 - 0.04 * 0.30155107726937647, ((ScaleTransform)group1.Children[1]).ScaleX);

        // 0.6s total: spring progress 1.0033822145841729 (the free spring's 1.0049814577061145 with its distance from 1 drawn in after its last crossing, ED-NEW-perf-motion-10 (ED-333 UNRESOLVED run-out, the lead's ruling 2026-10-13)),
        // above the 0.92 smoothstep edge.
        clocks.Frame(0.5);
        Assert.Equal(0, child.Opacity, 6); // gone before it would land
        var group2 = Assert.IsType<TransformGroup>(child.RenderTransform);
        Near(22 * 1.0033822145841729, ((TranslateTransform)group2.Children[0]).Y);
        Near(1 - 0.04 * 1.0033822145841729, ((ScaleTransform)group2.Children[1]).ScaleX);

        clocks.Frame(0.2); // 0.8s: past the 0.767s settling duration, the control leaves
        Assert.DoesNotContain(child, panel.Children);
        window.Close();
    }));

    [Fact]
    [Trait("Req", "PR-WIN-207")] // Chrome.swift:976: with Reduce Motion, a linear fade in place, no movement, no scaling
    public void SettleGlide_fades_linearly_in_place_under_reduce_motion() => headless.OnUiThread(() => WithReduceMotion(true, () =>
    {
        using var clocks = new AnimationClocks();
        var panel = new Canvas { Width = 300, Height = 200 };
        var window = Show(panel);
        var child = new LayoutProbe(new Size(20, 20)).Transition(SwiftTransition.SettleGlide);
        SwiftTransition.Insert(panel, 0, child);
        window.UpdateLayout();
        clocks.Frame(0.4); // the soft arrival (smooth 0.32 s) is over before the child leaves

        SwiftTransition.Remove(panel, child);
        clocks.Frame(0.1); // spring progress 0.30155107726937647; under reduce motion fade == progress, no movement
        Near(1 - 0.30155107726937647, child.Opacity);
        var group = Assert.IsType<TransformGroup>(child.RenderTransform);
        Near(0, ((TranslateTransform)group.Children[0]).Y);
        Near(1, ((ScaleTransform)group.Children[1]).ScaleX);
        window.Close();
    }));

    // ---- PR-WIN-227: ChromeQuietButtonStyle ----

    [Fact]
    [Trait("Req", "PR-WIN-227")] // Chrome.swift:277-279
    public void ChromeQuietButtonStyle_returns_the_label_unchanged()
    {
        var label = new TextBlock { Text = "x" };
        var style = new ChromeQuietButtonStyle();
        Assert.Same(label, style.MakeBody(label));
    }

    // ---- PR-WIN-228, PR-WIN-229: ChromeRevealLayout ----

    [Fact]
    [Trait("Req", "PR-WIN-228")] // Chrome.swift:294-298: the slot height is the child's natural height times fraction clamped to 0...1
    public void ChromeRevealLayout_measures_the_slot_to_the_childs_natural_height_times_fraction() => headless.OnUiThread(() =>
    {
        var child = new LayoutProbe(new Size(120, 80));
        var layout = new ChromeRevealLayout(0.25) { Children = { child } };
        var window = Show(layout);
        Assert.Equal(80 * 0.25, layout.Bounds.Height, 3);
        Assert.Equal(120, layout.Bounds.Width, 3);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-228")] // Chrome.swift:296: min(max(fraction, 0), 1)
    public void ChromeRevealLayout_clamps_fraction_outside_0_1() => headless.OnUiThread(() =>
    {
        var child = new LayoutProbe(new Size(50, 40));
        var over = new ChromeRevealLayout(2) { Children = { child } };
        Show(over);
        Assert.Equal(40, over.Bounds.Height, 3);

        var under = new ChromeRevealLayout(-1) { Children = { new LayoutProbe(new Size(50, 40)) } };
        Show(under);
        Assert.Equal(0, under.Bounds.Height, 3);
    });

    [Fact]
    [Trait("Req", "PR-WIN-229")] // Chrome.swift:300-303: the child is always placed at the origin, full natural height, never moves
    public void ChromeRevealLayout_keeps_the_child_mounted_at_full_height_whatever_the_fraction() => headless.OnUiThread(() =>
    {
        var child = new LayoutProbe(new Size(60, 50));
        var layout = new ChromeRevealLayout(0) { Children = { child } };
        var window = Show(layout);
        Assert.Contains(child, layout.Children);
        Assert.Equal(new Size(layout.Bounds.Width, 50), child.Arranged[^1]);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-229")] // Chrome.swift:289-292: fraction is the layout's animatable value
    public void ChromeRevealLayout_exposes_fraction_as_animatable_data()
    {
        var layout = new ChromeRevealLayout(0.5);
        Assert.Equal(0.5, layout.AnimatableData);
        layout.AnimatableData = 0.9;
        Assert.Equal(0.9, layout.Fraction);
    }

    // ---- PR-WIN-196, UI-CHAT-058: PaneTopVeil ----

    [Fact]
    [Trait("Req", "PR-WIN-196")] // Chrome.swift:590: `if progress > 0`
    [Trait("Req", "UI-WIN-047")] // UI/Chrome/Chrome.swift:579-627 pane top veil: progress 0 draws nothing
    [Trait("Req", "UI-CHAT-058")]
    public void PaneTopVeil_draws_nothing_at_zero_progress() => headless.OnUiThread(() =>
    {
        var model = new ChromeScrollModel();
        var veil = new PaneTopVeil(model);
        var window = Show(veil);
        Assert.Empty(VisualQuery.Descendants<Border>(veil));
        Assert.True(veil.IsHitTestVisible == false); // :624 .allowsHitTesting(false)
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-196")] // Chrome.swift:41, :622-623
    [Trait("Req", "UI-WIN-047")] // UI/Chrome/Chrome.swift:41 veilHeight 72
    [Trait("Req", "UI-CHAT-058")]
    public void PaneTopVeil_is_72_points_tall_and_its_opacity_tracks_progress() => headless.OnUiThread(() =>
    {
        var model = new ChromeScrollModel();
        model.Update(travel: 44); // progress 1 (quantized)
        var veil = new PaneTopVeil(model);
        var window = Show(veil);
        Assert.Equal(72, veil.Bounds.Height, 3);
        var host = Assert.IsAssignableFrom<Panel>(veil.GetVisualDescendants().First(v => v is Panel));
        Assert.Equal(0.08 + 0.92 * 1, host.Opacity, 3);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-196")] // Chrome.swift:587-588, :596: dark scrim = 0.14 + 0.18*progress over black
    [Trait("Req", "UI-WIN-047")] // UI/Chrome/Chrome.swift:587-588, :596 dark scrim formula with progress
    public void PaneTopVeil_dark_base_gradient_starts_at_the_scrim_alpha_over_black() => headless.OnUiThread(() =>
    {
        var model = new ChromeScrollModel();
        model.Update(travel: 44); // progress 1
        var veil = new PaneTopVeil(model);
        var window = Show(veil, ThemeVariant.Dark);
        var layers = VisualQuery.Descendants<Border>(veil).ToList();
        Assert.Equal(2, layers.Count);
        var brush = Assert.IsType<LinearGradientBrush>(layers[0].Background);
        var expected = SwiftColor.Black.Opacity(0.14 + 0.18 * 1).Resolve(ThemeVariant.Dark);
        AssertColor(expected, brush.GradientStops[0].Color);
        Assert.Equal(0, brush.GradientStops[0].Offset);
        Assert.Equal(0.28, brush.GradientStops[1].Offset);
        Assert.Equal(0.62, brush.GradientStops[2].Offset);
        Assert.Equal(1, brush.GradientStops[3].Offset);
        Assert.Equal(0, brush.GradientStops[3].Color.A); // fully clear at the bottom
        window.Close();
    });

    // ---- PR-WIN-197, PR-WIN-231 (partial): ChromeCompactTitle ----

    [Fact]
    [Trait("Req", "PR-WIN-197")] // Chrome.swift:638-644
    [Trait("Req", "UI-WIN-048")] // UI/Chrome/Chrome.swift:630-647 compact chrome title offset and opacity
    [Trait("Req", "PR-WIN-231")]
    public void ChromeCompactTitle_opacity_equals_progress_and_offset_runs_from_5_to_0() => headless.OnUiThread(() =>
    {
        var model = new ChromeScrollModel();
        var title = new ChromeCompactTitle("Thread", model);
        var window = Show(title);
        var text = Assert.IsType<TextBlock>(title.Child);
        Assert.Equal("Thread", text.Text);
        Assert.Equal(1, text.MaxLines);
        Assert.Equal(0, text.Opacity);
        Assert.Equal(5, ((TranslateTransform)text.RenderTransform!).Y, 3);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(title));

        model.Update(travel: 44); // progress 1
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, text.Opacity);
        Assert.Equal(0, ((TranslateTransform)text.RenderTransform!).Y, 3);
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(title));
        window.Close();
    });

    // ---- PR-WIN-198: SidebarSymbol ----

    [Fact]
    [Trait("Req", "PR-WIN-198")] // Chrome.swift:661-667: fitted into symbolSize * scale, monochrome
    [Trait("Req", "UI-WIN-050")] // UI/Chrome/Chrome.swift:661-667 glyph fitted 14pt times scale, monochrome
    public void SidebarSymbol_is_fitted_into_symbolSize_times_scale_monochrome() => headless.OnUiThread(() =>
    {
        var normal = new SidebarSymbol("gearshape");
        var image = Assert.IsType<SwiftImage>(normal.Child);
        Assert.Equal(Chrome.SymbolSize, image.Width);
        Assert.Equal(Chrome.SymbolSize, image.Height);
        Assert.Equal(Marrow.UI.Port.SymbolRenderingMode.Monochrome, image.SymbolRendering);
        Assert.Equal(ContentMode.Fit, image.AspectContentMode);
        Assert.True(image.IsResizable);

        var scaled = new SidebarSymbol("gearshape", scale: 1.15);
        var scaledImage = Assert.IsType<SwiftImage>(scaled.Child);
        Near(Chrome.SymbolSize * 1.15, scaledImage.Width);
        Near(Chrome.SymbolSize * 1.15, scaledImage.Height);
    });

    // ---- PR-WIN-199: SidebarIconBadge ----

    [Fact]
    [Trait("Req", "PR-WIN-199")] // Chrome.swift:674-678: a 20-point box at 0.85 of the label colour, nothing behind it
    [Trait("Req", "UI-WIN-050")] // UI/Chrome/Chrome.swift:674-678 badge box 20x20, primary label 0.85, nothing behind
    public void SidebarIconBadge_is_a_20_point_box_at_0_85_opacity_with_no_background() => headless.OnUiThread(() =>
    {
        var glyph = new TextBlock { Text = "x" };
        var badge = new SidebarIconBadge(() => glyph);
        var window = Show(badge, ThemeVariant.Light);
        var box = Assert.IsType<Panel>(badge.Child);
        Assert.Equal(Chrome.IconSize, box.Width);
        Assert.Equal(Chrome.IconSize, box.Height);
        Assert.Null(box.Background);
        AssertColor(Label(0.85, ThemeVariant.Light), Effective(TextElement.GetForeground(glyph)));
        window.Close();
    });

    // ---- PR-WIN-200: SidebarRow ----

    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:722-728: 28pt row, radius 7, three fills, trailing padding reserves the accessory
    [Trait("Req", "UI-WIN-049")] // UI/Chrome/Chrome.swift:681-757 sidebar row 28pt tall, radius 7
    public void SidebarRow_is_28_points_tall_with_the_three_fill_states() => headless.OnUiThread(() =>
    {
        var row = new SidebarRow("Chat", () => { }, () => new TextBlock { Text = "i" }, _ => new TextBlock(), isSelected: false, accessoryWidth: 10);
        var window = Show(row, ThemeVariant.Light);
        Assert.Equal(28, row.Bounds.Height, 3);
        var shape = Assert.IsAssignableFrom<Border>(row.GetVisualDescendants().OfType<Border>().First(b => b.CornerRadius.TopLeft == Chrome.RowCornerRadius));
        AssertColor(Color.FromArgb(0, 0, 0, 0), Effective(shape.Background)); // clear at rest, unselected
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:742: fill = overlay(0.12) when selected
    [Trait("Req", "UI-WIN-049")] // UI/Chrome/Chrome.swift:742 fill selected 0.12
    public void SidebarRow_selected_fill_is_the_label_colour_at_0_12() => headless.OnUiThread(() =>
    {
        var row = new SidebarRow("Chat", () => { }, () => new TextBlock(), isSelected: true);
        var window = Show(row, ThemeVariant.Light);
        var shape = row.GetVisualDescendants().OfType<Border>().First(b => b.CornerRadius.TopLeft == Chrome.RowCornerRadius);
        AssertColor(Label(0.12, ThemeVariant.Light), Effective(shape.Background));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:717-718: medium and full opacity when selected, regular and 0.92 otherwise
    [Trait("Req", "UI-WIN-049")] // UI/Chrome/Chrome.swift:717-718 title weight and opacity states
    public void SidebarRow_title_weight_and_opacity_follow_selection() => headless.OnUiThread(() =>
    {
        var selected = new SidebarRow("Chat", () => { }, () => new TextBlock(), isSelected: true);
        Show(selected, ThemeVariant.Light);
        var selectedText = selected.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Chat");
        Assert.Equal(FontWeight.Medium, TextElement.GetFontWeight(selectedText));
        AssertColor(Label(1, ThemeVariant.Light), Effective(selectedText.Foreground));

        var unselected = new SidebarRow("Chat", () => { }, () => new TextBlock(), isSelected: false);
        Show(unselected, ThemeVariant.Light);
        var unselectedText = unselected.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Chat");
        Assert.Equal(FontWeight.Regular, TextElement.GetFontWeight(unselectedText));
        AssertColor(Label(0.92, ThemeVariant.Light), Effective(unselectedText.Foreground));
    });

    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:709, :729-730: the row is a Button and answers its action
    [Trait("Req", "UI-WIN-049")] // UI/Chrome/Chrome.swift:709, :729-730 row is a button, runs its action
    public void SidebarRow_is_a_button_that_runs_its_action() => headless.OnUiThread(() =>
    {
        var ran = false;
        var row = new SidebarRow("Chat", () => ran = true, () => new TextBlock());
        var window = Show(row);
        var button = row.GetVisualDescendants().OfType<Button>().First();
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(ran);
        var peer = ControlAutomationPeer.CreatePeerForElement(button);
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:738: AddTraits(isSelected ? [.isSelected, .isButton] : .isButton)
    [Trait("Req", "UI-WIN-049")] // UI/Chrome/Chrome.swift:738 Button role plus Selected when selected
    public void SidebarRow_reports_isSelected_through_the_selection_item_provider() => headless.OnUiThread(() =>
    {
        var selected = new SidebarRow("Chat", () => { }, () => new TextBlock(), isSelected: true);
        Show(selected);
        var selectedButton = selected.GetVisualDescendants().OfType<Button>().First();
        var selectedPeer = ControlAutomationPeer.CreatePeerForElement(selectedButton);
        var selectedProvider = Assert.IsAssignableFrom<ISelectionItemProvider>(selectedPeer.GetProvider<ISelectionItemProvider>());
        Assert.True(selectedProvider.IsSelected);

        var unselected = new SidebarRow("Chat", () => { }, () => new TextBlock(), isSelected: false);
        Show(unselected);
        var unselectedButton = unselected.GetVisualDescendants().OfType<Button>().First();
        var unselectedPeer = ControlAutomationPeer.CreatePeerForElement(unselectedButton);
        var unselectedProvider = Assert.IsAssignableFrom<ISelectionItemProvider>(unselectedPeer.GetProvider<ISelectionItemProvider>());
        Assert.False(unselectedProvider.IsSelected);
    });

    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:748-757: SidebarRow(title:isSelected:action:icon:) with an empty accessory
    public void SidebarRow_shorthand_init_omits_the_accessory() => headless.OnUiThread(() =>
    {
        var row = new SidebarRow("Chat", () => { }, () => new TextBlock());
        var window = Show(row);
        Assert.NotEmpty(row.GetVisualDescendants().OfType<Button>());
        window.Close();
    });

    // Chrome.swift:729, :731-737: the hover flips isHovering inside withAnimation(Chrome.hover) (easeOut 0.1 s), so the fill
    // eases to the 0.06 overlay rather than snapping, and an accessory branch the hover swaps crossfades on SwiftUI's default
    // .opacity transition (OD-0076); an accessory the closure hands back unchanged is the same view and is left alone.
    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:729-737, :741-745: the hover fill and the accessory under the hover's animation
    public void SidebarRow_hover_eases_its_fill_crossfades_a_new_accessory_and_leaves_one_handed_back_alone() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var idle = new TextBlock { Text = "idle" };
        var hovered = new TextBlock { Text = "hovered" };
        var kept = new TextBlock { Text = "kept" };
        var swapping = new SidebarRow("Chat", () => { }, () => new TextBlock(), hovering => hovering ? hovered : idle);
        var keeping = new SidebarRow("Chat", () => { }, () => new TextBlock(), _ => kept);
        var window = Show(new StackPanel { Width = 300, Children = { swapping, keeping } }, ThemeVariant.Light);
        void Step(double seconds)
        {
            time.Advance(TimeSpan.FromSeconds(seconds));
            frames.Advance(TimeSpan.FromSeconds(seconds));
            Dispatcher.UIThread.RunJobs();
        }
        try
        {
            var shape = swapping.GetVisualDescendants().OfType<Border>().First(b => b.CornerRadius.TopLeft == Chrome.RowCornerRadius);
            Assert.Equal(1d, idle.Opacity);
            Assert.Null(hovered.Parent);
            window.MouseMove(new Point(20, 14)); // onto the first row
            Dispatcher.UIThread.RunJobs();
            Step(0.05);
            var p = Animation.EaseOut(0.1).Progress(0.05);
            var end = Chrome.Overlay(0.06).Resolve(ThemeVariant.Light);
            var fill = ((ISolidColorBrush)shape.Background!).Color;
            Assert.Equal((byte)Math.Round(end.A * p), fill.A); // :729 part way along easeOut 0.1 s, not a snap
            Assert.InRange(fill.A, (byte)1, (byte)(end.A - 1));
            Assert.Equal(p, hovered.Opacity, 3); // the hovered branch fades in ...
            Assert.Equal(1 - p, idle.Opacity, 3); // ... over the idle one fading out where it was
            Assert.Same(hovered.Parent, idle.Parent);
            Step(0.06);
            Assert.Null(idle.Parent); // gone once the fade is over
            Assert.Equal(1d, hovered.Opacity);
            AssertColor(end, ((ISolidColorBrush)shape.Background!).Color);
            var keptHost = kept.Parent;
            Assert.NotNull(keptHost);
            window.MouseMove(new Point(20, 42)); // onto the second row, whose closure hands back the view it gave before
            Dispatcher.UIThread.RunJobs();
            Step(0.05);
            Assert.Same(keptHost, kept.Parent); // the same view, where it was ...
            Assert.Equal(1d, kept.Opacity); // ... never faded
            Assert.Single(((Panel)keptHost!).Children.OfType<TextBlock>(), t => t.Text == "kept");
        }
        finally { window.Close(); }
    });

    // Chrome.swift:729 `.background { shape.fill(fill).animation(Chrome.hover, value: isSelected) }`: a row handed a new
    // isSelected (SettingsView.swift:149 `isSelected: page == item`, the row kept under its ForEach id) eases its fill to the
    // new state (:741-745) on Chrome.hover, easeOut 0.1 s, whatever transaction carried the change: `.animation(_:value:)`
    // puts its own curve on a change of its value, so a change carried by none and one carried by another curve both ease
    // on it. The title's weight and opacity (:717-718) and the selected trait (:738) have no animation of their own, and the
    // change here carries none, so they take the new value at once; the trait is the one the row's peer reports.
    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:717-718, :729, :738, :741-745: a selection change eases the fill on Chrome.hover
    public void SidebarRow_handed_a_new_selection_eases_its_fill_on_chrome_hover_and_sets_its_title_and_trait_at_once() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var row = new SidebarRow("Chat", () => { }, () => new TextBlock());
        var window = Show(new StackPanel { Width = 300, Children = { row } }, ThemeVariant.Light);
        void Step(double seconds)
        {
            time.Advance(TimeSpan.FromSeconds(seconds));
            frames.Advance(TimeSpan.FromSeconds(seconds));
            Dispatcher.UIThread.RunJobs();
        }
        try
        {
            var shape = row.GetVisualDescendants().OfType<Border>().First(b => b.CornerRadius.TopLeft == Chrome.RowCornerRadius);
            var title = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Chat");
            var button = row.GetVisualDescendants().OfType<Button>().First();
            var provider = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(button).GetProvider<ISelectionItemProvider>());
            var selected = Label(0.12, ThemeVariant.Light); // :742
            var p = Animation.EaseOut(0.1).Progress(0.05); // :729 Chrome.hover, half way through
            byte Alpha() => Effective(shape.Background).A;
            Assert.False(row.IsSelected);
            Assert.False(provider.IsSelected);
            Assert.Equal(0, Alpha()); // :744 clear

            row.IsSelected = true; // no transaction
            Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsSelected);
            Assert.Equal(FontWeight.Medium, TextElement.GetFontWeight(title)); // :717 at once
            AssertColor(Label(1, ThemeVariant.Light), Effective(title.Foreground)); // :718 at once
            Assert.True(provider.IsSelected); // :738 the peer it had reports the new state
            Assert.Equal(0, Alpha()); // the fill has not jumped
            Step(0.05);
            Assert.InRange((int)Alpha(), (int)Math.Round(selected.A * p) - 1, (int)Math.Round(selected.A * p) + 1); // part way on easeOut 0.1 s
            Assert.InRange(Alpha(), (byte)1, (byte)(selected.A - 1));
            Step(0.06);
            AssertColor(selected, Effective(shape.Background));

            Transaction.WithAnimation(Animation.Linear(1), () => row.IsSelected = false); // another curve in force
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(FontWeight.Regular, TextElement.GetFontWeight(title)); // :717
            AssertColor(Label(0.92, ThemeVariant.Light), Effective(title.Foreground)); // :718
            Assert.False(provider.IsSelected); // :738
            Step(0.05);
            Assert.InRange((int)Alpha(), (int)Math.Round(selected.A * (1 - p)) - 1, (int)Math.Round(selected.A * (1 - p)) + 1); // on Chrome.hover, not the linear 1 s
            Assert.InRange(Alpha(), (byte)1, (byte)(selected.A - 1));
            Step(0.06);
            Assert.Equal(0, Alpha()); // :744 clear again after 0.1 s

            row.IsSelected = false; // the same value: nothing moves
            Dispatcher.UIThread.RunJobs();
            Step(0.05);
            Assert.Equal(0, Alpha());

            // :741-745 the fill is the selection's first and the hover's after it, whichever of the two changed.
            var hovered = Label(0.06, ThemeVariant.Light); // :743
            window.MouseMove(new Point(20, 14)); // onto the row
            Dispatcher.UIThread.RunJobs();
            Step(0.11);
            AssertColor(hovered, Effective(shape.Background));
            row.IsSelected = true; // selected under the pointer
            Dispatcher.UIThread.RunJobs();
            Step(0.11);
            AssertColor(selected, Effective(shape.Background));
            window.MouseMove(new Point(350, 250)); // off the row: still selected, so nothing changes
            Dispatcher.UIThread.RunJobs();
            Step(0.11);
            AssertColor(selected, Effective(shape.Background));
            window.MouseMove(new Point(20, 14)); // back onto it
            Dispatcher.UIThread.RunJobs();
            Step(0.11);
            row.IsSelected = false; // deselected under the pointer: the hover's wash, not clear
            Dispatcher.UIThread.RunJobs();
            Step(0.05);
            Assert.InRange(Alpha(), (byte)(hovered.A + 1), (byte)(selected.A - 1)); // on its way down from 0.12 to 0.06
            Step(0.06);
            AssertColor(hovered, Effective(shape.Background));
        }
        finally { window.Close(); }
    });

    // ---- PR-WIN-201: RowAccessoryIcon ----

    [Fact]
    [Trait("Req", "PR-WIN-201")] // Chrome.swift:767-771: a semibold subheadline glyph in Chrome.secondaryText, in a 20x20 hit box
    [Trait("Req", "UI-WIN-051")] // UI/Chrome/Chrome.swift:759-773 row accessory icon 20x20, subheadline semibold secondaryText
    public void RowAccessoryIcon_is_20_by_20_with_secondaryText_glyph() => headless.OnUiThread(() =>
    {
        var icon = new RowAccessoryIcon("chevron.right");
        var window = Show(icon, ThemeVariant.Light);
        var box = Assert.IsType<Panel>(icon.Child);
        Assert.Equal(20, box.Width);
        Assert.Equal(20, box.Height);
        var image = Assert.IsType<SwiftImage>(box.Children[0]);
        AssertColor(Label(0.7, ThemeVariant.Light), Effective(image.Foreground));
        window.Close();
    });

    // ---- PR-WIN-202, UI-WIN-052: ChromeCard (B3 52bc792 :1006-1027, gained `lazy`) ----

    [Fact]
    [Trait("Req", "PR-WIN-202")] // Chrome.swift:1021-1025 (B3, was :1014-1020): continuous rounded-16 fill at overlay(0.03)
    [Trait("Req", "UI-WIN-052")] // UI/Chrome/Chrome.swift:1006-1027 (B3) chrome card fill 0.03, radius 16
    public void ChromeCard_is_a_16_radius_fill_at_0_03() => headless.OnUiThread(() =>
    {
        var row = new TextBlock { Text = "row" };
        var card = new ChromeCard(() => row);
        var window = Show(card, ThemeVariant.Light);
        var border = Assert.IsType<Border>(card.Child);
        Assert.Equal(16, border.CornerRadius.TopLeft);
        AssertColor(Label(0.03, ThemeVariant.Light), Effective(border.Background));
        Assert.Contains(row, VisualQuery.Descendants<TextBlock>(border));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-202")] // Chrome.swift:1009 (B3): `var lazy = false` - the default is eager, not lazy
    [Trait("Req", "UI-WIN-052")] // UI/Chrome/Chrome.swift:1006-1027 (B3) eager-by-default state
    public void ChromeCard_lazy_defaults_to_false() => headless.OnUiThread(() =>
    {
        var card = new ChromeCard(() => new TextBlock { Text = "row" });

        Assert.False(card.Lazy);
    });

    [Fact]
    [Trait("Req", "PR-WIN-202")] // Chrome.swift:1009, :1015-1016 (B3): a caller can opt a card into lazy: true
    [Trait("Req", "UI-WIN-052")] // UI/Chrome/Chrome.swift:1006-1027 (B3) lazy (only where the caller passes lazy: true) state
    public void ChromeCard_records_lazy_true_when_the_caller_passes_it() => headless.OnUiThread(() =>
    {
        var card = new ChromeCard(() => new TextBlock { Text = "row" }, lazy: true);

        Assert.True(card.Lazy);
    });

    [Fact]
    [Trait("Req", "PR-WIN-202")] // Chrome.swift:1015-1019 (B3): Avalonia has no LazyVStack-shaped host for a single already-built subtree, so lazy: true still renders the row (ENGINEERING DECISION: a recorded eager fallback, not a virtualizing host)
    public void ChromeCard_lazy_true_still_builds_and_shows_its_content() => headless.OnUiThread(() =>
    {
        var row = new TextBlock { Text = "row" };
        var card = new ChromeCard(() => row, lazy: true);
        var window = Show(card, ThemeVariant.Light);

        Assert.Contains(row, VisualQuery.Descendants<TextBlock>(card));
        window.Close();
    });

    // ---- PR-WIN-203, PR-WIN-231 (partial): ChromeSection ----

    [Fact]
    [Trait("Req", "PR-WIN-203")] // Chrome.swift:1040-1042: semibold body heading in secondaryText
    [Trait("Req", "UI-WIN-053")] // UI/Chrome/Chrome.swift:1029-1053 chrome section header semibold body, secondaryText
    [Trait("Req", "PR-WIN-231")]
    public void ChromeSection_heading_is_semibold_body_in_secondaryText() => headless.OnUiThread(() =>
    {
        var section = new ChromeSection("General", () => new TextBlock { Text = "content" });
        var window = Show(section, ThemeVariant.Light);
        var heading = section.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "General");
        Assert.Equal(FontWeight.SemiBold, TextElement.GetFontWeight(heading));
        AssertColor(Label(0.7, ThemeVariant.Light), Effective(heading.Foreground));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-203")] // Chrome.swift:1043-1047: accessory after the title, trailing pushed to the far right with 8pt before it
    [Trait("Req", "UI-WIN-053")] // UI/Chrome/Chrome.swift:1029-1053 accessory and trailing control layout
    public void ChromeSection_places_an_accessory_and_a_trailing_control() => headless.OnUiThread(() =>
    {
        var accessory = new TextBlock { Text = "info" };
        var trailing = new TextBlock { Text = "clear" };
        var section = new ChromeSection("General", () => new TextBlock { Text = "content" }, accessory, trailing);
        var window = Show(section);
        Assert.Contains(accessory, VisualQuery.Descendants<TextBlock>(section));
        Assert.Contains(trailing, VisualQuery.Descendants<TextBlock>(section));
        Assert.Equal(8, trailing.Margin.Left);
        window.Close();
    });

    // ---- PR-WIN-204, PR-WIN-231 (partial): ChromeRow ----

    [Fact]
    [Trait("Req", "PR-WIN-204")] // Chrome.swift:1064-1070: body title, subheadline detail only when non-nil and non-empty
    [Trait("Req", "UI-WIN-054")] // UI/Chrome/Chrome.swift:1055-1080 chrome row title and detail typography/colors
    [Trait("Req", "PR-WIN-231")]
    public void ChromeRow_shows_the_detail_only_when_present_and_non_empty() => headless.OnUiThread(() =>
    {
        var withDetail = new ChromeRow("Title", () => new TextBlock { Text = "ctl" }, "Detail");
        Show(withDetail, ThemeVariant.Light);
        var detailText = withDetail.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Detail");
        Assert.NotNull(detailText);
        AssertColor(Label(0.7, ThemeVariant.Light), Effective(detailText!.Foreground));

        var withEmptyDetail = new ChromeRow("Title", () => new TextBlock { Text = "ctl" }, "");
        Show(withEmptyDetail);
        Assert.DoesNotContain(withEmptyDetail.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "");

        var withNullDetail = new ChromeRow("Title", () => new TextBlock { Text = "ctl" });
        Show(withNullDetail);
        Assert.Single(withNullDetail.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Title");
    });

    // ---- PR-WIN-205: ChromeRowDivider ----

    [Fact]
    [Trait("Req", "PR-WIN-205")] // Chrome.swift:1083-1086: a hairline inset 16pt from the leading edge only
    public void ChromeRowDivider_is_inset_16_points_leading_only() => headless.OnUiThread(() =>
    {
        var divider = new ChromeRowDivider();
        Show(divider);
        Assert.Equal(16, divider.Margin.Left);
        Assert.Equal(0, divider.Margin.Right);
        Assert.Equal(0, divider.Margin.Top);
        Assert.Equal(0, divider.Margin.Bottom);
    });

    // ---- PR-WIN-213: the field capsule takes the glass without the press response ----

    [Fact]
    [Trait("Req", "PR-WIN-213")] // Chrome.swift:137-139, :179-183: the same 0.3 tint, no .interactive()
    public void ChromeGlassFieldCapsule_carries_the_tint_without_the_interactive_flag() => headless.OnUiThread(() =>
    {
        var field = new TextBlock { Text = "field" };
        var surface = field.ChromeGlassFieldCapsule();
        var window = Show(surface);
        var fill = Assert.Single(VisualQuery.Descendants<Border>(surface), b => SwiftGlass.GetEffect(b) is not null);
        var glass = SwiftGlass.GetEffect(fill)!;
        Assert.False(glass.IsInteractive);
        Assert.Equal(0.3, glass.TintOpacity);
        window.Close();
    });
}
