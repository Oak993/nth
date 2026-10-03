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

// REVIEW (final review of unit/effort-title-fidelity, round 1, 2026-10-03, finding 5): the kept letters of a change run
// ahead of the label's frame when it widens (High -> Extra High, 2-chat-use.mov 34.84 s). The same lead on a narrowing
// (Extra High -> High, 35.77-35.96 s) pulls the kept "High" away from the chevron: the gap between them is the width
// change times the frame's progress less the letters', and the chevron is what moves first. The video's frames for the
// narrowing, set by the chevron's progress, are in the comments of MorphingTextBlock.NarrowingKeptLead. Its kept letters
// of a change that takes over an unfinished one (Max -> Extra High taken over by Extra High -> High, as the drag goes
// down) also eased twice: from where they were to the point the change had brought them to, by the change's curve again.
[Collection("Headless")]
public sealed class MorphingTextBlockNarrowingTests(HeadlessSession headless)
{
    private static double KeptProgress(string from, string to, double seconds, bool narrowing)
    {
        var kept = MorphingTextBlock.Glyphs(from, to, seconds, Animation.Snappy(0.2), narrowing).Where(glyph => glyph.OldIndex is not null && glyph.NewIndex is not null).ToList();
        Assert.Equal(4, kept.Count); // "High"
        return kept.Select(glyph => glyph.Progress).Distinct().Single();
    }

    // Each letter of the line <paramref name="line"/> as drawn now, by its index: its opacity (its copies' opacities added up,
    // over 1.69 for a blurred letter's seventeen copies, MorphingTextBlock.DrawBlurred), its size (1 its own) and how far its
    // middle is below its place on the line, in font sizes.
    private static Dictionary<int, (double Opacity, double Scale, double Rise)> Drawn(DrawnMorphingTextBlock label, string line)
    {
        var recording = new DrawingGroup();
        using (var context = recording.Open()) label.DrawInto(context);
        var middle = label.TextLayout.TextLines[0].Height / 2;
        var copies = new Dictionary<int, List<(double Opacity, double Scale, Point At)>>();
        void Walk(Drawing drawing, double opacity, Matrix outer, Matrix place)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    var inner = opacity * (group.IsSet(DrawingGroup.OpacityProperty) ? group.Opacity : 1);
                    var own = group.Transform?.Value ?? Matrix.Identity;
                    foreach (var child in group.Children) Walk(child, inner, own * outer, own);
                    break;
                case GlyphRunDrawing { GlyphRun: { } run } when run.Characters.ToString() == line:
                    var cluster = run.GlyphInfos[0].GlyphCluster;
                    if (!copies.TryGetValue(cluster, out var list)) copies[cluster] = list = [];
                    list.Add((opacity, outer.M22, new Point(0, middle - place.M32).Transform(outer)));
                    break;
            }
        }
        Walk(recording, 1, Matrix.Identity, Matrix.Identity);
        return copies.ToDictionary(pair => pair.Key, pair =>
        {
            var sum = pair.Value.Sum(copy => copy.Opacity);
            var centre = pair.Value.Average(copy => copy.At.Y);
            return (pair.Value.Count >= 17 ? sum / 1.69 : sum, pair.Value[0].Scale, (centre - middle) / label.FontSize);
        });
    }

    // ED-238 final round 1 (7): a kept letter taken over from an unfinished change eases once, from where it is. Its first
    // test reads only where the letter is along the line; with its opacity jumping to whole or its place below the line to the
    // line at the takeover (mutants E6, E9), the unit's own tests passed (final review round 4). Taken over while it is half
    // arrived, every kept letter carries on from its fade, size and place below the line, with no jump, and is whole, full
    // size and on the line once the change has run.
    [Fact]
    [Trait("Decision", "ED-238")] // the kept letters a takeover carries (MorphingTextBlock.Resolve, the carried kept letters)
    public void A_kept_letter_taken_over_half_arrived_carries_on_from_its_fade_size_and_place_with_no_jump() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new DrawnMorphingTextBlock { Text = "Max", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = Brushes.White, MaxLines = 1, GrowthAnchor = 0.5 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { label, new Border { Width = 6, Height = 12 } } };
        var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = row };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        void Advance(int milliseconds)
        {
            for (var ms = 0; ms < milliseconds; ms++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
                frames.Advance(TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }
        try
        {
            label.SetText("Extra High", Animation.Snappy(0.2));
            Dispatcher.UIThread.RunJobs();
            Advance(180); // the "H", "i" and "g" of "High" part way in, the "h" not yet begun
            var before = Drawn(label, "Extra High");
            label.SetText("High", Animation.Snappy(0.2));
            Dispatcher.UIThread.RunJobs();
            Advance(1);
            var after = Drawn(label, "High");
            string Show(Dictionary<int, (double Opacity, double Scale, double Rise)> letters, int from) => string.Join(" ", Enumerable.Range(from, 4)
                .Select(i => letters.TryGetValue(i, out var s) ? $"{"High"[i - from]}: {s.Opacity:F2} size {s.Scale:F2} low {s.Rise:F3}" : $"{"High"[i - from]}: not drawn"));
            var seen = $"before the takeover {Show(before, 6)}; 1 ms after {Show(after, 0)}";
            var carried = 0;
            for (var i = 0; i < 4; i++)
            {
                if (!before.TryGetValue(6 + i, out var was))
                {
                    Assert.True(!after.TryGetValue(i, out var now) || now.Opacity <= 0.02, $"\"{"High"[i]}\", not drawn before the takeover, jumps in: {seen}");
                    continue;
                }
                carried++;
                Assert.True(was.Opacity is > 0.05 and < 0.95 && was.Scale < 0.95 && was.Rise > 0.02, $"\"{"High"[i]}\" is not part way in before the takeover: {seen}");
                Assert.True(after.TryGetValue(i, out var then), $"\"{"High"[i]}\" is not drawn after the takeover: {seen}");
                Assert.True(Math.Abs(then.Opacity - was.Opacity) <= 0.03, $"\"{"High"[i]}\"'s fade jumps at the takeover: {seen}");
                Assert.True(Math.Abs(then.Scale - was.Scale) <= 0.03, $"\"{"High"[i]}\"'s size jumps at the takeover: {seen}");
                Assert.True(Math.Abs(then.Rise - was.Rise) <= 0.02, $"\"{"High"[i]}\"'s place below the line jumps at the takeover: {seen}");
            }
            Assert.True(carried >= 2, $"only {carried} kept letters part way in at the takeover: {seen}");
            Advance((int)Math.Ceiling(MorphingTextBlock.Length(Animation.Snappy(0.2)) * 1000) - 1);
            Assert.False(label.IsMorphing);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Theory]
    [Trait("Decision", "ED-238")] // the kept letters' fitted clocks (MorphingTextBlock.KeptLead, NarrowingKeptLead)
    [InlineData(0.02)]
    [InlineData(0.04)]
    [InlineData(0.06)]
    [InlineData(0.08)]
    public void The_kept_letters_lead_the_frame_when_a_change_widens_and_follow_it_when_it_narrows(double seconds)
    {
        var frame = Animation.Snappy(0.2).Progress(seconds);
        Assert.True(KeptProgress("High", "Extra High", seconds, narrowing: false) > frame + 0.01, "the kept letters do not lead a widening");
        Assert.True(KeptProgress("Extra High", "High", seconds, narrowing: true) < frame - 0.01, "the kept letters do not follow a narrowing");
    }

    [Fact]
    [Trait("Decision", "ED-238")] // 2-chat-use.mov 35.77-35.96 s, the narrowing fitted in MorphingTextBlock.NarrowingKeptLead
    public void The_kept_letters_of_a_narrowing_that_takes_over_an_unfinished_change_stay_near_the_chevron_as_in_the_video() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new MorphingTextBlock { Text = "Max", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = Brushes.White, MaxLines = 1, GrowthAnchor = 0.5 };
        var chevron = new Border { Width = 6, Height = 12, Background = Brushes.Red, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { label, chevron } };
        var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = row };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            (int Chevron, int Gap) Measure()
            {
                var pixels = RenderedPixels.Capture(window);
                int? white = null, red = null;
                for (var x = 0; x < pixels.Width; x++)
                    for (var y = 0; y < pixels.Height; y++)
                    {
                        var (r, g, b) = pixels.At(x, y);
                        if (r > 70 && g > 70 && b > 70 && (red is null || x < red)) white = x; // the rightmost letter column left of the chevron (letters fade in: not only the whole white)
                        if (red is null && r > 200 && g < 60) red = x;
                    }
                return (red ?? 0, (red ?? 0) - (white ?? 0));
            }
            void Advance(int milliseconds)
            {
                clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
                frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
            label.SetText("Extra High", Animation.Snappy(0.2));
            Dispatcher.UIThread.RunJobs();
            for (var ms = 0; ms < 118; ms += 2) Advance(2); // the drag turns round with the first change at 60 % of its way
            label.SetText("High", Animation.Snappy(0.2));
            Dispatcher.UIThread.RunJobs();
            var series = new List<(int Ms, int Chevron, int Gap)>();
            for (var ms = 0; ms < 400; ms += 5)
            {
                Advance(5);
                var (at, gap) = Measure();
                series.Add((ms, at, gap));
            }
            var start = series[0].Chevron;
            var final = series[^1];
            Assert.True(start > final.Chevron, $"the chevron does not go left ({start} -> {final.Chevron})");
            var text = string.Join(" ", series.Where(s => s.Ms % 10 == 0).Select(s => $"{s.Ms}:{s.Chevron}/{s.Gap}"));
            // The frames where the kept "High" can be seen (the letters the first change was still bringing in are dim, and the
            // rightmost bright thing is a leaving letter far to the left, as in the video's first frames of this change).
            var seen = series.Where(s => s.Gap < 15).ToList();
            // Through the chevron's way the kept "High" holds nearer to it than it settles, by a few points, never farther
            // (video: 2, 5, 3 px short at 60, 73, 80 % of the way): the lead of a widening would hold it 3 points farther.
            var early = seen.Where(s => (double)(start - s.Chevron) / (start - final.Chevron) is >= 0.5 and <= 0.85).ToList();
            Assert.True(early.Count >= 3, $"only {early.Count} frames of the chevron's way show the kept letters ({text})");
            foreach (var (ms, chevronX, gap) in early)
                Assert.True(gap - final.Gap is >= -9 and <= -1, $"at {ms} ms, {(double)(start - chevronX) / (start - final.Chevron):P0} of the chevron's way, the gap is {gap} against {final.Gap} settled ({text})");
            // And as the chevron arrives it is within a few points of where it settles (video: 1, 1, 2, 2, 2 px short): the
            // letters eased twice stayed 6 short.
            var late = seen.Where(s => (double)(start - s.Chevron) / (start - final.Chevron) is >= 0.9 and < 1.0).ToList();
            Assert.True(late.Count >= 3, $"only {late.Count} frames of the chevron's arrival show the kept letters ({text})");
            foreach (var (ms, chevronX, gap) in late)
                Assert.True(gap - final.Gap is >= -3 and <= 1, $"at {ms} ms, {(double)(start - chevronX) / (start - final.Chevron):P0} of the chevron's way, the gap is {gap} against {final.Gap} settled ({text})");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
