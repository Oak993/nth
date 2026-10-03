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

// MorphingTextBlock: SwiftUI's .contentTransition(.numericText()) (EffortSlider.swift:574, UsageViews.swift:235,
// HydraMergePopover.swift:204, Popovers.swift:311), measured from 2-chat-use.mov 34.80-35.75 s (see the control's note).
[Collection("Headless")]
public sealed class MorphingTextBlockTests(HeadlessSession headless)
{
    private static readonly Animation Change = Animation.Smooth(0.3);

    private static (Window Window, MorphingTextBlock Text, Control After) Host(string text)
    {
        var label = new MorphingTextBlock { Text = text, FontSize = 22 };
        var after = new Border { Width = 10, Height = 10 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { label, after } };
        var window = new Window { Width = 400, Height = 100, Content = row };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, label, after);
    }

    private static void Step(ManualFrameSource frames, ManualTimeProvider clock, Window window, int milliseconds)
    {
        clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // EffortSlider.swift:574 the title's content transition
    public void Outside_an_animation_the_text_changes_at_once() => headless.OnUiThread(() =>
    {
        var (window, label, _) = Host("High");
        try
        {
            label.SetText("Extra High", animation: null);
            Assert.False(label.IsMorphing);
            Assert.Equal("Extra High", label.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void Inside_an_animation_the_width_glides_so_a_neighbour_slides_and_the_change_settles_at_its_end() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (window, label, after) = Host("High");
        try
        {
            var from = label.Bounds.Width;
            var neighbourFrom = after.Bounds.X;
            label.SetText("Extra High", Change);
            Assert.True(label.IsMorphing);
            Assert.Equal("Extra High", label.Text); // the text itself is the new one at once (accessibility, layout)
            Step(frames, clock, window, 60);
            var midway = label.Bounds.Width;
            var neighbourMidway = after.Bounds.X;
            for (var i = 0; i < 40 && label.IsMorphing; i++) Step(frames, clock, window, 20);
            Assert.False(label.IsMorphing);
            var to = label.Bounds.Width;
            Assert.True(to > from + 20, $"the new text is wider: {from} -> {to}");
            Assert.InRange(midway, from + 1, to - 1); // part way, not a jump
            Assert.InRange(neighbourMidway, neighbourFrom + 1, after.Bounds.X - 1); // the chevron's place slides
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void Shared_letters_glide_and_new_letters_arrive_one_after_another_small_blurred_and_faint()
    {
        var pairs = MorphingTextBlock.KeptLetters("High", "Extra High");
        Assert.Equal([(0, 6), (1, 7), (2, 8), (3, 9)], pairs); // "High" is kept and moves, not morphed letter by letter

        var early = MorphingTextBlock.Glyphs("High", "Extra High", 0.08, Change); // past the first new letter's short wait
        var arriving = early.Where(glyph => glyph.OldIndex is null).OrderBy(glyph => glyph.NewIndex).ToList();
        Assert.Equal([0, 1, 2, 3, 4], arriving.Select(glyph => glyph.NewIndex!.Value)); // E x t r a (the space draws nothing)
        // Left to right: each later letter is no further along than the one before it, and the last has not begun.
        for (var i = 1; i < arriving.Count; i++) Assert.True(arriving[i].Progress <= arriving[i - 1].Progress);
        Assert.True(arriving[0].Progress > 0);
        Assert.Equal(0, arriving[^1].Progress);
        Assert.Equal(0, arriving[^1].Opacity);
        Assert.Equal(MorphingTextBlock.StartScale, arriving[^1].Scale);
        Assert.Equal(MorphingTextBlock.MaxBlur, arriving[^1].Blur);

        // Settled once the last letter's turn has run the change's curve: the turns come later than the change's own
        // duration (video 2026-10-03: "Max" still arriving 0.19-0.3 s into the slider's 0.26 s snappy change).
        var late = MorphingTextBlock.Glyphs("High", "Extra High", MorphingTextBlock.Length(Change), Change);
        Assert.All(late, glyph => Assert.Equal(1, glyph.Opacity, 3));
        Assert.All(late, glyph => Assert.Equal(1, glyph.Scale, 3));
        Assert.All(late, glyph => Assert.Equal(0, glyph.Blur, 3));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void Leaving_letters_shrink_blur_and_fade_where_they_were()
    {
        var mid = MorphingTextBlock.Glyphs("Extra High", "Max", 0.1, Change);
        var leaving = mid.Where(glyph => glyph.NewIndex is null).ToList();
        Assert.NotEmpty(leaving);
        Assert.All(leaving, glyph => Assert.True(glyph.FromOld));
        Assert.Contains(leaving, glyph => glyph.Opacity < 1 && glyph.Scale < 1 && glyph.Blur > 0);
        var end = MorphingTextBlock.Glyphs("Extra High", "Max", MorphingTextBlock.Length(Change), Change).Where(glyph => glyph.NewIndex is null);
        Assert.All(end, glyph => Assert.Equal(0, glyph.Opacity, 3));
    }

    // SOURCE FACT, measured every frame by the round-1 review (2-chat-use.mov, High -> Extra High at 34.84 s, the slider's
    // snappy 0.2 change; review\kept_high_touches_chevron_34.72-34.97.png): the gap between the kept "High" and the chevron
    // (the label's frame) is 9 px at rest, then 1, 3, 4, 6, 7 at 34.865, 34.887, 34.907, 34.928, 34.950 s, and settles back.
    // The chevron rides the frame, which follows the change's own curve from 34.860 s (fitted on the chevron alone, within
    // 0.4 px); the label grew by 42 px, so the gap is 9 + 42 * (frame's progress - the kept letters' progress). The kept
    // letters' own curve, fitted to all five frames, must give those gaps within 2 px (each edge is read to the pixel;
    // the fit's worst frame is 1.5 off), and give the gap back at the end.
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void The_kept_letters_run_ahead_of_the_labels_frame_as_the_video_measures()
    {
        var change = Animation.Snappy(0.2);
        const double start = 34.8604;
        var frames = new (double Time, double Gap)[] { (34.865, 1), (34.887, 3), (34.907, 4), (34.928, 6), (34.950, 7) };
        double Gap(double seconds)
        {
            var kept = MorphingTextBlock.Glyphs("High", "Extra High", seconds, change).Single(glyph => glyph.OldIndex == 0);
            return 9 + 42 * (change.Progress(seconds) - kept.Progress);
        }
        foreach (var (time, gap) in frames)
            Assert.True(Math.Abs(Gap(time - start) - gap) <= 2, $"at {time} s the gap is {Gap(time - start):F1} px; the video's {gap}");
        Assert.InRange(Gap(0), 1, 9 - 42 * 0.02); // ahead from the change's first frame, never touching
        for (var ms = 100; ms <= 400; ms += 20) Assert.InRange(Gap(ms / 1000.0), 8, 10); // and back to the gap at rest
        Assert.Equal(1.0, MorphingTextBlock.Glyphs("High", "Extra High", MorphingTextBlock.Length(change), change).Single(glyph => glyph.OldIndex == 0).Progress); // exactly home
    }

    // A letter a change keeps is drawn by the text renderer through the whole change as at rest: the same weight and
    // edges on the frame the change takes it over, every frame while it runs, and the frame it hands it back (round 1
    // drew it as an outline, greyscale and thinner, so it snapped at both ends; REVIEW ede6bf6fa). "2024" -> "2025" keeps
    // "202" where it stands; its pixels stay those of the label at rest at every scale.
    [Theory]
    [Trait("Req", "PR-COMPOSER-111")]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void A_kept_letter_is_drawn_as_at_rest_from_the_changes_first_frame_to_after_its_last(double scale) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new MorphingTextBlock { Text = "2024", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = Brushes.White };
        var host = new Border { Background = Brushes.Black, Child = label, Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.Measure(Size.Infinity);
        host.Arrange(new Rect(host.DesiredSize));
        var size = host.Bounds.Size;
        SkiaSharp.SKBitmap Draw()
        {
            host.Measure(Size.Infinity);
            host.Arrange(new Rect(host.DesiredSize));
            var pixels = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
            bitmap.Render(host);
            using var stream = new MemoryStream();
            bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            stream.Position = 0;
            return SkiaSharp.SKBitmap.Decode(stream);
        }
        using var rest = Draw();
        int Worst(SkiaSharp.SKBitmap other)
        {
            var worst = 0;
            for (var y = 0; y < rest.Height; y++)
                for (var x = 0; x < (int)(rest.Width * 0.55); x++) // the kept "202", clear of the rolling last figure's blur
                {
                    var a = rest.GetPixel(x, y);
                    var b = other.GetPixel(x, y);
                    worst = Math.Max(worst, Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue))));
                }
            return worst;
        }
        label.SetText("2025", Animation.Snappy(0.2));
        var seen = 0;
        while (label.IsMorphing && seen < 60)
        {
            using (var during = Draw()) Assert.True(Worst(during) <= 24, $"at {scale}x, {seen * 20} ms into the change, the kept letters differ by {Worst(during)} levels from the label at rest");
            clock.Advance(TimeSpan.FromMilliseconds(20));
            frames.Advance(TimeSpan.FromMilliseconds(20));
            Dispatcher.UIThread.RunJobs();
            seen++;
        }
        Assert.False(label.IsMorphing);
        Assert.True(seen >= 5, $"the change ran {seen} frames");
        using var after = Draw();
        Assert.True(Worst(after) <= 24, $"at {scale}x the kept letters differ by {Worst(after)} levels once the change hands them back");
    });
}
