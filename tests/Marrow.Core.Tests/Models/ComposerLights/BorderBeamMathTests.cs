using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Tests.Models.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the composer light's mathematics. Every expected value below was
// returned by the approved playground's own function under node (E:/Droppy/work/design/composer-lights/playground/src/
// beam.js and logic.js spliced as gen.py does, BBT from border-beam-tables.json), 2026-10-03; the function is named on
// each test. None comes from running the C#.
public sealed class BorderBeamMathTests
{
    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbFitK('md', {W: 820, H: 52})
    public void Fit_scale_maps_the_site_card_to_the_composer()
    {
        var k = BorderBeamMath.FitK(BeamType.Large, 820, 52);
        Assert.Equal((348, 122), (k.RefW, k.RefH));
        Assert.Equal(2.3563218390804597, k.Kx, 12);
        Assert.Equal(0.4262295081967213, k.Ky, 12);
        Assert.Equal(0.8079991022503509, k.Kd, 12);
        Assert.Equal(1.535031543350318, k.Ke, 12);
    }

    [Theory]
    [Trait("Decision", "ED-251")] // beam.js bbFitBlob over BBT.G.colorful.border with bbFitK('md', 820 x 52): x and y as the CSS it writes
    [InlineData(0, 270.6, -7.29, 164.94252873563218, 32.31996409001403)]      // "33%", "calc(0% - 7.29px)"
    [InlineData(1, 98.4, -4.93, 141.3793103448276, 28.27996857876228)]        // "12%", "calc(0% - 4.93px)"
    [InlineData(2, 11.22, 35.516, 61.40126173401272, 29.83606557377049)]      // "calc(0% + 11.22px)", "68.3%"
    [InlineData(3, 11.22, 35.516, 30.70063086700636, 15.6)]                   // the end's along-radius floor: .3 x 52
    [InlineData(4, 610.08, 52, 424.13793103448273, 25.855971272011228)]       // "74.4%", "calc(100%)"
    [InlineData(6, 769.98, 0, 174.367816091954, 25.855971272011228)]          // "93.9%", "calc(0%)"
    [InlineData(8, 820, 14.092, 79.82164025421655, 20.45901639344262)]        // "calc(100%)", "27.1%"
    public void Blobs_are_mapped_edge_by_edge(int index, double x, double y, double w, double h)
    {
        var k = BorderBeamMath.FitK(BeamType.Large, 820, 52);
        var b = BorderBeamTables.Shared.LargeBlobs["colorful"][index];
        var f = BorderBeamMath.FitBlob(b.X, b.Y, b.W, b.H, k, 820, 52);
        Assert.Equal(x, f.X(820), 9);
        Assert.Equal(y, f.Y(52), 9);
        Assert.Equal(w, f.W, 9);
        Assert.Equal(h, f.H, 9);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbFitBlob, Small's blobs on 820 x 52 (bbFitK('sm')): "calc(0% + 4.79px)" "68%", "72%" "calc(0% - 1.18px)"
    public void Small_blobs_map_with_their_own_reference()
    {
        var k = BorderBeamMath.FitK(BeamType.Small, 820, 52);
        var s = BorderBeamTables.Shared.SmallBorder["colorful"];
        var a = BorderBeamMath.FitBlob(s[0].X, s[0].Y, s[0].W, s[0].H, k, 820, 52);
        Assert.Equal(4.79, a.X(820), 9); Assert.Equal(0.68 * 52, a.Y(52), 9); Assert.Equal(30.80352484468527, a.W, 9); Assert.Equal(26, a.H, 9);
        var c = BorderBeamMath.FitBlob(s[2].X, s[2].Y, s[2].W, s[2].H, k, 820, 52);
        Assert.Equal(0.72 * 820, c.X(820), 9); Assert.Equal(-1.18, c.Y(52), 9); Assert.Equal(691.1428571428571, c.W, 9); Assert.Equal(9.866604503958174, c.H, 9);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbPerim(820, 52, 22): L and at(u), from the top centre clockwise
    public void The_outline_is_walked_evenly_from_the_top_centre()
    {
        Assert.Equal(1706.2300767579509, BorderBeamMath.PerimeterLength(820, 52, 22), 9);
        (double U, double X, double Y)[] expected =
        [
            (0, 410, 0), (0.1, 580.6230076757952, 0), (0.24, 816.2347781918436, 9.69175624655303), (0.25, 820, 26),
            (0.26, 816.2347781918436, 42.308243753446966), (0.5, 410, 52), (0.74, 3.7652218081564044, 42.308243753446924),
            (0.76, 3.7652218081563973, 9.691756246553085), (0.99, 392.9376992324205, 0), (-0.2, 68.75398464840987, 0), (1.3, 751.2460153515901, 52),
        ];
        foreach (var (u, x, y) in expected)
        {
            var at = BorderBeamMath.PerimeterAt(820, 52, 22, u);
            Assert.Equal(x, at.X, 9);
            Assert.Equal(y, at.Y, 9);
        }
        Assert.Equal(905.6637061435918, BorderBeamMath.PerimeterLength(348, 122, 20), 9); // bbPerimLen(348, 122, 20)
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbProf(BB_PROF.w1, f) and bbProf(BB_PROF.bld, f)
    public void Window_profiles_are_the_packages_stops()
    {
        double[] at = [0, 0.33, 0.4, 0.5, 0.86, 0.94, 1.2, -0.1];
        double[] w1 = [0, 0.05000000000000015, 0.22499999999999973, 0.8374999999999999, 0.3500000000000012, 0.033333333333333465, 0, 0.18333333333333335];
        for (var i = 0; i < at.Length; i++) Assert.Equal(w1[i], BorderBeamMath.Profile(BorderBeamMath.WindowW1, at[i]), 12);
        double[] bAt = [0.69, 0.7, 0.703, 0.71, 0.8];
        double[] bld = [0.45, 0.85, 0.85, 0.65, 0.014999999999999956];
        for (var i = 0; i < bAt.Length; i++) Assert.Equal(bld[i], BorderBeamMath.Profile(BorderBeamMath.BloomDark, bAt[i]), 12);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbEase, logic.js bez, beam.js bbSwing
    public void The_curves_are_the_playgrounds()
    {
        double[] x = [0, 0.1, 0.25, 0.5, 0.75, 0.9, 1];
        double[] ease = [1.7053022947736973e-13, 0.019722466475291932, 0.12916175579568281, 0.49999964237213135, 0.8708382442043172, 0.980277533524708, 0.9999999999998295];
        double[] bez = [2.682209611748452e-08, 0.03648654069698196, 0.14824842249910436, 0.6341142389639117, 0.9099950367607944, 0.9750928083389546, 0.9999999749660431];
        for (var i = 0; i < x.Length; i++)
        {
            Assert.Equal(ease[i], BorderBeamMath.EaseInOut(x[i]), 12);
            Assert.Equal(bez[i], BorderBeamMath.Speen(x[i]), 12);
        }
        double[] ph = [0, 0.1, 0.25, 0.5, 0.6, 0.75, 1.3];
        double[] swing = [-0.9999999999996589, -0.8366800534670115, -7.152557373046875e-07, 0.9999999999996589, 0.8366800534670115, 7.152557373046875e-07, 0.3362327222749002];
        for (var i = 0; i < ph.Length; i++) Assert.Equal(swing[i], BorderBeamMath.Swing(ph[i]), 12);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbKf(BB_TRAVEL_X, f, false), bbKf(BB_BREATHE, f, true), bbLinePeriods(3.6925)
    public void Line_keyframes_and_periods()
    {
        double[] f = [0, 0.05, 0.55, 0.97];
        double[] x = [0.06, 0.105, 0.53, 0.9129999999999999];
        for (var i = 0; i < f.Length; i++) Assert.Equal(x[i], BorderBeamMath.Keyframes(BorderBeamMath.TravelX, f[i], false), 12);
        double[] g = [0, 0.1, 0.3, 0.9];
        double[] breathe = [0.8000000000000768, 0.9493476374881475, 1.227547661841, 1.0500001788139344];
        for (var i = 0; i < g.Length; i++) Assert.Equal(breathe[i], BorderBeamMath.Keyframes(BorderBeamMath.Breathe, g[i], true), 12);
        Assert.Equal((4.8, 4.9, 6.3), BorderBeamMath.LinePeriods(3.6925));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbOsc('pulse-inner', 'dark') and bbOscAt over BB_OSC['pulse-outside/light'] at tau .7
    public void Pulse_oscillators()
    {
        var osc = BorderBeamMath.Oscillators(BeamType.PulseInner, true);
        Assert.Equal(17, osc.Length);
        Assert.Equal([0.72, 1.308, 2.3400000000000003, 0], osc[0]);
        Assert.Equal([-33, 29.7, 3.04, 0], osc[2]);
        Assert.Equal([0.6599999999999999, 1.34, 2.4, 0], osc[12]);
        Assert.Equal([0.52, 1, 3.002, 1.577], osc[16]);
        var light = BorderBeamMath.Oscillators(BeamType.PulseOutside, false);
        double[] at07 = [0.8339898085129842, 1.2375751805708768, -14.243466105740298, 7.320701385355461, 1.2419268822194554, 0.9182801034047912, 12.077793331841042, -15.969622939728069, 0.922401544362373, 1.1993490565718923, -6.571979203648149, -12.227233102964119, 0.7670166537012777, 1, 1, 1, 1];
        for (var i = 0; i < 17; i++) Assert.Equal(at07[i], BorderBeamMath.OscillatorAt(light[i], 0.7), 12);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // beam.js bbR, bbFitDur(820 x 52 and 640 x 44, r 22), logic.js beamDur(BEAM_DEFAULTS) and mixHex
    public void Radii_durations_and_mixes()
    {
        Assert.Equal(8, BorderBeamMath.GlowRadius(8, 1));
        Assert.Equal(0.75, BorderBeamMath.GlowRadius(3, 0.25));
        Assert.Equal(29.25, BorderBeamMath.GlowRadius(22.5, 1.3));
        Assert.Equal(0.5, BorderBeamMath.GlowRadius(6, 0.05));
        Assert.Equal(20, BorderBeamMath.GlowRadius(15, 1.333));
        Assert.Equal(1.883955451878769, BorderBeamMath.FitDurationScale(BeamType.Large, 820, 52, 22), 12);
        Assert.Equal(9.421618886725877, BorderBeamMath.FitDurationScale(BeamType.Small, 820, 52, 22), 12);
        Assert.Equal(1.8095838587641866, BorderBeamMath.FitDurationScale(BeamType.Line, 820, 52, 22), 12);
        Assert.Equal(1, BorderBeamMath.FitDurationScale(BeamType.PulseInner, 820, 52, 22));
        Assert.Equal(1.4687903111655054, BorderBeamMath.FitDurationScale(BeamType.Large, 640, 44, 22), 12);
        Assert.Equal(1.669150521609538, BorderBeamMath.FitDurationScale(BeamType.Line, 640, 44, 22), 12);
        // OD-0089: "Duration Auto: 3.69 s a turn"
        Assert.Equal(3.692552685682387, BorderBeamMath.Duration(BorderBeamOptions.Default, 820, 52, 22), 12);
        Assert.Equal("#b6aaff", BorderBeamMath.MixHex("#8E7CFF", "#ffffff", 0.35));
        Assert.Equal("#9ccafd", BorderBeamMath.MixHex("#4B9FFC", "#ffffff", 0.45));
        Assert.Equal("#99c2e6", BorderBeamMath.MixHex("#0067C0", "#ffffff", 0.6));
    }
}
