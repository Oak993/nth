// PORT-ONLY: the Tactus pill's drawn surfaces (OD-0145, ED-NEW-pcctl-24; DESIGN.md 10.6 "Glass, motion and one clock";
// canvas `.pv-g`, `.pb.stp`): the body's tint over the blur, the dense panel without it (ED-238), the shadow only outside
// the body and only where the band draws it, Stop's silver capsule, high contrast, and the pose's opacity. Pixels of an
// SKBitmap, no window.
using Marrow.Core.Port.PcControl.Pill;
using Marrow.UI.Port.PcControl;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-24")]
public sealed class PillSurfacePainterTests
{
    private static readonly PillPose Pose = new(new PillVector(300, 50), 0.5, 400, 48, 0, 1, 1, 1, 1);
    private static readonly SKRect Body = new(100, 50, 500, 98);

    private static SKBitmap Draw(PillSurfaceKind kind, SKRect shape, PillSurfaceLook look, PillPose? pose = null, Action<PillSurfacePainter>? after = null)
    {
        var bitmap = new SKBitmap(new SKImageInfo(600, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var painter = new PillSurfacePainter();
        painter.Paint(canvas, kind, shape, pose ?? Pose, look, 1);
        canvas.Flush();
        after?.Invoke(painter);
        return bitmap;
    }

    [Fact]
    public void Without_glass_the_body_is_the_dense_panel_with_its_shadow_outside()
    {
        PillPaint painted = PillPaint.None;
        using var bitmap = Draw(PillSurfaceKind.Body, Body, PillSurfaceLook.Default, after: p => painted = p.LastPainted);

        Assert.Equal(PillPaint.Shadow | PillPaint.Body, painted);
        var centre = bitmap.GetPixel(300, 74);
        Assert.True(centre.Alpha >= 214, $"dense alpha {centre.Alpha}");
        Assert.True(centre.Red < 80, $"dark tint {centre.Red}");
        // the shadow falls below the body (0 10 26 -8), not above it, and never under it
        Assert.True(bitmap.GetPixel(300, 110).Alpha > 10, $"shadow {bitmap.GetPixel(300, 110).Alpha}");
        Assert.True(bitmap.GetPixel(300, 110).Alpha > bitmap.GetPixel(300, 38).Alpha);
        Assert.Equal(0, bitmap.GetPixel(300, 4).Alpha);
    }

    [Fact]
    public void Over_the_glass_the_tint_is_lighter_than_the_dense_panel()
    {
        using var dense = Draw(PillSurfaceKind.Body, Body, PillSurfaceLook.Default);
        using var glass = Draw(PillSurfaceKind.Body, Body, PillSurfaceLook.Default with { Glass = true });

        var tint = glass.GetPixel(300, 74).Alpha;
        Assert.InRange(tint, (byte)128, (byte)200);
        Assert.True(tint < dense.GetPixel(300, 74).Alpha);
    }

    [Fact]
    public void A_band_without_the_shadow_draws_none_and_a_shadow_band_draws_only_it()
    {
        PillPaint painted = PillPaint.None;
        using var noShadow = Draw(PillSurfaceKind.Body, Body, PillSurfaceLook.Default with { Paint = PillPaint.Body | PillPaint.Content }, after: p => painted = p.LastPainted);
        Assert.Equal(PillPaint.Body, painted);
        Assert.Equal(0, noShadow.GetPixel(300, 110).Alpha);
        Assert.True(noShadow.GetPixel(300, 74).Alpha > 200);

        using var shadowOnly = Draw(PillSurfaceKind.Body, Body, PillSurfaceLook.Default with { Paint = PillPaint.Shadow }, after: p => painted = p.LastPainted);
        Assert.Equal(PillPaint.Shadow, painted);
        Assert.True(shadowOnly.GetPixel(300, 110).Alpha > 10);
        // the shadow is clipped out of the body, so the region band's body shows through cleanly
        Assert.Equal(0, shadowOnly.GetPixel(300, 74).Alpha);
    }

    [Fact]
    public void Stop_is_a_silver_capsule_drawn_only_with_the_content()
    {
        var capsule = new SKRect(100, 50, 270, 86);
        PillPaint painted = PillPaint.None;
        using var stop = Draw(PillSurfaceKind.Stop, capsule, PillSurfaceLook.Default, after: p => painted = p.LastPainted);

        Assert.Equal(PillPaint.Content, painted);
        var top = stop.GetPixel(185, 54);
        var bottom = stop.GetPixel(185, 82);
        Assert.True(top.Red > 230 && top.Alpha == 255, $"silver top {top}");
        Assert.True(bottom.Red < top.Red, "the silver darkens downward");
        Assert.Equal(0, stop.GetPixel(98, 68).Alpha);

        using var shadowBand = Draw(PillSurfaceKind.Stop, capsule, PillSurfaceLook.Default with { Paint = PillPaint.Shadow }, after: p => painted = p.LastPainted);
        Assert.Equal(PillPaint.None, painted);
        Assert.Equal(0, shadowBand.GetPixel(185, 68).Alpha);
    }

    [Fact]
    public void High_contrast_draws_system_black_with_a_border_and_no_shadow()
    {
        using var bitmap = Draw(PillSurfaceKind.Body, Body, PillSurfaceLook.Default with { HighContrast = true });

        var centre = bitmap.GetPixel(300, 74);
        Assert.Equal(new SKColor(0, 0, 0, 255), centre);
        Assert.Equal((byte)255, bitmap.GetPixel(300, 51).Red);
        Assert.Equal(0, bitmap.GetPixel(300, 110).Alpha);
    }

    [Fact]
    public void The_body_carries_the_pose_s_opacity_and_Stop_leaves_it_to_the_content_layer()
    {
        var faded = Pose with { Opacity = 0 };
        using var body = Draw(PillSurfaceKind.Body, Body, PillSurfaceLook.Default, faded);
        Assert.Equal(0, body.GetPixel(300, 74).Alpha);

        using var half = Draw(PillSurfaceKind.Body, Body, PillSurfaceLook.Default, Pose with { Opacity = 0.5 });
        using var full = Draw(PillSurfaceKind.Body, Body, PillSurfaceLook.Default);
        Assert.True(half.GetPixel(300, 74).Alpha < full.GetPixel(300, 74).Alpha);

        using var stop = Draw(PillSurfaceKind.Stop, new SKRect(100, 50, 270, 86), PillSurfaceLook.Default, faded);
        Assert.Equal((byte)255, stop.GetPixel(185, 68).Alpha);
    }
}
