using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Tests.Models.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the beam's layers as the playground's generator writes them. Expected
// values are the radial-gradients of beam.js bbSheet(c) for c = {size, color, th, stat: false, fit, bad: false, W: 820,
// H: 52}, run under node (2026-10-03), each "at" position resolved against the 820 x 52 box; listed first-on-top as CSS.
public sealed class BorderBeamSceneTests
{
    private static List<(double Cx, double Cy, double Rx, double Ry, double R, double G, double B, double A)> Layer(BeamFrame f, int layer)
    {
        var list = new List<(double, double, double, double, double, double, double, double)>();
        for (var i = 0; i < f.Ellipses.Count; i++)
        {
            var e = f.Ellipses.At(i);
            if (e.Layer == layer) list.Add((e.Cx, e.Cy, e.Rx, e.Ry, e.R, e.G, e.B, e.A));
        }
        list.Reverse(); // the buffer is bottom first; CSS lists the top first
        return list;
    }

    private static BeamFrame Build(BorderBeamOptions b, bool dark = true)
    {
        var f = new BeamFrame();
        var clock = new ComposerLightClock(true, !dark);
        var v = new BeamValues();
        BorderBeamScene.Build(f, BeamSlotConfig.For(b, dark), v, clock, 820, 52);
        return f;
    }

    // bbSheet md fitted dark colorful: .b2 background (after the highlight) and .b1 > i background
    private static readonly double[][] MdFitStroke =
    [
        [270.6, -7.29, 164.94, 32.32], [98.4, -4.93, 141.38, 28.28], [11.22, 35.516, 61.4, 29.84], [11.22, 35.516, 30.7, 15.6], [610.08, 52, 424.14, 25.86],
        [451, 52, 200.29, 21.01], [769.98, 0, 174.37, 25.86], [820, 14.092, 39.91, 17.9], [820, 14.092, 79.82, 20.46],
    ];
    private static readonly double[][] MdFitInner =
    [
        [270.6, -7.29, 148.45, 29.09], [98.4, -4.93, 127.24, 25.45], [11.22, 35.516, 55.26, 26.85], [11.22, 35.516, 27.63, 14.04], [610.08, 52, 381.72, 23.27],
        [451, 52, 180.26, 18.91], [769.98, 0, 156.93, 23.27], [820, 14.092, 35.92, 16.11], [820, 14.092, 71.84, 18.41],
    ];

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbRot fitted (the default): stroke blobs at scale 1, inner blobs at .9 with alpha .45, bands 22.62 / 42.98 px
    public void The_default_beam_is_the_fitted_large_sheet()
    {
        var f = Build(BorderBeamOptions.Default);
        Assert.Equal(0, f.Mode);
        var stroke = Layer(f, BeamLayer.Stroke);
        var inner = Layer(f, BeamLayer.Inner);
        Assert.Equal(9, stroke.Count);
        Assert.Equal(9, inner.Count);
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(MdFitStroke[i], new[] { Math.Round(stroke[i].Cx, 3), Math.Round(stroke[i].Cy, 3), Math.Round(stroke[i].Rx, 2), Math.Round(stroke[i].Ry, 2) });
            Assert.Equal(MdFitInner[i], new[] { Math.Round(inner[i].Cx, 3), Math.Round(inner[i].Cy, 3), Math.Round(inner[i].Rx, 2), Math.Round(inner[i].Ry, 2) });
            Assert.Equal(1, stroke[i].A, 6);
            Assert.Equal(0.45, inner[i].A, 6);
        }
        Assert.Equal(255 / 255.0, stroke[0].R, 6); Assert.Equal(50 / 255.0, stroke[0].G, 6); Assert.Equal(100 / 255.0, stroke[0].B, 6);
        Assert.Equal(0.45 * 100 / 255, inner[4].R, 6);   // premultiplied
        Assert.True(f.Bands);
        Assert.Equal((22.62, 42.98), (f.BandV, f.BandH));
        Assert.Equal(new CssColor(255, 255, 255, 0.27), f.Shadow);
        Assert.Equal(4.5, f.ShadowSigma);
        Assert.Equal(21, f.RingRadius);
        Assert.False(f.InnerWindowW2);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbRot as the package: blobs at their percent positions and px sizes, the inner ones Math.round(v * .9)
    public void As_the_package_keeps_the_stock_geometry()
    {
        var f = Build(BorderBeamOptions.Default with { Fitted = false });
        var stroke = Layer(f, BeamLayer.Stroke);
        var inner = Layer(f, BeamLayer.Inner);
        Assert.Equal((270.6, -3.848, 70.0, 40.0), (Math.Round(stroke[0].Cx, 3), Math.Round(stroke[0].Cy, 3), stroke[0].Rx, stroke[0].Ry));
        Assert.Equal((17.22, 35.516, 40.0, 70.0), (Math.Round(stroke[2].Cx, 3), Math.Round(stroke[2].Cy, 3), stroke[2].Rx, stroke[2].Ry));
        double[][] innerSizes = [[63, 36], [54, 32], [36, 63], [18, 32], [162, 29], [77, 23], [67, 29], [23, 38], [47, 43]];
        for (var i = 0; i < 9; i++) Assert.Equal(innerSizes[i], new[] { inner[i].Rx, inner[i].Ry });
        Assert.Equal((28.0, 28.0), (f.BandV, f.BandH));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbRot Small fitted, light, ocean: Se's border and inner blobs, no bands, the w2 window, inset 5 px
    public void Small_uses_its_own_blobs_and_window()
    {
        var f = Build(BorderBeamOptions.Default.WithType(BeamType.Small) with { Palette = BeamPalette.Ocean }, dark: false);
        var stroke = Layer(f, BeamLayer.Stroke);
        var inner = Layer(f, BeamLayer.Inner);
        Assert.Equal(8, stroke.Count); Assert.Equal(8, inner.Count);
        Assert.Equal((4.79, 35.36, 30.8, 26.0), (Math.Round(stroke[0].Cx, 3), Math.Round(stroke[0].Cy, 3), Math.Round(stroke[0].Rx, 2), Math.Round(stroke[0].Ry, 2)));
        Assert.Equal((590.4, -1.18, 691.14, 9.87), (Math.Round(stroke[2].Cx, 3), Math.Round(stroke[2].Cy, 3), Math.Round(stroke[2].Rx, 2), Math.Round(stroke[2].Ry, 2)));
        Assert.Equal((606.8, 52.0, 492.0, 7.67), (Math.Round(inner[3].Cx, 3), Math.Round(inner[3].Cy, 3), Math.Round(inner[3].Rx, 2), Math.Round(inner[3].Ry, 2)));
        Assert.Equal(0.35, inner[3].A, 6);
        Assert.False(f.Bands);
        Assert.True(f.InnerWindowW2);
        Assert.Equal(2.5, f.ShadowSigma);
        Assert.Equal(new CssColor(0, 0, 0, 1), f.HighlightColor);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbSpikes fitted (820 x 52): twelve spikes at the search bar's spacing, x0 36.25 px, step 63.44 px
    public void Line_lays_its_spikes_across_the_composer()
    {
        double[] xs = [36.25, 99.69, 163.13, 226.57, 290.01, 353.45, 416.89, 480.33, 543.77, 607.21, 670.65, 734.09];
        double[] lifts = [2.48, 4.95, 3.71, 2.48, 4.95, 2.48, 3.71, 2.48, 4.95, 3.71, 2.48, 4.95];
        var f = Build(BorderBeamOptions.Default.WithType(BeamType.Line));
        Assert.Equal(1, f.Mode);
        var bloom = Layer(f, BeamLayer.Bloom);
        Assert.Equal(12 + 2, bloom.Count);                       // the spikes, then the two glows under them
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(xs[i], bloom[i].Cx, 4);               // the shader's floats
            Assert.Equal(52 - lifts[i], bloom[i].Cy, 4);
        }
        Assert.Equal(9 + 1, Layer(f, BeamLayer.Stroke).Count);   // the highlight over Ve's nine
        Assert.Equal(9, Layer(f, BeamLayer.Inner).Count);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // the "As 1.4.1 ships" defect: a double-comma colour voids Line's inner and bloom and Small's inner (border-beam.md 9)
    public void Faithful_drops_the_layers_the_package_loses()
    {
        var line = Build(BorderBeamOptions.Default.WithType(BeamType.Line) with { Palette = BeamPalette.Forest, Faithful = true });
        Assert.Empty(Layer(line, BeamLayer.Inner));
        Assert.Empty(Layer(line, BeamLayer.Bloom));
        Assert.NotEmpty(Layer(line, BeamLayer.Stroke));
        var fixedLine = Build(BorderBeamOptions.Default.WithType(BeamType.Line) with { Palette = BeamPalette.Forest });
        Assert.Equal(9, Layer(fixedLine, BeamLayer.Inner).Count);
        var small = Build(BorderBeamOptions.Default.WithType(BeamType.Small) with { Palette = BeamPalette.Gold, Faithful = true });
        Assert.Empty(Layer(small, BeamLayer.Inner));
        Assert.Equal(8, Layer(small, BeamLayer.Stroke).Count);
        var ocean = Build(BorderBeamOptions.Default.WithType(BeamType.Small) with { Palette = BeamPalette.Ocean, Faithful = true });
        Assert.Equal(8, Layer(ocean, BeamLayer.Inner).Count);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbPulseInner / bbPulseOutside: their layers and the blob alpha 1 - op / 2 (ge)
    public void The_pulses_build_their_layers()
    {
        var pi = Build(BorderBeamOptions.Default.WithType(BeamType.PulseInner));
        Assert.Equal(2, pi.Mode);
        Assert.Equal(9, Layer(pi, BeamLayer.Stroke).Count);
        Assert.Equal(9 + 4, Layer(pi, BeamLayer.Inner).Count);
        var bloom = Layer(pi, BeamLayer.Bloom);
        Assert.Equal(7, bloom.Count);
        Assert.Equal(22, pi.RingRadius);
        var po = Build(BorderBeamOptions.Default.WithType(BeamType.PulseOutside));
        Assert.Equal(3, po.Mode);
        Assert.Equal(8, Layer(po, BeamLayer.Stroke).Count);
        Assert.Equal(8, Layer(po, BeamLayer.OuterCore).Count);
        Assert.Equal(7, Layer(po, BeamLayer.OuterBloom).Count);
        Assert.Empty(Layer(po, BeamLayer.Inner));
        // the blurred blobs keep their volume: amplitude times area is the alpha (.77 dark) times the unblurred area
        var (rx, ry, amp) = BorderBeamScene.Blurred(30, 10, 3);
        Assert.Equal(30 * 10, rx * ry * amp, 9);
        Assert.True(rx > 30 && ry > 10);
    }
}
