using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeWashFadeTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Wash_colour_changes_use_one_kept_program_without_managed_frame_allocations()
    {
        using var painter = new StrikePainter();
        using var bitmap = new SKBitmap(298, 34);
        using var canvas = new SKCanvas(bitmap);
        var channels = new StrikeChannels(1274);
        var geometry = new StrikeGeometry(298, 34, 272, 24);
        var target = StrikeTint.For(EffortBrand.Silver);
        for (var i = 0; i < 120; i++) painter.Paint(canvas, channels.Scene(geometry, 5 + i / 240.0, StrikeTint.Claude));
        var shaders = painter.WashShadersMade;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 120; i++)
            painter.Paint(canvas, channels.Scene(geometry, 5 + i / 240.0, StrikeTint.Mix(StrikeTint.Claude, target, i / 119.0)));
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(shaders, painter.WashShadersMade);
        Assert.Equal(0, bytes);
    }
}
