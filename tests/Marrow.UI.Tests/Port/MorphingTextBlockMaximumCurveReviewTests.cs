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

// REVIEW (final review of unit/effort-title-fidelity, round 5, 2026-10-03).
//
// The narrowing takeover as the source runs it. SOURCE FACT (EffortSlider.swift:579-583): the title's HStack carries
// `.animation(.smooth(duration: 0.3), value: isMaxEffort)`, so a change into or out of maximum (Max -> Extra High here,
// isMaxEffort true -> false) animates the title, its numericText morph and its frame, on smooth(0.3), not on the slider's
// snappy(0.2) (:631). SOURCE FACT, 2-chat-use.mov (study effort-title-2026-10-03/video_dec_r3, the chevron's centre x per
// frame; fit by work/claude-verify/resume-2026-10-03/effort-rev-r5-chevron/chevron_fit.out): Max -> Extra High's chevron
// follows smooth(0.3) from 35.623 s (0.06, 0.15, 0.33, 0.48, 0.60, 0.72 at 35.643-35.743 s; rms 0.007), not snappy(0.2)
// (rms 0.099); High -> Extra High at 34.86 s, where maximum does not change, follows snappy(0.2) (rms 0.008, smooth 0.106);
// Extra High -> Max at 35.05 s follows smooth(0.3) (rms 0.013, snappy 0.097). Extra High -> High (maximum unchanged) takes
// it over at 35.753 s, 130 ms in.
//
// The round 4 fit of the narrowing takeover's "a" (MorphingTextBlock.Lingering, 1c712aa30; ED-238 final round 4 (1)) and the
// REVIEW test it was fitted to (MorphingTextBlockColourAndResidualReviewTests.REVIEW_The_a_carried_through_the_narrowing_
// takeover_stays_on_the_h_as_long_as_the_video_shows) both run Max -> Extra High on snappy(0.2) from 35.635 s, as the port's
// card does (EffortSlider.cs:573 passes the slider's transaction for every change). Run on the source's own curve, the
// video's facts for the "a" (the same frames and bounds as that test: plain over the "h" at 35.865 s, 0.75 of its ink at
// 35.887 s, gone at 35.987 s; ink is a floor for the opacity) are read here.
[Collection("Headless")]
public sealed class MorphingTextBlockMaximumCurveReviewTests(HeadlessSession headless)
{
    // Each letter of the old line's "Extra" (character 0 to 4 of "Extra High") as drawn now: its copies' opacities added up,
    // over the copies a blurred letter is drawn as (MorphingTextBlock.DrawBlurred: 0.25 + 8 x 0.12 + 8 x 0.06 of its opacity).
    private static Dictionary<int, double> Extra(DrawnMorphingTextBlock label)
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
                case GlyphRunDrawing { GlyphRun: { } run } when run.Characters.ToString() == "Extra High":
                    var cluster = run.GlyphInfos[0].GlyphCluster;
                    if (cluster > 4) break;
                    var (sum, copies) = sums.TryGetValue(cluster, out var known) ? known : (0, 0);
                    sums[cluster] = (sum + opacity, copies + 1);
                    break;
            }
        }
        Walk(recording, 1);
        return sums.ToDictionary(pair => pair.Key, pair => pair.Value.Copies >= 17 ? pair.Value.Sum / 1.69 : pair.Value.Sum);
    }

    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111's morph (EffortSlider.swift:574) through the narrowing takeover, on :582's smooth(0.3) for the change out of maximum
    public void REVIEW_The_a_carried_through_the_narrowing_takeover_stays_on_the_h_when_Max_to_Extra_High_runs_on_smooth_0_3() => headless.OnUiThread(() =>
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
            label.SetText("Extra High", Animation.Smooth(0.3)); // 35.623 s, out of maximum: :582's smooth(0.3)
            Dispatcher.UIThread.RunJobs();
            Advance(130);
            label.SetText("High", Animation.Snappy(0.2)); // 35.753 s, the takeover, maximum unchanged: the slider's snappy(0.2)
            Dispatcher.UIThread.RunJobs();
            var series = new List<string>();
            double full = 0, still = 0, gone = 0;
            foreach (var (at, ms) in new[] { (35.865, 112), (35.887, 134), (35.907, 154), (35.928, 175), (35.943, 190), (35.965, 212), (35.987, 234) })
            {
                Advance(ms - (series.Count == 0 ? 0 : new[] { 112, 134, 154, 175, 190, 212, 234 }[series.Count - 1]));
                var a = Extra(label).GetValueOrDefault(4);
                series.Add($"{at:F3} s {a:F2}");
                if (ms == 112) full = a;
                if (ms == 134) still = a;
                if (ms == 234) gone = a;
            }
            var seen = $"the \"a\" at {string.Join(", ", series)} (video ink 1.00, 0.75, 0.50, 0.28, 0.22, 0.04, 0)";
            Assert.True(full >= 0.8, $"112 ms after the takeover the \"a\" is not plain over the \"h\" (video 35.865 s: its fullest): {seen}");
            Assert.True(still >= 0.55, $"134 ms after the takeover the \"a\" is mostly gone (video 35.887 s: 0.75 of its ink): {seen}");
            Assert.True(gone <= 0.1, $"234 ms after the takeover the \"a\" still shows (video 35.987 s: gone): {seen}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
