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

// ED-238, final review round 5 (unit/effort-title-fidelity, 2026-10-03): the letters' pace, and the narrowing takeover on the
// source's own curves.
//
// SOURCE FACT (EffortSlider.swift:579-583): the title's HStack carries `.animation(.smooth(duration: 0.3), value:
// isMaxEffort)` (and `value: isFast`), so a change into or out of maximum runs the title on smooth(0.3) whatever the slider's
// withAnimation(.snappy(duration: 0.2)) (:631). SOURCE FACT, 2-chat-use.mov (the chevron's centre x frame by frame,
// work/claude-verify/resume-2026-10-03/effort-rev-r5-chevron/chevron_fit.out): the label's frame follows smooth(0.3) in Extra
// High -> Max from 35.051 s and Max -> Extra High from 35.623 s (rms 0.013 and 0.007; snappy(0.2) 0.097 and 0.099), and
// snappy(0.2) in High -> Extra High (rms 0.008). The letters do not slow with the frame: in those smooth(0.3) changes "Max"
// arrives about 70 ms a letter and the nine leaving letters of "Extra High" go about 20 ms apart (MorphingTextBlock's file
// note), the 0.14 s spread of the snappy changes' letters, not the 0.18 s that 55 % of smooth(0.3)'s settling would give; and
// the narrowing takeover's "a" (below) keeps the video's ink only if it arrives at that pace. ENGINEERING DECISION (ED-238
// final round 5): a change's arriving and leaving letters take their turns and run their curve at one pace,
// MorphingTextBlock.LetterPace (the slider's snappy(0.2), on which they were measured), whatever the change's animation;
// the change's animation moves the label's frame and the kept letters riding with it.
[Collection("Headless")]
public sealed class MorphingTextBlockPaceTests(HeadlessSession headless)
{
    private static readonly Animation Slider = Animation.Snappy(0.2);

    // The arriving and leaving letters of a change at a moment, in a fixed order, with everything drawn of them.
    private static List<string> Rolling(List<MorphingTextBlock.GlyphState> glyphs) => glyphs
        .Where(g => g.OldIndex is null || g.NewIndex is null)
        .OrderBy(g => g.FromOld).ThenBy(g => g.SourceIndex)
        .Select(g => $"{(g.FromOld ? "out" : "in")}{g.SourceIndex}:{g.Progress:F6}/{g.Opacity:F6}/{g.Scale:F6}/{g.Blur:F6}/{g.Rise:F6}")
        .ToList();

    [Theory]
    [Trait("Decision", "ED-238")] // ED-238 final round 5: the letters' pace (LetterPace), a part of PR-COMPOSER-111's morph
    [InlineData("smooth(0.3)")] // EffortSlider.swift:582, into and out of maximum
    [InlineData("spring(0.32, 0.9)")] // Chrome.panelSlide, the usage count's (UsageViews.swift:235, :254-255)
    [InlineData("snappy(0.22)")] // the copy label's and MiddleTruncatingText's confirmation (HydraMergePopover.swift:190, Popovers.swift:263)
    [InlineData("linear(0.5)")]
    public void A_changes_letters_roll_at_the_letters_own_pace_whatever_its_animation_while_its_kept_letters_ride_with_it(string name)
    {
        var animation = name switch
        {
            "smooth(0.3)" => Animation.Smooth(0.3),
            "spring(0.32, 0.9)" => Animation.Spring(response: 0.32, dampingFraction: 0.9),
            "snappy(0.22)" => Animation.Snappy(0.22),
            _ => Animation.Linear(0.5),
        };
        foreach (var (from, to, narrowing) in new[] { ("Extra High", "Max", true), ("Max", "Extra High", false), ("High", "Extra High", false), ("Extra High", "High", true), ("12", "13", false) })
            for (var ms = 0; ms <= 700; ms += 5)
            {
                var seconds = ms / 1000.0;
                Assert.Equal(Rolling(MorphingTextBlock.Glyphs(from, to, seconds, Slider, narrowing)), Rolling(MorphingTextBlock.Glyphs(from, to, seconds, animation, narrowing)));
                // the takeover's shared turns too: a change sharing its leaving turns with 1.4 letters still being taken away
                Assert.Equal(Rolling(MorphingTextBlock.Glyphs(from, to, seconds, Slider, narrowing, 1.4)), Rolling(MorphingTextBlock.Glyphs(from, to, seconds, animation, narrowing, 1.4)));
            }
        // The letters' pace: the first new letter 0.04 s in, the turns over 0.14 s, each on snappy(0.2)'s curve.
        Assert.Equal(Animation.Snappy(0.2).Duration, MorphingTextBlock.LetterPace.Duration, 9);
        Assert.Equal(Animation.Snappy(0.2).Progress(0.05), MorphingTextBlock.LetterPace.Progress(0.05), 9);
        // The kept "High" of High -> Extra High rides with the change's own animation, as the label's frame does.
        double Kept(Animation curve, double seconds) => MorphingTextBlock.Glyphs("High", "Extra High", seconds, curve).Single(g => g.OldIndex == 0).Progress;
        var kept = Kept(animation, 0.06);
        var own = animation.Progress((0.06 + MorphingTextBlock.KeptLead) / MorphingTextBlock.KeptStretch);
        Assert.True(Math.Abs(kept - own) < 1e-9, $"the kept letters do not follow the change's own {name}: {kept:F3}, its curve {own:F3}");
        // And the change runs until its letters and its frame have both finished.
        var length = MorphingTextBlock.Length(animation);
        Assert.True(length >= MorphingTextBlock.Length(Slider) - 1e-9 && length >= animation.Duration * MorphingTextBlock.KeptStretch, $"the change ends at {length:F3} s");
        foreach (var (from, to) in new[] { ("Extra High", "Max"), ("High", "Extra High") })
            Assert.All(MorphingTextBlock.Glyphs(from, to, length, animation), g => Assert.True(g.NewIndex is null ? g.Opacity < 1e-3 : Math.Abs(g.Opacity - 1) < 1e-3 && Math.Abs(g.Progress - 1) < 1e-3, $"{from} -> {to}: letter {g.SourceIndex} not settled at the change's end"));
    }

    // Each letter of the old line's "Extra" (character 0 to 4 of "Extra High") as drawn now: its copies' opacities added up,
    // over the copies a blurred letter is drawn as (MorphingTextBlock.DrawBlurred: 0.25 + 8 x 0.12 + 8 x 0.06 of its opacity).
    private static Dictionary<int, double> Drawn(DrawnMorphingTextBlock label, string line, int upTo = int.MaxValue)
    {
        var recording = new DrawingGroup();
        using (var context = recording.Open()) label.DrawInto(context);
        var sums = new Dictionary<int, (double Sum, int Copies)>();
        void Walk(Drawing drawing, double opacity)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    var inner = opacity * (group.IsSet(DrawingGroup.OpacityProperty) ? group.Opacity : 1);
                    foreach (var child in group.Children) Walk(child, inner);
                    break;
                case GlyphRunDrawing { GlyphRun: { } run } when run.Characters.ToString() == line:
                    var cluster = run.GlyphInfos[0].GlyphCluster;
                    if (cluster > upTo) break;
                    var (sum, copies) = sums.TryGetValue(cluster, out var known) ? known : (0, 0);
                    sums[cluster] = (sum + opacity, copies + 1);
                    break;
            }
        }
        Walk(recording, 1);
        return sums.ToDictionary(pair => pair.Key, pair => pair.Value.Copies >= 17 ? pair.Value.Sum / 1.69 : pair.Value.Sum);
    }

    private static (Window Window, DrawnMorphingTextBlock Label, Action<int> Advance) Title(string text, ManualFrameSource frames, ManualTimeProvider clock)
    {
        var label = new DrawnMorphingTextBlock { Text = text, FontSize = 17, FontWeight = FontWeight.Medium, Foreground = Brushes.White, MaxLines = 1, GrowthAnchor = 0.5 };
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
        return (window, label, Advance);
    }

    // The narrowing takeover as the source runs it: Max -> Extra High on smooth(0.3) from 35.623 s (out of maximum, :582; the
    // chevron's fit), Extra High -> High on the slider's snappy(0.2) at 35.753 s, 130 ms in. SOURCE FACT (2-chat-use.mov,
    // study effort-title-2026-10-03/video_dec_r3; tools/takeover_a_ink.py, work/claude-verify/resume-2026-10-03/
    // effort-fix-r5.narrowing-takeover-measure.txt): the "a" of "Extra", over the settled "h", keeps 1.00, 0.75, 0.50, 0.28,
    // 0.22, 0.04 and 0 of its fullest ink at 35.865, 35.887, 35.907, 35.928, 35.943, 35.965 and 35.987 s, and the "E" begins
    // to leave at about 35.76 s. Ink falls at least as fast as the letter's opacity (a leaving letter also shrinks and blurs),
    // so the drawn "a" may stand a little over the ink, hardly under it. Round 4's fit (Max -> Extra High on snappy(0.2) from
    // 35.635 s) met the ink at rms 0.045; on the source's curves, with the letters at their pace, this asks for better.
    [Fact]
    [Trait("Decision", "ED-238")] // ED-238 final round 5: the narrowing takeover's "a" on the source's curves (EffortSlider.swift:582), a part of PR-COMPOSER-111's morph
    public void The_narrowing_takeovers_a_on_the_sources_curves_keeps_the_videos_ink() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (window, label, advance) = Title("Max", frames, clock);
        try
        {
            label.SetText("Extra High", Animation.Smooth(0.3)); // 35.623 s
            Dispatcher.UIThread.RunJobs();
            advance(130);
            label.SetText("High", Animation.Snappy(0.2)); // 35.753 s
            Dispatcher.UIThread.RunJobs();
            advance(10); // 35.763 s
            var e = Drawn(label, "Extra High", 4).GetValueOrDefault(0);
            var video = new (int Ms, double Ink)[] { (112, 1.00), (134, 0.75), (154, 0.50), (175, 0.28), (190, 0.22), (212, 0.04), (234, 0) };
            var drawn = new List<double>();
            var at = 10;
            foreach (var (ms, _) in video)
            {
                advance(ms - at);
                at = ms;
                drawn.Add(Drawn(label, "Extra High", 4).GetValueOrDefault(4));
            }
            var rms = Math.Sqrt(video.Select((v, i) => Math.Pow(drawn[i] - v.Ink, 2)).Average());
            var under = video.Select((v, i) => v.Ink - drawn[i]).Max();
            var seen = $"the \"a\" {string.Join(", ", drawn.Select(a => a.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))} against the ink {string.Join(", ", video.Select(v => v.Ink.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))} (rms {rms:F3}, at most {under:F2} under it); the \"E\" {e:F2} 10 ms after the takeover";
            Assert.True(rms <= 0.035, $"the \"a\" does not keep the video's ink: {seen}");
            Assert.True(under <= 0.05, $"the \"a\" falls under the video's ink: {seen}");
            Assert.True(e < 0.98, $"the \"E\" has not begun to leave 10 ms after the takeover (video: about 35.76 s): {seen}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    // A change that takes over two unfinished ones shares its leaving turns with every letter both are still taking away, each
    // counted as drawn (MorphingTextBlock.Lingering walks the whole chain; final review round 5: a walk of only the change taken
    // over, mutant R5-LG1, passed every test). No video shows three changes in flight; the rule is extrapolated from the two
    // measured takeovers (ED-238 final round 4 (1)). Max -> Extra High at 0, Extra High -> High at 60 ms, High -> Medium at 90
    // ms: the third change's leaving "High" shares its turns with the "Max" of the first and the "Extra" of the second. The
    // leave of the "h" is read as its drawing beside the same label with no third change (the "h" the second change is still
    // bringing in: the takeover's leave multiplies it), and its turn must be where the rule puts it with every letter counted.
    [Fact]
    [Trait("Decision", "ED-238")] // ED-238 final round 4 (1) and round 5: Lingering over a chain of changes, a part of PR-COMPOSER-111's morph
    public void A_change_taking_over_two_unfinished_ones_shares_its_leaving_turns_with_the_letters_both_still_take_away() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (window, label, advance) = Title("Max", frames, clock);
        var (twinWindow, twin, _) = Title("Max", frames, clock);
        try
        {
            label.SetText("Extra High", Slider);
            twin.SetText("Extra High", Slider);
            Dispatcher.UIThread.RunJobs();
            advance(60);
            label.SetText("High", Slider);
            twin.SetText("High", Slider);
            Dispatcher.UIThread.RunJobs();
            advance(30);
            // Everything still being taken away as the third change begins: the "Max" of the first, the "Extra" of the second.
            var max = Drawn(label, "Max").Values.Sum();
            var extra = Drawn(label, "Extra High", 4).Values.Sum();
            Assert.True(max > 0.5 && extra > 0.3, $"the first two changes' leaving letters do not still show at the third: \"Max\" {max:F2}, \"Extra\" {extra:F2}");
            label.SetText("Medium", Slider);
            Dispatcher.UIThread.RunJobs();
            // The "h" of "High" is the last of its four leaving letters: its turn with every letter counted, and with the second
            // change's alone.
            double TurnOf(double lingering) => 3 * MorphingTextBlock.SpreadShare * MorphingTextBlock.LetterPace.Duration / (4 + lingering - 1);
            var turn = TurnOf(max + extra);
            var takenOnly = TurnOf(extra);
            Assert.True(takenOnly - turn > 0.02, $"the setup does not tell the chain from the change taken over: {turn:F3} s against {takenOnly:F3} s");
            var series = new List<string>();
            var at = 0;
            foreach (var ms in new[] { 60, 80, 100, 120, 140, 160 })
            {
                advance(ms - at);
                at = ms;
                var leaving = Drawn(label, "High").GetValueOrDefault(3);
                var arriving = Drawn(twin, "High").GetValueOrDefault(3);
                Assert.True(arriving > 0.2, $"the \"h\" the second change brings in is too faint to read the leave against at {ms} ms: {arriving:F2}");
                var leave = leaving / arriving;
                var expected = 1 - Math.Clamp(Turned(ms / 1000.0 - turn), 0, 1);
                series.Add($"{ms} ms: {leave:F2} (rule {expected:F2}, the change taken over alone {1 - Math.Clamp(Turned(ms / 1000.0 - takenOnly), 0, 1):F2})");
                Assert.True(Math.Abs(leave - expected) <= 0.03, $"the \"h\" does not leave on its turn with every letter still leaving counted ({turn * 1000:F0} ms; {takenOnly * 1000:F0} ms counting only the change taken over): {string.Join("; ", series)}");
            }
        }
        finally { window.Close(); twinWindow.Close(); Dispatcher.UIThread.RunJobs(); }

        static double Turned(double local) => local <= 0 ? 0 : MorphingTextBlock.LetterPace.Progress(local);
    });
}
