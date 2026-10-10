using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using SkiaSharp;

namespace Marrow.App.Tests.Nth;

// nth motion pack, site group D, the Marrow.App sites: step 11 (connecting labels) and step 14 (the model colours' loading tube).
// Expected pixels come from the review page's step 14 notes: a tube whose light sweeps every 1.6 s clipped to the tube, three flat
// --mark dots (r 2, 11 px apart) in the middle, and a 0.2 s fade of both when the colours arrive.
[Collection("Headless")]
public sealed class NthSitesDTests(HeadlessSession headless)
{
    private const int W = 168, H = 12;

    private static SKBitmap Paint(PaletteLoadingPaint paint, bool loading, double phase, bool still = false, bool dark = false)
    {
        var bitmap = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bitmap);
        paint.Draw(canvas, new SKRect(0, 0, W, H), loading, phase, still, dark);
        return bitmap;
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void While_loading_the_tube_shows_three_flat_dots_in_the_middle_and_a_light_inside_it()
    {
        using var paint = new PaletteLoadingPaint();
        // dot 1 (the middle one) swells first at phase 0.18 and sits at the tube's centre; dots 0 and 2 are 11 px either side
        using var frame = Paint(paint, true, .5);
        var mark = NthPalette.Light.Mark;
        var centre = frame.GetPixel(W / 2, H / 2);
        Assert.Equal(mark.Red, centre.Red); Assert.Equal(mark.Green, centre.Green); Assert.Equal(mark.Blue, centre.Blue);
        Assert.True(centre.Alpha >= 204, $"alpha {centre.Alpha}"); // .8 of the mark at rest, more as the dot swells
        Assert.True(frame.GetPixel(W / 2 - 11, H / 2).Alpha > 0);
        Assert.True(frame.GetPixel(W / 2 + 11, H / 2).Alpha > 0);
        Assert.Equal(0, frame.GetPixel(W - 12, 1).Alpha); // off the dots and the light: nothing
        // the light sweeps by the 1.6 s clock: somewhere along the tube there is light at a moment it is travelling, clipped to the tube
        using var moving = Paint(paint, true, .8);
        var lit = false;
        for (var x = 0; x < W; x++) if (moving.GetPixel(x, 2).Alpha > 0 && Math.Abs(x - W / 2) > 14) lit = true;
        Assert.True(lit, "the light crosses the tube");
        // clipped to the tube: the corner outside the capsule (r 6) stays clear
        Assert.Equal(0, moving.GetPixel(0, 0).Alpha);
        Assert.Equal(0, moving.GetPixel(W - 1, H - 1).Alpha);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_calm_tube_has_still_dots_and_no_light()
    {
        using var paint = new PaletteLoadingPaint();
        using var a = Paint(paint, true, .5, still: true);
        using var b = Paint(paint, true, 1.0, still: true);
        for (var x = 0; x < W; x++) for (var y = 0; y < H; y++) Assert.Equal(a.GetPixel(x, y), b.GetPixel(x, y));
        Assert.True(a.GetPixel(W / 2, H / 2).Alpha > 0);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_dark_tube_uses_the_dark_marks()
    {
        using var paint = new PaletteLoadingPaint();
        using var frame = Paint(paint, true, .5, dark: true);
        var mark = NthPalette.Dark.Mark;
        var centre = frame.GetPixel(W / 2, H / 2);
        Assert.Equal((mark.Red, mark.Green, mark.Blue), (centre.Red, centre.Green, centre.Blue));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void When_the_colours_arrive_the_light_and_the_dots_fade_out_in_0_2_seconds_and_then_nothing_is_drawn()
    {
        using var paint = new PaletteLoadingPaint();
        using (Paint(paint, true, 2.0)) { }
        using var start = Paint(paint, false, 2.0); // loading just ended: the first frame is still whole
        using var half = Paint(paint, false, 2.1);
        using var end = Paint(paint, false, 2.2);
        var a0 = start.GetPixel(W / 2, H / 2).Alpha; var a1 = half.GetPixel(W / 2, H / 2).Alpha; var a2 = end.GetPixel(W / 2, H / 2).Alpha;
        Assert.True(a0 > a1 && a1 > 0, $"{a0} > {a1} > 0");
        Assert.Equal(0, a2);
        using var later = Paint(paint, false, 3.0);
        for (var x = 0; x < W; x++) for (var y = 0; y < H; y++) Assert.Equal(0, later.GetPixel(x, y).Alpha);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_palette_that_never_loaded_draws_nothing()
    {
        using var paint = new PaletteLoadingPaint();
        using var frame = Paint(paint, false, 1.0);
        for (var x = 0; x < W; x++) for (var y = 0; y < H; y++) Assert.Equal(0, frame.GetPixel(x, y).Alpha);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_connecting_row_is_a_12_point_spinner_in_its_16_point_slot_and_the_label_with_the_light() => headless.OnUiThread(() =>
    {
        var row = ConnectingWait.Row("Connecting", SwiftFont.Subheadline, Chrome.SecondaryText);
        Assert.Equal(Orientation.Horizontal, row.Orientation);
        Assert.Equal(6, row.Spacing);
        var mark = Assert.IsType<NthStatusMark>(row.Children[0]);
        Assert.Equal(NthStatus.Working, mark.Status);
        Assert.Equal(12, mark.MarkSize);
        Assert.Equal(16, mark.Width); Assert.Equal(16, mark.Height);
        var label = Assert.IsType<NthShineText>(row.Children[1]);
        Assert.Equal("Connecting", label.Text);
        Assert.True(label.IsShining);
        Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, label.TextWrapping);
        Assert.False(label.IsHitTestVisible);
        Assert.Equal(11, label.FontSize);
    });
}
