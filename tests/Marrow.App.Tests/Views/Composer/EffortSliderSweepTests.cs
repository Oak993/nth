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

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :373-837.
[Collection("Headless")]
public sealed class EffortSliderSweepTests(HeadlessSession headless)
{
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
            using var harness = new AppModelHarness(withRuntime: false);
            var model = new AppModel();
            var backs = 0;
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
    [Trait("Req", "PR-COMPOSER-125")] // App/Views/Composer/EffortSlider.swift:802-818,833-836 (haptic boundary remains STOP)
    [Trait("Req", "UI-COMPOSER-035")] // App/Views/Composer/EffortSlider.swift:740-831
    public void Drag_clamps_and_snaps_to_each_stop_and_preserves_track_and_knob_sizes()
    {
        headless.OnUiThread(() =>
        {
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
