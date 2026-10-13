using Marrow.UI.Port.Armilla;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Armilla;

[Trait("Decision", "ED-NEW-dextra-smooth-1")]
public sealed class DextraSmoothAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Warm_live_frames_with_all_six_node_shapes_allocate_nothing(bool reduced)
    {
        using var surface = SKSurface.Create(new SKImageInfo(176, 176));
        using var canvas = new ArmillaSkiaCanvas();
        canvas.Begin(surface.Canvas);
        var engine = new ArmillaEngine(new ArmillaLook
        {
            Size = 176, State = ArmillaState.Thinking, Reduce = reduced,
            Nodes = ArmillaDrive.Asturs(6),
        });
        for (var i = 0; i < 120; i++) { engine.Tick(1.0 / 60, i / 60.0); engine.Draw(canvas); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 120; i < 720; i++) { engine.Tick(1.0 / 60, i / 60.0); engine.Draw(canvas); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(bytes == 0, $"{bytes} B over 600 frames ({bytes / 600.0} B/frame), reduced={reduced}");
    }
}
