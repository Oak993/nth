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

// REVIEW (final review of unit/effort-title-fidelity, round 4, 2026-10-03).
//
// (1) The colours. MorphingTextBlock's file note gives as SOURCE FACT that "leaving letters keep the old colour, arriving
// ones wear the new one from their first frame (an orange "M" beside a pink leaving "High")", and the effort card relies on
// it (EffortSlider.cs:580-583: the text is set first, then the new colour at once when the text morphs). The test that names
// it, EffortTitleMorphTests.A_title_that_morphs_into_new_letters_draws_them_in_the_new_colour_at_once, reads only the
// label's Foreground property, which is the new colour whatever the label draws: with every letter of a change drawn in the
// new colour (mutant C1), the arriving ones in the old (C2), or the leaving letters of a change taken over in the new colour
// (C3), every UI Port, Popover and Traceability test and every App Views.Composer test passes
// (work/claude-verify/resume-2026-10-03/effort-rev-r4.mutants*.txt). SOURCE FACT, 2-chat-use.mov (study
// effort-title-2026-10-03/video_inc_r3, 35.143-35.207 s): in Extra High -> Max the arriving "M" and "a" are orange while the
// leaving "High" is still pink; (video_dec_r3, 35.743-35.807 s) in Extra High -> High taking over Max -> Extra High, the
// leaving "x" of "Max" is still orange beside the pink "Ext" (strips: work/claude-verify/resume-2026-10-03/
// effort-rev-r4-frames/vinc_12_19.png, high_8_14.png).
//
// (2) The narrowing takeover's residual that ED-238 final round 3 (4) leaves OPEN for the lead, pinned to the one letter the
// fixer could measure. SOURCE FACT (video_dec_r3, frames 35.865-36.007 s; work/claude-verify/resume-2026-10-03/effort-rev-r4-frames/dec_14_21.png, each frame beside
// its difference from the settled 36.007 s frame): after Extra High -> High takes over Max -> Extra High at 35.753 s, the
// "a" of "Extra" is plainly drawn over the "h" at 35.865 and 35.887 s, still clear at 35.907 s, faint and raised at
// 35.928-35.943 s, a trace at 35.965 s and gone at 35.987 s (the fixer's ink in the "a"'s columns: 1.00, 0.75, 0.50, 0.28,
// 0.22, 0.04, 0 of its fullest; effort-fix-r5.narrowing-takeover-measure.txt). Ink falls at least as fast as the letter's
// opacity (a leaving letter also shrinks and blurs), so the ink is a floor for the opacity. The port draws the "a" at 0.61 and
// 0.36 at 35.865 and 35.887 s (its turn by its place among nine letters, 72 ms after the takeover, where the frames fit 105 ms).
// Either reading ED-238 names for a fit (a turn about 105 ms after the takeover; the turns spread over the seven glyphs still
// leaving, the "a" at 95 ms) passes this test. NOTE for whoever fits it: MorphingTextBlockTakeoverReviewTests' last assertion
// (every letter of Extra under 0.05 at 35.953 s, "gone at 35.943 s") is refuted by the same frames (the "a" faint at 35.943 s
// and a trace at 35.965 s); a fit to the "a" leaves it at about 0.07-0.13 there, so that sample belongs at 35.987 s, as here.
[Collection("Headless")]
public sealed class MorphingTextBlockColourAndResidualReviewTests(HeadlessSession headless)
{
    private static readonly Color Pink = Color.FromRgb(0xF2, 0x4B, 0x7C);
    private static readonly Color Orange = Color.FromRgb(0xE9, 0x7B, 0x48);

    // The colours each letter is drawn in now, by the line its glyph run comes from and its index in that line.
    private static Dictionary<(string Line, int Index), HashSet<Color>> Colours(DrawnMorphingTextBlock label)
    {
        var recording = new DrawingGroup();
        using (var context = recording.Open()) label.DrawInto(context);
        var result = new Dictionary<(string, int), HashSet<Color>>();
        void Walk(Drawing drawing)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    foreach (var child in group.Children) Walk(child);
                    break;
                case GlyphRunDrawing { GlyphRun: { } run } glyphs:
                    var key = (run.Characters.ToString(), run.GlyphInfos[0].GlyphCluster);
                    if (!result.TryGetValue(key, out var set)) result[key] = set = [];
                    set.Add(glyphs.Foreground is ISolidColorBrush solid ? solid.Color : Colors.Transparent);
                    break;
            }
        }
        Walk(recording);
        return result;
    }

    private static string Show(Dictionary<(string Line, int Index), HashSet<Color>> colours) =>
        string.Join("; ", colours.OrderBy(pair => pair.Key.Line).ThenBy(pair => pair.Key.Index)
            .Select(pair => $"{pair.Key.Line}[{pair.Key.Index}] {string.Join(",", pair.Value.Select(c => c == Pink ? "pink" : c == Orange ? "orange" : c.ToString()))}"));

    private static (DrawnMorphingTextBlock Label, Window Window, Action<int> Advance) Title(string text, Color colour, ManualFrameSource frames, ManualTimeProvider clock)
    {
        var label = new DrawnMorphingTextBlock { Text = text, FontSize = 17, FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(colour), MaxLines = 1, GrowthAnchor = 0.5 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { label, new Border { Width = 6, Height = 12 } } };
        var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = row };
        window.Show();
        Dispatcher.UIThread.RunJobs();
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
        return (label, window, Advance);
    }

    // The card's own order (EffortSlider.cs:573 then :583): the text is set inside the slider's animation, then the colour at once.
    private static void Change(DrawnMorphingTextBlock label, string text, Color colour)
    {
        label.SetText(text, Animation.Snappy(0.2));
        label.Foreground = new SolidColorBrush(colour);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111's morph (EffortSlider.swift:574) with PR-COMPOSER-112's colour change, as the reference runtime draws it
    public void REVIEW_Leaving_letters_keep_the_old_colour_and_arriving_ones_wear_the_new_one_from_their_first_frame() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (label, window, advance) = Title("Extra High", Pink, frames, clock);
        try
        {
            Change(label, "Max", Orange); // about 35.04 s
            advance(110); // about 35.15 s: an orange "M" beside the pink leaving "High"
            var colours = Colours(label);
            var leaving = colours.Where(pair => pair.Key.Line == "Extra High").ToList();
            var arriving = colours.Where(pair => pair.Key.Line == "Max").ToList();
            Assert.True(leaving.Count > 0 && arriving.Count > 0, $"both lines are drawn: {Show(colours)}");
            Assert.True(leaving.All(pair => pair.Value.SetEquals([Pink])), $"a leaving letter of \"Extra High\" is not drawn in its old pink (video 35.143-35.207 s): {Show(colours)}");
            Assert.True(arriving.All(pair => pair.Value.SetEquals([Orange])), $"an arriving letter of \"Max\" is not drawn in the new orange (video: the \"M\" orange from its first frame): {Show(colours)}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111's morph (EffortSlider.swift:574) through a takeover, as the reference runtime draws it
    public void REVIEW_The_leaving_letters_of_a_change_taken_over_keep_that_changes_old_colour() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (label, window, advance) = Title("Max", Orange, frames, clock);
        try
        {
            Change(label, "Extra High", Pink); // 35.635 s
            advance(118);
            Change(label, "High", Pink); // 35.753 s, the takeover
            advance(34); // 35.787 s: the orange "x" of "Max" beside the pink "Ext"
            var colours = Colours(label);
            var max = colours.Where(pair => pair.Key.Line == "Max").ToList();
            Assert.True(max.Count > 0, $"the leaving letters of \"Max\" are drawn: {Show(colours)}");
            Assert.True(max.All(pair => pair.Value.SetEquals([Orange])), $"a leaving letter of \"Max\" is not drawn in its orange (video 35.743-35.807 s): {Show(colours)}");
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

    // Each letter of the line <paramref name="line"/> as drawn now, by its index: its opacity (its copies' opacities added up,
    // over 1.69 for a blurred letter's seventeen copies), its size (1 its own) and how far its middle is below its place on the
    // line in font sizes (MorphingTextBlockTakeoverRulesReviewTests.Shapes' reading).
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

    // ED-238 final round 1 (7): "a kept letter taken over from an unfinished change eases once"; its tests read only where the
    // letter is along the line (MorphingTextBlockNarrowingTests): with the kept letters' opacity, size or place below the line
    // jumping to rest at the takeover (mutants E6, E7, E9), every UI Port, Popover and Traceability test and every App
    // Views.Composer test passes. SOURCE FACT (video_dec_r3, 35.743-35.843 s; effort-rev-r4-frames/high_8_14.png): the "High"
    // that Max -> Extra High had not yet begun to bring in when Extra High -> High took it over (35.753 s) is not drawn at
    // 35.765 s (that frame is 35.743 s's), shows faint, small and low at 35.787 s, and is drawn whole by 35.843 s.
    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111's morph (EffortSlider.swift:574) through the narrowing takeover: the kept letters it carries
    public void REVIEW_The_kept_letters_a_takeover_carries_in_fade_grow_and_rise_into_place_as_in_the_video() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (label, window, advance) = Title("Max", Pink, frames, clock);
        try
        {
            Change(label, "Extra High", Pink); // 35.635 s
            advance(118);
            Change(label, "High", Pink); // 35.753 s, the takeover
            advance(12); // 35.765 s
            var first = Drawn(label, "High");
            advance(22); // 35.787 s
            var faint = Drawn(label, "High");
            advance(56); // 35.843 s
            var whole = Drawn(label, "High");
            string Show(Dictionary<int, (double Opacity, double Scale, double Rise)> letters) => string.Join(" ", letters.OrderBy(pair => pair.Key)
                .Select(pair => $"{"High"[pair.Key]}: {pair.Value.Opacity:F2} size {pair.Value.Scale:F2} low {pair.Value.Rise:F2}"));
            var seen = $"35.765 s {Show(first)}; 35.787 s {Show(faint)}; 35.843 s {Show(whole)}";
            Assert.True(first.Values.All(letter => letter.Opacity <= 0.35), $"12 ms after the takeover the kept \"High\" is drawn (video 35.765 s: not yet): {seen}");
            Assert.True(faint.Count == 4 && faint.Values.All(letter => letter.Opacity <= 0.75 && letter.Scale < 0.9 && letter.Rise > 0.05),
                $"34 ms after the takeover the kept \"High\" is not faint, small and low (video 35.787 s): {seen}");
            Assert.True(whole.Count == 4 && whole.Values.All(letter => letter.Opacity >= 0.7 && letter.Scale >= 0.85 && Math.Abs(letter.Rise) <= 0.12),
                $"90 ms after the takeover the kept \"High\" is not drawn whole on its line (video 35.843 s): {seen}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    // ED-238 round 2 (2): a letter the taken-over change was still bringing in "is that change's letter now (MorphingTextBlock.Live,
    // on its own clock) under the leave". Its opacity is the arrival's times the leave's; with the arrival's left out (mutant T3,
    // `Opacity = glyph.Opacity`), the faint "r" and "a" jump to full at the takeover, and every UI Port, Popover and Traceability
    // test and every App Views.Composer test passes (the round 3 REVIEW test reads them 47 ms later, when they are whole either
    // way). SOURCE FACT (video_inc_r3, 35.037-35.078 s; effort-rev-r4-frames/vinc_12_19.png): the "r" and "a" of "Extra",
    // small and faint at the takeover (about 35.05 s), are still faint at 35.057 s and whole only by 35.078-35.100 s.
    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111's morph (EffortSlider.swift:574) through the widening takeover
    public void REVIEW_The_letters_still_arriving_at_a_takeover_go_on_fading_in_and_do_not_jump_to_full() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (label, window, advance) = Title("High", Pink, frames, clock);
        try
        {
            Change(label, "Extra High", Pink); // 34.838 s
            advance(215);
            var before = Drawn(label, "Extra High");
            Change(label, "Max", Orange); // about 35.053 s, the takeover
            advance(4); // 35.057 s
            var after = Drawn(label, "Extra High");
            string Show(Dictionary<int, (double Opacity, double Scale, double Rise)> letters) => string.Join(" ", new[] { 3, 4 }
                .Select(i => letters.TryGetValue(i, out var s) ? $"{"Extra High"[i]}: {s.Opacity:F2}" : $"{"Extra High"[i]}: not drawn"));
            var seen = $"at the takeover {Show(before)}; 4 ms after {Show(after)}";
            Assert.True(before.TryGetValue(4, out var a0) && a0.Opacity < 0.5, $"the \"a\" is not still faint at the takeover (video 35.037 s): {seen}");
            Assert.True(after.TryGetValue(4, out var a1) && a1.Opacity <= a0.Opacity + 0.15, $"4 ms after the takeover the still arriving \"a\" jumps toward full (video 35.057 s: still faint): {seen}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111's morph (EffortSlider.swift:574) through the narrowing takeover; ED-238 final round 3 (4), OPEN
    public void REVIEW_The_a_carried_through_the_narrowing_takeover_stays_on_the_h_as_long_as_the_video_shows() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (label, window, advance) = Title("Max", Pink, frames, clock);
        try
        {
            Change(label, "Extra High", Pink); // 35.635 s
            advance(118);
            Change(label, "High", Pink); // 35.753 s, the takeover
            advance(112); // 35.865 s
            var full = Extra(label).GetValueOrDefault(4);
            advance(22); // 35.887 s
            var still = Extra(label).GetValueOrDefault(4);
            advance(100); // 35.987 s
            var gone = Extra(label).GetValueOrDefault(4);
            var seen = $"the \"a\" at 35.865 s {full:F2}, at 35.887 s {still:F2}, at 35.987 s {gone:F2} (video ink 1.00, 0.75, 0)";
            Assert.True(full >= 0.8, $"112 ms after the takeover the \"a\" is fading already (video 35.865 s: plain over the \"h\", its fullest): {seen}");
            Assert.True(still >= 0.55, $"134 ms after the takeover the \"a\" is mostly gone (video 35.887 s: 0.75 of its ink): {seen}");
            Assert.True(gone <= 0.1, $"234 ms after the takeover the \"a\" still shows (video 35.987 s: gone): {seen}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
