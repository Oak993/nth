using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// ED-252 review round 8: the knob is drawn through Avalonia's drawing context at the opacity the control is shown at.
// JewelKnobOperation.Render reads lease.CurrentOpacity and hands it to the orb's blit, the knob's own shadow and the lift's
// shadow group, which Avalonia does not do for a custom drawing. The source fades the whole slider whenever the model list
// opens or closes: EffortSlider.swift:180-187 .transition(.opacity) under .animation(.easeOut(duration: 0.12), value:
// showsModels), which the port's ModelEffortPanel plays through SwiftTransition (EffortSlider.cs:206-211). Avalonia draws
// each primitive of a faded control at its own alpha times the opacity, so a pixel the knob alone draws is, at opacity o,
// the pixel under it plus o times what the knob adds to it at full opacity: not the knob opaque (the opacity lost), nor at
// o squared (the opacity applied twice), nor each of its three parts at its own opacity but one of them not.
[Collection("Headless")]
public sealed class EffortSliderJewelOpacityTests(HeadlessSession headless)
{
    private static (int R, int G, int B) Pixel(Avalonia.Controls.TopLevel window, int x, int y)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var row = new byte[buffer.RowBytes];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
        Assert.Equal(255, row[x * 4 + 3]); // the window is opaque
        return (row[x * 4], row[x * 4 + 1], row[x * 4 + 2]); // Rgba8888 on this headless surface
    }

    // a pixel at opacity o: the pixel under the drawing plus o times what the drawing adds at full opacity (each channel within 2 of 255)
    private static void AssertFaded((int R, int G, int B) full, (int R, int G, int B) under, (int R, int G, int B) shown, double opacity, string what)
    {
        var adds = Math.Max(Math.Abs(full.R - under.R), Math.Max(Math.Abs(full.G - under.G), Math.Abs(full.B - under.B)));
        Assert.True(adds >= 10, $"{what}: the premise, the drawing adds {adds} to the pixel under it (under {under}, full {full})");
        (int Got, int Under, int Full, string Channel)[] channels = [(shown.R, under.R, full.R, "red"), (shown.G, under.G, full.G, "green"), (shown.B, under.B, full.B, "blue")];
        foreach (var (got, below, whole, channel) in channels)
        {
            var expected = below + opacity * (whole - below);
            Assert.True(Math.Abs(got - expected) <= 2, $"{what} at opacity {opacity}, {channel}: {got}, not {expected:F1} (under {below}, at full opacity {whole})");
        }
    }

    // the knob at rest (its centre, and its own shadow just below it) and held (the centre, and the lift's shadow, below
    // the reach of its own), read at full opacity, with the knob hidden, and at the opacity under test, all on the frame the
    // slider has just drawn. The CPU renderer draws the gem's picture on every other frame and reuses it between (ED-252),
    // so the first reads after a frame may show the last frame's picture: they are drawn until the picture is the frame's
    // own, and the read at full opacity is repeated at the end to show that it did not change.
    private static void FadeKnob(JewelRig rig, Action<double> set, JewelKnobView view, double[] opacities)
    {
        var cx = (int)(rig.Origin.X + rig.Slider.KnobX);
        var cy = (int)(rig.Origin.Y + EffortSlider.ThumbSize / 2);
        (string What, int X, int Y)[] points = rig.Slider.IsHeld
            ? [("the orb's centre", cx, cy), ("the lift's shadow", cx, cy + 36), ("the lift's shadow", cx, cy + 40)]
            : [("the orb's centre", cx, cy), ("the knob's own shadow", cx, cy + 27)];
        for (var i = 0; i < 3; i++) { view.InvalidateVisual(); Pixel(rig.Window, 0, 0); }
        foreach (var opacity in opacities)
        {
            foreach (var (what, x, y) in points)
            {
                set(1); view.IsVisible = true; var full = Pixel(rig.Window, x, y);
                set(opacity); view.IsVisible = false; var under = Pixel(rig.Window, x, y);
                view.IsVisible = true; var shown = Pixel(rig.Window, x, y);
                set(1); Assert.True(full == Pixel(rig.Window, x, y), $"{what}: the knob's picture changed between two reads of the same frame");
                set(opacity);
                AssertFaded(full, under, shown, opacity, what);
            }
        }
        set(opacities[^1]);
    }

    [Theory]
    [Trait("Decision", "ED-252")] // JewelKnobOperation.Render: lease.CurrentOpacity into the orb's blit, the knob's own shadow and the lift's shadow group, on the CPU renderer (JewelCpuKnob) and the GPU's program (JewelKnobRenderer, drawn here on the CPU)
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void The_orb_and_both_shadows_are_drawn_at_the_opacity_of_the_control_they_are_in(bool gpuProgram, bool held) => headless.OnUiThread(() =>
    {
        JewelKnobRenderer.SupersampleOnCpu = gpuProgram;
        try
        {
            using var rig = JewelFixtures.Rig(index: 1);
            rig.Frame();
            var view = rig.Slider.GetVisualDescendants().OfType<JewelKnobView>().Single();
            if (held)
            {
                rig.Down(rig.Slider.KnobX);
                for (var i = 0; i < 20; i++) rig.Frame();
                Assert.True(rig.Slider.LiftAmount > 0.99, $"lifted {rig.Slider.LiftAmount}");
            }
            FadeKnob(rig, o => view.Opacity = o, view, [0.5, 0.25]);
            if (held) rig.Up(rig.Slider.KnobX);
        }
        finally { JewelKnobRenderer.SupersampleOnCpu = false; }
    });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:180-187 .transition(.opacity) under .easeOut(0.12) on the model list's switch, as ModelEffortPanel plays it (EffortSlider.cs:206-211): the knob is drawn at the slider's opacity on every frame of the fade
    public void The_knob_follows_the_fade_the_model_lists_switch_plays_on_the_slider() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.FadingRig();
        var view = rig.Slider.GetVisualDescendants().OfType<JewelKnobView>().Single();
        var inside = 0;
        for (var i = 0; i < 12; i++)
        {
            rig.Frame();
            var opacity = rig.Slider.Opacity;
            if (opacity is < 0.05 or > 0.95) continue; // the knob and what is under it are not told apart at the ends of the fade
            inside++;
            FadeKnob(rig, o => rig.Slider.Opacity = o, view, [opacity]);
            Assert.Equal(opacity, rig.Slider.Opacity); // read back: the fade's own value
        }
        Assert.True(inside >= 3, $"the fade was read on {inside} frames");
        for (var i = 0; i < 12; i++) rig.Frame();
        Assert.Equal(1, rig.Slider.Opacity);
    });

    // ---- the drawings alone: the premultiplied alpha of the shadows at opacity 1 and one half, on a transparent bitmap

    private static SKBitmap Shadows(bool dark, double lift, double opacity)
    {
        var u = new JewelUniforms();
        u.Values[21] = dark ? 1 : 0;
        u.Lift = (float)lift;
        var bitmap = new SKBitmap(new SKImageInfo(120, 130, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        JewelKnobRenderer.For(null, 96).Shadows(canvas, u.Values, new SKRect(36, 30, 84, 78), (float)opacity);
        return bitmap;
    }

    [Theory]
    [Trait("Decision", "ED-252")] // jewel-knob.html:21/:35 --knobShadow and :118 s.lift, drawn at the control's opacity: the knob's own shadow alone, then the lift's shadows alone, each at half the alpha at half the opacity
    [InlineData(true)]
    [InlineData(false)]
    public void Each_shadow_has_half_the_alpha_at_half_the_opacity(bool dark)
    {
        using var ownFull = Shadows(dark, 0, 1);
        using var ownHalf = Shadows(dark, 0, 0.5);
        using var bothFull = Shadows(dark, 1, 1);
        using var bothHalf = Shadows(dark, 1, 0.5);
        int ownBiggest = 0, liftBiggest = 0;
        for (var y = 0; y < ownFull.Height; y++)
            for (var x = 0; x < ownFull.Width; x++)
            {
                double a1 = ownFull.GetPixel(x, y).Alpha, a5 = ownHalf.GetPixel(x, y).Alpha;
                Assert.True(Math.Abs(2 * a5 - a1) <= 2, $"the knob's own shadow at {x},{y}: alpha {a5} at half opacity, {a1} at full");
                ownBiggest = Math.Max(ownBiggest, (int)a1);
                // what the lift's group adds over the knob's own shadow: A = own + lift (1 - own), so lift = (A - own) / (1 - own)
                double Lift(SKBitmap both, SKBitmap own) { double o = own.GetPixel(x, y).Alpha, a = both.GetPixel(x, y).Alpha; return (a - o) / (1 - o / 255); }
                var l1 = Lift(bothFull, ownFull);
                var l5 = Lift(bothHalf, ownHalf);
                Assert.True(Math.Abs(2 * l5 - l1) <= 4, $"the lift's shadows at {x},{y}: alpha {l5:F1} at half opacity, {l1:F1} at full"); // the 8-bit layers' rounding, three bytes in each
                liftBiggest = Math.Max(liftBiggest, (int)l1);
            }
        Assert.True(ownBiggest >= 30 && liftBiggest >= 60, $"the premise: the shadows reach alpha {ownBiggest} (own) and {liftBiggest} (lift)");
    }
}
