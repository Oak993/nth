using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :506-657 EffortSliderCard: the title and its colour
// (PR-COMPOSER-110, 112), the line under it (114), the pair caption (116) and the card's own layout (118).
[Collection("Headless")]
public sealed class EffortSliderCardTests(HeadlessSession headless)
{
    private static readonly ThemeVariant Dark = ThemeVariant.Dark;

    private static Avalonia.Controls.Window Show(Control card, double width = 330, double height = 500)
    {
        var host = new Border { Child = card, Width = width, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Content = host, Width = width, Height = height };
        window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }

    private static EffortSliderCard Card(string[] levels, string? chosen, string? defaultEffort, ProviderKind? provider = null, bool supportsFast = false, bool fast = false,
        EffortPairLook? pair = null, Action? titleTap = null, string modelName = "Test model") =>
        new(modelName, provider, new ValueList<string>(levels), defaultEffort, supportsFast,
            Binding<string?>.Constant(chosen), Binding<bool>.Constant(fast), () => { }, pair, titleTap);

    private static MorphingTextBlock Title(Control card) => card.GetVisualDescendants().OfType<MorphingTextBlock>().Single();

    private static Color Rgb(SwiftColor colour) => colour.Resolve(Dark);

    // ---- PR-COMPOSER-110: the title --------------------------------------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-COMPOSER-110")] // App/Views/Composer/EffortSlider.swift:521-534 resolvedIndex, title; Core/Models/Provider.swift:284-290 effortTitle
    [InlineData("low,medium,high", "low", "high", "Low")]               // the thread's own effort
    [InlineData("low,medium,high", "bogus", "high", "High")]            // not offered: the model's default
    [InlineData("low,medium,high", null, "bogus", "Medium")]            // no default on offer: "medium"
    [InlineData("low,medium,high", "bogus", "bogus", "Medium")]         // neither on offer: "medium"
    [InlineData("low,high", "bogus", null, "Low")]                      // no "medium" at all: index 0
    [InlineData("low,high", null, "bogus", "Low")]
    [InlineData("low,xhigh", "xhigh", null, "Extra High")]              // effortTitle's two special spellings
    [InlineData("low,extra-high", "extra-high", null, "Extra High")]
    [InlineData("low,", "", null, "Default")]                           // the empty string
    [InlineData("low,max", "max", null, "Max")]                         // only the first character is upper-cased
    [InlineData("low,ultraThink", "ultraThink", null, "UltraThink")]    // the rest is kept as it is
    [InlineData("", null, null, "Standard")]                            // a model with no efforts
    public void The_title_is_the_resolved_efforts_title_or_standard_and_the_model_line_button_still_renders(string levels, string? chosen, string? defaultEffort, string expected) =>
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var tapped = 0;
            var card = Card(levels.Length == 0 ? [] : levels.Split(','), chosen, defaultEffort, titleTap: () => tapped++);
            var window = Show(card);
            try
            {
                Assert.Equal(expected, Title(card).Text);
                var button = card.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), "Choose a model"));
                Assert.True(button.IsEnabled); // "Standard" too: the model line button still renders
                Assert.DoesNotContain(Title(card).GetVisualAncestors(), ancestor => ancestor is Button); // the title is plain text, not the button
                Assert.DoesNotContain(button.GetVisualDescendants(), visual => visual is MorphingTextBlock);
                Assert.Single(button.GetVisualDescendants().OfType<SwiftImage>(), image => image.SymbolName == "chevron.right"); // the chevron after the line
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, tapped);
            }
            finally { window.Close(); }
        });

    // ---- PR-COMPOSER-112: the title's colour ------------------------------------------------------------------------------

    private static void AssertSolid(Color expected, IBrush? brush, string why)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.True(Near(expected, solid.Color), $"{why}: {solid.Color} is not {expected}");
    }

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 1 && Math.Abs(a.G - b.G) <= 1 && Math.Abs(a.B - b.B) <= 1 && Math.Abs(a.A - b.A) <= 1;

    private static void AssertGradient(Color[] expected, IBrush? brush, string why)
    {
        var gradient = Assert.IsType<LinearGradientBrush>(brush);
        Assert.Equal(new RelativePoint(0, 0.5, RelativeUnit.Relative), gradient.StartPoint); // .leading
        Assert.Equal(new RelativePoint(1, 0.5, RelativeUnit.Relative), gradient.EndPoint);   // .trailing
        Assert.Equal(expected.Length, gradient.GradientStops.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(Near(expected[i], gradient.GradientStops[i].Color), $"{why}: stop {i} is {gradient.GradientStops[i].Color}, not {expected[i]}");
            Assert.Equal(i / (double)(expected.Length - 1), gradient.GradientStops[i].Offset, 6); // evenly spaced
        }
    }

    // The colours the Swift source literals give (EffortSlider.swift:839-847, :876-883, :931-935), written here from the
    // source, not read back from the port's own tables.
    private static readonly Color Gold = Rgb(new SwiftColor(1.0, 0.84, 0.36));            // :845 EffortPalette.fast
    private static readonly Color ClaudeTitle = Rgb(new SwiftColor(0.93, 0.58, 0.44));    // :876
    private static readonly Color GeminiBlue = Rgb(new SwiftColor(0.26, 0.52, 0.96));     // :932
    private static readonly Color GeminiViolet = Rgb(new SwiftColor(0.61, 0.45, 0.80));
    private static readonly Color GeminiRose = Rgb(new SwiftColor(0.85, 0.40, 0.44));

    [Theory]
    [Trait("Req", "PR-COMPOSER-112")] // App/Views/Composer/EffortSlider.swift:543-555 titleStyle: the four (maximum, fast) cases without a pair
    [Trait("Req", "UI-COMPOSER-031")] // App/Views/Composer/EffortSlider.swift:543-555
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Without_a_pair_the_title_is_the_accent_gold_the_brand_or_the_brand_into_gold(bool maximum, bool fast) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var card = Card(["low", "medium", "high"], maximum ? "high" : "low", null, ProviderKind.Claude, supportsFast: true, fast: fast);
        var window = Show(card);
        try
        {
            var title = Title(card);
            var theme = title.ActualThemeVariant;
            var gold = new SwiftColor(1.0, 0.84, 0.36).Resolve(theme);
            var claude = new SwiftColor(0.93, 0.58, 0.44).Resolve(theme);
            switch (maximum, fast)
            {
                case (false, false): // Chrome.accent mixed 22 percent toward white (:841)
                    // the default colour space of Color.mix(with:by:) is perceptual (ED-286): an independent Oklab mix
                    AssertSolid(OklabReference.MixWhite(Chrome.Accent.Resolve(theme), 0.22), title.Foreground, "neither");
                    break;
                case (false, true): AssertSolid(gold, title.Foreground, "fast mode alone is gold"); break;
                case (true, false): AssertSolid(claude, title.Foreground, "maximum alone is the brand's title colour"); break;
                default: AssertGradient([claude, gold], title.Foreground, "both: the brand into gold"); break;
            }
        }
        finally { window.Close(); }
    });

    private static SwiftColor EffortView_Resolved(SwiftColor colour, ThemeVariant theme)
    {
        var resolved = colour.Resolve(theme);
        return new SwiftColor(resolved.R / 255.0, resolved.G / 255.0, resolved.B / 255.0, resolved.A / 255.0);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-112")] // App/Views/Composer/EffortSlider.swift:545-551,880 Antigravity's brand title at maximum is the Gemini sweep, and with fast mode it is the brand's violet into gold
    [Trait("Req", "UI-COMPOSER-031")] // App/Views/Composer/EffortSlider.swift:545-551,880
    public void Antigravity_at_maximum_sweeps_the_gemini_colours_and_Silver_wears_the_primary_label_colour() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var antigravity = Card(["low", "high"], "high", null, ProviderKind.Antigravity);
        var window = Show(antigravity);
        try { AssertGradient([GeminiBlue, GeminiViolet, GeminiRose], Title(antigravity).Foreground, "Antigravity at maximum"); }
        finally { window.Close(); }
        var both = Card(["low", "high"], "high", null, ProviderKind.Antigravity, supportsFast: true, fast: true);
        window = Show(both);
        try { AssertGradient([GeminiViolet, Gold], Title(both).Foreground, "Antigravity at maximum with fast mode: brand title 0.61, 0.45, 0.80 into gold"); }
        finally { window.Close(); }
        var codex = Card(["low", "high"], "high", null, ProviderKind.Codex);
        window = Show(codex);
        try { AssertSolid(Chrome.PrimaryText.Resolve(Title(codex).ActualThemeVariant), Title(codex).Foreground, "Silver's title colour is the primary label colour (:880)"); }
        finally { window.Close(); }
    });

    [Theory]
    [Trait("Req", "PR-COMPOSER-112")] // App/Views/Composer/EffortSlider.swift:545-548 in a pair the title runs from the lead's colour into the heads', gold last in fast mode
    [Trait("Req", "UI-COMPOSER-031")] // App/Views/Composer/EffortSlider.swift:545-548
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void In_a_pair_the_title_always_runs_from_the_leads_colour_into_the_heads_with_gold_last_in_fast_mode(bool maximum, bool fast) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var model = new AppModel();
        var hydra = new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Deepseek, Name = "Pair" };
        var pair = new EffortPairLook(hydra, model.Providers, null);
        var card = Card(["low", "medium", "high"], maximum ? "high" : "low", null, ProviderKind.Claude, supportsFast: true, fast: fast, pair: pair);
        var window = Show(card);
        try
        {
            var theme = Title(card).ActualThemeVariant;
            // the heads' provider is DeepSeek: its fill and title colours (:911-920, :877) lightened 18 percent toward white (:964), in the
            // default (perceptual, Oklab) colour space: the values of an independent Oklab implementation (oklab.py), not the port's

            var expected = new List<Color>
            {
                new SwiftColor(0.93, 0.58, 0.44).Resolve(theme),                                  // the lead's title colour
                Color.FromRgb(90, 126, 254),                                                     // pair.brand.titleColors: (0.24, 0.36, 0.98) toward white by 0.18
                Color.FromRgb(147, 168, 255),                                                    // (0.49, 0.58, 1.0) toward white by 0.18
            };
            if (fast) expected.Add(new SwiftColor(1.0, 0.84, 0.36).Resolve(theme));
            AssertGradient(expected.ToArray(), Title(card).Foreground, "a pair's title");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-112")] // App/Views/Composer/EffortSlider.swift:582-583 .animation(.smooth(duration: 0.3), value: isFast): fast mode alone fades the title's colour over 0.3 s
    [Trait("Req", "UI-COMPOSER-031")] // App/Views/Composer/EffortSlider.swift:582-583
    public void Turning_fast_mode_on_fades_the_titles_colour_to_gold_over_smooth_point_three_and_back() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var fast = new ObservableBoolBox { Value = false };
        var card = new EffortSliderCard("Test model", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high"]), null, true,
            Binding<string?>.Constant("low"), fast.AsBinding(), () => { });
        var window = Show(card);
        try
        {
            var title = Title(card);
            var theme = title.ActualThemeVariant;
            var plain = Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color;
            var gold = new SwiftColor(1.0, 0.84, 0.36).Resolve(theme);
            var smooth = Animation.Smooth(0.3);
            void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
            Color Along(Color from, Color to, double p) => Color.FromRgb((byte)Math.Round(from.R + (to.R - from.R) * p), (byte)Math.Round(from.G + (to.G - from.G) * p), (byte)Math.Round(from.B + (to.B - from.B) * p));
            fast.Value = true; Dispatcher.UIThread.RunJobs();
            Assert.True(Near(plain, Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color), "the colour starts from where it was");
            Step(100);
            var mid = Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color;
            Assert.True(Near(Along(plain, gold, smooth.Progress(0.1)), mid), $"100 ms into smooth(0.3) the title is {mid}, not {Along(plain, gold, smooth.Progress(0.1))}");
            Step(300);
            AssertSolid(gold, title.Foreground, "settled on gold");
            fast.Value = false; Dispatcher.UIThread.RunJobs();
            Step(100);
            var back = Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color;
            Assert.True(Near(Along(gold, plain, smooth.Progress(0.1)), back), $"on the way back {back}");
            Step(300);
            AssertSolid(plain, title.Foreground, "settled on the accent again");
        }
        finally { window.Close(); }
    });

    // ---- PR-COMPOSER-114: the line under the title ------------------------------------------------------------------------

    private static EffortPairLook Pair(string name, string? headsEffort = null)
    {
        var model = new AppModel();
        return new EffortPairLook(new HydraPair(ProviderKind.Claude) { Name = name, WorkerEffort = headsEffort }, model.Providers, null);
    }

    private static Color InkOf(Control control, SwiftColor expected) => expected.Resolve(control.ActualThemeVariant);

    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static double Left(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.X;

    [Fact]
    [Trait("Req", "PR-COMPOSER-114")] // App/Views/Composer/EffortSlider.swift:599-617 a pair: the Hydra mark at 12 in primaryText 0.75, then the pair's title at .body in primaryText 0.7, spacing 5
    public void A_pairs_subtitle_is_the_hydra_mark_and_the_pairs_title() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var card = Card(["low", "high"], "low", null, ProviderKind.Claude, pair: Pair("Duo Team"), modelName: "Sonnet");
        var window = Show(card);
        try
        {
            var mark = card.GetVisualDescendants().OfType<HydraMarkImage>().Single();
            Assert.Equal((12, 12), (mark.Width, mark.Height));
            var image = Assert.IsType<SwiftImage>(mark.Child);
            Assert.Equal(InkOf(image, Chrome.PrimaryText.Opacity(0.75)), Solid(image.Foreground)); // :603 foregroundStyle(primaryText.opacity(0.75))
            Assert.Empty(card.GetVisualDescendants().OfType<ProviderIcon>()); // a pair has the Hydra mark in place of the provider's icon
            var title = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Duo Team");
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Sonnet"); // the model's name gives way to the pair's title
            Assert.Equal(13, title.FontSize); // .body
            Assert.Equal(1, title.MaxLines);
            Assert.Equal(InkOf(title, Chrome.PrimaryText.Opacity(0.7)), Solid(title.Foreground)); // :611
            Assert.Equal(5, Left(title, card) - (Left(mark, card) + 12), 0.5); // :599 HStack(spacing: 5)
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-114")] // App/Views/Composer/EffortSlider.swift:599-617 a plain chat: the provider's icon at 12 beside the model's name; no provider, no icon
    public void A_plain_subtitle_is_the_providers_icon_and_the_models_name_and_a_card_with_no_provider_has_no_icon() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var plain = Card(["low", "high"], "low", null, ProviderKind.Claude, modelName: "Sonnet");
        var window = Show(plain);
        try
        {
            var icon = plain.GetVisualDescendants().OfType<ProviderIcon>().Single();
            Assert.Equal((12, 12, 12), (icon.Size, icon.Width, icon.Height));
            Assert.Empty(plain.GetVisualDescendants().OfType<HydraMarkImage>());
            var name = plain.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Sonnet");
            Assert.Equal(5, Left(name, plain) - (Left(icon, plain) + 12), 0.5);
            Assert.Equal(InkOf(name, Chrome.PrimaryText.Opacity(0.7)), Solid(name.Foreground));
        }
        finally { window.Close(); }
        var bare = Card(["low", "high"], "low", null, provider: null, modelName: "Sonnet");
        window = Show(bare);
        try
        {
            Assert.Empty(bare.GetVisualDescendants().OfType<ProviderIcon>());
            Assert.Empty(bare.GetVisualDescendants().OfType<HydraMarkImage>());
            var name = bare.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Sonnet");
            // with no mark there is no 5 point gap: the name is centred alone in the card
            Assert.Equal(bare.Bounds.Width / 2, name.TranslatePoint(new Point(name.Bounds.Width / 2, 0), bare)!.Value.X, 1);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-114")] // App/Views/Composer/EffortSlider.swift:617-618 .padding(.horizontal, 44) and :614 minimumScaleFactor(0.85): a long pair title shrinks, then is cut, between the buttons
    public void A_long_pair_title_stays_inside_the_44_point_insets_shrinks_to_85_percent_and_is_cut() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var longTitle = string.Join(" + ", Enumerable.Repeat("Opus 5 (1M context)", 5)); // far wider than the 210 points the block has
        var card = Card(["low", "high"], "low", null, ProviderKind.Claude, supportsFast: true, pair: Pair(longTitle));
        var window = Show(card);
        try
        {
            var title = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == longTitle);
            var mark = card.GetVisualDescendants().OfType<HydraMarkImage>().Single();
            // the card is 330 wide, padded 16: the block is inset a further 44 each side, so mark and title share x in [60, 270]
            Assert.True(Left(mark, card) >= 60 - 0.5, $"the mark starts at {Left(mark, card)}, inside the left button's 44");
            var right = Left(title, card) + title.Bounds.Width;
            Assert.True(right <= 270 + 0.5, $"the title ends at {right}, inside the right button's 44");
            Assert.Contains(title.TextLayout.TextLines, line => line.HasCollapsed); // cut with an ellipsis after shrinking
            Assert.Equal(13 * 0.85, title.FontSize, 5); // :614 as far as 85 percent
            // the fast-mode button (30 at the card's left padding) and the reset button (30 at the right) are clear of it
            var fast = card.GetVisualDescendants().OfType<FastModeButton>().Single();
            var reset = card.GetVisualDescendants().OfType<RoundIconButton>().Single();
            Assert.True(Left(fast, card) + fast.Bounds.Width <= Left(mark, card));
            Assert.True(right <= Left(reset, card));
        }
        finally { window.Close(); }
    });

    // ---- PR-COMPOSER-118: the card's layout ------------------------------------------------------------------------------------

    private static Rect BoundsIn(Control control, Visual root) => new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

    [Fact]
    [Trait("Req", "PR-COMPOSER-118")] // App/Views/Composer/EffortSlider.swift:558,653-655 VStack(spacing: 16), padding 16 / 14 / 16 within the 330 point popover
    [Trait("Req", "UI-COMPOSER-030")] // App/Views/Composer/EffortSlider.swift:557-558,653-655 VStack(spacing: 16), .padding(.horizontal, 16), .padding(.top, 14), .padding(.bottom, 16)
    public void The_rows_sit_16_apart_inside_a_card_padded_16_left_and_right_14_above_and_16_below() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var card = Card(["low", "medium", "high", "xhigh", "max"], "medium", null, ProviderKind.Claude, supportsFast: true, pair: Pair("Duo"));
        var window = Show(card);
        try
        {
            Assert.Equal(330, card.Bounds.Width, 0.5);
            var fast = BoundsIn(card.GetVisualDescendants().OfType<FastModeButton>().Single(), card);
            var reset = BoundsIn(card.GetVisualDescendants().OfType<RoundIconButton>().Single(), card);
            var titleBlock = card.GetVisualDescendants().OfType<MorphingTextBlock>().Single().GetVisualAncestors().OfType<StackPanel>().First(s => s.Spacing == 2); // the centred title block
            var block = BoundsIn(titleBlock, card);
            var slider = BoundsIn(card.GetVisualDescendants().OfType<EffortSlider>().Single(), card);
            var caption = BoundsIn(card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text!.StartsWith("The lead's effort", StringComparison.Ordinal)), card);
            // the card's sides are 16 in and the header starts 14 from the top
            Assert.Equal(16, fast.X, 0.5);
            Assert.Equal(330 - 16, reset.Right, 0.5);
            Assert.Equal(14, Math.Min(Math.Min(fast.Y, reset.Y), block.Y), 0.5);
            Assert.Equal(16, slider.X, 0.5);
            Assert.Equal(330 - 32, slider.Width, 0.5);
            // header -> slider 16, slider -> caption 16 less the caption's 6 point lift, caption -> bottom 16
            var headerBottom = Math.Max(Math.Max(fast.Bottom, reset.Bottom), block.Bottom);
            Assert.Equal(headerBottom + 16, slider.Y, 0.5);
            Assert.Equal(slider.Bottom + 16 - 6, caption.Y, 0.5);
            Assert.Equal(caption.Bottom + 16, card.Bounds.Height, 0.5);
        }
        finally { window.Close(); }
        // without a pair there is no caption: the slider's own bottom meets the 16 point padding
        var plain = Card(["low", "medium", "high"], "medium", null, ProviderKind.Claude);
        window = Show(plain);
        try
        {
            var slider = BoundsIn(plain.GetVisualDescendants().OfType<EffortSlider>().Single(), plain);
            Assert.Equal(slider.Bottom + 16, plain.Bounds.Height, 0.5);
            Assert.Equal(14, BoundsIn(plain.GetVisualDescendants().OfType<RoundIconButton>().Single(), plain).Y, 0.5);
        }
        finally { window.Close(); }
    });

    // ---- PR-COMPOSER-116: the pair caption --------------------------------------------------------------------------------------

    // The caption's own opacity and offset as drawn: what every ancestor up to the card contributes (the soft-appear modifier is
    // a wrapper round the text, and the text scaling another).
    private static (double Opacity, double OffsetY) Shown(Control caption, Control card)
    {
        double opacity = caption.Opacity, offset = 0;
        for (var node = caption.GetVisualParent(); node is not null && !ReferenceEquals(node, card); node = node.GetVisualParent())
        {
            if (node is Visual visual) opacity *= visual.Opacity;
            if (node is Visual { RenderTransform: TranslateTransform translate }) offset += translate.Y;
            if (node is Visual { RenderTransform: TransformGroup group }) offset += group.Children.OfType<TranslateTransform>().Sum(t => t.Y);
        }
        return (opacity, offset);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-116")] // App/Views/Composer/EffortSlider.swift:642-651 .subheadline, secondary, one line, tail, minimumScaleFactor(0.9), padding(.top, -6); a card made with a pair has its caption whole from the first paint
    public void The_pair_caption_is_a_secondary_subheadline_lifted_6_and_whole_from_the_cards_first_paint() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        using var harness = new AppModelHarness(withRuntime: false);
        var pair = Pair("Duo", "high");
        var card = Card(["low", "medium", "high"], "medium", null, ProviderKind.Claude, pair: pair);
        var window = Show(card);
        try
        {
            var caption = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == pair.Caption);
            Assert.Equal("The lead's effort · heads run at high effort", caption.Text);
            Assert.Equal(11, caption.FontSize); // .subheadline
            Assert.Equal(1, caption.MaxLines);
            Assert.Equal(TextTrimming.CharacterEllipsis, caption.TextTrimming);
            Assert.Equal(new Thickness(0, -6, 0, 0), caption.Margin); // pulled 6 up into the 16 above
            Assert.Equal(InkOf(caption, Chrome.SecondaryText), Solid(caption.Foreground));
            // whole from the first paint: no fade, no 4 point rise (the popover grows with its caption in it: reference runtime,
            // 2-chat-use.mov 43.665-43.757 s), and nothing starts when the frames come
            Assert.Equal((1.0, 0.0), Shown(caption, card));
            clock.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.RunJobs();
            Assert.Equal((1.0, 0.0), Shown(caption, card));
        }
        finally { window.Close(); }
        // allowed to shrink to 90 percent before it is cut
        var narrow = Card(["low", "medium", "high"], "medium", null, ProviderKind.Claude, pair: pair);
        window = Show(narrow, width: 160);
        try
        {
            var caption = narrow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == pair.Caption);
            Assert.Equal(11 * 0.9, caption.FontSize, 5);
            Assert.Contains(caption.TextLayout.TextLines, line => line.HasCollapsed);
        }
        finally { window.Close(); }
    });

    private static EffortCardPanelEnv PanelFor(HydraPair pair, bool leading)
    {
        var owned = new List<IDisposable>();
        var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
        owned.Add(FrameClock.Source.Override(frames)); owned.Add(SwiftClock.Provider.Override(time));
        var harness = new AppModelHarness(withRuntime: false); owned.Add(harness);
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "c1", "medium", RuntimeMode.Supervised)
        {
            HydraEnabled = true,
            HydraPairID = leading ? pair.Id : null,
        };
        harness.WriteLibrary([], [thread]);
        var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        Marrow.App.Tests.App.Fixtures.HydraCookbookResolveFixture.SetModels(model.Providers, ProviderKind.Codex,
            new ModelOption("c1", "Codex one", efforts: new ValueList<string>(["low", "medium", "high"]), defaultEffort: "medium"));
        model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(pair);
        var env = new EffortCardPanelEnv { Model = model, Thread = thread, Time = time, Frames = frames };
        foreach (var value in owned) env.Own(value);
        return env;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-116")] // App/Views/Composer/EffortSlider.swift:650, Chrome.swift:949-961 .transition(.softAppear): arrives in 0.32 s smooth with a 4 point rise, leaves in 0.12 s ease-out, for a caption that joins or leaves a card on screen
    public void A_pair_joining_a_live_card_soft_appears_its_caption_and_one_leaving_fades_it_out_while_the_card_stays() => headless.OnUiThread(() =>
    {
        var pair = new HydraPair(ProviderKind.Codex) { Name = "Duo", WorkerEffort = "high" };
        using var env = PanelFor(pair, leading: false);
        var panel = new ModelEffortPanel(env.Thread.Id, false);
        var host = new Border { Child = panel, Width = 330, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        AppEnvironment.SetAppModel(host, env.Model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 330, Height = 500 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var card = Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text!.StartsWith("The lead's effort", StringComparison.Ordinal));
            Assert.Single(card.GetVisualDescendants().OfType<ProviderIcon>());
            Assert.Empty(card.GetVisualDescendants().OfType<HydraMarkImage>());

            // the chat enters the pair while the slider is on screen: the same card, the caption soft-appears
            env.Model.UpdateThread(env.Thread.Id, current => current with { HydraPairID = pair.Id });
            Dispatcher.UIThread.RunJobs();
            Assert.Same(card, Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>()));
            var caption = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "The lead's effort · heads run at high effort");
            var soft = Animation.SoftAppear;
            var chain = string.Join(" < ", Enumerable.Range(0, 1).Select(_ => caption).Concat(caption.GetVisualAncestors().TakeWhile(a => !ReferenceEquals(a, card)).OfType<Control>()).Select(c => $"{c.GetType().Name}[{c.Opacity}, {c.RenderTransform?.GetType().Name}]"));
            Assert.True(Math.Abs(Shown(caption, card).Opacity) < 0.001, $"right after the flip the caption shows {Shown(caption, card)}; chain {chain}");
            Assert.Equal(4, Shown(caption, card).OffsetY, 3);
            env.Step(160);
            var (opacity, rise) = Shown(caption, card);
            Assert.Equal(soft.Progress(0.16), opacity, 3);
            Assert.Equal(4 * (1 - soft.Progress(0.16)), rise, 3);
            Assert.InRange(opacity, 0.2, 0.99);
            env.Step(400);
            Assert.Equal((1.0, 0.0), Shown(caption, card));
            // what else the pair reaches follows in place: the line under the title, the title's colours, the track
            Assert.Empty(card.GetVisualDescendants().OfType<ProviderIcon>());
            Assert.Single(card.GetVisualDescendants().OfType<HydraMarkImage>());
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Duo");
            Assert.IsType<LinearGradientBrush>(card.GetVisualDescendants().OfType<MorphingTextBlock>().Single().Foreground);
            Assert.Single(card.GetVisualDescendants().OfType<TrackEffect>());
            // and the chat leaves the pair: the caption fades out over 0.12 s ease-out and is gone, the card the same
            env.Model.UpdateThread(env.Thread.Id, current => current with { HydraPairID = null });
            Dispatcher.UIThread.RunJobs();
            Assert.Same(card, Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>()));
            var leaving = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text!.StartsWith("The lead's effort", StringComparison.Ordinal));
            Assert.Equal(1, Shown(leaving, card).Opacity, 3);
            env.Step(60);
            Assert.Equal(1 - Animation.EaseOut(0.12).Progress(0.06), Shown(leaving, card).Opacity, 3);
            env.Step(80);
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text!.StartsWith("The lead's effort", StringComparison.Ordinal));
            Assert.Single(card.GetVisualDescendants().OfType<ProviderIcon>());
            Assert.Empty(card.GetVisualDescendants().OfType<HydraMarkImage>());
        }
        finally { window.Close(); }
    });
}
