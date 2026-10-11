using Marrow.UI.Port.Nth;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Port;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Chrome;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :373-837.
[Collection("Headless")]
public sealed class EffortSliderSweepTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-252")] // App/Views/Composer/EffortSlider.swift:802-818 (a part of PR-COMPOSER-123 and PR-COMPOSER-125, whose knob OD-0090 replaced: 48 px, the page's springs)
    public void Releasing_a_drag_springs_from_the_drag_position_to_the_nearest_stop()
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider();
            var frames = new ManualFrameSource();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var chosen = 0;
            var slider = new EffortSlider(3, new Binding<int>(() => chosen, value => chosen = value), "Low");
            var window = Show(slider);
            var origin = slider.TranslatePoint(default, window)!.Value;
            var knob = Assert.Single(slider.GetVisualDescendants().OfType<Panel>(), p => p.Width == 48 && p.Height == 48); // OD-0090: the knob is 48, not :713's 42
            window.MouseDown(new Point(origin.X + 100, origin.Y + 21), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(1, Assert.IsType<ScaleTransform>(knob.RenderTransform).ScaleX); // spring begins at the resting size
            time.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(1, Assert.IsType<ScaleTransform>(knob.RenderTransform).ScaleX);
            var draggedLeft = Canvas.GetLeft(knob);
            window.MouseUp(new Point(origin.X + 100, origin.Y + 21), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(draggedLeft, Canvas.GetLeft(knob)); // not a jump to the snapped centre
            OneSecondOfFrames(time, frames);
            Assert.Equal(141, Canvas.GetLeft(knob)); // width330, inset24 (OD-0090), selected1 => centre165 minus24
            Assert.Equal(1, Assert.IsType<ScaleTransform>(knob.RenderTransform).ScaleX);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-116")] // App/Views/Composer/EffortSlider.swift:641-650
    [Trait("Req", "UI-COMPOSER-037")] // App/Views/Composer/EffortSlider.swift:641-650
    [Trait("Req", "UI-COMPOSER-033")] // App/Views/Composer/EffortSlider.swift:659-683
    [Trait("Req", "PR-COMPOSER-112")] // App/Views/Composer/EffortSlider.swift:543-555
    public void Pair_card_uses_a_gradient_and_lifts_its_single_line_caption_by_six_points()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var model = new AppModel();
            var pair = new EffortPairLook(new HydraPair(ProviderKind.Codex) { WorkerEffort = "high" }, model.Providers, null);
            var card = new EffortSliderCard("Lead", ProviderKind.Codex, new ValueList<string>(["low", "high"]), "low", true,
                new Binding<string?>(() => "low", _ => { }), new Binding<bool>(() => true, _ => { }), () => { }, pair);
            var window = Show(card);
            var caption = Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "The lead's effort · heads run at high effort");
            Assert.Equal(new Thickness(0, -6, 0, 0), caption.Margin);
            Assert.Equal(11, caption.FontSize);
            Assert.Equal(1, caption.MaxLines);
            var title = Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Low");
            var gradient = Assert.IsType<LinearGradientBrush>(title.Foreground);
            Assert.Equal(pair.Brand.TitleColors.Count + 2, gradient.GradientStops.Count); // brand + heads + fast
            Assert.Equal(EffortPalette.Fast.Resolve(Avalonia.Styling.ThemeVariant.Light), gradient.GradientStops[^1].Color);
            var fast = Assert.Single(card.GetVisualDescendants().OfType<FastModeButton>());
            Assert.Equal(30, Assert.Single(fast.GetVisualDescendants().OfType<Panel>(), p => p.Width == 30).Height);
            Assert.Equal("On", AutomationProperties.GetItemStatus(Assert.Single(fast.GetVisualDescendants().OfType<Button>())));
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-116")] // App/Views/Composer/EffortSlider.swift:558 VStack(spacing: 16), :567 VStack(spacing: 2): both centre alignment
    [Trait("Req", "UI-COMPOSER-037")] // App/Views/Composer/EffortSlider.swift:641-650 the pair's caption
    public void The_pair_caption_the_title_and_the_model_line_button_are_each_centred_at_their_own_width()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var model = new AppModel();
            var pair = new EffortPairLook(new HydraPair(ProviderKind.Codex) { WorkerEffort = "high" }, model.Providers, null);
            var card = new EffortSliderCard("A model with a long name", ProviderKind.Codex, new ValueList<string>(["low", "high"]), "low", false,
                new Binding<string?>(() => "low", _ => { }), new Binding<bool>(() => false, _ => { }), () => { }, pair, onTitleTap: () => { });
            var window = Show(card);
            // A VStack centres each child at its own width; the StackPanel stretched the caption across the column (its text then
            // began at the left edge) and the model line's hover plate across the title's width.
            var caption = Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "The lead's effort · heads run at high effort");
            var modelButton = Assert.Single(card.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), "Choose a model"));
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>(), text => text.Text == "Low");
            Assert.DoesNotContain(title.GetVisualAncestors(), ancestor => ancestor is Button); // OD-0139: the effort title is plain text, the model line is the button
            foreach (var child in new Control[] { caption, modelButton, title })
            {
                Assert.Equal(child.DesiredSize.Width, child.Bounds.Width, 0.5); // at its own width
                Assert.Equal(card.Bounds.Width / 2, child.TranslatePoint(new Point(child.Bounds.Width / 2, 0), card)!.Value.X, 0.5);
            }
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-122")] // App/Views/Composer/EffortSlider.swift:626-637
    public void Slider_binding_writes_effort_inside_the_point_two_second_snappy_transaction()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            string? effort = "low";
            Animation? transaction = null;
            var card = new EffortSliderCard("Model", null, new ValueList<string>(["low", "high"]), "low", false,
                new Binding<string?>(() => effort, value => { effort = value; transaction = Transaction.Current; }),
                new Binding<bool>(() => false, _ => { }), () => { });
            var window = Show(card);
            var slider = Assert.Single(card.GetVisualDescendants().OfType<EffortSlider>());
            Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(slider)).SetValue(1);
            Assert.Equal("high", effort);
            Assert.NotNull(transaction);
            // The source supplies snappy(duration: 0.2); Animation.Duration is the physical spring's settling
            // time, so compare the full record and its input parameters, not that derived settling time.
            Assert.Equal(Animation.Snappy(0.2), transaction);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-092")] // App/Views/Composer/EffortSlider.swift:758-767,799,1024-1026; the fade is Animation.Smooth(0.3) (ED-092(2)) settling per ED-011, never ED-117 cadence (no TimelineView, DisplayLink or 1 ms rule); PR-132 needs a capture comparison (see STOP).
    [Trait("Decision", "ED-011")]
    public void Pair_keeps_effect_identity_across_kind_changes_while_a_solo_track_crossfades()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var span = TimeSpan.FromSeconds(Animation.Smooth(0.3).Duration); // :799 smooth(duration: 0.3)
            var selected = 0;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "Low", true);
            var window = Show(slider);
            var before = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            Assert.Equal(1, before.Opacity);
            selected = 2; slider.UpdateLook(true, "High", null); // :1024-1026 solo identity is the whole look, so it changes
            var pairNow = slider.GetVisualDescendants().OfType<TrackEffect>().ToArray();
            Assert.Equal(2, pairNow.Length); // :765 .transition(.opacity): outgoing retained while the replacement fades in
            var incoming = Assert.Single(pairNow, e => !ReferenceEquals(e, before));
            Assert.Equal(0, incoming.Opacity);
            time.Advance(TimeSpan.FromTicks(span.Ticks / 2)); frames.Advance(TimeSpan.FromTicks(span.Ticks / 2)); Dispatcher.UIThread.RunJobs();
            Assert.InRange(before.Opacity, 0.01, 0.99);
            Assert.InRange(incoming.Opacity, 0.01, 0.99);
            time.Advance(span); frames.Advance(span); Dispatcher.UIThread.RunJobs();
            var after = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            Assert.NotSame(before, after);
            Assert.Equal(1, after.Opacity);
            window.Close();
            var pair = new EffortPairBrand(new HydraPair(ProviderKind.Codex));
            var paired = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "High", false, pair: pair);
            var pairWindow = Show(paired);
            before = Assert.Single(paired.GetVisualDescendants().OfType<TrackEffect>());
            selected = 0; paired.UpdateLook(true, "Low", pair);
            Assert.Same(before, Assert.Single(paired.GetVisualDescendants().OfType<TrackEffect>()));
            pairWindow.Close();
        });
    }

    private static TrackLook.Kind KindOf(TrackEffect effect) =>
        ((TrackLook)typeof(TrackEffect).GetField("look", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(effect)!).TrackLookKind;

    [Fact]
    [Trait("Decision", "ED-092")] // App/Views/Composer/EffortSlider.swift:758-767,799; the fade curve is Animation.Smooth(0.3) (ED-092(2)), settling per ED-011.
    [Trait("Decision", "ED-011")]
    public void Card_effort_change_crossfades_keeping_the_outgoing_look()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var effort = new ObservableStringBox { Value = "high" };
            var fast = new ObservableBoolBox { Value = true };
            var card = new EffortSliderCard("Model", null, new ValueList<string>(["low", "medium", "high"]), "low", true,
                effort.AsBinding(), fast.AsBinding(), () => { });
            var window = Show(card);
            var slider = Assert.Single(card.GetVisualDescendants().OfType<EffortSlider>());
            var outgoing = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            Assert.Equal(TrackLook.Kind.Fusion, KindOf(outgoing)); // maximum with fast mode
            effort.Value = "medium"; Dispatcher.UIThread.RunJobs(); // the card's and the slider's Track each run LayoutTrack; no clock advance
            var effects = slider.GetVisualDescendants().OfType<TrackEffect>().ToArray();
            Assert.Equal(2, effects.Length);
            Assert.Contains(outgoing, effects);
            Assert.Equal(TrackLook.Kind.Fusion, KindOf(outgoing)); // Swift :765-766: the view leaving under .transition(.opacity) keeps its own look
            var incoming = Assert.Single(effects, e => !ReferenceEquals(e, outgoing));
            Assert.Equal(TrackLook.Kind.Fast, KindOf(incoming));
            Assert.Equal(1, outgoing.Opacity);
            Assert.Equal(0, incoming.Opacity);
            var elapsed = TimeSpan.FromSeconds(Math.Log(1000) / (2 * Math.PI / 0.3) / 2);
            time.Advance(elapsed); frames.Advance(elapsed); Dispatcher.UIThread.RunJobs();
            var w = 2 * Math.PI / 0.3; // ED-092(2)/ED-011: critical spring p(t) = 1-exp(-wt)*(1+wt)
            var progress = 1 - Math.Exp(-w * elapsed.TotalSeconds) * (1 + w * elapsed.TotalSeconds);
            Assert.Equal(progress, incoming.Opacity, 6);
            Assert.Equal(1 - progress, outgoing.Opacity, 6);
            Assert.Equal(TrackLook.Kind.Fusion, KindOf(outgoing));
            Assert.Equal(TrackLook.Kind.Fast, KindOf(incoming));
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-092")] // App/Views/Composer/EffortSlider.swift:758-767,799; later passes with the same look must reach only the incoming effect.
    [Trait("Decision", "ED-011")]
    public void A_second_layout_with_the_same_look_updates_only_the_incoming_effect()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var selected = 2;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "High", true);
            var window = Show(slider);
            var outgoing = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            Assert.Equal(TrackLook.Kind.Fusion, KindOf(outgoing));
            selected = 1; slider.UpdateLook(true, "Medium", null); // solo identity change: fusion -> fast
            Assert.Equal(2, slider.GetVisualDescendants().OfType<TrackEffect>().Count());
            slider.UpdateLook(true, "Medium", null); // a later pass with the same look: pointer move, spring frame, resize
            var effects = slider.GetVisualDescendants().OfType<TrackEffect>().ToArray();
            Assert.Equal(2, effects.Length);
            Assert.Contains(outgoing, effects);
            Assert.Equal(TrackLook.Kind.Fusion, KindOf(outgoing)); // the fading view keeps its look; only the incoming takes the new one
            Assert.Equal(TrackLook.Kind.Fast, KindOf(Assert.Single(effects, e => !ReferenceEquals(e, outgoing))));
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-092")] // App/Views/Composer/EffortSlider.swift:758-767,799,1024-1026; a kind change under the same pair identity must reach the kept pair effect.
    [Trait("Decision", "ED-011")]
    public void A_kind_change_during_a_solo_to_pair_crossfade_reaches_the_kept_pair_effect()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var selected = 2;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "High", false);
            var window = Show(slider);
            var solo = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            var pair = new EffortPairBrand(new HydraPair(ProviderKind.Codex));
            slider.UpdateLook(false, "High", pair); // identity change: solo supercharged -> pair
            var paired = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>(), e => !ReferenceEquals(e, solo));
            slider.UpdateLook(true, "High", pair); // fast toggled within 0.3 s: same pair identity, kind becomes fusion
            var span = TimeSpan.FromSeconds(1);
            time.Advance(span); frames.Advance(span); Dispatcher.UIThread.RunJobs();
            var kept = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            Assert.Same(paired, kept);
            Assert.Equal(TrackLook.Kind.Fusion, KindOf(kept)); // Swift :1024-1026: the kept pair view draws the current look
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-092")] // App/Views/Composer/EffortSlider.swift:758-767,799; each leaving view ends its own removal ~0.33 s after it leaves.
    [Trait("Decision", "ED-011")]
    public void Rapid_identity_changes_keep_only_the_fades_swiftui_would_keep()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var selected = 1;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "Medium", true);
            var window = Show(slider);
            var step = TimeSpan.FromMilliseconds(100);
            var peak = 0;
            for (var i = 0; i < 20; i++)
            {
                selected = selected == 1 ? 2 : 1; slider.UpdateLook(true, selected == 2 ? "High" : "Medium", null); // Fast <-> Fusion
                time.Advance(step); frames.Advance(step); Dispatcher.UIThread.RunJobs();
                peak = Math.Max(peak, slider.GetVisualDescendants().OfType<TrackEffect>().Count());
            }
            Assert.Equal(4, peak); // one incoming plus at most three outgoing after each 100 ms frame; ln(1000)/(2*pi/0.3) < 400 ms
            time.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Dispatcher.UIThread.RunJobs();
            Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-092")] // App/Views/Composer/EffortSlider.swift:799; Animation.Smooth(0.3) is Spring(response: 0.3, dampingFraction: 1) (ED-092(2)), settling in ln(1000)/(2*pi/0.3) = 0.32982 s (ED-011).
    [Trait("Decision", "ED-011")] // Critically-damped progress at t is 1 - e^(-wt)(1+wt), w = 2*pi/0.3; at half the span (0.16491 s) that is 0.8592 in / 0.1408 out, while 0.2 s gives 0.9652/0.0348 and 0.4 s gives 0.7308/0.2692.
    public void Crossfade_opacities_match_the_smooth_point_three_spring_at_a_fixed_time()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var span = TimeSpan.FromSeconds(Animation.Smooth(0.3).Duration); // :799 smooth(duration: 0.3)
            var selected = 0;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "Low", true);
            var window = Show(slider);
            var before = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            selected = 2; slider.UpdateLook(true, "High", null); // solo identity change: fast -> fusion
            var half = TimeSpan.FromSeconds(Math.Log(1000) / (2 * Math.PI / 0.3) / 2); // independent fixed time, ED-011
            time.Advance(half); frames.Advance(half); Dispatcher.UIThread.RunJobs();
            var mid = slider.GetVisualDescendants().OfType<TrackEffect>().ToArray();
            Assert.Equal(2, mid.Length);
            Assert.Contains(before, mid);
            var incoming = Assert.Single(mid, e => !ReferenceEquals(e, before));
            // ED-092(2), ED-011: w = 2*pi/0.3; p(t) = 1 - exp(-w*t)*(1+w*t).
            var w = 2 * Math.PI / 0.3;
            var expected = 1 - Math.Exp(-w * half.TotalSeconds) * (1 + w * half.TotalSeconds);
            Assert.Equal(expected, incoming.Opacity, 6);
            Assert.Equal(1 - expected, before.Opacity, 6);
            time.Advance(span - half - TimeSpan.FromMilliseconds(10)); frames.Advance(span - half - TimeSpan.FromMilliseconds(10)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, slider.GetVisualDescendants().OfType<TrackEffect>().Count()); // the outgoing view is still attached one frame before its fade ends
            time.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
            var after = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            Assert.NotSame(before, after);
            Assert.Equal(1, after.Opacity);
            window.Close();
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Source-derived assertions are partial evidence, not a full Req claim.
    // App/Views/Composer/EffortSlider.swift:1070-1071; partial source proof, whole record remains STOP #2.
    public void Track_effect_never_hit_tests_and_is_hidden_from_assistive_tech()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            foreach (var kind in new[] { TrackLook.Kind.Plain, TrackLook.Kind.Supercharged, TrackLook.Kind.Fast, TrackLook.Kind.Fusion })
            {
                foreach (var paired in new[] { false, true })
                {
                    var effect = new TrackEffect(new TrackLook(kind, EffortBrand.Purple, paired ? new EffortPairBrand(new HydraPair(ProviderKind.Codex)) : null));
                    var window = Show(effect);
                    Assert.False(effect.IsHitTestVisible);
                    Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(effect));
                    window.Close();
                }
            }
        });
    }

    [Theory]
    [Trait("Decision", "ED-117")] // ED-117(7): cadence expectations rest on ED-117(2)-(6), so Decision ED-117 + Oracle O-34, never Req.
    [Trait("Oracle", "O-34")] // App/Views/Composer/EffortSlider.swift:1049-1050,1070-1071; PR-133 needs a ten-second run (see STOP).
    [InlineData(TrackLook.Kind.Plain, false, true)] // DEVIATION OD-0075: the plain track draws the liquid too, at 60 Hz
    [InlineData(TrackLook.Kind.Supercharged, false, true)] // DEVIATION OD-0051: the solo supercharged track is the liquid tube, at 60 Hz (source :1049 30 Hz)
    [InlineData(TrackLook.Kind.Supercharged, true, true)]
    [InlineData(TrackLook.Kind.Fast, false, true)]
    [InlineData(TrackLook.Kind.Fast, true, true)]
    [InlineData(TrackLook.Kind.Fusion, false, true)]
    [InlineData(TrackLook.Kind.Fusion, true, true)]
    public void Track_timeline_respects_the_thirty_or_sixty_hertz_interval(TrackLook.Kind kind, bool hasPair, bool ticksAtTwentyMilliseconds)
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var effect = new TrackEffect(new TrackLook(kind, EffortBrand.Purple, hasPair ? new EffortPairBrand(new HydraPair(ProviderKind.Codex)) : null));
            var window = Show(effect);
            window.Activate(); Dispatcher.UIThread.RunJobs();
            var field = typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.False(effect.IsHitTestVisible); // :1070; also covered independently above
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(effect)); // :1071
            // Cadence-oracle expectations below (ED-117/O-34); source hit-test/accessibility also have a separate Req test.
            time.Advance(TimeSpan.FromMilliseconds(20)); frames.Advance(TimeSpan.FromMilliseconds(20)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(ticksAtTwentyMilliseconds ? 0.02 : 0, (double)field.GetValue(effect)!, 6);
            time.Advance(TimeSpan.FromMilliseconds(20)); frames.Advance(TimeSpan.FromMilliseconds(20)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.04, (double)field.GetValue(effect)!, 6);
            window.Close();
        });
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-106")] // App/Views/Composer/EffortSlider.swift:59-103
    [Trait("Req", "PR-COMPOSER-107")] // App/Views/Composer/EffortSlider.swift:82-103,142-149
    [Trait("Req", "PR-COMPOSER-108")] // App/Views/Composer/EffortSlider.swift:108-117
    [Trait("Req", "UI-COMPOSER-012")] // App/Views/Composer/EffortSlider.swift:59-123
    [InlineData(false)]
    [InlineData(true)]
    public void Chip_keeps_full_help_in_compact_mode_and_locks_only_the_full_chip(bool compact)
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "custom-model-id", null, RuntimeMode.Supervised);
            harness.WriteLibrary([], [thread]);
            var model = new AppModel();
            foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
                if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(System.IO.Path.Combine(harness.Scratch, "missing.exe"), provider);
            var chip = new ModelEffortButton(thread, false, compact);
            using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
            var window = Host(chip, model);
            var button = Assert.Single(chip.GetVisualDescendants().OfType<Button>());
            Assert.Equal("custom-model-id", ToolTip.GetTip(button));
            Assert.Equal("custom-model-id", AutomationProperties.GetName(button));
            Assert.False(button.Focusable);
            var labels = chip.GetVisualDescendants().OfType<TextBlock>().ToArray();
            if (compact) Assert.Empty(labels);
            else Assert.Contains(labels, text => text.Text == "custom-model-id");
            var frame = Assert.IsType<Grid>(Assert.IsType<Border>(button.Content).Child); // :114 the real chip style wraps the same label.
            var natural = frame.Bounds.Width;
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            if (compact) Assert.True(double.IsNaN(frame.Width));
            else Assert.Equal(natural, frame.Width);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(double.IsNaN(frame.Width));
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-146")] // App/Views/Composer/EffortSlider.swift:277-325
    [Trait("Req", "PR-COMPOSER-150")] // App/Views/Composer/EffortSlider.swift:169-175
    [Trait("Req", "UI-COMPOSER-040")] // App/Views/Composer/EffortSlider.swift:310-311
    [Trait("Req", "UI-COMPOSER-041")] // App/Views/Composer/EffortSlider.swift:325
    public void Pair_sections_exist_only_with_hydra_and_choosing_a_pair_enters_it_and_dismisses()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, null, null, RuntimeMode.Supervised) { HydraEnabled = true };
            harness.WriteLibrary([], [thread]);
            var model = new AppModel();
            model.Settings.HydraEnabled = true;
            var pair = new HydraPair(ProviderKind.Codex);
            model.Settings.AddHydraPair(pair);
            var panel = new ModelEffortPanel(thread.Id, false);
            var dismissed = 0;
            Marrow.UI.Chrome.Popovers.SetClosePopover(panel, () => dismissed++);
            var window = Host(panel, model);
            Assert.Single(panel.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "Choose a model"))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Pairs");
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Models");
            var row = Assert.Single(panel.GetVisualDescendants().OfType<HydraPairListRow>());
            Assert.Single(row.GetVisualDescendants().OfType<Button>()).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(pair.Id, model.Thread(thread.Id)!.HydraPairID);
            Assert.Equal(1, dismissed);
            window.Close();
        });
    }

    private static Avalonia.Controls.Window Host(Control content, AppModel model)
    {
        var host = new Border { Child = content };
        AppEnvironment.SetAppModel(host, model);
        return Show(host);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-109")] // App/Views/Composer/EffortSlider.swift:161-188
    [Trait("Req", "PR-COMPOSER-121")] // App/Views/Composer/EffortSlider.swift:220-229
    [Trait("Req", "PR-COMPOSER-124")] // App/Views/Composer/EffortSlider.swift:235-239
    public void Panel_reset_clears_fast_and_effort_and_remembers_the_model_preference()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "test-model", "high", RuntimeMode.Supervised, true);
            harness.WriteLibrary([], [thread]);
            var model = new AppModel();
            var panel = new ModelEffortPanel(thread.Id, false);
            var window = Host(panel, model);
            Assert.Equal(330, panel.Width);
            var reset = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "Reset to default"));
            reset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(model.Thread(thread.Id)!.Effort);
            Assert.False(model.Thread(thread.Id)!.FastMode);
            var preference = model.Settings.Preference(ProviderKind.Codex, "test-model");
            Assert.Null(preference.Effort);
            Assert.False(preference.FastMode);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-149")] // App/Views/Composer/EffortSlider.swift:242-257
    public void Choosing_a_model_carries_valid_preference_and_clears_cross_provider_session_and_pair()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "old", null, RuntimeMode.Supervised) { ProviderSessionID = "session", HydraPairID = Guid.NewGuid() };
            harness.WriteLibrary([], [thread]);
            var model = new AppModel();
            model.Settings.SetPreference(new ModelPreference { Effort = "high", FastMode = true }, ProviderKind.Codex, "new");
            var option = new ModelOption("new", "New", null, new ValueList<string>(["low", "high"]), "low", fastTier: "fast");
            var panel = new ModelEffortPanel(thread.Id, false);
            var window = Host(panel, model);
            typeof(ModelEffortPanel).GetMethod("Choose", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(panel, [new ModelCatalog.Entry(ProviderKind.Codex, option), thread]);
            var updated = model.Thread(thread.Id)!;
            Assert.Equal(ProviderKind.Codex, updated.Provider);
            Assert.Equal("new", updated.Model);
            Assert.Equal("high", updated.Effort);
            Assert.True(updated.FastMode);
            Assert.Null(updated.ProviderSessionID);
            Assert.Null(updated.ProviderResumeAt);
            Assert.Null(updated.HydraPairID);
            Assert.Equal(ProviderKind.Codex, model.Settings.DefaultProvider);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-141")] // App/Views/Composer/EffortSlider.swift:289-306
    [Trait("Req", "PR-COMPOSER-142")] // App/Views/Composer/EffortSlider.swift:281-287
    [Trait("Req", "PR-COMPOSER-145")] // App/Views/Composer/EffortSlider.swift:340-345
    [Trait("Req", "UI-COMPOSER-038")] // App/Views/Composer/EffortSlider.swift:290-299
    [Trait("Req", "UI-COMPOSER-039")] // App/Views/Composer/EffortSlider.swift:300-302
    [Trait("Req", "UI-COMPOSER-044")] // App/Views/Composer/EffortSlider.swift:340-345
    [Trait("Req", "UI-COMPOSER-045")] // App/Views/Composer/EffortSlider.swift:271-287,348-351
    public void Empty_list_reserves_one_row_and_back_is_an_action()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var model = new AppModel();
            var backs = 0;
            model.Settings.AddToModelList(new ModelPin(ProviderKind.Codex, "not-in-catalogue"));
            var list = new ModelList(new ChatThread(Guid.NewGuid(), ProviderKind.Codex, null, null, RuntimeMode.Supervised), false, _ => { }, () => backs++);
            var window = Host(list, model);
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Select model" && b.FontSize == 13);
            // nth step 7: three skeleton rows stand in for the "Loading models…" line, which stays as their accessible name
            var skeleton = Assert.Single(list.GetVisualDescendants().OfType<NthSkeleton>());
            Assert.Equal("Loading models…", AutomationProperties.GetName(skeleton));
            Assert.Equal(120, Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>()).Height);
            var back = Assert.Single(list.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "Back"));
            back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, backs);
            Assert.Equal(new Thickness(6), Assert.IsType<Border>(list.Child).Padding);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-148")] // App/Views/Composer/EffortSlider.swift:409-410
    [Trait("Req", "UI-COMPOSER-042")] // App/Views/Composer/EffortSlider.swift:384-419
    public void Pair_row_has_two_lines_a_fused_mark_and_a_retained_checkmark_column()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var calls = 0;
            var row = new HydraPairListRow(new HydraPair(ProviderKind.Codex), "Lead + Heads", "Codex · High", 46, true, true, () => calls++);
            var window = Show(row);
            Assert.Single(row.GetVisualDescendants().OfType<HydraFusedPairIcon>());
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Lead + Heads" && b.FontSize == 14);
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Codex · High" && b.FontSize == 12);
            Assert.Single(row.GetVisualDescendants().OfType<Button>()).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, calls);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-135")] // EffortSlider.swift:1134-1148; DEVIATION OD-0051: the liquid tube replaces the particle fill at Max
    [Trait("Req", "PR-COMPOSER-137")] // EffortSlider.swift:1204-1225; DEVIATION OD-0051: and the fusion's sweeping sheen (no artificial shine)
    public void Max_and_fusion_draw_the_liquid_tube_in_place_of_the_particles_and_the_sheen()
    {
        // the source's particle and sheen painters are gone; both looks draw the liquid
        Assert.Null(typeof(TrackEffect).GetMethod("DrawParticles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        Assert.Null(typeof(TrackEffect).GetMethod("DrawSheen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        Assert.True(TrackEffect.IsLiquid(new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Claude)));
        Assert.True(TrackEffect.IsLiquid(new TrackLook(TrackLook.Kind.Fusion, EffortBrand.Claude)));
        // DEVIATION OD-0073: fast mode below the top is the liquid too, with Strike in place of the streaks and bolts
        // (PR-COMPOSER-136; EffortSliderStrikeTests)
        Assert.True(TrackEffect.IsLiquid(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude)));
    }

    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 330, Height = 220, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // A second of a 60 Hz display's frames. OD-0090's locked page moves the knob's springs by at most 50 ms a frame
    // (jewel-knob.html:813 tick()), so one frame a second long is a stall, after which the knob goes on from where it was.
    private static void OneSecondOfFrames(ManualTimeProvider time, ManualFrameSource frames)
    {
        var frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        for (var i = 0; i < 60; i++) { time.Advance(frame); frames.Advance(frame); Dispatcher.UIThread.RunJobs(); }
    }

    private static EffortSliderCard Card(string[] levels, string? chosen, string? defaultLevel, Action? titleTap = null, Action? reset = null) =>
        new("Test model", null, new ValueList<string>(levels), defaultLevel, false,
            new Binding<string?>(() => chosen, value => chosen = value),
            new Binding<bool>(() => false, _ => { }), reset ?? (() => { }), onTitleTap: titleTap);

    [Theory]
    [Trait("Req", "PR-COMPOSER-110")] // App/Views/Composer/EffortSlider.swift:521-534
    [InlineData("low", "high", "Low")]
    [InlineData("invalid", "high", "High")]
    [InlineData(null, "invalid", "Medium")]
    public void Card_resolves_chosen_then_default_then_medium(string? chosen, string? defaultLevel, string title)
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var window = Show(Card(["low", "medium", "high"], chosen, defaultLevel));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == title);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-252")] // App/Views/Composer/EffortSlider.swift:620-624 .frame(height: EffortSlider.thumbSize) (PR-COMPOSER-117 and UI-COMPOSER-036, whose 42 point height OD-0090 made the 48 px knob's)
    [Trait("Req", "PR-COMPOSER-117")] // EffortSlider.swift:620-624 as amended by OD-0090 (48 px)
    [Trait("Req", "UI-COMPOSER-036")] // EffortSlider.swift:620-624 as amended by OD-0090 (48 px)
    public void Empty_levels_show_standard_and_the_single_level_note()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var window = Show(Card([], null, null));
            var texts = window.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(texts, text => text.Text == "Standard");
            var note = Assert.Single(texts, text => text.Text == "This model has one reasoning level.");
            Assert.Equal(48, Assert.IsAssignableFrom<Panel>(note.Parent).Height); // OD-0090: the thumb size the note's slot takes is the 48 px knob (the line is centred in it)
            Assert.Equal(12, note.FontSize);
            Assert.Empty(window.GetVisualDescendants().OfType<EffortSlider>());
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // App/Views/Composer/EffortSlider.swift:568-597
    [Trait("Req", "UI-COMPOSER-031")] // App/Views/Composer/EffortSlider.swift:568-597
    [Trait("Req", "UI-COMPOSER-034")] // App/Views/Composer/EffortSlider.swift:564
    public void Model_line_and_reset_are_real_buttons_with_exact_help_and_actions_and_the_title_is_plain_text()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var tapped = 0;
            var resets = 0;
            var window = Show(Card([], null, null, () => tapped++, () => resets++));
            foreach (var help in new[] { "Choose a model", "Reset to default" })
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), help));
                if (help == "Choose a model") Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Test model"); // the model line (OD-0139)
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
            Assert.Equal(1, tapped);
            Assert.Equal(1, resets);
            var standard = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Standard");
            Assert.Equal(17, standard.FontSize);
            Assert.Equal(FontWeight.Medium, standard.FontWeight);
            Assert.DoesNotContain(standard.GetVisualAncestors(), ancestor => ancestor is Button); // the effort title is no button (OD-0139)
            window.Close();
            var disabled = Show(Card([], null, null));
            Assert.Contains(disabled.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "") && !b.IsEnabled && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Test model"));
            disabled.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-118")] // App/Views/Composer/EffortSlider.swift:558,653-655
    [Trait("Req", "UI-COMPOSER-030")] // App/Views/Composer/EffortSlider.swift:558-655 the card's geometry
    [Trait("Req", "PR-COMPOSER-114")] // App/Views/Composer/EffortSlider.swift:601-617
    [Trait("Req", "UI-COMPOSER-032")] // App/Views/Composer/EffortSlider.swift:601-617
    public void Card_has_source_padding_spacing_and_model_subtitle()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var card = Card([], null, null);
            var window = Show(card);
            var border = Assert.IsType<Border>(card.Child);
            Assert.Equal(new Thickness(16, 14, 16, 16), border.Padding);
            Assert.Equal(16, Assert.IsType<StackPanel>(border.Child).Spacing);
            var subtitle = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Test model");
            Assert.Equal(13, subtitle.FontSize);
            Assert.Equal(1, subtitle.MaxLines);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-252")] // App/Views/Composer/EffortSlider.swift:712-713,740-799, :802-818,833-836, :740-831 (parts of PR-COMPOSER-123, PR-COMPOSER-125 and UI-COMPOSER-035, whose 42 point knob OD-0090 replaced)
    public void Drag_clamps_and_snaps_to_each_stop_and_preserves_track_and_knob_sizes()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var selected = 0;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, value => selected = value), "Low");
            var window = Show(slider);
            Assert.Equal(48, slider.Height); // OD-0090: the control is as tall as the 48 px knob
            var bounds = new Rect(slider.TranslatePoint(default, window)!.Value, slider.Bounds.Size);
            window.MouseDown(new Point(bounds.X + 21, bounds.Center.Y), MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(new Point(bounds.X + bounds.Width / 2, bounds.Center.Y), RawInputModifiers.None);
            Assert.Equal(1, selected);
            window.MouseMove(new Point(bounds.Right + 50, bounds.Center.Y), RawInputModifiers.None);
            Assert.Equal(2, selected);
            window.MouseMove(new Point(bounds.X - 50, bounds.Center.Y), RawInputModifiers.None);
            Assert.Equal(0, selected);
            window.MouseUp(bounds.Center, MouseButton.Left, RawInputModifiers.None);
            Assert.Contains(slider.GetVisualDescendants().OfType<Border>(), b => b.Height == 34);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-252")] // App/Views/Composer/EffortSlider.swift:820 .frame(height: Self.thumbSize), :822-830 (PR-COMPOSER-126, whose 42 point height OD-0090 made the 48 px knob's)
    public void Accessibility_adjustments_clamp_at_the_extremes()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var selected = 0;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, value => selected = value), "Medium");
            var window = Show(slider);
            Assert.Equal(48, slider.Height); // OD-0090: the control reserves the 48 px knob's height
            Assert.Equal("Reasoning effort", AutomationProperties.GetName(slider));
            var provider = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(slider));
            Assert.Equal(0, provider.Minimum);
            Assert.Equal(2, provider.Maximum);
            Assert.Equal(1, provider.SmallChange);
            provider.SetValue(100);
            Assert.Equal(2, selected);
            provider.SetValue(-100);
            Assert.Equal(0, selected);
            Assert.Equal("Medium", AutomationProperties.GetItemStatus(slider));
            window.Close();
        });
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-127")] // App/Views/Composer/EffortSlider.swift:729-737
    // App/Views/Composer/EffortSlider.swift:985-998; capture method remains STOP #4, no whole-record Req.
    [InlineData(0, false, TrackLook.Kind.Plain)]
    [InlineData(2, false, TrackLook.Kind.Supercharged)]
    [InlineData(0, true, TrackLook.Kind.Fast)]
    [InlineData(2, true, TrackLook.Kind.Fusion)]
    public void Slider_maps_maximum_and_fast_to_the_four_looks(int index, bool fast, TrackLook.Kind kind)
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var slider = new EffortSlider(3, new Binding<int>(() => index, _ => { }), "Medium", fast);
            var window = Show(slider);
            var fill = Assert.Single(slider.GetVisualDescendants().OfType<Border>(), b => b.Name == "EffortFill");
            var theme = Avalonia.Styling.ThemeVariant.Light;
            switch (kind)
            {
                case TrackLook.Kind.Plain: // Swift :990 is Chrome.accent; DEVIATION OD-0075 (liquid at every effort level): the brand's fill under the liquid
                case TrackLook.Kind.Supercharged:
                    Assert.Equal(EffortBrand.Purple.FillColor.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background).Color); // Swift :991
                    break;
                default:
                    // Swift :992-996 (amber fast fill, brand-to-amber fusion); DEVIATION OD-0073 (ED-248): fast mode is the
                    // model's liquid with Strike at every effort, so the fill under it is the brand's, as at the top
                    Assert.Equal(EffortBrand.Purple.FillColor.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background).Color);
                    break;
            }
            var effects = slider.GetVisualDescendants().OfType<TrackEffect>().ToArray();
            Assert.Equal(kind, Assert.Single(effects).Look.TrackLookKind); // every look draws the liquid effect (OD-0075; Swift :760 draws none for plain)
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-143")] // App/Views/Composer/EffortSlider.swift:433-476
    [Trait("Req", "PR-COMPOSER-144")] // App/Views/Composer/EffortSlider.swift:328-336
    [Trait("Req", "UI-COMPOSER-043")] // App/Views/Composer/EffortSlider.swift:433-476
    public void Locked_model_row_keeps_detail_and_selection_column_but_cannot_choose()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var calls = 0;
            var entry = new ModelCatalog.Entry(ProviderKind.Codex, new ModelOption("test", "Test model", null, null, null));
            var row = new ModelListRow(entry, "New chats only", false, 46, false, false, () => calls++);
            var window = Show(row);
            var button = Assert.Single(row.GetVisualDescendants().OfType<Button>());
            Assert.False(button.IsEnabled);
            Assert.Equal(0.45, button.Opacity);
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "New chats only" && b.FontSize == 12);
            Assert.Contains(row.GetVisualDescendants().OfType<Control>(), b => b.Height == 46);
            Assert.Equal(0, calls);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-113")] // App/Views/Composer/EffortSlider.swift:527-529,730
    public void A_single_level_never_reads_as_maximum()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var window = Show(Card(["high"], null, null)); // one stop: count > 1 fails, so never maximum
            var title = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "High");
            Assert.Equal(EffortPalette.Title.Resolve(title.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
            var slider = Assert.Single(window.GetVisualDescendants().OfType<EffortSlider>());
            var fill = Assert.Single(slider.GetVisualDescendants().OfType<Border>(), b => b.Name == "EffortFill");
            // DEVIATION OD-0075 (liquid at every effort level): the plain track is the liquid over the brand's fill (Swift :990 has the accent and :760 no effect)
            Assert.Equal(new SwiftColor(0.55, 0.34, 0.97).Resolve(fill.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background).Color);
            Assert.Equal(TrackLook.Kind.Plain, Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>()).Look.TrackLookKind);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-147")] // App/Views/Composer/EffortSlider.swift:364-367; App/Views/Chat/HydraButton.swift:676-693
    public void Pair_efforts_cover_the_five_cases_with_dot_separators()
    {
        static string? Efforts(string? lead, string? heads) =>
            Marrow.App.Views.Chat.HydraPairSummary.Efforts(new HydraPair(ProviderKind.Codex) { OrchestratorEffort = lead, WorkerEffort = heads });
        Assert.Equal("High effort", Efforts("high", "high")); // Swift HydraButton :687
        Assert.Equal("Lead high, heads medium effort", Efforts("high", "medium")); // :688
        Assert.Equal("Lead high effort", Efforts("high", null)); // :689
        Assert.Equal("Heads medium effort", Efforts(null, "medium")); // :690
        Assert.Null(Efforts(null, null)); // :691
        Assert.Equal("Extra High effort", Efforts("xhigh", "xhigh")); // :687 .capitalized capitalises each word
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "opus", null, RuntimeMode.Supervised) { HydraEnabled = true };
            harness.WriteLibrary([], [thread]);
            var model = new AppModel();
            DisableExecutables(model, harness);
            model.Settings.HydraEnabled = true;
            var cases = new (string? Lead, string? Heads, string Detail)[]
            {
                ("high", "high", "Claude · High effort"),
                ("high", "medium", "Claude · Lead high, heads medium effort"),
                ("high", null, "Claude · Lead high effort"),
                (null, "medium", "Claude · Heads medium effort"),
                (null, null, "Claude"),
                ("xhigh", "xhigh", "Claude · Extra High effort"),
            };
            foreach (var item in cases)
            {
                var pair = new HydraPair(ProviderKind.Claude) { OrchestratorEffort = item.Lead, WorkerEffort = item.Heads };
                model.Settings.AddHydraPair(pair);
                var list = new ModelList(thread, false, _ => { }, () => { });
                var window = Host(list, model);
                var row = Assert.Single(list.GetVisualDescendants().OfType<HydraPairListRow>());
                Assert.Equal(item.Detail, row.GetVisualDescendants().OfType<TextBlock>().ElementAt(1).Text); // actual production detail(for:) :364-367
                window.Close();
                model.Settings.RemoveHydraPair(pair.Id);
            }
            var named = new HydraPair(ProviderKind.Claude)
            {
                Name = "My pair", OrchestratorModel = "opus", WorkerProvider = ProviderKind.Codex,
                WorkerModel = "custom-head", OrchestratorEffort = "high", WorkerEffort = "medium",
            };
            model.Settings.AddHydraPair(named);
            var namedList = new ModelList(thread, false, _ => { }, () => { });
            var namedWindow = Host(namedList, model);
            var namedRow = Assert.Single(namedList.GetVisualDescendants().OfType<HydraPairListRow>());
            var texts = namedRow.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Equal("My pair", texts[0].Text);
            Assert.Equal("Opus + custom-head · Claude lead, Codex heads · Lead high, heads medium effort", texts[1].Text); // Swift :365; HydraButton :677,:697-704
            namedWindow.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-121")] // App/Views/Composer/EffortSlider.swift:219-226,230-235
    [Trait("Req", "PR-COMPOSER-124")] // App/Views/Composer/EffortSlider.swift:230-235; App/AppSettings.swift:859-862 default preference removal
    public void Reset_with_an_unoffered_pair_effort_clears_and_restart_reads_it_back()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var pair = new HydraPair(ProviderKind.Claude) { OrchestratorEffort = "unoffered" };
            var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "opus", "high", RuntimeMode.Supervised, true) { HydraEnabled = true, HydraPairID = pair.Id };
            harness.WriteLibrary([], [thread]);
            var model = new AppModel();
            model.Settings.HydraEnabled = true;
            model.Settings.AddHydraPair(pair);
            var panel = new ModelEffortPanel(thread.Id, false);
            var window = Host(panel, model);
            // Precondition comes through production bindings, not a test-side SetPreference.
            SetPanelEffort(panel, 4); // Claude seed :71-77: max
            Assert.Equal(new ModelPreference { Effort = "max", FastMode = true }, model.Settings.Preference(ProviderKind.Claude, "opus"));
            ResetPanel(panel);
            Assert.Null(model.Thread(thread.Id)!.Effort); // Swift :219-226 ignores unoffered pair effort
            Assert.False(model.Thread(thread.Id)!.FastMode);
            var restarted = new AppModel();
            Assert.Equal(new ModelPreference(), restarted.Settings.Preference(ProviderKind.Claude, "opus"));
            Assert.False(restarted.Settings.ModelPreferences.ContainsKey("claude/opus")); // :859-862 removes default entry
            Assert.Equal("opus", restarted.Settings.LastModel(ProviderKind.Claude));
            Assert.Null(restarted.Settings.LastEffort(ProviderKind.Claude));
            Assert.Equal("opus", harness.Defaults.Dictionary("lastModels")!["claude"]);
            Assert.False(harness.Defaults.Dictionary("lastEfforts")!.ContainsKey("claude"));
            using var stored = System.Text.Json.JsonDocument.Parse(Assert.IsType<byte[]>(harness.Defaults.Object("modelPreferences")));
            Assert.False(stored.RootElement.TryGetProperty("claude/opus", out _));
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-122")] // App/Views/Composer/EffortSlider.swift:626-638,204-217,230-235
    [Trait("Req", "PR-COMPOSER-124")] // App/Views/Composer/EffortSlider.swift:230-235; App/AppSettings.swift:859-862,899-902
    public void A_chosen_preference_survives_restart_for_a_new_chat()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var project = new Project("Fixture", harness.NewFolder("project"));
            var thread = new ChatThread(project.Id, ProviderKind.Claude, "opus", "low", RuntimeMode.Supervised);
            harness.WriteLibrary([project], [thread]);
            var model = new AppModel();
            DisableExecutables(model, harness);
            var panel = new ModelEffortPanel(thread.Id, false);
            var window = Host(panel, model);
            SetPanelEffort(panel, 2); // production accessibility -> card snappy(0.2) -> effort binding -> Remember, high
            Assert.Single(Assert.Single(panel.GetVisualDescendants().OfType<FastModeButton>()).GetVisualDescendants().OfType<Button>())
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("high", model.Thread(thread.Id)!.Effort);
            Assert.True(model.Thread(thread.Id)!.FastMode);
            // Choose reads the preference produced above; never seed SetPreference in this test.
            var choose = typeof(ModelEffortPanel).GetMethod("Choose", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            choose.Invoke(panel, [new ModelCatalog.Entry(ProviderKind.Claude, model.Providers.Model("sonnet", ProviderKind.Claude)!), model.Thread(thread.Id)!]);
            Dispatcher.UIThread.RunJobs();
            choose.Invoke(panel, [new ModelCatalog.Entry(ProviderKind.Claude, model.Providers.Model("opus", ProviderKind.Claude)!), model.Thread(thread.Id)!]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("high", model.Thread(thread.Id)!.Effort);
            Assert.True(model.Thread(thread.Id)!.FastMode);
            window.Close();
            var restarted = new AppModel(); // new settings and registry, same isolated defaults store
            Assert.Equal(new ModelPreference { Effort = "high", FastMode = true }, restarted.Settings.Preference(ProviderKind.Claude, "opus"));
            Assert.Equal(new ModelPreference { Effort = "high", FastMode = true }, restarted.Settings.ModelPreferences["claude/opus"]); // AppSettings.swift :859-862
            Assert.Equal("opus", restarted.Settings.LastModels["claude"]); // :899-902 raw provider keys
            Assert.Equal("high", restarted.Settings.LastEfforts["claude"]);
            Assert.Equal("opus", harness.Defaults.Dictionary("lastModels")!["claude"]); // literal user-defaults keys, AppSettings.swift :140-147
            Assert.Equal("high", harness.Defaults.Dictionary("lastEfforts")!["claude"]);
            using var preferences = System.Text.Json.JsonDocument.Parse(Assert.IsType<byte[]>(harness.Defaults.Object("modelPreferences")));
            var stored = preferences.RootElement.GetProperty("claude/opus");
            Assert.Equal("high", stored.GetProperty("effort").GetString());
            Assert.True(stored.GetProperty("fastMode").GetBoolean());
            Assert.Equal("opus", restarted.Settings.LastModel(ProviderKind.Claude));
            Assert.Equal("high", restarted.Settings.LastEffort(ProviderKind.Claude));
            restarted.SelectedThreadID = null; // force fresh-chat preference path, not carrying an existing thread
            var fresh = Assert.IsType<ChatThread>(restarted.NewThread(project, WorkspaceMode.Local)); // AppModel.swift :668-680
            Assert.Equal(ProviderKind.Claude, fresh.Provider);
            Assert.Equal("opus", fresh.Model);
            Assert.Equal("high", fresh.Effort);
            Assert.True(fresh.FastMode);
        });
    }
    [Fact]
    [Trait("Decision", "ED-092")] // App/Views/Composer/EffortSlider.swift:765; removal views end with the view lifetime
    public void Detaching_mid_fade_does_not_leave_ghost_effects_when_the_slider_is_shown_again()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var selected = 1;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "Medium", true);
            var window = Show(slider);
            var outgoing = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            selected = 2; slider.UpdateLook(true, "High", null);
            time.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, slider.GetVisualDescendants().OfType<TrackEffect>().Count());
            window.Close(); window.Content = null;
            var shownAgain = Show(slider);
            var kept = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            Assert.NotSame(outgoing, kept);
            Assert.Equal(TrackLook.Kind.Fusion, KindOf(kept));
            Assert.Equal(1, kept.Opacity);
            shownAgain.Close();
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Source-derived assertions are partial evidence, not a full Req claim.
    // App/Views/Composer/EffortSlider.swift:1024-1026; source identity only, capture verification remains STOP #1.
    public void Pair_kind_changes_preserve_the_effect_instance_and_its_clock_origin()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var index = 2;
            var pair = new EffortPairBrand(new HydraPair(ProviderKind.Claude));
            var slider = new EffortSlider(3, new Binding<int>(() => index, _ => { }), "High", pair: pair);
            var window = Show(slider);
            var effect = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            var origin = typeof(TrackEffect).GetField("startedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(effect);
            index = 1; slider.UpdateLook(true, "Medium", pair);
            Assert.Same(effect, Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>()));
            Assert.Equal(TrackLook.Kind.Fast, KindOf(effect));
            Assert.Equal(origin, typeof(TrackEffect).GetField("startedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(effect));
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-124")] // App/Views/Composer/EffortSlider.swift:230-235; guard: no model ID means nothing is remembered
    public void Reset_without_a_model_id_does_not_change_the_remembered_choices()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var chosen = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "opus", "low", RuntimeMode.Supervised);
            var noModel = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, null, "high", RuntimeMode.Supervised, true);
            harness.WriteLibrary([], [chosen, noModel]);
            var model = new AppModel();
            var firstPanel = new ModelEffortPanel(chosen.Id, false);
            var firstWindow = Host(firstPanel, model);
            SetPanelEffort(firstPanel, 2); // production Remember seeds a meaningful prior choice
            firstWindow.Close();
            var panel = new ModelEffortPanel(noModel.Id, false);
            var window = Host(panel, model);
            ResetPanel(panel);
            Assert.Null(model.Thread(noModel.Id)!.Effort);
            Assert.False(model.Thread(noModel.Id)!.FastMode);
            var restarted = new AppModel();
            Assert.Equal(new ModelPreference { Effort = "high" }, restarted.Settings.Preference(ProviderKind.Claude, "opus"));
            Assert.Single(restarted.Settings.ModelPreferences);
            Assert.Equal("opus", restarted.Settings.LastModel(ProviderKind.Claude));
            Assert.Equal("high", restarted.Settings.LastEffort(ProviderKind.Claude));
            window.Close();
        });
    }

    private static void DisableExecutables(AppModel model, AppModelHarness harness)
    {
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(System.IO.Path.Combine(harness.Scratch, "missing.exe"), provider);
    }

    private static void SetPanelEffort(ModelEffortPanel panel, int index)
    {
        var slider = Assert.Single(panel.GetVisualDescendants().OfType<EffortSlider>());
        Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(slider)).SetValue(index);
        Dispatcher.UIThread.RunJobs();
    }

    private static void ResetPanel(ModelEffortPanel panel)
    {
        Assert.Single(panel.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "Reset to default"))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-121")] // App/Views/Composer/EffortSlider.swift:219-235; Providers/ProviderRegistry.swift:71-77
    [Trait("Req", "PR-COMPOSER-124")] // App/Views/Composer/EffortSlider.swift:230-235; App/AppSettings.swift:859-862 default preference removal
    [InlineData(false)]
    [InlineData(true)]
    public void Reset_with_the_seeded_claude_catalog_persists_with_and_without_an_offered_pair(bool hasPair)
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var pair = new HydraPair(ProviderKind.Claude) { OrchestratorEffort = "high" };
            var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "opus", "low", RuntimeMode.Supervised, true)
                { HydraEnabled = hasPair, HydraPairID = hasPair ? pair.Id : null };
            harness.WriteLibrary([], [thread]);
            var model = new AppModel();
            model.Settings.HydraEnabled = hasPair;
            if (hasPair) model.Settings.AddHydraPair(pair);
            Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, model.Providers.Model("opus", ProviderKind.Claude)!.Efforts);
            var panel = new ModelEffortPanel(thread.Id, false);
            var window = Host(panel, model);
            SetPanelEffort(panel, 4);
            var before = new AppModel();
            Assert.Equal(new ModelPreference { Effort = "max", FastMode = true }, before.Settings.Preference(ProviderKind.Claude, "opus"));
            Assert.Equal("max", before.Settings.LastEffort(ProviderKind.Claude));
            ResetPanel(panel);
            Assert.Equal(hasPair ? "high" : null, model.Thread(thread.Id)!.Effort);
            Assert.False(model.Thread(thread.Id)!.FastMode);
            var restarted = new AppModel();
            Assert.Equal(new ModelPreference { Effort = hasPair ? "high" : null, FastMode = false }, restarted.Settings.Preference(ProviderKind.Claude, "opus"));
            Assert.Equal("opus", restarted.Settings.LastModel(ProviderKind.Claude));
            Assert.Equal(hasPair ? "high" : null, restarted.Settings.LastEffort(ProviderKind.Claude));
            Assert.Equal(hasPair, restarted.Settings.ModelPreferences.ContainsKey("claude/opus"));
            Assert.Equal("opus", harness.Defaults.Dictionary("lastModels")!["claude"]);
            var lastEfforts = harness.Defaults.Dictionary("lastEfforts")!;
            Assert.Equal(hasPair, lastEfforts.ContainsKey("claude"));
            if (hasPair) Assert.Equal("high", lastEfforts["claude"]);
            using var stored = System.Text.Json.JsonDocument.Parse(Assert.IsType<byte[]>(harness.Defaults.Object("modelPreferences")));
            Assert.Equal(hasPair, stored.RootElement.TryGetProperty("claude/opus", out var preference));
            if (hasPair) { Assert.Equal("high", preference.GetProperty("effort").GetString()); Assert.False(preference.GetProperty("fastMode").GetBoolean()); }
            window.Close();
        });
    }

    [Theory]
    [Trait("Rule", "R-TEST-4")] // Source-derived assertions are partial evidence, not a full Req claim.
    // App/Views/Composer/EffortSlider.swift:986-988: pair fill is brand + pair.colors for EVERY kind; STOP #4.
    [InlineData(TrackLook.Kind.Plain)]
    [InlineData(TrackLook.Kind.Supercharged)]
    [InlineData(TrackLook.Kind.Fast)]
    [InlineData(TrackLook.Kind.Fusion)]
    public void Pair_fill_keeps_exact_stops_for_every_kind(TrackLook.Kind kind)
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var index = kind is TrackLook.Kind.Supercharged or TrackLook.Kind.Fusion ? 2 : 1;
            var fast = kind is TrackLook.Kind.Fast or TrackLook.Kind.Fusion;
            var pair = new EffortPairBrand(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex });
            var slider = new EffortSlider(3, new Binding<int>(() => index, _ => { }), "Effort", fast, EffortBrand.Claude, pair);
            var window = Show(slider);
            var fill = Assert.Single(slider.GetVisualDescendants().OfType<Border>(), b => b.Name == "EffortFill");
            var gradient = Assert.IsType<LinearGradientBrush>(fill.Background);
            Assert.Equal(3, gradient.GradientStops.Count); // Swift :948-961: Claude + silver heads' white 0.8 and 0.95, no fast stop
            Assert.Equal(EffortBrand.Claude.FillColor.Resolve(Avalonia.Styling.ThemeVariant.Light), gradient.GradientStops[0].Color);
            Assert.Equal(new SwiftColor(white: 0.8).Resolve(Avalonia.Styling.ThemeVariant.Light), gradient.GradientStops[1].Color);
            Assert.Equal(new SwiftColor(white: 0.95).Resolve(Avalonia.Styling.ThemeVariant.Light), gradient.GradientStops[2].Color);
            Assert.Equal(0, gradient.GradientStops[0].Offset);
            Assert.Equal(0.5, gradient.GradientStops[1].Offset);
            Assert.Equal(1, gradient.GradientStops[2].Offset);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-115")] // App/Views/Composer/EffortSlider.swift:191-197,500-503 the body re-reads hydraLaunch's resolved heads effort
    public void The_pair_caption_follows_the_heads_effort_the_lead_effort_resolves_to()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var pair = new HydraPair(ProviderKind.Claude);
            var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "opus", null, RuntimeMode.Supervised, true) { HydraEnabled = true, HydraPairID = pair.Id };
            harness.WriteLibrary([], [thread]);
            var model = new AppModel();
            model.Settings.HydraEnabled = true;
            model.Settings.HydraTempersHeadEffort = true;
            model.Settings.AddHydraPair(pair);
            var panel = new ModelEffortPanel(thread.Id, false);
            var window = Host(panel, model);
            Assert.Single(panel.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "The lead's effort · heads keep their own");

            model.UpdateThread(thread.Id, current => current with { Effort = "low" });
            Dispatcher.UIThread.RunJobs();
            Assert.Single(panel.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "The lead's effort · heads run at low effort");
            window.Close();
        });
    }

    private static (EffortSlider Slider, ObservableIntBox Index, Panel Knob, Avalonia.Controls.Window Window) OpenSlider(int index, bool fast = false, EffortBrand brand = EffortBrand.Purple)
    {
        var box = new ObservableIntBox { Value = index };
        var slider = new EffortSlider(3, box.AsBinding(), "Title", fast, brand);
        var window = Show(slider);
        var knob = Assert.Single(slider.GetVisualDescendants().OfType<Panel>(), p => p.Width == 48 && p.Height == 48); // OD-0090: 48, not :713's 42
        return (slider, box, knob, window);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // App/Views/Composer/EffortSlider.swift:802-810 the first drag change assigns dragX inside withAnimation(.spring(response: 0.22, dampingFraction: 0.8)) (a part of PR-COMPOSER-125; OD-0090's knob)
    public void The_first_contact_springs_the_knob_from_its_resting_stop_to_the_pointer()
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider();
            var frames = new ManualFrameSource();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var (slider, _, knob, window) = OpenSlider(0);
            var origin = slider.TranslatePoint(default, window)!.Value;
            Assert.Equal(0, Canvas.GetLeft(knob));

            window.MouseDown(new Point(origin.X + 200, origin.Y + 21), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(0, Canvas.GetLeft(knob)); // no jump to the pointer
            time.Advance(TimeSpan.FromMilliseconds(60)); frames.Advance(TimeSpan.FromMilliseconds(60)); Dispatcher.UIThread.RunJobs();
            Assert.InRange(Canvas.GetLeft(knob), 1, 175);
            OneSecondOfFrames(time, frames);
            Assert.Equal(176, Canvas.GetLeft(knob), 9); // pointer 200 minus the knob's 24 px radius (OD-0090)
            window.MouseUp(new Point(origin.X + 200, origin.Y + 21), MouseButton.Left, RawInputModifiers.None);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-252")] // App/Views/Composer/EffortSlider.swift:793 .scaleEffect(dragX == nil ? 1 : 1.06): SwiftUI scales about the centre (a part of PR-COMPOSER-123; OD-0090's knob)
    public void The_dragged_knob_scales_about_its_centre()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var (_, _, knob, window) = OpenSlider(0);
            Assert.Equal(RelativePoint.Center, knob.RenderTransformOrigin);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-252")] // App/Views/Composer/EffortSlider.swift:763-799 .animation(.smooth(duration: 0.3), value: look) covers the stops (part of PR-COMPOSER-127, not its mapping); the knob's glow (:780-784) is gone with the lens (OD-0090)
    public void A_look_change_crossfades_the_stop_dots_and_the_knob_has_no_glow()
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider();
            var frames = new ManualFrameSource();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var (slider, _, _, window) = OpenSlider(2, fast: false, brand: EffortBrand.Silver);
            var stop = slider.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().First(e => e.Width == 5);
            // OD-0090: the source's blurred glow disc under the lens (:780-784) is gone with the lens: the Jewel knob has no glow
            Assert.DoesNotContain(slider.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(), e => e.Effect is not null);
            var stopBefore = ((ISolidColorBrush)stop.Fill!).Color;

            slider.UpdateLook(true, "High", null); // silver maximum, then fast mode on: supercharged becomes fusion
            Assert.Equal(stopBefore, ((ISolidColorBrush)stop.Fill!).Color); // the crossfade starts from the old colours
            time.Advance(TimeSpan.FromMilliseconds(120)); frames.Advance(TimeSpan.FromMilliseconds(120)); Dispatcher.UIThread.RunJobs();
            var stopMid = ((ISolidColorBrush)stop.Fill!).Color;
            Assert.NotEqual(stopBefore, stopMid);
            time.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(stopMid, ((ISolidColorBrush)stop.Fill!).Color);
            window.Close();
        });
    }

    [Fact]
    [Trait("Decision", "ED-252")] // App/Views/Composer/EffortSlider.swift:799,822-830 an adjustable-action step changes the look; OD-0090/OD-0076: the knob springs to the stop, no jump
    public void An_accessibility_step_that_changes_the_look_glides_the_knob()
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider();
            var frames = new ManualFrameSource();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var (slider, _, knob, window) = OpenSlider(0);
            Assert.Equal(0, Canvas.GetLeft(knob));

            Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(slider)).SetValue(2);
            Dispatcher.UIThread.RunJobs(); // the tracked index change reaches the slider
            Assert.Equal(0, Canvas.GetLeft(knob)); // not a jump to the last stop
            time.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
            Assert.InRange(Canvas.GetLeft(knob), 1, 281);
            OneSecondOfFrames(time, frames);
            Assert.Equal(282, Canvas.GetLeft(knob), 9); // width 330: inset 24 (OD-0090), step 141, stop 2 centre 306 minus 24
            window.Close();
        });
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-128")] // App/Views/Composer/EffortSlider.swift:889-909 brand.fill: Meta and silver are two-stop gradients at maximum effort
    [InlineData(EffortBrand.Meta, 0.0, 0.39, 0.88, 0.0, 0.51, 0.98)]
    [InlineData(EffortBrand.Silver, 1.0, 1.0, 1.0, 0.88, 0.88, 0.91)]
    public void Meta_and_silver_maximum_effort_fills_are_their_source_gradients(EffortBrand brand, double r1, double g1, double b1, double r2, double g2, double b2)
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var (slider, _, _, window) = OpenSlider(2, fast: false, brand: brand);
            var fill = Assert.Single(slider.GetVisualDescendants().OfType<Border>(), b => b.Name == "EffortFill");
            var gradient = Assert.IsType<LinearGradientBrush>(fill.Background);
            Assert.Equal(2, gradient.GradientStops.Count);
            Assert.Equal(new SwiftColor(r1, g1, b1).Resolve(Avalonia.Styling.ThemeVariant.Light), gradient.GradientStops[0].Color);
            Assert.Equal(new SwiftColor(r2, g2, b2).Resolve(Avalonia.Styling.ThemeVariant.Light), gradient.GradientStops[1].Color);
            window.Close();
        });
    }
}

// Former STOPs of this file, closed (blitz-effortslider2):
// - PR-COMPOSER-133: EffortSliderCadenceTests proves every clause headless (ten seconds at 120 Hz draw 600 frames for every look, now the plain
//   one too (OD-0075), reduced motion and a minimised window pause it, a fill of 8 points or less draws nothing, never a hit target, hidden from
//   accessibility); the count on a real display (EffortSliderNativeFrameTests, excluded by the !~Native filter) is extra evidence, not run here.
// - PR-COMPOSER-132 (the effect's identity and clock) is asserted in EffortSliderTrackTests and, with the popover open in the real composer, in
//   EffortChipLiveInComposerTests; the plain look draws the effect too (OD-0075).
// - PR-COMPOSER-112 and UI-COMPOSER-031: Color.mix(with:by:) names no colour space at :841 and :964, so SwiftUI's default (perceptual, Oklab)
//   applies: ENGINEERING DECISION ED-286 (OD-0098); SwiftColor.MixPerceptual, tested against an independent Oklab implementation
//   (SwiftColorPerceptualMixTests, Fixtures/OklabReference).
// - PR-COMPOSER-127 and PR-COMPOSER-128: OD-0075 (liquid at every effort level) is built; the records carry its deviation. The per-model and
//   per-effort shade of OD-0063 is the colour engine's (unbuilt, ED-286): every effort wears the maker's palette until then.
