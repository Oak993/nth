using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// EffortSlider.swift:574 `.contentTransition(.numericText())` (also UsageViews.swift:235, HydraMergePopover.swift:204,
// Popovers.swift:311), the effort title changed under the slider's withAnimation(.snappy(duration: 0.2)) (:631).
// SOURCE FACT, the reference runtime, every frame (E:\Droppy\work\reference-video\study\effort-title-2026-10-03;
// 2-chat-use.mov High -> Extra High 34.84 s, Extra High -> Max 35.04 s, Max -> Extra High 35.635 s, Medium -> High
// 184.22 s, High -> Extra High 184.46 s):
// - arriving letters rise from below their place (the letter's centre starts about 6.6 px, 0.39 em of the 17 pt title,
//   below where it settles) while growing from about half size; leaving letters rise away upward by about the same
//   (7-7.5 px) while shrinking; the direction is the same whether the effort goes up or down;
// - only a common beginning or end of the two titles is kept ("High" in High <-> Extra High); "Extra High" -> "Max",
//   "Max" -> "Extra High" and "Medium" -> "High" keep nothing (no shared "a", "x" or "i" glides);
// - letters go one after another, left to right, over about 0.14 s whatever their number ("Max" arrives M, a, x about
//   70 ms apart, "Extra" about 30 ms apart, the nine leaving letters of "Extra High" about 20 ms apart);
// - each letter then follows the change's own curve (the first arriving "E" tracks the snappy spring from its start);
// - a gradient title is filled through the new text's own box set at the label's moving leading edge: its colours span
//   the new text from the first frame, gliding letters pass through them, and nothing outside the box shows (6 Luna +
//   6 Luna pair: "Medi" coloured across the whole sweep and "um" gone at once, 184.24 s; "Extra" cut through its "a" and
//   "High" gone, 184.715 s).
[Collection("Headless")]
public sealed class MorphingTextBlockRollTests(HeadlessSession headless)
{
    private static readonly Animation Slider = Animation.Snappy(0.2);

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void Arriving_letters_rise_from_below_and_leaving_letters_rise_away_upward()
    {
        Assert.InRange(MorphingTextBlock.Travel, 0.35, 0.45);
        var start = MorphingTextBlock.Glyphs("Extra High", "Max", 0, Slider);
        Assert.All(start.Where(g => g.OldIndex is null), g => Assert.Equal(MorphingTextBlock.Travel, g.Rise, 3)); // below
        Assert.All(start.Where(g => g.NewIndex is null), g => Assert.Equal(0, g.Rise, 3));
        var early = MorphingTextBlock.Glyphs("Extra High", "Max", 0.08, Slider);
        var first = early.Single(g => g.NewIndex == 0);
        Assert.InRange(first.Rise, 0.01, MorphingTextBlock.Travel - 0.01); // on its way up
        Assert.Contains(early, g => g.NewIndex is null && g.Rise < -0.01); // the first leaving letters already rise away
        var end = MorphingTextBlock.Glyphs("Extra High", "Max", MorphingTextBlock.Length(Slider), Slider);
        Assert.All(end.Where(g => g.NewIndex is null), g => Assert.Equal(-MorphingTextBlock.Travel, g.Rise, 2)); // above
        Assert.All(end.Where(g => g.OldIndex is null), g => Assert.Equal(0, g.Rise, 2));
        // Going down the same way: Max -> Extra High still brings the new letters up from below.
        Assert.All(MorphingTextBlock.Glyphs("Max", "Extra High", 0, Slider).Where(g => g.OldIndex is null), g => Assert.True(g.Rise > 0));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void Only_a_common_beginning_or_end_is_kept()
    {
        Assert.Equal([(0, 6), (1, 7), (2, 8), (3, 9)], MorphingTextBlock.KeptLetters("High", "Extra High"));
        Assert.Equal([(6, 0), (7, 1), (8, 2), (9, 3)], MorphingTextBlock.KeptLetters("Extra High", "High"));
        Assert.Empty(MorphingTextBlock.KeptLetters("Extra High", "Max"));
        Assert.Empty(MorphingTextBlock.KeptLetters("Max", "Extra High"));
        Assert.Empty(MorphingTextBlock.KeptLetters("Medium", "High"));
        Assert.Equal([(0, 0), (1, 1), (2, 2)], MorphingTextBlock.KeptLetters("Copy", "Copied"));
        var glyphs = MorphingTextBlock.Glyphs("Extra High", "Max", 0.1, Slider);
        Assert.Equal(3, glyphs.Count(g => g.OldIndex is null));  // M, a, x all arrive
        Assert.Equal(9, glyphs.Count(g => g.NewIndex is null));  // E x t r a H i g h all leave (the space draws nothing)
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void The_letters_go_left_to_right_over_about_0_14_s_whatever_their_number()
    {
        Assert.InRange(MorphingTextBlock.SpreadShare * Slider.Duration, 0.12, 0.16);
        Assert.InRange(MorphingTextBlock.ArrivalShare * Slider.Duration, 0.03, 0.05);
        foreach (var (from, to) in new[] { ("Extra High", "Max"), ("High", "Extra High") })
        {
            var arriving = (double t) => MorphingTextBlock.Glyphs(from, to, t, Slider).Where(g => g.OldIndex is null).OrderBy(g => g.NewIndex).ToList();
            Assert.Equal(0, arriving(0.03)[0].Progress); // the first new letter waits about 40 ms (video: "E" first seen at 27-49 ms, faint)
            Assert.True(arriving(0.05)[0].Progress > 0);
            Assert.Equal(0, arriving(0.15)[^1].Progress); // "x" of Max has not begun 150 ms in (video: first seen at about 0.19 s)
            Assert.True(arriving(0.2)[^1].Progress > 0);
            var mid = arriving(0.1);
            for (var i = 1; i < mid.Count; i++) Assert.True(mid[i].Progress <= mid[i - 1].Progress);
        }
        var leaving = MorphingTextBlock.Glyphs("Extra High", "Max", 0.12, Slider).Where(g => g.NewIndex is null).OrderBy(g => g.OldIndex).ToList();
        Assert.True(leaving[0].Opacity < 0.2);  // "E" is all but gone
        Assert.True(leaving[^1].Opacity > 0.9); // "h" has hardly begun (video: half gone at about 0.18 s)
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void Each_letter_follows_the_changes_own_curve_from_its_turn()
    {
        var wait = MorphingTextBlock.ArrivalShare * Slider.Duration;
        foreach (var t in new[] { 0.05, 0.07, 0.09, 0.11, 0.15, 0.2 })
        {
            // Video, the arriving "E" (34.84 s): its rise left 6.0, 4.6, 3.2, 2.2, 0.5 px at 49, 69, 90, 112, 155 ms.
            var first = MorphingTextBlock.Glyphs("High", "Extra High", t, Slider).Single(g => g.NewIndex == 0);
            Assert.Equal(Slider.Progress(t - wait), first.Progress, 6);
            var leaving = MorphingTextBlock.Glyphs("Extra High", "Max", t, Slider).Single(g => g.OldIndex == 0);
            Assert.Equal(Slider.Progress(t), leaving.Progress, 6); // a leaving letter goes at once
        }
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void The_morph_runs_until_its_last_letter_has_settled() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new MorphingTextBlock { Text = "Extra High", FontSize = 17 };
        var window = new Window { Width = 300, Height = 80, Content = label };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            label.SetText("Max", Slider);
            var ms = 0;
            void StepTo(double seconds)
            {
                while (ms < seconds * 1000) { clock.Advance(TimeSpan.FromMilliseconds(5)); frames.Advance(TimeSpan.FromMilliseconds(5)); ms += 5; Dispatcher.UIThread.RunJobs(); }
            }
            StepTo(Slider.Duration + 0.05);
            Assert.True(label.IsMorphing); // the last letters still run after the change's own duration
            StepTo(MorphingTextBlock.Length(Slider) + 0.02);
            Assert.False(label.IsMorphing);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void A_morph_draws_the_leaving_letters_above_the_line_and_the_arriving_ones_below_it() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new MorphingTextBlock { Text = "HEH", FontSize = 30, Foreground = Brushes.White, Margin = new Thickness(20) };
        var window = new Window { Width = 240, Height = 110, Background = Brushes.Black, Content = new Border { Child = label, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var rest = RenderedPixels.Capture(window).InkRows() ?? throw new Xunit.Sdk.XunitException("nothing drawn");
            label.SetText("NUN", Slider);
            clock.Advance(TimeSpan.FromMilliseconds(70)); // the old letters well on their way, the new ones begun
            frames.Advance(TimeSpan.FromMilliseconds(70));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var moving = RenderedPixels.Capture(window).InkRows() ?? throw new Xunit.Sdk.XunitException("nothing drawn");
            Assert.True(moving.Top < rest.Top - 1, $"leaving letters rise above the cap line: {moving.Top} vs {rest.Top}");
            Assert.True(moving.Bottom > rest.Bottom + 2, $"arriving letters come from below the baseline: {moving.Bottom} vs {rest.Bottom}");
        }
        finally { window.Close(); }
    });

    // Video, the 6 Luna pair: as "Extra" arrives the kept "High" glides right through the colours (blue, green, purple
    // at 184.50 s where it was orange and blue at rest), the gradient spanning the new text from the frame's edge.
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void A_gradient_spans_the_new_text_so_gliding_letters_pass_through_its_colours() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (window, label) = Gradient("HHHH");
        try
        {
            var rest = RenderedPixels.Capture(window);
            var first = Letters(rest)[0];
            var before = Blueness(rest, first.X0, first.X1, first.Y);
            label.SetText("MMHHHH", Slider); // "HHHH" kept, gliding right by two letters; the M's wait their turn
            Step(frames, clock, window, 20);
            var moving = RenderedPixels.Capture(window);
            var glidingFirst = Letters(moving)[0];
            var during = Blueness(moving, glidingFirst.X0, glidingFirst.X1, glidingFirst.Y);
            // Riding its own colour it would already be a third of the way to blue (its place in "MMHHHH"); passing
            // through the new text's gradient from where it still is, it is about as red as it was.
            Assert.True(during < before + 0.1 && during < 0.3, $"the first H's blueness: {before:F2} at rest, {during:F2} gliding");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void A_gradient_title_shows_nothing_outside_the_new_texts_box() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (window, label) = Gradient("HHHHHHHH");
        try
        {
            var fourth = Letters(RenderedPixels.Capture(window))[3];
            label.SetText("HHHH", Slider); // the last four leave; the first four are kept
            Step(frames, clock, window, 10);
            var pixels = RenderedPixels.Capture(window);
            var beyond = 0;
            for (var x = fourth.X1 + 4; x < pixels.Width; x++)
                for (var y = 0; y < pixels.Height; y++)
                    if (pixels.Level(x, y) > 30) beyond++;
            Assert.Equal(0, beyond); // the leaving "HHHH" lies outside the new text's box: gone at once
            label.Foreground = Brushes.White; // a plain colour draws them as they leave
            label.SetText("HHHHHHHH", null);
            label.SetText("HHHH", Slider);
            Step(frames, clock, window, 10);
            pixels = RenderedPixels.Capture(window);
            beyond = 0;
            for (var x = fourth.X1 + 4; x < pixels.Width; x++)
                for (var y = 0; y < pixels.Height; y++)
                    if (pixels.Level(x, y) > 30) beyond++;
            Assert.True(beyond > 50, $"a plain-colour title draws its leaving letters: {beyond} pixels");
        }
        finally { window.Close(); }
    });

    private static (Window Window, MorphingTextBlock Label) Gradient(string text)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Red, 0), new GradientStop(Colors.Blue, 1) },
        };
        var label = new MorphingTextBlock { Text = text, FontSize = 30, Foreground = gradient, Margin = new Thickness(10) };
        var window = new Window { Width = 400, Height = 80, Background = Brushes.Black, Content = new Border { Child = label, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, label);
    }

    private static void Step(ManualFrameSource frames, ManualTimeProvider clock, Window window, int milliseconds)
    {
        for (var ms = 0; ms < milliseconds; ms += 5)
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            frames.Advance(TimeSpan.FromMilliseconds(5));
            Dispatcher.UIThread.RunJobs();
        }
        window.UpdateLayout();
    }

    // Video 35.12-35.40 s: the arriving orange "M" of Max has its left edge at x 150-152 from its first frame to the end,
    // while the centred label's leading edge is still narrowing towards it.
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void In_a_centred_label_arriving_letters_appear_where_they_will_settle() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new MorphingTextBlock { Text = "HHHHHHHH", FontSize = 30, Foreground = Brushes.White, GrowthAnchor = 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
        var window = new Window { Width = 400, Height = 90, Background = Brushes.Black, Content = label };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var rest = RenderedPixels.Capture(window).InkRows() ?? throw new Xunit.Sdk.XunitException("nothing drawn");
            label.SetText("MM", Slider);
            Step(frames, clock, window, 85); // the first M on its way up (the second's turn not come), the frame still narrowing
            var below = InkCentre(RenderedPixels.Capture(window), rest.Bottom + 2, int.MaxValue); // only it lies this low
            Step(frames, clock, window, (int)(MorphingTextBlock.Length(Slider) * 1000) + 20);
            var settledPixels = RenderedPixels.Capture(window);
            var settledFirst = InkCentre(settledPixels, 0, (int)InkCentre(settledPixels, 0, int.MaxValue)); // the first M, settled
            Assert.InRange(below, settledFirst - 2, settledFirst + 2);
        }
        finally { window.Close(); }
    });

    // The x centre of the ink in rows from fromRow down, left of column beforeColumn.
    private static double InkCentre(RenderedPixels pixels, int fromRow, int beforeColumn)
    {
        double sum = 0, weight = 0;
        for (var y = fromRow; y < pixels.Height; y++)
            for (var x = 0; x < Math.Min(beforeColumn, pixels.Width); x++)
            {
                var level = pixels.Level(x, y);
                if (level <= 20) continue;
                sum += x * level;
                weight += level;
            }
        Assert.True(weight > 0, "nothing drawn there");
        return sum / weight;
    }

    // Video 35.04 s: "Extra High" -> "Max" came while "r" and "a" were still arriving (faint, low at 35.037 s); they then
    // left from there, raised and fading (35.10-35.14 s), never drawn whole in between.
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void A_change_that_interrupts_another_carries_on_from_where_its_letters_are() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new MorphingTextBlock { Text = "HHHH", FontSize = 30, Foreground = Brushes.White, Margin = new Thickness(10) };
        var window = new Window { Width = 400, Height = 90, Background = Brushes.Black, Content = new Border { Child = label, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var right = (RenderedPixels.Capture(window) is var rest ? Enumerable.Range(0, rest.Width).Last(x => Enumerable.Range(0, rest.Height).Any(y => rest.Level(x, y) > 60)) : 0) + 3;
            double Ink() { var pixels = RenderedPixels.Capture(window); double sum = 0; for (var x = right; x < pixels.Width; x++) for (var y = 0; y < pixels.Height; y++) sum += pixels.Level(x, y); return sum; }
            void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            label.SetText("HHHHMM", Slider);
            Step(75); // the first M part way in, the second only begun
            var arriving = Ink();
            Assert.True(arriving > 0);
            label.SetText("HHHH", Slider);
            Step(5);
            var after = Ink();
            Assert.True(after < arriving * 1.15 + 1, $"the half-arrived letters leave from where they were: {arriving:F0} -> {after:F0}");
        }
        finally { window.Close(); }
    });

    // Each H's columns along the row through the middle of the ink (an H is one run across its bar), and that row.
    private static List<(int X0, int X1, int Y)> Letters(RenderedPixels pixels)
    {
        var (top, bottom) = pixels.InkRows() ?? throw new Xunit.Sdk.XunitException("nothing drawn");
        var y = (top + bottom) / 2;
        var runs = new List<(int, int, int)>();
        int? start = null;
        for (var x = 0; x <= pixels.Width; x++)
        {
            var ink = x < pixels.Width && pixels.Level(x, y) > 60;
            if (ink && start is null) start = x;
            if (!ink && start is { } s) { runs.Add((s, x - 1, y)); start = null; }
        }
        Assert.NotEmpty(runs);
        return runs;
    }

    // Blue over red plus blue, over the inked pixels of a row segment.
    private static double Blueness(RenderedPixels pixels, int x0, int x1, int y)
    {
        double red = 0, blue = 0;
        for (var x = x0; x <= x1; x++)
        {
            var (r, _, b) = pixels.At(x, y);
            red += r;
            blue += b;
        }
        return blue / Math.Max(1, red + blue);
    }
}
