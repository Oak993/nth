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
        var pairs = MorphingTextBlock.CommonSubsequence("High", "Extra High").Pairs;
        Assert.Equal([(0, 6), (1, 7), (2, 8), (3, 9)], pairs); // "High" is kept and moves, not morphed letter by letter

        var early = MorphingTextBlock.Glyphs("High", "Extra High", 0.03, Change);
        var arriving = early.Where(glyph => glyph.OldIndex is null).OrderBy(glyph => glyph.NewIndex).ToList();
        Assert.Equal([0, 1, 2, 3, 4], arriving.Select(glyph => glyph.NewIndex!.Value)); // E x t r a (the space draws nothing)
        // Left to right: each later letter is no further along than the one before it, and the last has not begun.
        for (var i = 1; i < arriving.Count; i++) Assert.True(arriving[i].Progress <= arriving[i - 1].Progress);
        Assert.True(arriving[0].Progress > 0);
        Assert.Equal(0, arriving[^1].Progress);
        Assert.Equal(0, arriving[^1].Opacity);
        Assert.Equal(MorphingTextBlock.StartScale, arriving[^1].Scale);
        Assert.Equal(MorphingTextBlock.MaxBlur, arriving[^1].Blur);

        var late = MorphingTextBlock.Glyphs("High", "Extra High", Change.Duration, Change);
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
        var end = MorphingTextBlock.Glyphs("Extra High", "Max", Change.Duration, Change).Where(glyph => glyph.NewIndex is null);
        Assert.All(end, glyph => Assert.Equal(0, glyph.Opacity, 3));
    }
}
