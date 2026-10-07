using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Voice;

[Collection("Headless")]
[Trait("Decision", "ED-292")]
public sealed class DictationClusterParityTests(HeadlessSession headless)
{
    private const string Urdu = "یہایکبہتلمبااردولفظہےیہایکبہتلمبااردولفظہے";

    [Theory]
    [InlineData(Urdu, "")]
    [InlineData("a\u0301bcdefghijklmnopqrstuvwxyzffiabcdefg", "")]
    [InlineData(Urdu, "English ")]
    public void Cluster_reflow_preserves_the_first_frame_for_RTL_ligatures_combining_marks_and_mixed_font_baselines(string word, string prefix)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig(140, prefix, prefix.Length, prefix.Length);
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(1);
            var before = DictationProbe.Frame(rig.Window);
            rig.Editor.Width = rig.Layer.Width = 100; rig.Window.UpdateLayout(); rig.Step();
            var after = DictationProbe.Frame(rig.Window);
            Assert.True(DictationProbe.Same(before, after, new PixelRect(0, 0, 140, 500)),
                $"a cluster changed its displayed position: {DictationProbe.Difference(before, after, new PixelRect(0, 0, 140, 500)):F6}");
        });
    }

    [Fact]
    public void Repeated_wrapped_revisions_keep_each_captured_picture_until_its_own_expiry()
    {
        headless.OnUiThread(() =>
        {
            const string first = "abcdefghijklmnopqrstuvwxyzzabcdefghijklmnopqrstuvwxyzz";
            const string second = "differentlongtextdifferentlongtextdifferentlongtext";
            const string third = "short";
            using var rig = new DictationFragmentRig();
            rig.Begin(); rig.Live(WordOp.Add(0, first, false)); rig.Step(); rig.Step(1);
            rig.Live(WordOp.Revise(0, second)); rig.Step(); rig.Step(.1);
            rig.Live(WordOp.Revise(0, third)); rig.Step(); rig.Step(.1);
            var ink = SwiftColor.LabelColor.Resolve(rig.Editor.ActualThemeVariant);
            var e = CssCurves.At(CssCurves.Ease, .1, .22); var z = CssCurves.At(CssCurves.Ez, .1, .3);
            SolidColorBrush Brush(double alpha) => new(Color.FromArgb((byte)Math.Round(ink.A * alpha), ink.R, ink.G, ink.B));
            using var a = rig.Oracle(first, foreground: Brush(1 - CssCurves.At(CssCurves.Ease, .2, .22)));
            using var b = rig.Oracle(second, foreground: Brush(e * (1 - e)));
            using var c = rig.Oracle(third, foreground: Brush(e));
            var secondRise = 6 * (1 - z);
            rig.AssertPicture(context =>
            {
                c.Draw(context, new Point(0, rig.Editor.Padding.Top + 6 * (1 - z)));
                a.Draw(context, new Point(0, rig.Editor.Padding.Top - 6 * CssCurves.At(CssCurves.Ez, .2, .3)));
                b.Draw(context, new Point(0, rig.Editor.Padding.Top + secondRise + (-6 - secondRise) * z));
            });
            rig.Step(.5); rig.AssertWordPixels(third, 0, third.Length, 1, 0);
        });
    }

    [Fact]
    public void Tentative_mask_and_ink_preserve_the_editors_disabled_ligatures()
    {
        headless.OnUiThread(() =>
        {
            const string word = "officeaffiffioffice";
            using var rig = new DictationFragmentRig(220);
            var features = FontFeatureCollection.Parse("liga=0");
            Avalonia.Controls.Documents.TextElement.SetFontFeatures(rig.Editor, features);
            rig.Begin(); rig.Live(WordOp.Add(0, word, true)); rig.Step(); rig.Step(1);
            var ink = SwiftColor.LabelColor.Resolve(rig.Editor.ActualThemeVariant);
            using var expected = new TextLayout(word, new Typeface(rig.Editor.FontFamily, rig.Editor.FontStyle, rig.Editor.FontWeight), rig.Editor.FontSize,
                new SolidColorBrush(Color.FromArgb((byte)Math.Round(ink.A * .6), ink.R, ink.G, ink.B)), TextAlignment.Left, TextWrapping.Wrap,
                maxWidth: 220, fontFeatures: features);
            rig.AssertPicture(context => expected.Draw(context, new Point(0, rig.Editor.Padding.Top)));
        });
    }

    [Fact]
    public void Wrapped_picture_and_editor_share_line_height_and_inherited_letter_spacing()
    {
        headless.OnUiThread(() =>
        {
            const string word = "officeaffiffiofficeoffice";
            using var rig = new DictationFragmentRig(100);
            rig.Editor.LineHeight = 28;
            Avalonia.Controls.Documents.TextElement.SetLetterSpacing(rig.Editor, 2);
            rig.Window.UpdateLayout();
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(.15);
            var ink = SwiftColor.LabelColor.Resolve(rig.Editor.ActualThemeVariant);
            var alpha = CssCurves.At(CssCurves.Ease, .15, .3);
            using var expected = new TextLayout(word, new Typeface(rig.Editor.FontFamily), rig.Editor.FontSize,
                new SolidColorBrush(Color.FromArgb((byte)Math.Round(ink.A * alpha), ink.R, ink.G, ink.B)), TextAlignment.Left, TextWrapping.Wrap,
                maxWidth: 100, lineHeight: 28, letterSpacing: 2);
            rig.AssertPicture(context => expected.Draw(context, new Point(0, rig.Editor.Padding.Top + 6 * (1 - CssCurves.At(CssCurves.Ez, .15, .4)))));
            rig.Step(1); rig.Land(); rig.Step(1); rig.Step();
            Assert.Equal(28, rig.Presenter.TextLayout.TextLines[0].Height);
            Assert.Equal(expected.Height, rig.Presenter.TextLayout.Height);
        });
    }

    [Fact]
    public void Landing_waits_for_reflow_clusters_when_the_first_word_anchor_and_final_caret_stay_fixed()
    {
        headless.OnUiThread(() =>
        {
            const string word = "abcdefghijklmnopqrstuvwxyzzabcdefghijklmnopqrstuvwxyzz\ntail";
            using var rig = new DictationFragmentRig(180);
            using var before = rig.Oracle(word);
            var width = Enumerable.Range(130, 45).Reverse().First(candidate =>
            {
                using var layout = new TextLayout(word, new Typeface(rig.Editor.FontFamily), rig.Editor.FontSize, Brushes.Black, TextAlignment.Left, TextWrapping.Wrap, maxWidth: candidate);
                return layout.TextLines.Count == before.TextLines.Count && layout.HitTestTextPosition(22) != before.HitTestTextPosition(22);
            });
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(1);
            var anchor = rig.Layer.DisplayedPlace(0, SwiftClock.Now);
            rig.Land(); rig.Step(.18);
            var caret = rig.Layer.CaretRect(SwiftClock.Now);
            rig.Editor.Width = rig.Layer.Width = width; rig.Window.UpdateLayout(); rig.Step();
            Assert.Equal(anchor, rig.Layer.DisplayedPlace(0, SwiftClock.Now)); Assert.Equal(caret, rig.Layer.CaretRect(SwiftClock.Now));
            Assert.False(rig.Layer.HandedOver); rig.Step(.29); Assert.False(rig.Layer.HandedOver);
            rig.Step(.01); var last = DictationProbe.Frame(rig.Window); rig.Step();
            Assert.True(rig.Layer.HandedOver);
            Assert.True(DictationProbe.Same(last, DictationProbe.Frame(rig.Window), new PixelRect(0, 0, 180, 500)));
        });
    }
}
