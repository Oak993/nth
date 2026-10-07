using Avalonia;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Voice;

[Collection("Headless")]
[Trait("Decision", "ED-292")]
public sealed class DictationFragmentTransitionTests(HeadlessSession headless)
{
    private const string Long = "abcdefghijklmnopqrstuvwxyzzabcdefghijklmnopqrstuvwxyzz";

    [Theory]
    [InlineData(Long, "short")]
    [InlineData("short", Long)]
    [InlineData(Long, "differentlongtextdifferentlongtextdifferentlongtextdifferentlongtext")]
    public void Revision_preserves_every_old_fragment_while_new_fragment_count_changes(string before, string after)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig();
            rig.Begin(); rig.Live(WordOp.Add(0, before, false)); rig.Step(); rig.Step(1);
            rig.Live(WordOp.Revise(0, after)); rig.Step();
            rig.AssertWordPixels(before, 0, before.Length, 1, 0);
            rig.Step(.11);
            var ink = SwiftColor.LabelColor.Resolve(rig.Editor.ActualThemeVariant);
            var e = CssCurves.At(CssCurves.Ease, .11, .22); var z = CssCurves.At(CssCurves.Ez, .11, .3);
            using var old = rig.Oracle(before, foreground: new SolidColorBrush(Color.FromArgb((byte)Math.Round(ink.A * (1 - e)), ink.R, ink.G, ink.B)));
            using var next = rig.Oracle(after, foreground: new SolidColorBrush(Color.FromArgb((byte)Math.Round(ink.A * e), ink.R, ink.G, ink.B)));
            rig.AssertPicture(context => { next.Draw(context, new Point(0, rig.Editor.Padding.Top + 6 * (1 - z))); old.Draw(context, new Point(0, rig.Editor.Padding.Top - 6 * z)); });
            rig.Step(.4); rig.AssertWordPixels(after, 0, after.Length, 1, 0);
        });
    }

    [Fact]
    public void Wrapped_word_withdraws_with_the_same_opacity_and_rise_on_every_line()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig();
            rig.Begin(); rig.Live(WordOp.Add(0, Long, false)); rig.Step(); rig.Step(1);
            rig.Layer.Apply(new(TranscriptUpdateKind.Withdraw, [], [])); rig.Step(); rig.Step(.13);
            rig.AssertWordPixels(Long, 0, Long.Length, 1 - CssCurves.At(CssCurves.Ease, .13, .26), 5 * CssCurves.At(CssCurves.Ez, .13, .3));
        });
    }

    [Fact]
    public void Width_reflow_keeps_all_glyphs_at_their_displayed_positions_on_its_first_frame()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig(140);
            rig.Begin(); rig.Live(WordOp.Add(0, Long, false)); rig.Step(); rig.Step(1);
            var before = DictationProbe.Frame(rig.Window);
            // Keep the surface and origin fixed; only the field's wrapping constraint changes.
            rig.Editor.Width = rig.Layer.Width = 110; rig.Window.UpdateLayout(); rig.Step();
            var after = DictationProbe.Frame(rig.Window);
            // x < 100 covers the text and stays clear of the old caret, whose first-frame position is unchanged.
            var region = new PixelRect(0, 0, 100, 100);
            Assert.True(DictationProbe.Same(before, after, region), $"a width change jumped {DictationProbe.Difference(before, after, region):F6} pixels");
        });
    }

    [Fact]
    public void Wrapped_landing_and_withdraw_hand_over_identical_editor_pixels_and_one_undo()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig(90, "original");
            rig.Editor.SelectionStart = 0; rig.Editor.SelectionEnd = "original".Length;
            rig.Begin(); rig.Live(WordOp.Add(0, Long, false)); rig.Step(); rig.Step(1);
            rig.Land();
            (int Stride, byte[] Bytes, PixelSize Size)? last = null;
            for (var i = 0; i < 100 && !rig.Layer.HandedOver; i++)
            {
                if (rig.Layer.CaretFade(SwiftClock.Now) == 0) last = DictationProbe.Frame(rig.Window);
                rig.Step(1.0 / 60);
            }
            Assert.True(rig.Layer.HandedOver); Assert.NotNull(last);
            Assert.True(DictationProbe.Same(last!.Value, DictationProbe.Frame(rig.Window), new PixelRect(0, 0, 90, 250)));
            rig.Editor.Undo(); Assert.Equal("original", rig.Editor.Text);
            var original = DictationProbe.Frame(rig.Window);
            rig.Begin(); rig.Live(WordOp.Add(0, Long, false)); rig.Step(); rig.Step(1);
            rig.Layer.Apply(new(TranscriptUpdateKind.Withdraw, [], [])); rig.Step();
            for (var i = 0; i < 100 && !rig.Layer.HandedOver; i++) rig.Step(1.0 / 60);
            Assert.True(rig.Layer.HandedOver);
            Assert.True(DictationProbe.Same(original, DictationProbe.Frame(rig.Window), new PixelRect(0, 0, 90, 250)));
        });
    }

    [Fact]
    public void Scrolling_follows_the_final_wrapped_line_on_the_locked_curve()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new DictationFragmentRig(scrolling: true);
            rig.Begin(); rig.Live(WordOp.Add(0, Long, false)); rig.Step();
            using var oracle = rig.Oracle(Long);
            var target = Math.Min(oracle.Height + rig.Editor.Padding.Top + rig.Editor.Padding.Bottom - rig.Scroll!.Viewport.Height,
                rig.Scroll.Extent.Height - rig.Scroll.Viewport.Height);
            Assert.True(target > 0);
            rig.Step(); Assert.Equal(0, rig.Scroll.Offset.Y);
            rig.Step(.18);
            Assert.Equal(target * CssCurves.At(CssCurves.Ez, .18, .36), rig.Scroll.Offset.Y, 6);
            rig.Step(.18); Assert.Equal(target, rig.Scroll.Offset.Y, 6);
        });
    }
}
