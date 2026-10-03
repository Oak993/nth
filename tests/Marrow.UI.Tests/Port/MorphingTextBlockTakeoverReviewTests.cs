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

// REVIEW (final review of unit/effort-title-fidelity, round 2, 2026-10-03): the fixer's own frame-by-frame check found, and
// left OPEN in ED-238, that the leaving letters of a change that takes over an unfinished one leave too soon. SOURCE FACT,
// 2-chat-use.mov (study effort-title-2026-10-03/sbs_dec_handover_head.png, the video's half): Max -> Extra High starts at
// 35.635 s and Extra High -> High takes it over at 35.753 s, as the drag turns round. The letters of "Extra", still
// arriving at the takeover, go on showing while they are carried right: "xtra" plainly visible over the arriving "High" at
// 35.807-35.843 s (54-90 ms after the takeover), an "a" on its "h" at 35.865-35.907 s (112-154 ms), faint at 35.928 s, gone at
// 35.943 s (190 ms). The port's leave from the state they had at the takeover (MorphingTextBlock.InFlight, frozen, times
// the leave), so they are faint at 35.805-35.820 s and gone from 35.845 s. PR-COMPOSER-111's title is the record this unit
// claims; the owner's rule OD-0076/OD-0069 is the Mac's motion where the Mac animates.
[Collection("Headless")]
public sealed class MorphingTextBlockTakeoverReviewTests(HeadlessSession headless)
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
                case GlyphRunDrawing { GlyphRun: { } run } when run.Characters.ToString().StartsWith("Extra", StringComparison.Ordinal):
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
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111's morph (EffortSlider.swift:574, numericText) as the reference runtime draws a takeover
    public void REVIEW_The_letters_still_arriving_when_a_change_is_taken_over_go_on_showing_before_they_leave() => headless.OnUiThread(() =>
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
        try
        {
            void Advance(int milliseconds)
            {
                for (var ms = 0; ms < milliseconds; ms += 2)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(2));
                    frames.Advance(TimeSpan.FromMilliseconds(2));
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }
            }
            string Show(Dictionary<int, double> letters) => string.Join(" ", Enumerable.Range(0, 5).Select(i => $"{"Extra"[i]}:{(letters.TryGetValue(i, out var o) ? o : 0):F2}"));
            label.SetText("Extra High", Animation.Snappy(0.2)); // 35.635 s
            Dispatcher.UIThread.RunJobs();
            Advance(118);
            label.SetText("High", Animation.Snappy(0.2)); // 35.753 s, the takeover
            Dispatcher.UIThread.RunJobs();
            Advance(80); // 35.833 s
            var plain = Extra(label);
            Advance(50); // 35.883 s
            var a = Extra(label);
            Advance(70); // 35.953 s
            var gone = Extra(label);
            var seen = $"80 ms after the takeover {Show(plain)}; 130 ms {Show(a)}; 200 ms {Show(gone)}";
            Assert.True(plain.Count(pair => pair.Key >= 1 && pair.Value >= 0.3) >= 3, $"80 ms after the takeover (video 35.833 s: \"xtra\" plainly visible) three of x, t, r, a are not at 0.3: {seen}");
            Assert.True(a.Values.DefaultIfEmpty(0).Max() >= 0.3, $"130 ms after the takeover (video 35.865-35.907 s: an \"a\" on the \"h\") no letter of Extra is at 0.3: {seen}");
            Assert.True(gone.Values.All(opacity => opacity < 0.05), $"200 ms after the takeover (video: gone at 35.943 s) a letter of Extra still shows: {seen}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
