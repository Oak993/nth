using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.App.Tests.Port.Voice;

[Collection("Headless")]
[Trait("Decision", "ED-292")]
public sealed class DictationFragmentParityTests(HeadlessSession headless)
{
    [Fact]
    public void Editor_detects_each_paragraph_base_without_changing_physical_left_alignment()
    {
        headless.OnUiThread(() =>
        {
            const string text = "123 یہ ٹیسٹ\n123 English\n123 שלום עולם";
            using var rig = new DictationFragmentRig(260, text);
            Assert.Equal(TextAlignment.Left, rig.Editor.TextAlignment);
            var start = 0; var y = 0.0;
            foreach (var paragraph in text.Split('\n'))
            {
                using var oracle = rig.Oracle(paragraph, paragraph.Contains("English", StringComparison.Ordinal) ? FlowDirection.LeftToRight : FlowDirection.RightToLeft);
                for (var i = 0; i < paragraph.Length; i++)
                {
                    var expected = oracle.HitTestTextRange(i, 1).First();
                    var actual = rig.Presenter.TextLayout.HitTestTextRange(start + i, 1).First();
                    Assert.Equal(expected.X, actual.X); Assert.Equal(y + expected.Y, actual.Y);
                }
                y += oracle.Height; start += paragraph.Length + 1;
            }
        });
    }

    [Fact]
    public void Natural_RTL_base_preserves_the_existing_Urdu_FLIP_guard()
    {
        headless.OnUiThread(() =>
        {
            string[] words = ["یہ", "ایک", "ٹیسٹ", "ہے"];
            using var rig = new DictationFragmentRig(240);
            rig.Begin(); rig.Live(WordOp.Add(0, words[0], false), WordOp.Add(1, words[1], false), WordOp.Add(2, words[2], false)); rig.Step(); rig.Step(1);
            using var before = rig.Oracle(string.Join(' ', words.Take(3)));
            using var after = rig.Oracle(string.Join(' ', words));
            var starts = new[] { 0, words[0].Length + 1, words[0].Length + words[1].Length + 2, words.Take(3).Sum(word => word.Length) + 3 };
            var oldPlaces = starts.Take(3).Select((start, i) => before.HitTestTextRange(start, words[i].Length).First().TopLeft).ToArray();
            var newPlaces = starts.Select((start, i) => after.HitTestTextRange(start, words[i].Length).First().TopLeft).ToArray();
            Assert.True(Math.Abs(newPlaces[0].X - oldPlaces[0].X) > 5, "the first word moves when a word is added at the left end of the run");
            rig.Live(WordOp.Add(3, words[3], true));
            for (var frame = 1; frame <= 25; frame++)
            {
                rig.Step(1.0 / 60);
                var progress = CssCurves.At(CssCurves.Ez, frame / 60.0, .3);
                for (var i = 0; i < 3; i++)
                {
                    var expected = oldPlaces[i] + (newPlaces[i] - oldPlaces[i]) * progress;
                    var actual = rig.Layer.DisplayedPlace(i, SwiftClock.Now);
                    Assert.True(Math.Abs(actual.X - expected.X) < 1e-3 && Math.Abs(actual.Y - expected.Y) < 1e-3);
                }
                Assert.Equal(newPlaces[3], rig.Layer.DisplayedPlace(3, SwiftClock.Now));
            }
        });
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyzzabcdefghijklmnopqrstuvwxyzz", false)]
    [InlineData("یہایکبہتلمبااردولفظہےیہایکبہتلمبااردولفظہے", true)]
    [InlineData("a\u0301bcdefghijklmnopqrstuvwxyzffiabcdefg", false)]
    public void Every_wrapped_fragment_uses_the_locked_arrival_pixels(string word, bool rtl)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig();
            using var oracle = rig.Oracle(word, rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
            Assert.True(oracle.HitTestTextRange(0, word.Length).Count() > 1);
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step();
            rig.AssertWordPixels(word, 0, word.Length, 0, 6, rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
            rig.Step(.15);
            rig.AssertWordPixels(word, 0, word.Length, CssCurves.At(CssCurves.Ease, .15, .3), 6 * (1 - CssCurves.At(CssCurves.Ez, .15, .4)), rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
            rig.Step(.5); rig.AssertWordPixels(word, 0, word.Length, 1, 0, rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
        });
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyzzabcdefghijklmnopqrstuvwxyzz", false)]
    [InlineData("یہایکبہتلمبااردولفظہےیہایکبہتلمبااردولفظہے", true)]
    public void Caret_uses_the_final_character_on_the_final_wrapped_line(string word, bool rtl)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig();
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(1);
            using var layout = rig.Oracle(word, rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
            var line = layout.TextLines[^1];
            var x = line.GetDistanceFromCharacterHit(new CharacterHit(word.Length - 1, 1));
            var caret = rig.Layer.CaretRect(SwiftClock.Now)!.Value;
            Assert.Equal(rig.Editor.Padding.Top + layout.Height - line.Height + (line.Height - 17) / 2, caret.Y);
            Assert.Equal(rig.Editor.Padding.Left + x + (rtl ? -2.5 : 1), caret.X);
        });
    }

    [Fact]
    public void Empty_revision_and_empty_add_keep_a_finite_caret_and_can_withdraw()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig();
            rig.Begin(); rig.Live(WordOp.Add(0, "longlonglonglonglonglonglonglong", false)); rig.Step(); rig.Step(1);
            rig.Live(WordOp.Revise(0, ""), WordOp.Add(1, "", true)); rig.Step(); rig.Step(.4);
            var caret = rig.Layer.CaretRect(SwiftClock.Now)!.Value;
            Assert.True(double.IsFinite(caret.X) && double.IsFinite(caret.Y));
            rig.Layer.Apply(new(TranscriptUpdateKind.Withdraw, [], [])); rig.Step(); rig.Step(1); rig.Step(1); rig.Step();
            Assert.True(rig.Layer.HandedOver); Assert.Equal("", rig.Editor.Text);
        });
    }
}
