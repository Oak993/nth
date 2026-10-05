using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using System.Runtime.InteropServices;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// V-T131: CSS background-clip:text changes the colour, never the glyph's coverage.
[Collection("Headless")]
public sealed class ComposerVoicePillCoverageTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(13.5, 1.0)]
    [InlineData(13.5, 1.25)]
    [InlineData(13.5, 1.5)]
    [InlineData(13.5, 2.0)]
    [InlineData(15.0, 1.0)]
    [InlineData(15.0, 1.25)]
    [InlineData(15.0, 1.5)]
    [InlineData(15.0, 2.0)]
    [Trait("Decision", "ED-292")]
    public void The_sweep_changes_colour_without_increasing_glyph_coverage(double font, double scale)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var label = new VoicePillLabel { Words = VoicePillWordSets.For(VoicePillWords.L1).Listening, FontSize = font };
            label.Measure(Size.Infinity);
            label.Arrange(new Rect(label.DesiredSize));
            var plain = Render(label, 1.35, scale);
            Assert.Contains(plain.Alpha, alpha => alpha is > 0 and < 255);
            foreach (var sweep in new[] { 0.85, 0.5, 0.1 })
            {
                var lit = Render(label, sweep, scale);
                var worst = plain.Alpha.Zip(lit.Alpha, (a, b) => Math.Abs(a - b)).Max();
                Assert.False(plain.Pixels.SequenceEqual(lit.Pixels), "the source sweep still changes the rendered colour");
                Assert.True(worst <= 1, $"the sweep increased glyph coverage by {worst}/255 at {font} DIP, scale {scale}, phase {sweep}");
            }
        });
    }

    private static (byte[] Alpha, byte[] Pixels) Render(VoicePillLabel label, double sweep, double scale)
    {
        label.Sweep = sweep;
        var size = new PixelSize((int)Math.Ceiling(230 * scale), (int)Math.Ceiling(50 * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateTranslation(8.25, 8.5))) label.Render(context);
        var pixels = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, size.Width * 4); }
        finally { handle.Free(); }
        var alpha = new byte[size.Width * size.Height];
        for (var i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
        return (alpha, pixels);
    }
}
