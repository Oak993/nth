using Avalonia;
using Marrow.App.Tests.Fixtures;
using Xunit;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
[Trait("Decision", "OD-0147")]
public sealed class ComposerCentreShadowGuardTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(1.25, false)]
    [InlineData(1.25, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Approved_shadow_rows_never_count_as_outline(double scale, bool dark) => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig();
        var (view, window) = rig.Open(900);
        try
        {
            rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, dark));
            rig.PumpWindow(window);
            Run(rig, window, .4);
            rig.Main.OnMain(() =>
            {
                DictationProbe.Blur(view);
                var pixels = DictationProbe.Frame(window);
                var rect = Frame(view, window);
                var x = (int)(rect.Center.X * scale);
                var backdrop = Avalonia.Media.Color.FromRgb(48, 56, 70);
                var checkedRows = 0;
                for (var y = (int)Math.Floor(rect.Top * scale) - 10; y <= (int)Math.Ceiling(rect.Bottom * scale) + 10; y++)
                {
                    var logicalY = (y + .5) / scale;
                    if (Math.Max(rect.Top - logicalY, logicalY - rect.Bottom) <= .7 / scale) continue;
                    Assert.False(ComposerCentreProbe.RowIsOutline(pixels, rect, scale, x, y, backdrop), $"Shadow-only row {y} at {scale}, dark={dark} counted as outline");
                    checkedRows++;
                }
                Assert.True(checkedRows >= 18);
                // A real two-level departure still counts; subtracting the shadow must not relax the detector.
                var row = (int)Math.Floor(rect.Top * scale) - 10;
                var index = row * pixels.Stride + x * 4;
                var sample = pixels.Bytes[index];
                var one = (byte[])pixels.Bytes.Clone();
                one[index] = (byte)(sample + 1);
                Assert.False(ComposerCentreProbe.RowIsOutline((pixels.Stride, one, pixels.Size), rect, scale, x, row, backdrop));
                one[index] = (byte)(sample + 2);
                Assert.True(ComposerCentreProbe.RowIsOutline((pixels.Stride, one, pixels.Size), rect, scale, x, row, backdrop));
            });
        }
        finally { rig.Close(window); }
    });
}
