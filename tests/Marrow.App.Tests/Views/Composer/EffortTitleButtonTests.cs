using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// EffortSlider.swift:568-597 (B3 52bc792), the effort card's title:
//   Button { onTitleTap?() } label: { HStack(spacing: 4) { Text(verbatim: title).font(.system(.title2, weight: .medium))
//   .contentTransition(.numericText()); if onTitleTap != nil { Image(systemName: "chevron.right")
//   .font(.system(.callout, weight: .semibold)).foregroundStyle(Chrome.secondaryText) } } ... .padding(.horizontal, 8)
//   .padding(.vertical, 2).background { RoundedRectangle(cornerRadius: 7, style: .continuous).fill(isTitleHovering &&
//   onTitleTap != nil ? Chrome.overlay(0.08) : Color.clear) } } .buttonStyle(.plain).disabled(onTitleTap == nil)
//   .onHover { hovering in withAnimation(Chrome.hover) { isTitleHovering = hovering } }
//   .help(onTitleTap == nil ? "" : "Choose a model")
// and ModelEffortPanel's onTitleTap (:218 `onTitleTap: { showsModels = true }`): the click switches the popover to the model list.
// The title's morph (numericText) is MorphingTextBlock's, proved by its own tests (ED-238); here it is the label's type.
[Collection("Headless")]
public sealed class EffortTitleButtonTests(HeadlessSession headless)
{
    private static Color ColourOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Rect BoundsIn(Visual visual, Visual root) => new(visual.TranslatePoint(default, root)!.Value, visual.Bounds.Size);

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // App/Views/Composer/EffortSlider.swift:568-597, :218
    public void The_effort_title_is_a_button_with_a_chevron_a_hover_plate_and_its_tooltip_and_opens_the_model_list() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        void Advance(int milliseconds)
        {
            for (var ms = 0; ms < milliseconds; ms += 5)
            {
                clock.Advance(TimeSpan.FromMilliseconds(5));
                frames.Advance(TimeSpan.FromMilliseconds(5));
                Dispatcher.UIThread.RunJobs();
            }
        }
        using var harness = new AppModelHarness(withRuntime: false);
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, null, null, RuntimeMode.Supervised);
        harness.WriteLibrary([], [thread]);
        var model = new AppModel();
        var panel = new ModelEffortPanel(thread.Id, false);
        var host = new Border { Child = panel };
        AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Width = 330, Height = 400, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // The button, its exact tooltip, enabled.
            var button = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "Choose a model"));
            Assert.True(button.IsEnabled);
            var plate = Assert.IsType<Border>(button.Content);
            var row = Assert.IsType<StackPanel>(plate.Child);
            Assert.Equal(Orientation.Horizontal, row.Orientation);
            // The title: .title2 at medium, 17 pt (ED-059), with the numeric-text morph (MorphingTextBlock).
            var title = Assert.IsType<MorphingTextBlock>(row.Children[0]);
            Assert.Equal(17, title.FontSize);
            Assert.Equal(FontWeight.Medium, title.FontWeight);
            // The chevron after it: chevron.right in .callout at semibold, 12 pt, in Chrome.secondaryText, 4 pt after the title.
            var chevron = Assert.Single(row.GetVisualDescendants().OfType<SwiftImage>(), image => image.SymbolName == "chevron.right");
            Assert.Same(chevron, row.Children[1]);
            var chevronFont = SwiftImage.GetFont(chevron)!.Resolve();
            Assert.Equal(12, chevronFont.FontSize);
            Assert.Equal(FontWeight.SemiBold, chevronFont.FontWeight);
            Assert.Equal(Chrome.SecondaryText.Resolve(chevron.ActualThemeVariant), ColourOf(chevron.Foreground));
            var titleBox = BoundsIn(title, window);
            var chevronBox = BoundsIn(chevron, window);
            Assert.Equal(4, chevronBox.X - titleBox.Right, 3);
            // The plate: the row padded 8 across and 2 down, a 7 pt continuous rounded rectangle, clear at rest.
            var plateBox = BoundsIn(plate, window);
            var rowBox = BoundsIn(row, window);
            Assert.Equal(8, rowBox.X - plateBox.X, 3);
            Assert.Equal(8, plateBox.Right - rowBox.Right, 3);
            Assert.Equal(2, rowBox.Y - plateBox.Y, 3);
            Assert.Equal(2, plateBox.Bottom - rowBox.Bottom, 3);
            Assert.Equal(new CornerRadius(7), plate.CornerRadius);
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(plate));
            Assert.Equal(0, ColourOf(plate.Background).A);
            // Hovering fades in the plate at 8 % of the label colour (Chrome.overlay(0.08)) over Chrome.hover (ease out, 0.1 s).
            var plate8 = Chrome.Overlay(0.08).Resolve(plate.ActualThemeVariant);
            Assert.True(plate8.A > 0);
            window.MouseMove(plateBox.Center, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Advance(40);
            var midway = ColourOf(plate.Background);
            Assert.True(midway.A > 0 && midway.A < plate8.A, $"40 ms into Chrome.hover the plate is at {midway}, not on its way to {plate8}");
            Advance(80);
            Assert.Equal(plate8, ColourOf(plate.Background));
            // And back over Chrome.hover when the pointer leaves (:592-594 withAnimation(Chrome.hover) both ways; final review
            // round 4: as first written only "clear 120 ms after" was read, and a plate cleared at once, mutant F4, passed).
            window.MouseMove(new Point(2, 398), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Advance(40);
            var fading = ColourOf(plate.Background);
            var along = plate8.A * (1 - Chrome.Hover.Progress(0.04));
            Assert.True(fading.A > 0 && fading.A < plate8.A && Math.Abs(fading.A - along) <= 1.5, $"40 ms after the pointer leaves the plate is at {fading}, not on Chrome.hover's way back ({along:F1} of {plate8.A})");
            Advance(80);
            Assert.Equal(0, ColourOf(plate.Background).A);
            // Clicking it switches the popover to the model list.
            Assert.Empty(panel.GetVisualDescendants().OfType<ModelList>());
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Advance(400);
            window.UpdateLayout();
            Assert.Single(panel.GetVisualDescendants().OfType<ModelList>());
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }

        // Where no handler is supplied (the website captures): no chevron, a disabled button, an empty tooltip, no plate on hover.
        // (The fill's own `&& onTitleTap != nil` cannot be told apart here: a disabled Avalonia button gets no pointer enter,
        // so the plate stays clear with or without it; mutant R-hoverguard survives for that reason, every other one dies.)
        string? chosen = null;
        var bare = new EffortSliderCard("Test model", null, new ValueList<string>(["low", "high"]), "high", false,
            new Binding<string?>(() => chosen, value => chosen = value), new Binding<bool>(() => false, _ => { }), () => { });
        var bareWindow = new Avalonia.Controls.Window { Width = 330, Height = 220, Content = bare };
        bareWindow.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var disabled = Assert.Single(bare.GetVisualDescendants().OfType<Button>(), b => b.Content is Border { Child: StackPanel row } && row.Children.Count > 0 && row.Children[0] is MorphingTextBlock);
            Assert.False(disabled.IsEnabled);
            Assert.Equal("", ToolTip.GetTip(disabled));
            Assert.DoesNotContain(disabled.GetVisualDescendants().OfType<SwiftImage>(), image => image.SymbolName == "chevron.right");
            var barePlate = Assert.IsType<Border>(disabled.Content);
            bareWindow.MouseMove(BoundsIn(barePlate, bareWindow).Center, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Advance(120);
            Assert.Equal(0, ColourOf(barePlate.Background).A);
        }
        finally { bareWindow.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0094's drift at the site: EffortSlider.cs's title, ENGINEERING DECISION 7 s per mirrored loop
    public void The_effort_titles_gradient_drifts_once_round_its_loop_in_seven_seconds() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        string? chosen = null;
        var card = new EffortSliderCard("Test model", null, new ValueList<string>(["low", "high"]), "high", false,
            new Binding<string?>(() => chosen, value => chosen = value), new Binding<bool>(() => false, _ => { }), () => { }, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 220, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.Equal(7, title.DriftPeriod);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
