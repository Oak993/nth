using Marrow.Testing;
using Marrow.UI.Port.ComposerLights;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.ComposerLights;

// ED-NEW-smooth2-5: looking up already shaded pixels must not allocate a frame-key array.
public sealed class ShaderLightStillAllocationTests
{
    [Fact]
    [Trait("Decision", "OD-0150")]
    public void A_warm_still_light_allocates_nothing_when_the_same_frame_is_drawn_again()
    {
        using var renderer = new ShaderLightRenderer();
        var frame = ShaderLightRenderTests.Frame("lumen");
        using var bitmap = new SKBitmap(600, 240);
        using var canvas = new SKCanvas(bitmap);
        canvas.Translate(80, 80);
        var draws = 0;
        void Draw()
        {
            for (var i = 0; i < 120; i++)
                if (renderer.DrawStill(canvas, frame, 1)) draws++;
        }
        var bytes = AllocationProbe.Measure(Draw, Draw);
        Assert.Equal(240, draws);
        Assert.Equal(1, renderer.StillShades);
        Assert.True(bytes == 0, $"The still light allocated {bytes} bytes for 120 copies ({bytes / 120.0} B/frame).");
    }
}
