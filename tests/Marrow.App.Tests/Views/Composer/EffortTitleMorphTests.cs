using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// EffortSlider.swift:573-574 Text(verbatim: title).contentTransition(.numericText()); :631 the slider writes the effort
// inside withAnimation(.snappy(duration: 0.2)). The morph itself is MorphingTextBlockTests'.
[Collection("Headless")]
public sealed class EffortTitleMorphTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    [Trait("Req", "UI-COMPOSER-031")]
    public void The_title_morphs_when_the_effort_changes_inside_an_animation_and_changes_at_once_outside_one() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.Equal("High", title.Text);
            Assert.Equal(0.5, title.GrowthAnchor); // :566-567 a centred title: the "M" of Max appears where it settles (video 35.12 s)

            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Max", title.Text);
            Assert.True(title.IsMorphing); // the change came inside the slider's animation

            effort.Value = "low";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Low", title.Text);
            Assert.False(title.IsMorphing); // no transaction: the text simply changes
        }
        finally { window.Close(); }
    });

    // EffortSlider.swift:543-555 titleStyle, :582-583 .animation(.smooth(duration: 0.3), value: isMaxEffort). SOURCE FACT
    // (reference runtime, 2-chat-use.mov 35.10-35.21 s, study effort-title-2026-10-03): when the title changes text and
    // colour together (Extra High -> Max), the arriving letters wear the new colour from their first frame (an orange
    // "M", R/G 1.4 over the glass, beside the pink leaving "High"); only a colour change without a new text fades.
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void A_title_that_morphs_into_new_letters_draws_them_in_the_new_colour_at_once() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            Dispatcher.UIThread.RunJobs();
            Assert.True(title.IsMorphing);
            var brush = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground);
            Assert.Equal(EffortBrand.Create(ProviderKind.Claude).TitleColor.Resolve(title.ActualThemeVariant), brush.Color); // :551 brand.title at Max
        }
        finally { window.Close(); }
    });

    // SOURCE FACT (reference runtime, 2-chat-use.mov 35.12-35.29 s): as "Extra High" narrows to "Max", the leaving "High"
    // is carried right past the chevron and fades there, raised; a plain SwiftUI button does not clip its label.
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void Leaving_letters_carried_past_the_chevron_are_drawn_not_clipped() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var effort = new ObservableStringBox { Value = "xhigh" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "xhigh", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { }, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 160, Content = card, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            var button = title.GetVisualAncestors().OfType<Avalonia.Controls.Button>().First();
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            for (var ms = 0; ms < 130; ms += 10)
            {
                clock.Advance(TimeSpan.FromMilliseconds(10));
                frames.Advance(TimeSpan.FromMilliseconds(10));
                Dispatcher.UIThread.RunJobs();
            }
            window.UpdateLayout();
            var box = new Avalonia.Rect(button.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value, button.Bounds.Size);
            using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
            using var pixels = frame.Lock();
            var row = new byte[pixels.RowBytes];
            var ink = 0;
            for (var y = (int)box.Top; y < (int)box.Bottom; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels.Address + y * pixels.RowBytes, row, 0, pixels.RowBytes);
                for (var x = (int)Math.Ceiling(box.Right) + 1; x < Math.Min(pixels.Size.Width, (int)box.Right + 40); x++)
                    if (Math.Max(row[x * 4], Math.Max(row[x * 4 + 1], row[x * 4 + 2])) > 40) ink++;
            }
            Assert.True(ink > 10, $"letters drawn past the title button's right edge: {ink} pixels");
        }
        finally { window.Close(); }
    });

    // OD-0094: the card's gradient title (here a pair's lead-to-heads colours) drifts at rest; a single-colour title
    // (a plain effort) stays as it is.
    [Theory]
    [Trait("Req", "PR-COMPOSER-111")] // the title (EffortSlider.swift:573-583) as ruled by OD-0094
    [InlineData(true)]
    [InlineData(false)]
    public void A_gradient_title_drifts_at_rest_and_a_single_colour_one_does_not(bool pair) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new Marrow.Testing.ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        using var harness = new AppModelHarness(withRuntime: false);
        var look = pair ? new EffortPairLook(new HydraPair(ProviderKind.Codex), new AppModel().Providers, null) : null;
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Lead", pair ? ProviderKind.Codex : ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { }, look, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 160, Content = card, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.Equal(pair, title.Foreground is Avalonia.Media.ILinearGradientBrush);
            byte[] Title()
            {
                window.UpdateLayout();
                var box = new Avalonia.Rect(title.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value, title.Bounds.Size);
                using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
                using var pixels = frame.Lock();
                var row = new byte[pixels.RowBytes];
                var result = new List<byte>();
                for (var y = (int)box.Top; y < (int)box.Bottom; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(pixels.Address + y * pixels.RowBytes, row, 0, pixels.RowBytes);
                    for (var x = (int)box.Left; x < (int)box.Right; x++) result.AddRange([row[x * 4], row[x * 4 + 1], row[x * 4 + 2]]);
                }
                return result.ToArray();
            }
            var start = Title();
            for (var ms = 0; ms < 1000; ms += 20)
            {
                clock.Advance(TimeSpan.FromMilliseconds(20));
                frames.Advance(TimeSpan.FromMilliseconds(20));
                Dispatcher.UIThread.RunJobs();
            }
            var later = Title();
            var moved = start.Zip(later, (a, b) => Math.Abs(a - b)).Max();
            if (pair) Assert.True(moved > 20, $"the pair's colours drift along the title: {moved}");
            else Assert.True(moved <= 1, $"a single colour stays: {moved}");
        }
        finally { window.Close(); }
    });
}
