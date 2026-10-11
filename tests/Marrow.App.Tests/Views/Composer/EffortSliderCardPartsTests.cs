using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :543-617 the title button and the line under it,
// :620-624 the one-level note's neighbours, :641-651 the pair caption, :659-708 the two round buttons. Each scenario mounts the real card.
[Collection("Headless")]
public sealed class EffortSliderCardPartsTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control card, double width = 330, double height = 500)
    {
        var host = new Border { Child = card, Width = width, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Content = host, Width = width, Height = height };
        window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }

    private static EffortSliderCard Card(string[] levels, string? chosen, string? defaultEffort, ProviderKind? provider = null, bool supportsFast = false, bool fast = false,
        EffortPairLook? pair = null, Action? titleTap = null, string modelName = "Test model", Action? reset = null, Binding<bool>? fastBinding = null) =>
        new(modelName, provider, new ValueList<string>(levels), defaultEffort, supportsFast,
            Binding<string?>.Constant(chosen), fastBinding ?? Binding<bool>.Constant(fast), reset ?? (() => { }), pair, titleTap);

    private static MorphingTextBlock Title(Control card) => card.GetVisualDescendants().OfType<MorphingTextBlock>().Single();
    private static Color Colour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static Color Ink(Control control, SwiftColor expected) => expected.Resolve(control.ActualThemeVariant);
    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 1 && Math.Abs(a.G - b.G) <= 1 && Math.Abs(a.B - b.B) <= 1 && Math.Abs(a.A - b.A) <= 1;
    private static double Left(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.X;
    private static Point Centre(Control control, Visual root) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;

    private static void Click(Avalonia.Controls.Window window, Point point)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // pointer hit testing consumes the rendered scene, not just layout
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertSolid(Color expected, IBrush? brush, string why)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.True(Near(expected, solid.Color), $"{why}: {solid.Color} is not {expected}");
    }

    private static void AssertGradient(Color[] expected, IBrush? brush, string why)
    {
        var gradient = Assert.IsType<LinearGradientBrush>(brush);
        Assert.Equal(new RelativePoint(0, 0.5, RelativeUnit.Relative), gradient.StartPoint); // .leading
        Assert.Equal(new RelativePoint(1, 0.5, RelativeUnit.Relative), gradient.EndPoint);   // .trailing
        Assert.Equal(expected.Length, gradient.GradientStops.Count);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(Near(expected[i], gradient.GradientStops[i].Color), $"{why}: stop {i} is {gradient.GradientStops[i].Color}, not {expected[i]}");
    }

    private static void Step(ManualTimeProvider clock, ManualFrameSource frames, int milliseconds)
    {
        clock.Advance(TimeSpan.FromMilliseconds(milliseconds)); frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Dispatcher.UIThread.RunJobs();
    }

    // ---- UI-COMPOSER-031: the model line button (OD-0139) ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-COMPOSER-031")] // App/Views/Composer/EffortSlider.swift:531-534,543-597
    public void The_model_line_is_the_button_with_a_chevron_a_hover_plate_and_a_tap_that_opens_the_list_and_the_efforts_title_is_plain_text_in_its_colour() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var taps = 0;
        var card = Card(["low", "medium", "high"], "low", null, ProviderKind.Claude, supportsFast: true, titleTap: () => taps++);
        var window = Show(card);
        try
        {
            var title = Title(card);
            Assert.Equal("Low", title.Text);
            var button = card.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), "Choose a model")); // the tooltip (OD-0139: on the model line)
            Assert.True(button.IsEnabled);
            // the title is plain text: in no button, no tooltip, no chevron beside it
            Assert.DoesNotContain(title.GetVisualAncestors(), ancestor => ancestor is Button);
            Assert.Null(ToolTip.GetTip(title));
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<Button>(), b => b.GetVisualDescendants().Contains(title));
            // :569-597 moved onto the model line (OD-0139): the line and the chevron; .padding(.horizontal, 8), .padding(.vertical, 2); a radius 7 continuous plate
            var plate = Assert.IsType<Border>(button.Content);
            Assert.Equal(new Thickness(8, 2), plate.Padding);
            Assert.Equal(new CornerRadius(7), plate.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(plate));
            var row = Assert.IsType<Grid>(plate.Child);
            var line = Assert.IsType<Grid>(row.Children[0]);
            var modelName = Assert.Single(line.GetVisualDescendants().OfType<TextBlock>()); Assert.Equal("Test model", modelName.Text);
            var chevron = Assert.IsType<SwiftImage>(row.Children[1]); Assert.Equal(2, row.Children.Count);
            Assert.Equal("chevron.right", chevron.SymbolName);
            Assert.Equal(4, Left(chevron, card) - (Left(line, card) + line.Bounds.Width), 0.75);
            // :573 the title is .title2 medium (17 pt); the chevron by the 13 pt line is Footnote semibold (10 pt, smaller than the old callout 12 pt) in secondary text
            Assert.Equal(17, title.FontSize); Assert.Equal(FontWeight.Medium, title.FontWeight);
            var chevronFont = SwiftImage.GetFont(chevron)!.Resolve(); Assert.Equal(10, chevronFont.FontSize); Assert.Equal(FontWeight.SemiBold, chevronFont.FontWeight);
            Assert.True(chevronFont.FontSize < modelName.FontSize);
            Assert.Equal(Ink(chevron, Chrome.SecondaryText), Colour(chevron.Foreground));
            // :470 the title block is inset 44 points on each side, the title and the model line under it with 2 points between
            var block = title.GetVisualAncestors().OfType<StackPanel>().First(panel => panel.Margin == new Thickness(44, 0));
            Assert.Equal(2, block.Spacing); Assert.Same(title, block.Children[0]); Assert.Same(button, block.Children[1]);

            // :543-555 the colour, state by state (the Swift literals: the accent mixed 22 percent toward white, the brand's title, gold)
            var theme = title.ActualThemeVariant;
            var gold = new SwiftColor(1.0, 0.84, 0.36).Resolve(theme);
            var claude = new SwiftColor(0.93, 0.58, 0.44).Resolve(theme);
            var accent = Chrome.Accent.Resolve(theme);
            var mixed = OklabReference.MixWhite(accent, 0.22); // Color.mix(with:by:)'s default space is perceptual (ED-286)
            AssertSolid(mixed, title.Foreground, "neither maximum nor fast");
            var fast = Card(["low", "medium", "high"], "low", null, ProviderKind.Claude, supportsFast: true, fast: true, titleTap: () => { });
            var maximum = Card(["low", "medium", "high"], "high", null, ProviderKind.Claude, supportsFast: true, titleTap: () => { });
            var both = Card(["low", "medium", "high"], "high", null, ProviderKind.Claude, supportsFast: true, fast: true, titleTap: () => { });
            var other = Show(new StackPanel { Children = { fast, maximum, both } }, height: 1600);
            AssertSolid(gold, Title(fast).Foreground, "fast mode alone");
            AssertSolid(claude, Title(maximum).Foreground, "the brand's title at maximum");
            AssertGradient([claude, gold], Title(both).Foreground, "the brand into gold");
            other.Close();
            using (var harness = new AppModelHarness(withRuntime: false))
            {
                var model = new AppModel();
                var look = new EffortPairLook(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Deepseek, Name = "Pair" }, model.Providers, null);
                var paired = Card(["low", "medium", "high"], "low", null, ProviderKind.Claude, supportsFast: true, fast: true, pair: look, titleTap: () => { });
                var pairWindow = Show(paired);
                AssertGradient([claude, Color.FromRgb(90, 126, 254), Color.FromRgb(147, 168, 255), gold], // the heads' (0.24, 0.36, 0.98) and (0.49, 0.58, 1.0) toward white by 0.18 in Oklab (oklab.py)
                    Title(paired).Foreground, "a pair: the lead into the heads, gold last in fast mode");
                pairWindow.Close();
            }

            // :586-589 hover: Chrome.overlay(0.08) plate, eased by Chrome.hover (easeOut 0.1)
            Assert.Equal(0, Colour(plate.Background).A);
            var point = Centre(button, window);
            window.MouseMove(point); Dispatcher.UIThread.RunJobs();
            Step(clock, frames, 50);
            var alpha = (int)Math.Round(Chrome.PrimaryText.Opacity(0.08).Resolve(plate.ActualThemeVariant).A * Animation.EaseOut(0.1).Progress(0.05));
            Assert.InRange((int)Colour(plate.Background).A, alpha - 1, alpha + 1);
            Step(clock, frames, 100);
            Assert.Equal(Chrome.PrimaryText.Opacity(0.08).Resolve(plate.ActualThemeVariant), Colour(plate.Background));
            // a press anywhere on the plate (the block is the whole model line button), once: the list is opened
            Click(window, point);
            Assert.Equal(1, taps);
            window.MouseMove(new Point(329, 499)); Step(clock, frames, 200);
            Assert.Equal(0, Colour(plate.Background).A);

            // :582-583 the colour changes over smooth(0.3) when fast mode flips with the text unchanged
            var fastBox = new ObservableBoolBox { Value = false };
            var fading = Card(["low", "medium", "high"], "low", null, ProviderKind.Claude, supportsFast: true, titleTap: () => { }, fastBinding: fastBox.AsBinding());
            var fadeWindow = Show(fading);
            var fadeTitle = Title(fading);
            var before = Colour(fadeTitle.Foreground);
            fastBox.Value = true; Dispatcher.UIThread.RunJobs();
            Step(clock, frames, 100);
            var middle = Colour(fadeTitle.Foreground);
            Assert.False(Near(before, middle) || Near(gold, middle), $"100 ms into the change the title is between {before} and {gold}, not {middle}");
            Step(clock, frames, 300);
            AssertSolid(gold, fadeTitle.Foreground, "settled");
            fadeWindow.Close();
        }
        finally { window.Close(); }

        // :562 no title-tap handler: no chevron, a disabled button and an empty tooltip
        var plain = Card(["low", "medium", "high"], "medium", null, ProviderKind.Claude);
        var plainWindow = Show(plain);
        try
        {
            Assert.Equal("Medium", Title(plain).Text);
            var button = plain.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) is "");
            Assert.False(button.IsEnabled);
            Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Test model"); // the plain model line
            Assert.DoesNotContain(button.GetVisualDescendants(), visual => visual is MorphingTextBlock); // and the title is outside it
            Assert.DoesNotContain(plain.GetVisualDescendants().OfType<SwiftImage>(), image => image.SymbolName == "chevron.right");
            var disabledPoint = Centre(button, plainWindow);
            plainWindow.MouseMove(disabledPoint); Dispatcher.UIThread.RunJobs(); Step(clock, frames, 200);
            Assert.Equal(0, Colour(Assert.IsType<Border>(button.Content).Background).A); // no hover plate without a handler
        }
        finally { plainWindow.Close(); }
    });

    // ---- UI-COMPOSER-033 and UI-COMPOSER-034: the round buttons ------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-COMPOSER-033")] // App/Views/Composer/EffortSlider.swift:561-562,659-683
    public void The_fast_mode_button_is_a_30_point_circle_at_the_headers_leading_end_a_bolt_that_fills_yellow_and_is_absent_without_a_fast_tier() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var fast = new ObservableBoolBox { Value = false };
        var card = Card(["low", "medium", "high"], "low", null, ProviderKind.Claude, supportsFast: true, titleTap: () => { }, fastBinding: fast.AsBinding());
        var window = Show(card);
        try
        {
            var control = card.GetVisualDescendants().OfType<FastModeButton>().Single();
            var button = Assert.Single(control.GetVisualDescendants().OfType<Button>());
            var circle = Assert.Single(control.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>());
            // 30 by 30 at the leading end: at the card's 16 point padding, the reset button the same at the trailing end
            Assert.Equal((30, 30), (circle.Bounds.Width, circle.Bounds.Height)); // the circle is 30 by 30, centred in the header's height as an HStack centres its children
            Assert.Equal(16, Left(control, card), 0.5);
            var headerMiddle = Title(card).GetVisualAncestors().OfType<StackPanel>().First(panel => panel.Margin == new Thickness(44, 0));
            Assert.Equal(headerMiddle.TranslatePoint(new Point(0, headerMiddle.Bounds.Height / 2), card)!.Value.Y, circle.TranslatePoint(new Point(0, 15), card)!.Value.Y, 0.75);
            var reset = card.GetVisualDescendants().OfType<RoundIconButton>().Single();
            Assert.Equal(card.Bounds.Width - 16 - 30, Left(reset, card), 0.5);
            Assert.True(Left(control, card) < Left(Title(card), card));
            // off: the plain bolt at .body semibold (13) in secondary text on a 6 percent plate; "Fast mode" with the value "Off"
            SwiftImage Bolt() => Assert.Single(control.GetVisualDescendants().OfType<SwiftImage>());
            Assert.Equal("bolt", Bolt().SymbolName);
            var font = SwiftImage.GetFont(Bolt())!.Resolve(); Assert.Equal(13, font.FontSize); Assert.Equal(FontWeight.SemiBold, font.FontWeight);
            Assert.Equal(Ink(Bolt(), Chrome.SecondaryText), Colour(Bolt().Foreground));
            Assert.Equal(Chrome.PrimaryText.Opacity(0.06).Resolve(circle.ActualThemeVariant), Colour(circle.Fill));
            Assert.Equal("Fast mode", ToolTip.GetTip(button)); Assert.Equal("Fast mode", AutomationProperties.GetName(button)); Assert.Equal("Off", AutomationProperties.GetItemStatus(button));
            // hover: the plate is 10 percent
            var point = Centre(control, window);
            window.MouseMove(point); Dispatcher.UIThread.RunJobs(); Step(clock, frames, 400);
            Assert.Equal(Chrome.PrimaryText.Opacity(0.1).Resolve(circle.ActualThemeVariant), Colour(circle.Fill));
            // a press toggles it inside spring(response 0.3, damping 0.7): the plate moves over time and settles on yellow at 18 percent
            Click(window, point);
            Assert.True(fast.Value);
            Step(clock, frames, 30);
            var moving = Colour(circle.Fill);
            Assert.False(Near(moving, Chrome.PrimaryText.Opacity(0.1).Resolve(circle.ActualThemeVariant)), $"30 ms into the spring the plate is on its way: {moving}");
            Step(clock, frames, 70);
            Assert.True(Bolt().RenderTransform is Avalonia.Media.ScaleTransform { ScaleX: > 1.2 }, "100 ms in, the bolt is at the swell of its one bounce (:670 .symbolEffect(.bounce, value: isOn))");
            Step(clock, frames, 1930);
            Assert.Equal(1, (Bolt().RenderTransform as Avalonia.Media.ScaleTransform)?.ScaleX ?? 1); // back at rest
            Assert.Equal("bolt.fill", Bolt().SymbolName);
            Assert.Equal(SwiftColor.Yellow.Resolve(Bolt().ActualThemeVariant), Colour(Bolt().Foreground));
            Assert.Equal(SwiftColor.Yellow.Opacity(0.18).Resolve(circle.ActualThemeVariant), Colour(circle.Fill)); // hovering or not, on is the yellow plate
            Assert.Equal(SymbolEffectKind.Bounce, Bolt().DeferredSymbolEffect);
            Assert.Equal("Fast mode is on", ToolTip.GetTip(button)); Assert.Equal("Fast mode", AutomationProperties.GetName(button)); Assert.Equal("On", AutomationProperties.GetItemStatus(button));
            window.MouseMove(new Point(329, 499)); Step(clock, frames, 400);
            Assert.Equal(SwiftColor.Yellow.Opacity(0.18).Resolve(circle.ActualThemeVariant), Colour(circle.Fill));
            // and back off
            Click(window, point);
            Assert.False(fast.Value);
        }
        finally { window.Close(); }
        // a model with no fast tier: the button is not there at all, and the reset button keeps its end
        var none = Card(["low", "high"], "low", null, ProviderKind.Claude, supportsFast: false);
        var noneWindow = Show(none);
        try
        {
            Assert.Empty(none.GetVisualDescendants().OfType<FastModeButton>());
            Assert.Single(none.GetVisualDescendants().OfType<RoundIconButton>());
        }
        finally { noneWindow.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-COMPOSER-034")] // App/Views/Composer/EffortSlider.swift:565,685-708
    public void The_reset_button_is_a_30_point_circle_at_the_trailing_end_that_clears_fast_and_the_effort() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var resets = 0;
        var card = Card(["low", "medium", "high"], "low", null, ProviderKind.Claude, supportsFast: true, titleTap: () => { }, reset: () => resets++);
        var window = Show(card);
        try
        {
            var control = card.GetVisualDescendants().OfType<RoundIconButton>().Single();
            var button = Assert.Single(control.GetVisualDescendants().OfType<Button>());
            var circle = Assert.Single(control.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>());
            Assert.Equal((30, 30), (circle.Bounds.Width, circle.Bounds.Height));
            Assert.Equal(card.Bounds.Width - 16 - 30, Left(control, card), 0.5); // the trailing end of the header
            // arrow.counterclockwise at .body medium (13) in secondary text
            var icon = Assert.Single(control.GetVisualDescendants().OfType<SwiftImage>());
            Assert.Equal("arrow.counterclockwise", icon.SymbolName);
            var font = SwiftImage.GetFont(icon)!.Resolve(); Assert.Equal(13, font.FontSize); Assert.Equal(FontWeight.Medium, font.FontWeight);
            Assert.Equal(Ink(icon, Chrome.SecondaryText), Colour(icon.Foreground));
            // the tooltip and the accessible name are the same string
            Assert.Equal("Reset to default", ToolTip.GetTip(button)); Assert.Equal("Reset to default", AutomationProperties.GetName(button));
            // the circle is clear at rest and Chrome.overlay(0.1) while hovered, eased by Chrome.hover
            Assert.Equal(0, Colour(circle.Fill).A);
            var point = Centre(control, window);
            window.MouseMove(point); Dispatcher.UIThread.RunJobs(); Step(clock, frames, 50);
            var alpha = (int)Math.Round(Chrome.PrimaryText.Opacity(0.1).Resolve(circle.ActualThemeVariant).A * Animation.EaseOut(0.1).Progress(0.05));
            Assert.InRange((int)Colour(circle.Fill).A, alpha - 1, alpha + 1);
            Step(clock, frames, 100);
            Assert.Equal(Chrome.PrimaryText.Opacity(0.1).Resolve(circle.ActualThemeVariant), Colour(circle.Fill));
            // a press resets once; a corner of the 30 point square is outside the circle and does not
            Click(window, point);
            Assert.Equal(1, resets);
            Click(window, control.TranslatePoint(new Point(1, 1), window)!.Value);
            Assert.Equal(1, resets);
            window.MouseMove(new Point(329, 499)); Step(clock, frames, 200);
            Assert.Equal(0, Colour(circle.Fill).A);
        }
        finally { window.Close(); }

        // :219-224 what a press does in the panel: fast mode off and the effort nil, or the pair's lead effort when the model offers it
        var withLead = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "c1", OrchestratorEffort = "low", Name = "Lead low" };
        var withStray = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "c1", OrchestratorEffort = "ultra", Name = "Lead ultra" }; // an effort the model does not offer
        foreach (var (pair, expected) in new[] { ((HydraPair?)null, (string?)null), (withLead, "low"), (withStray, null) })
        {
            using var env = Make(() => Thread(ProviderKind.Codex, "c1", hydra: pair is not null, pair: pair?.Id) with { Effort = "high", FastMode = true }, model =>
            {
                model.Settings.HydraEnabled = true; if (pair is not null) model.Settings.AddHydraPair(pair);
            }, (ProviderKind.Codex, [Option("c1", "C1", fast: true)]));
            var panel = new ModelEffortPanel(env.Thread.Id, false);
            var panelWindow = env.Show(panel);
            var resetButton = ButtonWithTip(panel, "Reset to default");
            Assert.Equal("high", env.Model.Thread(env.Thread.Id)!.Effort); Assert.True(env.Model.Thread(env.Thread.Id)!.FastMode);
            Click(panelWindow, Centre(resetButton, panelWindow));
            var after = env.Model.Thread(env.Thread.Id)!;
            Assert.Equal(expected, after.Effort); Assert.False(after.FastMode);
        }
    });

    // ---- UI-COMPOSER-037: the pair caption ------------------------------------------------------------------------------------------

    private static EffortPairLook Look(AppModel model, string? headsEffort, string? resolved = null) =>
        new(new HydraPair(ProviderKind.Claude) { Name = "Duo", WorkerEffort = headsEffort }, model.Providers, resolved);

    private static Control CaptionHost(Control card, TextBlock caption) =>
        caption.GetVisualAncestors().OfType<Control>().First(control => control.Parent is StackPanel { Spacing: 16 });

    [Fact]
    [Trait("Req", "UI-COMPOSER-037")] // App/Views/Composer/EffortSlider.swift:492-503,642-651; Chrome.swift:940-961
    public void The_pair_caption_says_whose_effort_it_is_in_the_sources_words_soft_appears_and_is_absent_without_efforts() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        using var harness = new AppModelHarness(withRuntime: false);
        var model = new AppModel();
        // the two sentences, the separator U+00B7, the effort lower-cased ("Extra High" reads "extra high"), the pair's own heads effort ahead of the launch's resolved one
        Assert.Equal("The lead's effort \u00B7 heads run at extra high effort", Look(model, "xhigh").Caption);
        Assert.Equal("The lead's effort \u00B7 heads run at high effort", Look(model, null, "high").Caption);
        Assert.Equal("The lead's effort \u00B7 heads run at low effort", Look(model, "low", "high").Caption);
        Assert.Equal("The lead's effort \u00B7 heads keep their own", Look(model, null).Caption);
        var look = Look(model, "xhigh");
        var card = Card(["low", "medium", "high"], "medium", null, ProviderKind.Claude, pair: look, titleTap: () => { });
        var window = Show(card);
        try
        {
            var caption = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == look.Caption);
            // .subheadline (11), secondary text, one line, the tail cut, padding(.top, -6), centred like the card's other rows, whole from the first paint
            Assert.Equal(11, caption.FontSize); Assert.Equal(1, caption.MaxLines); Assert.Equal(TextTrimming.CharacterEllipsis, caption.TextTrimming);
            Assert.Equal(new Thickness(0, -6, 0, 0), caption.Margin);
            Assert.Equal(Ink(caption, Chrome.SecondaryText), Colour(caption.Foreground));
            var host = CaptionHost(card, caption);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, host.HorizontalAlignment);
            Assert.Equal(1, host.Opacity);
            Assert.True(Math.Abs(card.Bounds.Width / 2 - caption.TranslatePoint(new Point(caption.Bounds.Width / 2, 0), card)!.Value.X) <= 1, "centred in the card");
        }
        finally { window.Close(); }
        // shrinks to 0.9 before it is cut
        var narrow = Card(["low", "medium", "high"], "medium", null, ProviderKind.Claude, pair: look);
        var narrowWindow = Show(narrow, width: 160);
        try
        {
            var caption = narrow.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == look.Caption);
            Assert.Equal(11 * 0.9, caption.FontSize, 5); Assert.Contains(caption.TextLayout.TextLines, line => line.HasCollapsed);
        }
        finally { narrowWindow.Close(); }
        // absent when the model has no efforts, even in a pair: the one-level note is there instead
        var none = Card([], null, null, ProviderKind.Claude, pair: look);
        var noneWindow = Show(none);
        try
        {
            Assert.DoesNotContain(none.GetVisualDescendants().OfType<TextBlock>(), text => text.Text!.StartsWith("The lead's effort", StringComparison.Ordinal));
            Assert.Contains(none.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "This model has one reasoning level.");
        }
        finally { noneWindow.Close(); }
        // softAppear: a pair that joins a card on screen brings the caption in over smooth(0.32) with a 4 point rise; one that leaves fades it out over easeOut(0.12)
        var live = Card(["low", "medium", "high"], "medium", null, ProviderKind.Claude);
        var liveWindow = Show(live);
        try
        {
            Assert.DoesNotContain(live.GetVisualDescendants().OfType<TextBlock>(), text => text.Text!.StartsWith("The lead's effort", StringComparison.Ordinal));
            live.UpdatePair(look); Dispatcher.UIThread.RunJobs();
            var caption = live.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == look.Caption);
            var host = CaptionHost(live, caption);
            Assert.Equal(0, host.Opacity, 3); Assert.Equal(4, Assert.IsType<TranslateTransform>(host.RenderTransform).Y, 3);
            Step(clock, frames, 160);
            var soft = Animation.SoftAppear;
            Assert.Equal(soft.Progress(0.16), host.Opacity, 3); Assert.Equal(4 * (1 - soft.Progress(0.16)), Assert.IsType<TranslateTransform>(host.RenderTransform).Y, 3);
            Step(clock, frames, 400);
            Assert.Equal(1, host.Opacity, 3); Assert.Equal(0, Assert.IsType<TranslateTransform>(host.RenderTransform).Y, 3);
            live.UpdatePair(null); Dispatcher.UIThread.RunJobs();
            Step(clock, frames, 60);
            Assert.Equal(1 - Animation.EaseOut(0.12).Progress(0.06), host.Opacity, 3);
            Step(clock, frames, 80);
            Assert.DoesNotContain(live.GetVisualDescendants().OfType<TextBlock>(), text => text.Text!.StartsWith("The lead's effort", StringComparison.Ordinal));
        }
        finally { liveWindow.Close(); }
    });
}
