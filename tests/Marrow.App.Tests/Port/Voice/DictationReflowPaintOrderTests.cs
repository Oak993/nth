using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Voice;

[Collection("Headless")]
[Trait("Decision", "ED-292")]
public sealed class DictationReflowPaintOrderTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("")]
    [InlineData("English ")]
    public void Each_settled_reflow_captures_its_actual_RTL_paint_order_and_reaches_exact_editor_pixels(string prefix)
    {
        headless.OnUiThread(() =>
        {
            const string word = "یہایکبہتلمبااردولفظہےیہایکبہتلمبااردولفظہے";
            using var rig = new DictationFragmentRig(140, prefix, prefix.Length, prefix.Length);
            rig.Begin(); rig.Live(WordOp.Add(0, word, false)); rig.Step(); rig.Step(1);
            foreach (var width in new[] { 100, 140, 100, 180, 100 })
            {
                var before = DictationProbe.Frame(rig.Window);
                rig.Editor.Width = rig.Layer.Width = width; rig.Window.UpdateLayout(); rig.Step();
                Assert.True(DictationProbe.Same(before, DictationProbe.Frame(rig.Window), new PixelRect(0, 0, 140, 500)), $"first frame at width {width} changed captured ink");
                rig.Step(.3);
                using var expected = new TextLayout(prefix + word, new Typeface(rig.Editor.FontFamily), rig.Editor.FontSize, SwiftColor.LabelColor.Brush(rig.Editor),
                    TextAlignment.Left, TextWrapping.Wrap, flowDirection: prefix.Length == 0 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, maxWidth: width);
                rig.AssertPicture(context => expected.Draw(context, new Point(0, rig.Editor.Padding.Top)));
            }
        });
    }
}
