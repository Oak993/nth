using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Voice;

[Collection("Headless")]
[Trait("Decision", "ED-292")]
public sealed class DictationParagraphLifecycleTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("یہ ایک ٹیسٹ\nEnglish suffix", "یہایکبہتلمبااردولفظہےیہایکبہتلمبااردولفظہے", false)]
    [InlineData("English before\n123 اردو\nשלום suffix", "differentlongtextdifferentlongtextdifferentlongtext", false)]
    [InlineData("یہ ایک ٹیسٹ\nEnglish suffix", "یہایکبہتلمبااردولفظہےیہایکبہتلمبااردولفظہے", true)]
    public void Mixed_paragraph_selection_lands_with_identical_pixels_one_undo_and_cancel_restoration(string draft, string word, bool still)
    {
        headless.OnUiThread(() =>
        {
            var start = draft.IndexOf('\n', StringComparison.Ordinal) + 1;
            var end = draft.IndexOf('\n', start); if (end < 0) end = draft.Length;
            using var rig = new DictationFragmentRig(120, draft, start, end, still);
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(1);
            rig.Land(); rig.Step(1);
            Assert.False(rig.Layer.HandedOver);
            var last = DictationProbe.Frame(rig.Window); rig.Step();
            Assert.True(rig.Layer.HandedOver);
            Assert.True(DictationProbe.Same(last, DictationProbe.Frame(rig.Window), new PixelRect(0, 0, 120, 500)));
            rig.Editor.Undo(); Assert.Equal(draft, rig.Editor.Text);
            rig.Editor.SelectionStart = start; rig.Editor.SelectionEnd = end; rig.Window.UpdateLayout();
            var original = DictationProbe.Frame(rig.Window);
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(1);
            rig.Layer.Apply(new(TranscriptUpdateKind.Withdraw, [], [])); rig.Step(); rig.Step(1); rig.Step(1); rig.Step();
            Assert.True(rig.Layer.HandedOver); Assert.Equal(draft, rig.Editor.Text);
            Assert.True(DictationProbe.Same(original, DictationProbe.Frame(rig.Window), new PixelRect(0, 0, 120, 500)));
        });
    }

    [Theory]
    [InlineData("یہٹیسٹ\n")]
    [InlineData("یہٹیسٹ\r\n")]
    [InlineData("یہٹیسٹ\u2029")]
    [InlineData("یہٹیسٹ\u2028")]
    public void Required_terminal_break_puts_the_live_caret_on_the_new_empty_line(string word)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig(120);
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(1);
            using var expected = rig.Oracle(word, FlowDirection.RightToLeft);
            var line = expected.TextLines[^1];
            var caret = rig.Layer.CaretRect(SwiftClock.Now)!.Value;
            var top = expected.TextLines.Take(expected.TextLines.Count - 1).Sum(previous => previous.Height);
            Assert.Equal(rig.Editor.Padding.Top + (top + (line.Height - 17) / 2), caret.Y);
            Assert.Equal(rig.Editor.Padding.Left + line.GetDistanceFromCharacterHit(new CharacterHit(word.Length)) + 1, caret.X);
        });
    }

    [Theory]
    [InlineData("lineHeight")]
    [InlineData("spacing")]
    [InlineData("features")]
    [InlineData("size")]
    public void Each_live_font_setting_independently_invalidates_the_picture_and_caret(string setting)
    {
        headless.OnUiThread(() =>
        {
            const string word = "officeaffiffiofficeoffice";
            using var rig = new DictationFragmentRig(100);
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(1);
            var prior = rig.Layer.CaretRect(SwiftClock.Now);
            var priorLayout = rig.Layer.LayoutIdentity;
            var before = DictationProbe.Frame(rig.Window);
            switch (setting)
            {
                case "lineHeight": rig.Editor.LineHeight = 32; break;
                case "spacing": Avalonia.Controls.Documents.TextElement.SetLetterSpacing(rig.Editor, 1); break;
                case "features": Avalonia.Controls.Documents.TextElement.SetFontFeatures(rig.Editor, FontFeatureCollection.Parse("liga=0")); break;
                case "size": rig.Editor.FontSize = 18; break;
            }
            rig.Window.UpdateLayout(); rig.Step();
            if (setting == "lineHeight")
                Assert.True(DictationProbe.Same(before, DictationProbe.Frame(rig.Window), new PixelRect(0, 0, 100, 500)), "baseline movement must start at the displayed glyph origins");
            rig.Step(1);
            using var expected = new TextLayout(word, new Typeface(rig.Editor.FontFamily), rig.Editor.FontSize, SwiftColor.LabelColor.Brush(rig.Editor), TextAlignment.Left,
                TextWrapping.Wrap, maxWidth: 100, lineHeight: rig.Editor.LineHeight,
                letterSpacing: Avalonia.Controls.Documents.TextElement.GetLetterSpacing(rig.Editor), fontFeatures: Avalonia.Controls.Documents.TextElement.GetFontFeatures(rig.Editor));
            rig.AssertPicture(context => expected.Draw(context, new Point(0, rig.Editor.Padding.Top)));
            var caret = rig.Layer.CaretRect(SwiftClock.Now)!.Value;
            Assert.NotSame(priorLayout, rig.Layer.LayoutIdentity);
            // V-8.3 requires the native caret, not displacement for a feature that native
            // shaping leaves unchanged (the bundled Inter has no liga substitution).
            if (setting != "features") Assert.NotEqual(prior, caret);
            var line = expected.TextLines[^1];
            Assert.Equal(rig.Editor.Padding.Top + expected.Height - line.Height + (line.Height - 17) / 2, caret.Y);
            Assert.Equal(line.GetDistanceFromCharacterHit(new CharacterHit(word.Length - 1, 1)) + 1, caret.X);
        });
    }

    [Fact]
    public void A_visibly_different_font_feature_rebuilds_the_picture_and_matches_the_editors_exact_glyphs_and_caret()
    {
        headless.OnUiThread(() =>
        {
            const string word = "111111111111111111111111";
            using var rig = new DictationFragmentRig(100);
            rig.Editor.FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter");
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(1);
            var priorLayout = rig.Layer.LayoutIdentity;
            var priorCaret = rig.Layer.CaretRect(SwiftClock.Now)!.Value;
            var before = DictationProbe.Frame(rig.Window);
            var features = FontFeatureCollection.Parse("cv01=1");
            Avalonia.Controls.Documents.TextElement.SetFontFeatures(rig.Editor, features);
            rig.Window.UpdateLayout(); rig.Step(); rig.Step(1);
            using var expected = new TextLayout(word, new Typeface(rig.Editor.FontFamily), rig.Editor.FontSize, SwiftColor.LabelColor.Brush(rig.Editor), TextAlignment.Left,
                TextWrapping.Wrap, maxWidth: 100, fontFeatures: features);
            Assert.NotSame(priorLayout, rig.Layer.LayoutIdentity);
            rig.AssertPicture(context => expected.Draw(context, new Point(0, rig.Editor.Padding.Top)));
            var caret = rig.Layer.CaretRect(SwiftClock.Now)!.Value;
            var firstLine = new PixelRect(0, (int)Math.Ceiling(rig.Editor.Padding.Top), 100, (int)Math.Floor(expected.TextLines[0].Height));
            Assert.True(firstLine.Bottom <= Math.Floor(Math.Min(priorCaret.Y, caret.Y)), "both live carets must be outside the changed-glyph witness");
            Assert.False(DictationProbe.Same(before, DictationProbe.Frame(rig.Window), firstLine));
            var line = expected.TextLines[^1];
            Assert.Equal(rig.Editor.Padding.Top + expected.Height - line.Height + (line.Height - 17) / 2, caret.Y);
            Assert.Equal(line.GetDistanceFromCharacterHit(new CharacterHit(word.Length - 1, 1)) + 1, caret.X);
        });
    }
}
