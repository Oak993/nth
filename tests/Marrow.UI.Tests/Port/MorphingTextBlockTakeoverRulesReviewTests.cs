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

// REVIEW (final review of unit/effort-title-fidelity, round 3, 2026-10-03): round 2's fix (7d16202ec) has a leaving letter
// that the taken-over change was still bringing in go on as that change's letter now (MorphingTextBlock.Live), "drawn whole,
// full size and plain" (ED-238 round 2 (2)). Its tests read only the letters' opacity: with the letter's size, its place
// below the line or its blur left frozen as they were at the takeover (mutants M-scale, M-rise, M-blur), every test passes.
// SOURCE FACT, 2-chat-use.mov, Extra High -> Max taking over High -> Extra High at about 35.05 s (frames 35.037, 35.057,
// 35.078, 35.100 s, the title cropped and enlarged, review scratch r3/v35_strip.png): the "r" and "a" of "Extra", small, low
// and soft at 35.037 s, are at full size, on the line and sharp at 35.078-35.100 s, before they rise away fading.
[Collection("Headless")]
public sealed class MorphingTextBlockTakeoverRulesReviewTests(HeadlessSession headless)
{
    // Each letter of the line <paramref name="line"/> as the label draws it now, by its index in that line: its size (1 its
    // own), how far its middle is below its place on the line in font sizes, and its blur (how far its copies spread, in
    // points of the letter's own size; 0 for a letter drawn once), read from what the label pushes into a recording.
    private static Dictionary<int, (double Scale, double Rise, double Blur)> Shapes(DrawnMorphingTextBlock label, string line)
    {
        var recording = new DrawingGroup();
        using (var context = recording.Open()) label.DrawInto(context);
        var middle = label.TextLayout.TextLines[0].Height / 2; // the letter's centre, about which it scales (MorphingTextBlock.DrawGlyph)
        var copies = new Dictionary<int, List<(double Scale, Point At, double Middle)>>();
        void Walk(Drawing drawing, Matrix outer, Matrix place)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    var own = group.Transform?.Value ?? Matrix.Identity;
                    foreach (var child in group.Children) Walk(child, own * outer, own);
                    break;
                case GlyphRunDrawing { GlyphRun: { } run } when run.Characters.ToString() == line:
                    var cluster = run.GlyphInfos[0].GlyphCluster;
                    // A point of the run that sits at the letter's middle when the letter is at rest (place is the letter's
                    // own place on the line, the innermost transform).
                    var local = new Point(0, middle - place.M32);
                    var at = local.Transform(outer);
                    if (!copies.TryGetValue(cluster, out var list)) copies[cluster] = list = [];
                    list.Add((outer.M22, at, middle));
                    break;
            }
        }
        Walk(recording, Matrix.Identity, Matrix.Identity);
        var result = new Dictionary<int, (double, double, double)>();
        foreach (var (cluster, list) in copies)
        {
            var scale = list[0].Scale;
            var centre = new Point(list.Average(copy => copy.At.X), list.Average(copy => copy.At.Y));
            var spread = list.Max(copy => Math.Sqrt(Math.Pow(copy.At.X - centre.X, 2) + Math.Pow(copy.At.Y - centre.Y, 2)));
            result[cluster] = (scale, (centre.Y - list[0].Middle) / label.FontSize, scale > 0 ? spread / scale : 0);
        }
        return result;
    }

    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111's morph (EffortSlider.swift:574, numericText) as the reference runtime draws a takeover
    public void REVIEW_The_letters_still_arriving_when_a_change_is_taken_over_grow_to_full_size_on_the_line_and_sharp() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new DrawnMorphingTextBlock { Text = "High", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = Brushes.White, MaxLines = 1, GrowthAnchor = 0.5 };
        var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { label } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            void Advance(int milliseconds)
            {
                for (var ms = 0; ms < milliseconds; ms += 1)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(1));
                    frames.Advance(TimeSpan.FromMilliseconds(1));
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }
            }
            label.SetText("Extra High", Animation.Snappy(0.2)); // 34.838 s
            Dispatcher.UIThread.RunJobs();
            Advance(215);
            var taken = Shapes(label, "Extra High");
            label.SetText("Max", Animation.Snappy(0.2)); // about 35.053 s, the takeover
            Dispatcher.UIThread.RunJobs();
            Advance(47); // 35.100 s
            var whole = Shapes(label, "Extra High");
            string Show(Dictionary<int, (double Scale, double Rise, double Blur)> letters) => string.Join(" ", new[] { 3, 4 }.Select(i =>
                letters.TryGetValue(i, out var s) ? $"{"Extra High"[i]}: size {s.Scale:F2} low {s.Rise:F3} em blur {s.Blur:F2}" : $"{"Extra High"[i]}: not drawn"));
            var seen = $"at the takeover {Show(taken)}; 47 ms after {Show(whole)}";
            Assert.True(taken.TryGetValue(4, out var a0) && a0.Scale < 0.8, $"the \"a\" is not still arriving, small, at the takeover (video 35.037 s): {seen}");
            foreach (var i in new[] { 3, 4 })
            {
                Assert.True(whole.TryGetValue(i, out var s), $"\"{"Extra High"[i]}\" is not drawn 47 ms after the takeover: {seen}");
                Assert.True(s.Scale >= 0.85, $"47 ms after the takeover (video 35.100 s: \"r\" and \"a\" at full size) \"{"Extra High"[i]}\" is still small: {seen}");
                Assert.True(Math.Abs(s.Rise) <= 0.12, $"47 ms after the takeover (video 35.100 s: \"r\" and \"a\" on the line) \"{"Extra High"[i]}\" is off its place: {seen}");
                Assert.True(s.Blur <= 1.5, $"47 ms after the takeover (video 35.100 s: \"r\" and \"a\" sharp) \"{"Extra High"[i]}\" is still soft: {seen}");
            }
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

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

    // ED-238, final review round 2 (2): "a taken-over line's leaving letters take their turns by their places among all its
    // letters, the kept ones counted ... five leaving letters from rest still go 36 ms apart". No test tells the two apart:
    // with every change's leaving letters taking their turns by place (mutant M-alltakeover, `takeover: true`), every UI Port,
    // Popover and Traceability test passes. From rest, Extra High -> High: the "r" and "a" of "Extra" take the fourth and
    // fifth of five turns spread over 55 % of the snappy 0.2's 0.259 s (107 and 142 ms), so 100 ms in they have not begun to
    // leave; by place they would be the fourth and fifth of nine (53 and 71 ms), already fading.
    [Fact]
    [Trait("Decision", "ED-238")] // the unit's own rule: a change from rest keeps its leaving letters' turns by their count
    public void REVIEW_The_leaving_letters_of_a_change_from_rest_take_their_turns_by_their_own_count() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new DrawnMorphingTextBlock { Text = "Extra High", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = Brushes.White, MaxLines = 1, GrowthAnchor = 0.5 };
        var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { label } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            label.SetText("High", Animation.Snappy(0.2));
            Dispatcher.UIThread.RunJobs();
            for (var ms = 0; ms < 100; ms += 1)
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
                frames.Advance(TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
            var letters = Extra(label);
            var seen = string.Join(" ", Enumerable.Range(0, 5).Select(i => $"{"Extra"[i]}:{(letters.TryGetValue(i, out var o) ? o : 0):F2}"));
            Assert.True(letters.GetValueOrDefault(3) >= 0.95 && letters.GetValueOrDefault(4) >= 0.95,
                $"100 ms into Extra High -> High from rest the \"r\" and \"a\" have begun to leave, as by their places among nine letters, not as the fourth and fifth of five (ED-238): {seen}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
