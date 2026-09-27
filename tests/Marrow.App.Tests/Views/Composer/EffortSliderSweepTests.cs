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
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :373-837.
[Collection("Headless")]
public sealed class EffortSliderSweepTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-123")] // App/Views/Composer/EffortSlider.swift:802-818
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
            var knob = Assert.Single(slider.GetVisualDescendants().OfType<Panel>(), p => p.Width == 42 && p.Height == 42);
            window.MouseDown(new Point(origin.X + 100, origin.Y + 21), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(1, Assert.IsType<ScaleTransform>(knob.RenderTransform).ScaleX); // spring begins at the resting size
            time.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(1, Assert.IsType<ScaleTransform>(knob.RenderTransform).ScaleX);
            var draggedLeft = Canvas.GetLeft(knob);
            window.MouseUp(new Point(origin.X + 100, origin.Y + 21), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(draggedLeft, Canvas.GetLeft(knob)); // not a jump to the snapped centre
            time.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(144, Canvas.GetLeft(knob)); // width330, inset21, selected1 => centre165 minus21
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
    [Trait("Req", "PR-COMPOSER-132")] // App/Views/Composer/EffortSlider.swift:758-764,1024-1026
    public void Pair_keeps_effect_identity_across_kind_changes_while_a_solo_track_restarts()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var selected = 0;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "Low", true);
            var window = Show(slider);
            var before = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            selected = 2; slider.UpdateLook(true, "High", null);
            var after = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            Assert.NotSame(before, after);
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

    [Theory]
    [Trait("Req", "PR-COMPOSER-133")] // App/Views/Composer/EffortSlider.swift:1049-1050,1070-1071
    [InlineData(TrackLook.Kind.Supercharged, false)]
    [InlineData(TrackLook.Kind.Fast, true)]
    public void Track_timeline_respects_the_thirty_or_sixty_hertz_interval(TrackLook.Kind kind, bool ticksAtTwentyMilliseconds)
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            var effect = new TrackEffect(new TrackLook(kind, EffortBrand.Purple));
            var window = Show(effect);
            window.Activate(); Dispatcher.UIThread.RunJobs();
            var field = typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            time.Advance(TimeSpan.FromMilliseconds(20)); frames.Advance(TimeSpan.FromMilliseconds(20)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(ticksAtTwentyMilliseconds ? 0.02 : 0, (double)field.GetValue(effect)!, 6);
            time.Advance(TimeSpan.FromMilliseconds(20)); frames.Advance(TimeSpan.FromMilliseconds(20)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.04, (double)field.GetValue(effect)!, 6);
            Assert.False(effect.IsHitTestVisible);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(effect));
            window.Close();
        });
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-106")] // App/Views/Composer/EffortSlider.swift:59-103 (provider artwork remains STOP)
    [Trait("Req", "PR-COMPOSER-107")] // App/Views/Composer/EffortSlider.swift:82-103,142-149
    [Trait("Req", "PR-COMPOSER-108")] // App/Views/Composer/EffortSlider.swift:108-117
    [Trait("Req", "UI-COMPOSER-012")] // App/Views/Composer/EffortSlider.swift:59-123 (chip style remains STOP)
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
            var frame = Assert.IsType<Grid>(button.Content);
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
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Loading models…" && b.FontSize == 13);
            Assert.Equal(35, Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>()).Height);
            var back = Assert.Single(list.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "Back"));
            back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, backs);
            Assert.Equal(new Thickness(6), Assert.IsType<Border>(list.Child).Padding);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-147")] // App/Views/Composer/EffortSlider.swift:384-419
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
    [Trait("Req", "PR-COMPOSER-135")] // App/Views/Composer/EffortSlider.swift:1134-1148
    public void Particle_rendering_uses_the_source_position_radius_and_shimmer()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var drawing = new DrawingGroup();
            using (var context = drawing.Open())
                typeof(TrackEffect).GetMethod("DrawParticles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, [context, new Size(100, 34), 0.0, SwiftColor.White,
                        new[] { new TrackEffect.Particle { Start = 0.5, Height = 0.5, Radius = 1, Brightness = 0.5, Speed = 12, ShimmerRate = 2 } }]);
            var particle = Assert.Single(drawing.Children.OfType<GeometryDrawing>());
            Assert.Equal(new Rect(49, 16, 2, 2), particle.Geometry!.Bounds); // x=.5*108-4=50, y=34*(.16+.68*.5)=17
            Assert.InRange(Assert.IsAssignableFrom<ISolidColorBrush>(particle.Brush).Color.A, 75, 77); // .5*.6=.3
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-136")] // App/Views/Composer/EffortSlider.swift:1151-1201
    public void Speed_streak_is_a_rounded_gradient_with_a_transparent_tail()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var drawing = new DrawingGroup();
            using (var context = drawing.Open())
                typeof(TrackEffect).GetMethod("DrawStreaks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, [context, new Size(100, 34), 0.0,
                        new[] { new TrackEffect.Streak { Start = 0.5, Length = 10, Thickness = 2, Height = 0.5, Brightness = 0.5 } }]);
            var streak = Assert.Single(drawing.Children.OfType<GeometryDrawing>());
            Assert.Equal(new Rect(45, 16, 10, 2), streak.Geometry!.Bounds); // .5*122-10-6=45
            var gradient = Assert.IsType<LinearGradientBrush>(streak.Brush);
            Assert.Equal(0, gradient.GradientStops[0].Color.A);
            Assert.InRange(gradient.GradientStops[1].Color.A, 127, 128);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-137")] // App/Views/Composer/EffortSlider.swift:1204-1225
    public void Sheen_starts_one_band_before_the_track_and_repeats_at_2_point_8_seconds()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            foreach (var time in new[] { 0.0, 2.8 })
            {
                var drawing = new DrawingGroup();
                using (var context = drawing.Open())
                    typeof(TrackEffect).GetMethod("DrawSheen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                        .Invoke(null, [context, new Size(100, 34), time, SwiftColor.White]);
                var sheen = Assert.Single(drawing.Children.OfType<GeometryDrawing>());
                Assert.Equal(new Rect(-46, 0, 56, 34), sheen.Geometry!.Bounds);
                var gradient = Assert.IsType<LinearGradientBrush>(sheen.Brush);
                Assert.Equal(3, gradient.GradientStops.Count);
                Assert.Equal(0, gradient.GradientStops[0].Color.A);
                Assert.InRange(gradient.GradientStops[1].Color.A, 55, 57);
                Assert.Equal(0, gradient.GradientStops[2].Color.A);
            }
        });
    }

    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 330, Height = 220, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static EffortSliderCard Card(string[] levels, string? chosen, string? defaultLevel, Action? titleTap = null, Action? reset = null) =>
        new("Test model", null, new ValueList<string>(levels), defaultLevel, false,
            new Binding<string?>(() => chosen, value => chosen = value),
            new Binding<bool>(() => false, _ => { }), reset ?? (() => { }), onTitleTap: titleTap);

    [Theory]
    [Trait("Req", "PR-COMPOSER-110")] // App/Views/Composer/EffortSlider.swift:521-534
    [Trait("Req", "PR-COMPOSER-113")] // App/Views/Composer/EffortSlider.swift:527-534
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
    [Trait("Req", "PR-COMPOSER-117")] // App/Views/Composer/EffortSlider.swift:620-624
    [Trait("Req", "UI-COMPOSER-036")] // App/Views/Composer/EffortSlider.swift:620-624
    public void Empty_levels_show_standard_and_the_single_level_note()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var window = Show(Card([], null, null));
            var texts = window.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(texts, text => text.Text == "Standard");
            var note = Assert.Single(texts, text => text.Text == "This model has one reasoning level.");
            Assert.Equal(42, note.Height);
            Assert.Equal(12, note.FontSize);
            Assert.Empty(window.GetVisualDescendants().OfType<EffortSlider>());
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // App/Views/Composer/EffortSlider.swift:568-597
    [Trait("Req", "UI-COMPOSER-031")] // App/Views/Composer/EffortSlider.swift:568-597
    [Trait("Req", "UI-COMPOSER-034")] // App/Views/Composer/EffortSlider.swift:564
    public void Title_and_reset_are_real_buttons_with_exact_help_and_actions()
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
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
            Assert.Equal(1, tapped);
            Assert.Equal(1, resets);
            var standard = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Standard");
            Assert.Equal(17, standard.FontSize);
            Assert.Equal(FontWeight.Medium, standard.FontWeight);
            window.Close();
            var disabled = Show(Card([], null, null));
            Assert.Contains(disabled.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "") && !b.IsEnabled);
            disabled.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-118")] // App/Views/Composer/EffortSlider.swift:558,653-655
    [Trait("Req", "UI-COMPOSER-030")] // App/Views/Composer/EffortSlider.swift:558-655
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
    [Trait("Req", "PR-COMPOSER-123")] // App/Views/Composer/EffortSlider.swift:712-713,740-799
    [Trait("Req", "PR-COMPOSER-125")] // App/Views/Composer/EffortSlider.swift:802-818,833-836 (windows_path silently drops haptics)
    [Trait("Req", "UI-COMPOSER-035")] // App/Views/Composer/EffortSlider.swift:740-831
    public void Drag_clamps_and_snaps_to_each_stop_and_preserves_track_and_knob_sizes()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var selected = 0;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, value => selected = value), "Low");
            var window = Show(slider);
            Assert.Equal(42, slider.Height);
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
    [Trait("Req", "PR-COMPOSER-126")] // App/Views/Composer/EffortSlider.swift:822-830
    public void Accessibility_adjustments_clamp_at_the_extremes()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var selected = 0;
            var slider = new EffortSlider(3, new Binding<int>(() => selected, value => selected = value), "Medium");
            var window = Show(slider);
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
    [Trait("Req", "PR-COMPOSER-128")] // App/Views/Composer/EffortSlider.swift:985-998
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
            if (kind == TrackLook.Kind.Fusion) Assert.IsType<LinearGradientBrush>(fill.Background);
            else Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background);
            var effects = slider.GetVisualDescendants().OfType<TrackEffect>().ToArray();
            if (kind == TrackLook.Kind.Plain) Assert.Empty(effects);
            else Assert.Single(effects);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-143")] // App/Views/Composer/EffortSlider.swift:433-476
    [Trait("Req", "PR-COMPOSER-144")] // App/Views/Composer/EffortSlider.swift:328-336
    [Trait("Req", "UI-COMPOSER-043")] // App/Views/Composer/EffortSlider.swift:433-476 (provider artwork remains STOP)
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
}
