using Marrow.App.Port.MultiAccount.Handshake;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.MultiAccount.Handshake;
using SkiaSharp;

namespace Marrow.App.Tests.Views.MultiAccount.Handshake;

// PORT-ONLY: MA-4-2, drawA :3098-3160; ED-252 render-thread allocation contract.
public sealed class HandshakePainterTests
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Warm_painter_draws_filaments_and_bloom_without_managed_frame_allocations()
    {
        using var painter = new HandshakePainter(256, 144, (240, 105, 90), (58, 214, 168));
        var frame = HandshakeGeometry.Draw(256, 144, 3000, false, false);
        using var bitmap = new SKBitmap(new SKImageInfo(256, 144, SKColorType.Rgba8888, SKAlphaType.Premul)); using var canvas = new SKCanvas(bitmap);
        for (var i = 0; i < 20; i++) painter.Draw(canvas, frame, 3000, false, false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 120; i++) painter.Draw(canvas, frame, 3000, false, false);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        canvas.Clear(SKColors.Transparent); painter.Draw(canvas, frame, 3000, false, false);
        Assert.True(bitmap.GetPixel(210, 118).Alpha > 0); Assert.Equal(0, bitmap.GetPixel(8, 8).Alpha);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Reduced_motion_has_a_static_gradient_strand_and_no_bloom()
    {
        using var painter = new HandshakePainter(256, 144, (240, 105, 90), (58, 214, 168));
        using var bitmap = new SKBitmap(new SKImageInfo(256, 144, SKColorType.Rgba8888, SKAlphaType.Premul)); using var canvas = new SKCanvas(bitmap);
        painter.Draw(canvas, HandshakeGeometry.Draw(256, 144, 3500, false, true), 3500, false, true);
        var arc = HandshakeGeometry.Arc(256, 144, .5); Assert.True(bitmap.GetPixel((int)Math.Round(arc.X), (int)Math.Round(arc.Y)).Alpha > 0);
        Assert.Equal(0, bitmap.GetPixel(210, 110).Alpha);
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Bloom_warmup_is_registered_under_graphics_and_draws_offscreen()
    {
        HandshakeGpuProgram.EnsureRegistered();
        try { var program = Assert.Single(GpuWarmup.Registered, p => p.Name == "Handshake neural filaments and bloom"); Assert.Equal("gpu", program.Step.Id); Assert.False(program.IsWork); Assert.Contains(program.Name, GpuWarmup.Programs); program.Drawing(new GpuWarmContext(null, 1)); }
        finally { HandshakeGpuProgram.Unregister(); }
    }
    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void Live_snapshot_draw_is_pixel_equal_to_the_existing_geometry_oracle_and_allocates_zero(bool dark, bool reduced)
    {
        using var painter = new HandshakePainter(256, 144, (240, 105, 90), (58, 214, 168));
        using var actual = new SKBitmap(new SKImageInfo(256, 144, SKColorType.Rgba8888, SKAlphaType.Premul)); using var ac = new SKCanvas(actual);
        using var expected = new SKBitmap(actual.Info); using var ec = new SKCanvas(expected);
        foreach (var t in new double[] { 1100, 1500, 2100, 2400, 2750, 3000, 3450, 3800, 4500, 4850, 5000, 5700 })
        {
            ac.Clear(SKColors.Transparent); ec.Clear(SKColors.Transparent);
            var snapshot = new HandshakeFrameSnapshot(Marrow.Core.Accounts.HandshakePhase.InSync, t, 0, "", false, reduced, (240, 105, 90), (58, 214, 168));
            painter.Draw(ac, snapshot, dark); painter.Draw(ec, HandshakeGeometry.Draw(256, 144, t, dark, reduced), t, dark, reduced);
            Assert.Equal(expected.Bytes, actual.Bytes);
        }
        var live = new HandshakeFrameSnapshot(Marrow.Core.Accounts.HandshakePhase.InSync, 3500, 0, "", false, reduced, (240, 105, 90), (58, 214, 168));
        for (var i = 0; i < 30; i++) painter.Draw(ac, live, dark);
        var before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 120; i++) painter.Draw(ac, live, dark);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
