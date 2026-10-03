using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (unit/jewel-knob): OD-0090 asks for the knob "on the composition thread (the orb as an SkSL shader, bounded
// per-frame cost, no per-frame allocation)" and ED-252 states "the render thread allocates nothing managed". These pin both
// claims: a slider frame in fast mode, once warm, makes no managed objects (EffortSlider.Advance builds a new TrackLook
// record through Look twice and a new StrikeGeometry record every frame: about 112 B, frame-cost.txt), and drawing a
// burst's rings on the render thread makes none either (BubbleBursts.DrawRings makes an SKPaint and an SKPath each call).
[Collection("Headless")]
public sealed class EffortSliderJewelAllocationTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: no per-frame allocation (the UI thread's share of a fast-mode slider frame)
    public void A_warm_fast_mode_slider_frame_allocates_nothing() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 2, fast: true);
        for (var i = 0; i < 30; i++) rig.Frame();
        for (var i = 0; i < 60; i++) rig.Slider.Advance(1.0 / 60, 1 + i / 60.0); // warm: caches, exits, fill colours
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 120; i++) rig.Slider.Advance(1.0 / 60, 2 + i / 60.0);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 120.0;
        Assert.True(perFrame == 0, $"a slider frame allocates {perFrame:F0} B");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: the render thread allocates nothing managed (a burst's rings, drawn with the bubbles)
    public void Drawing_a_bursts_rings_allocates_nothing()
    {
        var burst = new BubbleBurst(1, 30, 17, 2, 0.8, 1.1, 123456, 0, 0, 0);
        using var bitmap = new SKBitmap(new SKImageInfo(60, 34, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        BubbleBurst[] rings = [burst];
        for (var i = 0; i < 10; i++) BubbleBursts.DrawRings(canvas, rings, 0.85, 1.05, 1); // warm
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 60; i++) BubbleBursts.DrawRings(canvas, rings, 0.85, 1.0 + i / 600.0, 1);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 60.0;
        Assert.True(perFrame == 0, $"drawing the rings allocates {perFrame:F0} B a frame");
    }
}
