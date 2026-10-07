using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T131 (voice-SPEC V-12.12; OD-0097 amendment "no separator dot"): the clean pill's label. Its drawn text runs are exactly the current words (plus the en space and the name only when V-12.10 shows it) for
// every set and state; no U+00B7 or other separator glyph is laid out; a run that is not shown is not in the text layout (the sweep never paints it); and the glyphs' ink, the descenders of g, p and y included,
// lies inside the sweep's clip: the label rendered with the sweep lit and the same text with the sweep off have the same ink within 1 px.
[Collection("Headless")]
public sealed class ComposerVoiceCleanLabelTests(HeadlessSession headless)
{
    private const string E = "…";

    [Theory] // V-T131: the runs
    [InlineData(VoicePillWords.L1)]
    [InlineData(VoicePillWords.L2)]
    [InlineData(VoicePillWords.L3)]
    [InlineData(VoicePillWords.L4)]
    [InlineData(VoicePillWords.L5)]
    [InlineData(VoicePillWords.L6)]
    [Trait("Decision", "ED-292")]
    public void The_label_lays_out_exactly_the_current_words_and_no_separator_for_every_set_and_state(VoicePillWords set)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (pill, window) = Standalone(rig);
            rig.Main.OnMain(() =>
            {
                pill.Words = set;
                pill.Open = true;
                var words = VoicePillWordSets.For(set);
                var expected = new[] { words.Listening, words.Transcribing, words.Finishing };
                for (var i = 0; i < 3; i++)
                {
                    var runs = pill.Labels[i].Runs;
                    Assert.Equal([new VoicePillRun(expected[i], 600)], runs); // only the words: a name that is not shown is not in the layout
                    Assert.DoesNotContain("·", runs.Single().Text, StringComparison.Ordinal);
                    Assert.DoesNotContain("•", runs.Single().Text, StringComparison.Ordinal);
                    Assert.DoesNotContain(" ", runs.Single().Text, StringComparison.Ordinal); // and no en space without a name
                }
            });
            rig.Main.OnMain(() => pill.Language = "Hindi");
            rig.Main.OnMain(() =>
            {
                foreach (var label in pill.Labels)
                    Assert.Equal([new VoicePillRun(label.Words, 600), new VoicePillRun(" Hindi", 400)], label.Runs);
            });
            rig.Close(window);
        });
    }

    private static (int Width, int Height, bool[] Ink, int[] Strength) Render(VoiceComposerRig rig, string words, string? name, double sweep, bool dark)
    {
        var label = rig.Main.OnMain(() => new VoicePillLabel { Words = words, LanguageName = name, Sweep = sweep, Opacity = 1, Margin = new Thickness(8) });
        var host = rig.Main.OnMain(() => new Border { Background = new SolidColorBrush(dark ? Color.FromRgb(18, 18, 22) : Color.FromRgb(250, 249, 251)), Child = label, Width = 260, Height = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top });
        var window = rig.Main.OnMain(() => new Avalonia.Controls.Window { Content = host, Width = 300, Height = 80, RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light });
        rig.Main.OnMain(() => window.Show());
        rig.PumpWindow(window);
        var frame = rig.Main.OnMain(() => DictationProbe.Frame(window));
        var background = (frame.Bytes[(70 * frame.Stride) + 4 * 280], frame.Bytes[(70 * frame.Stride) + 4 * 280 + 1], frame.Bytes[(70 * frame.Stride) + 4 * 280 + 2]);
        var ink = new bool[frame.Size.Width * frame.Size.Height];
        var strength = new int[frame.Size.Width * frame.Size.Height];
        for (var y = 0; y < Math.Min(60, frame.Size.Height); y++)
        for (var x = 0; x < Math.Min(270, frame.Size.Width); x++)
        {
            var at = y * frame.Stride + x * 4;
            var most = Math.Max(Math.Abs(frame.Bytes[at] - background.Item1), Math.Max(Math.Abs(frame.Bytes[at + 1] - background.Item2), Math.Abs(frame.Bytes[at + 2] - background.Item3)));
            ink[y * frame.Size.Width + x] = most > 24;
            strength[y * frame.Size.Width + x] = most;
        }
        rig.Close(window);
        return (frame.Size.Width, frame.Size.Height, ink, strength);
    }

    [Theory] // V-T131: the ink with the sweep lit matches the ink with it off within 1 px, descenders included, and nothing is painted where no text is
    [InlineData("Typing" + E, false)]
    [InlineData("Hearing you" + E, false)]
    [InlineData("Wrapping up" + E, true)]
    [InlineData("Speak now", true)]
    [Trait("Decision", "ED-292")]
    public void The_ink_with_the_sweep_lit_matches_the_ink_without_it_and_a_descender_is_never_cut(string words, bool dark)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var off = Render(rig, words, null, 1.35, dark); // nothing lit
            Assert.Contains(true, off.Ink); // the text is there
            foreach (var position in new[] { 0.85, 0.5, 0.1 }) // the lit band crosses the text
            {
                var lit = Render(rig, words, null, position, dark);
                var (w, h) = (off.Width, off.Height);
                bool Near(bool[] ink, int x, int y)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var xx = x + dx;
                        var yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < w && yy < h && ink[yy * w + xx]) return true;
                    }
                    return false;
                }
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (lit.Ink[y * w + x]) Assert.True(Near(off.Ink, x, y), $"ink painted at ({x}, {y}) with the sweep at {position} where the text has none");
                    if (off.Ink[y * w + x]) Assert.True(Near(lit.Ink, x, y), $"ink lost at ({x}, {y}) with the sweep at {position}: the sweep's clip cut a glyph (off {off.Strength[y * w + x]}, lit {lit.Strength[y * w + x]})");
                }
            }
        });
    }
}
