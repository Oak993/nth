using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// DEVIATION OD-0073 (owner ruling 2026-10-02, "My choice"; ED-248): fast mode's look is variant A2 "Strike" of the locked
// canvas E:\Droppy\work\design\fast-mode-strike-A2-locked.dc.html (sha256 3f8ad01a...7a4408; the board's key 'B',
// strike()/strikeAt()), drawn over the model's own liquid at every effort, replacing the source's speed streaks and bolts
// (EffortSlider.swift:1151-1201). Spec: E:\Droppy\work\design\strike\SPEC.md. Expected values come from the canvas file:
// its constants, and its own strike() run against a recording context (tests/fixtures/strike/canvas-log.json, written by
// canvas-log.mjs from the locked file), never from running the port.
[Collection("Headless")]
public sealed class EffortSliderStrikeTests(HeadlessSession headless)
{
    private static readonly StrikeGeometry PortTrack = new(260, 34, 260 - 21, 21);

    private static void Time(TrackEffect effect, double seconds) =>
        typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(effect, seconds);

    private static TrackEffect Effect(TrackLook look, double width = 260, double height = 34)
    {
        var effect = new TrackEffect(look) { TrackWidth = width };
        effect.Measure(new Size(width, height)); effect.Arrange(new Rect(0, 0, width, height));
        return effect;
    }

    private static SKBitmap Frame(TrackEffect effect, double seconds)
    {
        Time(effect, seconds);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)effect.Bounds.Width, (int)effect.Bounds.Height));
        using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        return StrikeFixtures.Decode(bitmap);
    }

    private static int MaxDelta(SKBitmap a, SKBitmap b, int fromX, int toX, int fromY, int toY)
    {
        var max = 0;
        for (var y = fromY; y < toY; y++)
            for (var x = fromX; x < toX; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                max = Math.Max(max, Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue))));
            }
        return max;
    }

    // ---- the canvas's maths, bit for bit

    [Fact]
    [Trait("Decision", "ED-248")] // canvas hash()/vn()/fbm(); values from the locked file's own functions (node, 2026-10-02)
    public void Hash_and_noise_are_the_canvas_functions_bit_for_bit()
    {
        Assert.Equal(0.99652904551476240, Strike.Hash(0, 0), 15);
        Assert.Equal(0.55606216215528548, Strike.Hash(1, 503), 15);
        Assert.Equal(0.67265166202560067, Strike.Hash(-1, 7), 15);
        Assert.Equal(0.70279381610453129, Strike.Hash(123456, -9), 15);
        Assert.Equal(0.17984551447443664, Strike.Hash(int.MaxValue, int.MaxValue), 15);
        Assert.Equal(0.20072261127643287, Strike.Hash(int.MinValue, 5), 15);
        Assert.Equal(0.58242057543247938, Strike.Hash(7, 0x632be5ab), 15);
        Assert.Equal(-0.011815359815955162, Strike.Noise(0.5, 3), 14);
        Assert.Equal(0.84606289124861367, Strike.Noise(-3.7, 11), 14);
        Assert.Equal(0.44734668804449029, Strike.Noise(12.25, -4), 14);
        Assert.Equal(-0.35193490872087418, Strike.Fbm(100, 18426), 14);
        Assert.Equal(-0.12649464027155211, Strike.Fbm(-55.5, 3), 14);
        Assert.Equal(-0.39232117395395677, Strike.Fbm(400.125, 9999), 14);
    }

    [Theory]
    [Trait("Decision", "ED-248")] // strikeAt(k) = 2.4k + 0.2 + 0.5 hash(k, S+5); triple = hash(k, S+9) > .45; spark = hash(k, S+3) < .5 at +1.1 + .4 hash(k, S+2)
    [InlineData(2, 498, 5.1285594977671281, true, null)]                  // reference tube dark-claude, f024 (meta.json: 1.4736 + off 3.655)
    [InlineData(3, 498, 7.6810930359410117, true, 8.9134691842133176)]    // f177, spark f251
    [InlineData(2, 595, 5.2705632051220164, false, 6.6471586670493705)]   // dark-deepseek f024 (double), spark f107
    [InlineData(3, 595, 7.7942430632887403, false, null)]
    [InlineData(3, 692, 7.5802876522997389, true, 8.8202089647063993)]    // light-claude f024, spark f098
    [InlineData(4, 692, 10.297886664816177, true, 11.534961199597454)]
    [InlineData(3, 789, 7.7655805472517381, true, null)]                  // light-gemini f024
    [InlineData(4, 789, 10.285103288781828, false, 11.594395290897227)]   // f175 (double), spark f254
    public void Strikes_start_at_the_canvas_times_and_flicker_two_or_three_times(int k, int seed, double at, bool triple, double? spark)
    {
        Assert.Equal(at, Strike.StrikeAt(k, seed), 12);
        Assert.Equal(triple, Strike.Triple(k, seed));
        if (spark is { } s) Assert.Equal(s, Strike.SparkAt(k, seed)!.Value, 12);
        else Assert.Null(Strike.SparkAt(k, seed));
        // the slots: 2.4 s apart, each onset jittered within [0.2, 0.7)
        Assert.InRange(Strike.StrikeAt(k, seed) - k * Strike.Period, 0.2, 0.7);
    }

    [Fact]
    [Trait("Decision", "ED-248")] // I = min(1.1, pulse(0,.05,1) + pulse(.075,.04,.6) + [triple] pulse(.15,.055,.85)); ghost = e^(-a/.5)
    public void Brightness_follows_three_decaying_pulses_and_the_flash_ends_on_time()
    {
        Assert.Equal(1.0, Strike.Intensity(0, true), 12);
        Assert.Equal(Math.Exp(-0.074 / 0.05), Strike.Intensity(0.074, false), 12);
        Assert.Equal(0.6 + Math.Exp(-1.5), Strike.Intensity(0.075, true), 12);                   // .823
        Assert.Equal(0.823, Strike.Intensity(0.075, false), 3);
        Assert.Equal(0.85 + Math.Exp(-3) + 0.6 * Math.Exp(-0.075 / 0.04), Strike.Intensity(0.15, true), 12); // .992
        Assert.Equal(0.992, Strike.Intensity(0.15, true), 3);
        Assert.Equal(0.142, Strike.Intensity(0.15, false), 3);                                     // a double strike has only the residue
        Assert.Equal(0.0, Strike.Intensity(-0.001, true));
        // the flash is over (I <= .01) at about 266 ms (two flickers) and 398 ms (three)
        static double End(bool triple) { var a = 0.15; while (Strike.Intensity(a, triple) > 0.01) a += 0.0001; return a; }
        Assert.InRange(End(false), 0.265, 0.267);
        Assert.InRange(End(true), 0.397, 0.399);
        Assert.Equal(0.368, Strike.Ghost(0.5), 3);
        Assert.Equal(0.135, Strike.Ghost(1.0), 3);
        Assert.Equal(0.027, Strike.Ghost(1.8), 3);
        Assert.Equal(0.006, Math.Round(Strike.Ghost(Strike.Window) * 0.22, 3));                     // the window cuts it at 1.8 s
        Assert.Equal(2.4, Strike.Period);
        Assert.Equal(0.14, Strike.Lead);
        Assert.Equal(1.8, Strike.Window);
        Assert.Equal(0.12 + 0.04 * Math.Sin(2.6 * 3), Strike.Ambient(3), 12);
    }

    [Fact]
    [Trait("Decision", "ED-248")] // the leader: 7 steps of 20 ms from -140 ms, to 90 % of the channel; glow .32 width 4k; tip wash r .7h, a .2, sx 1.4
    public void The_leader_steps_out_of_the_knob_in_seven_twenty_millisecond_steps()
    {
        for (var step = 0; step < 7; step++)
        {
            var a = -0.14 + step * 0.02 + 0.01;
            Assert.Equal((int)Math.Floor((step + 1) / 7.0 * 50 * 0.9 + 0.5), Strike.LeaderTip(a, 50));
        }
        Assert.Equal(45, Strike.LeaderTip(-0.0001, 50));
        var channels = new StrikeChannels(13);
        var at = Strike.StrikeAt(1, 13);
        var ops = channels.Scene(PortTrack, at - 0.07, StrikeTint.Claude);
        var main = channels.Main(PortTrack, 1);
        var last = main.Length / 2 - 1;
        var tip = Strike.LeaderTip(-0.07, last);
        // three glow strokes from the knob to the tip, the tip's wash, then the ambient wash: no flash, no ghost
        Assert.Equal([StrikeOpKind.Stroke, StrikeOpKind.Stroke, StrikeOpKind.Stroke, StrikeOpKind.Wash, StrikeOpKind.Wash], ops.Select(o => o.Kind));
        Assert.Equal(4 * PortTrack.K, ops[0].Width, 12);
        Assert.Equal(0.045, ops[0].Alpha, 12); Assert.Equal(0.115, ops[1].Alpha, 12); Assert.Equal(0.294, ops[2].Alpha, 12);
        Assert.Equal((tip + 1) * 2, ops[0].Points.Length);
        Assert.Equal(PortTrack.Knob, ops[0].Points[0], 12);                                        // it leaves the knob
        Assert.Equal(main[tip * 2], ops[3].X, 12); Assert.Equal(main[tip * 2 + 1], ops[3].Y, 12);
        Assert.Equal(0.7 * 34 * 1.4, ops[3].Rx, 12); Assert.Equal(0.7 * 34, ops[3].Ry, 12); Assert.Equal(0.2, ops[3].Alpha, 12);
    }

    [Fact]
    [Trait("Decision", "ED-248")] // the whole scene against the locked canvas's own strike(), every logged frame of every tube
    public void Every_frame_draws_what_the_locked_canvas_draws()
    {
        var checkedOps = 0;
        foreach (var tube in StrikeFixtures.CanvasLog())
        {
            var channels = new StrikeChannels(tube.Seed);
            foreach (var frame in tube.Frames)
            {
                var ops = channels.Scene(tube.Geometry, frame.T, tube.Tint);
                var where = $"{tube.Name} frame {frame.Frame} t={frame.T}";
                Assert.True(frame.Ops.Count == ops.Count, $"{where}: {ops.Count} ops, canvas {frame.Ops.Count}");
                for (var i = 0; i < ops.Count; i++)
                {
                    var (port, canvas) = (ops[i], frame.Ops[i]);
                    if (canvas.Op == "wash")
                    {
                        Assert.True(port.Kind == StrikeOpKind.Wash, $"{where} op {i}: a wash");
                        Assert.True(Math.Abs(port.X - canvas.X) < 2e-4 && Math.Abs(port.Y - canvas.Y) < 2e-4, $"{where} op {i}: wash at {port.X},{port.Y} vs {canvas.X},{canvas.Y}");
                        Assert.True(Math.Abs(port.Rx - canvas.Rx) < 2e-4 && Math.Abs(port.Ry - canvas.Ry) < 2e-4, $"{where} op {i}: wash radii");
                        Assert.True(Math.Abs(Math.Min(1, port.Alpha) - canvas.Alpha) < 2e-4, $"{where} op {i}: wash alpha {port.Alpha} vs {canvas.Alpha}");
                        Assert.Equal(canvas.Color, $"rgba({port.Color.Red},{port.Color.Green},{port.Color.Blue},1)");
                    }
                    else
                    {
                        Assert.True(port.Kind == StrikeOpKind.Stroke, $"{where} op {i}: a stroke");
                        var (r, g, b, a) = StrikeFixtures.Rgba(canvas.Style!);
                        Assert.True(port.Color.Red == r && port.Color.Green == g && port.Color.Blue == b, $"{where} op {i}: colour {port.Color} vs {canvas.Style}");
                        Assert.True(Math.Abs(port.Alpha - a) < 1e-9, $"{where} op {i}: alpha {port.Alpha} vs {a}");
                        Assert.True(Math.Abs(port.Width - canvas.Width) < 2e-4, $"{where} op {i}: width {port.Width} vs {canvas.Width}");
                        Assert.True(port.Points.Length == canvas.Points.Length, $"{where} op {i}: {port.Points.Length / 2} points vs {canvas.Points.Length / 2}");
                        for (var p = 0; p < port.Points.Length; p++)
                            Assert.True(Math.Abs(port.Points[p] - canvas.Points[p]) < 2e-4, $"{where} op {i} point {p / 2}: {port.Points[p]} vs {canvas.Points[p]}");
                    }
                    checkedOps++;
                }
            }
        }
        Assert.True(checkedOps > 5000, $"ops compared: {checkedOps}");
    }

    [Fact]
    [Trait("Decision", "ED-248")] // the knob spark: about half the cycles, 1.1-1.5 s after a strike, never while a strike is under 0.5 s old, 1/9 s, fading (1-f)^2
    public void A_spark_lives_a_ninth_of_a_second_and_never_comes_while_a_strike_is_young()
    {
        var channels = new StrikeChannels(595);
        var geometry = new StrikeGeometry(300, 26, 287, 11);
        var spark = Strike.SparkAt(2, 595)!.Value;
        static bool IsSpark(StrikeOp o) => o.Kind == StrikeOpKind.Stroke && o.Points.Length == 8;
        var at = channels.Scene(geometry, spark + 0.05, StrikeTint.DeepSeek).Where(IsSpark).ToArray();
        Assert.Equal(3, at.Length);                                                                  // one glow: three strokes
        var f = 0.05 * 9;
        Assert.Equal(Math.Round(0.6 * (1 - f) * (1 - f) * 0.92, 3), at[2].Alpha, 9);
        Assert.Equal(287 - 0.85 * 11, at[0].Points[0], 9);                                           // it leaves from inside the knob
        Assert.DoesNotContain(channels.Scene(geometry, spark - 0.001, StrikeTint.DeepSeek), IsSpark);
        Assert.DoesNotContain(channels.Scene(geometry, spark + 1 / 9.0 + 0.001, StrikeTint.DeepSeek), IsSpark);
        // over 1000 s: sparks come in about half the cycles, at most one a cycle, never within 0.5 s after a strike
        var cycles = new HashSet<int>(); var frames = 0;
        for (var t = 0.0; t < 1000; t += 1 / 60.0)
        {
            var ops = channels.Scene(geometry, t, StrikeTint.DeepSeek);
            if (!ops.Any(IsSpark)) continue;
            frames++;
            var k0 = (int)Math.Floor(t / Strike.Period);
            for (var k = k0 - 1; k <= k0 + 1; k++) Assert.False(t - Strike.StrikeAt(k, 595) is >= -0.14 and < 0.5, $"a spark at {t} beside strike {k}");
            var owner = Enumerable.Range(k0 - 1, 2).Single(k => Strike.SparkAt(k, 595) is { } s && t >= s && t < s + 1 / 9.0);
            cycles.Add(owner);
        }
        Assert.InRange(cycles.Count, 416 * 0.35, 416 * 0.65);
        Assert.InRange(frames / (double)cycles.Count, 6, 7.01);                                       // 1/9 s at 60 Hz: 6 or 7 frames
    }

    [Fact]
    [Trait("Decision", "ED-248")] // each strike its own channel (seed S*37+k), the same strike always the same; built once, shimmer re-rolled at 30 Hz
    public void Each_strike_has_its_own_channel_built_once_and_the_shimmer_rerolls_thirty_times_a_second()
    {
        var a = new StrikeChannels(498); var b = new StrikeChannels(498); var other = new StrikeChannels(499);
        Assert.Equal(a.Main(PortTrack, 5), b.Main(PortTrack, 5));
        Assert.NotEqual(a.Main(PortTrack, 5), a.Main(PortTrack, 6));
        Assert.NotEqual(a.Main(PortTrack, 5), other.Main(PortTrack, 5));
        Assert.Equal(a.Branch(PortTrack, 5, 2), b.Branch(PortTrack, 5, 2));
        // the same moment draws the same, whatever came before
        var at = Strike.StrikeAt(7, 498);
        foreach (var t in new[] { 1.0, at + 0.01, at + 0.3, 500.0 }) a.Scene(PortTrack, t, StrikeTint.Claude);
        Assert.Equal(Points(new StrikeChannels(498).Scene(PortTrack, at + 0.02, StrikeTint.Claude)), Points(a.Scene(PortTrack, at + 0.02, StrikeTint.Claude)));
        // over one strike's whole life at 240 Hz its channel and branches are built once
        var fresh = new StrikeChannels(498);
        for (var t = at - 0.2; t < at + 1.9; t += 1 / 240.0) fresh.Scene(PortTrack, t, StrikeTint.Claude);
        Assert.InRange(fresh.ChannelsBuilt, 1, 3);                                                   // this strike, and its neighbours' windows
        // the jitter holds for a 30th of a second and then re-rolls; the ends never move
        var js = Math.Floor((at + 0.05) * 30);
        double t0 = js / 30 + 0.001, t1 = js / 30 + 0.03, t2 = (js + 1) / 30 + 0.001;
        double[] Main(double t) => fresh.Scene(PortTrack, t, StrikeTint.Claude).First(o => o.Kind == StrikeOpKind.Stroke && o.Width == 8 * PortTrack.K).Points;
        Assert.Equal(Main(t0), Main(t1));
        var rerolled = Main(t2);
        Assert.NotEqual(Main(t0), rerolled);
        Assert.Equal(Main(t0)[..2], rerolled[..2]);
        Assert.Equal(Main(t0)[^2..], rerolled[^2..]);
        var max = Main(t0).Zip(rerolled, (p, q) => Math.Abs(p - q)).Max();
        Assert.InRange(max, 0.01, 2 * 0.8 * PortTrack.K + 1e-9);                                     // +-0.8k each
    }

    private static double[][] Points(IReadOnlyList<StrikeOp> ops) => ops.Select(o => o.Kind == StrikeOpKind.Stroke ? o.Points : new[] { o.X, o.Y, o.Rx, o.Ry, o.Alpha }).ToArray();

    [Fact]
    [Trait("Decision", "ED-248")] // TINT: claude / deepseek / gemini, identical on dark and light; the core is white. DEVIATION OD-0090: the other brands no longer take claude's, they derive their own (EffortSliderJewelMaterialTests)
    public void Each_brand_strikes_in_its_canvas_tint_in_both_appearances()
    {
        static (SKColor, SKColor, SKColor) Colours(StrikeTint t) => (t.Outer, t.Inner, t.Wash);
        Assert.Equal((new SKColor(255, 146, 100), new SKColor(255, 222, 204), new SKColor(255, 168, 126)), Colours(StrikeTint.For(EffortBrand.Claude)));
        Assert.Equal((new SKColor(116, 142, 255), new SKColor(224, 231, 255), new SKColor(146, 168, 255)), Colours(StrikeTint.For(EffortBrand.Deepseek)));
        Assert.Equal((new SKColor(190, 160, 255), new SKColor(242, 234, 255), new SKColor(206, 184, 246)), Colours(StrikeTint.For(EffortBrand.Antigravity)));
        Assert.NotEqual(Colours(StrikeTint.Claude), Colours(StrikeTint.For(EffortBrand.Cursor)));
        var ops = new StrikeChannels(13).Scene(PortTrack, Strike.StrikeAt(1, 13) + 0.01, StrikeTint.Claude);
        Assert.Contains(ops, o => o.Kind == StrikeOpKind.Stroke && o.Color == SKColors.White);
    }

    // ---- pixels

    // The liquid without lightning, and with the scene at t: the inner flash at a point away from every stroke lifts the
    // liquid by exactly the screen blend of the washes (the canvas's mix-blend-mode: screen over the liquid).
    [Theory]
    [Trait("Decision", "ED-248")] // inner flash: wash at the knob, r 1.4h, sx 1.6, alpha I*.5, screen-blended; ambient .12 + .04 sin(2.6t)
    [InlineData(0.0)]
    [InlineData(0.075)]
    [InlineData(0.2)]
    public void The_inner_flash_lights_the_liquid_from_the_knob_by_the_screen_blend(double after) => headless.OnUiThread(() =>
    {
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude));
        var t = Strike.StrikeAt(1, 13) + after;
        var ops = new StrikeChannels(13).Scene(PortTrack, t, StrikeTint.Claude);
        var flow = LiquidFlow.At(t, true);
        using var plain = StrikeFixtures.Liquid(spec, new Size(260, 34), 260, t, flow, null);
        using var lit = StrikeFixtures.Liquid(spec, new Size(260, 34), 260, t, flow, ops);
        var strokes = ops.Where(o => o.Kind == StrikeOpKind.Stroke).ToArray();
        var flash = ops.First(o => o.Kind == StrikeOpKind.Wash);
        Assert.Equal(1.4 * 34 * 1.6, flash.Rx, 9);
        Assert.Equal(Strike.Intensity(after, true) * 0.5, flash.Alpha, 9);
        var tested = 0;
        // inside the tube, away from its rims; points 7+ px from every stroke (the widest halo reaches 3.4 px)
        for (var py = 2 * 5; py < 2 * 29; py += 3)
            for (var px = 2 * 120; px < 2 * 236; px += 7)
            {
                double x = (px + 0.5) / 2, y = (py + 0.5) / 2;
                if (strokes.Any(s => StrikeFixtures.Distance(x, y, s.Points) < 7)) continue;
                // the lightning is not bent by the glass (on the canvas it is a plain layer over the liquid)
                var expected = StrikeFixtures.Screen(plain.GetPixel(px, py), StrikeFixtures.Layer(ops, x, y, _ => 0), y, 34);
                var got = lit.GetPixel(px, py);
                Assert.True(Math.Abs(got.Red - expected.R) <= 4 && Math.Abs(got.Green - expected.G) <= 4 && Math.Abs(got.Blue - expected.B) <= 4,
                    $"({x},{y}) after {after}: {got} vs {expected} (under {plain.GetPixel(px, py)})");
                tested++;
            }
        Assert.True(tested > 10, $"points tested: {tested}");
        // clipped to the liquid: outside the capsule's corners by the knob nothing is drawn, flash or not
        foreach (var (cx, cy) in new[] { (519, 0), (519, 67), (514, 1), (514, 66) }) Assert.Equal(0, lit.GetPixel(cx, cy).Alpha);
        // and the flash is strongest at the knob: a point by the knob is lifted more than one 60 px further out
        var near = StrikeFixtures.Layer(ops, PortTrack.Knob - 20, 18.75, _ => 0).G;
        var far = StrikeFixtures.Layer(ops, PortTrack.Knob - 80, 18.75, _ => 0).G;
        Assert.True(near > far);
    });

    // The channel's brightest pixel at a main vertex (away from the branches) is the layer over it screen-blended: between
    // the 0.9 px white core seen at 60 % (antialiasing, and the glass's resampling, only spread a line that thin) and at
    // full coverage; near white at the strike and its flicker, a faint glow-out after.
    [Theory]
    [Trait("Decision", "ED-248")] // glow(a, wide): outer .14a at wide*k, inner .36a at 2.4k, white min(1,.92a) at max(.8,1.05k); ghost (4+9a)k .22g, 1.6k .22g
    [InlineData(0.0, 210)]   // floors only (near white); the expectation is the screen-blend range below
    [InlineData(0.075, 210)]
    [InlineData(0.6, 0)]
    public void The_channel_strikes_near_white_and_glows_out(double after, int atLeast) => headless.OnUiThread(() =>
    {
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude));
        var t = Strike.StrikeAt(1, 13) + after;
        var ops = new StrikeChannels(13).Scene(PortTrack, t, StrikeTint.Claude);
        var flow = LiquidFlow.At(t, true);
        using var plain = StrikeFixtures.Liquid(spec, new Size(260, 34), 260, t, flow, null);
        using var lit = StrikeFixtures.Liquid(spec, new Size(260, 34), 260, t, flow, ops);
        var main = after < 0.3
            ? ops.First(o => o.Kind == StrikeOpKind.Stroke && o.Width == 8 * PortTrack.K)
            : ops.First(o => o.Kind == StrikeOpKind.Stroke);                                         // the ghost (no flash left)
        var others = ops.Where(o => o.Kind == StrikeOpKind.Stroke && o.Points.Length != main.Points.Length).ToArray();
        var tested = 0;
        for (var i = 8; i < main.Points.Length / 2 - 8; i += 2)
        {
            double x = main.Points[i * 2], y = main.Points[i * 2 + 1];
            if (y < 0.3 * 34 || y > 0.7 * 34 || others.Any(o => StrikeFixtures.Distance(x, y, o.Points) < 9)) continue;
            int cx = (int)(x * 2), cy = (int)(y * 2);
            // the pixel the strokes lift most (the best covered one), whatever the liquid's own light does around it
            var brightest = SKColors.Black; var under = SKColors.Black; var lift = int.MinValue;
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    SKColor p = lit.GetPixel(cx + dx, cy + dy), q = plain.GetPixel(cx + dx, cy + dy);
                    var d = p.Red + p.Green + p.Blue - q.Red - q.Green - q.Blue;
                    if (d > lift) { lift = d; brightest = p; under = q; }
                }
            double Thin(StrikeOp o) => o.Points.Length != main.Points.Length ? 0 : o.Width < 2 ? 0.6 : 1;
            var high = StrikeFixtures.Screen(under, StrikeFixtures.Layer(ops, x, y, o => o.Points.Length == main.Points.Length ? 1 : 0), y, 34);
            var low = StrikeFixtures.Screen(under, StrikeFixtures.Layer(ops, x, y, Thin), y, 34);
            static bool Within(byte v, double lo, double hi) => v >= lo - 4 && v <= hi + 4;
            Assert.True(Within(brightest.Red, low.R, high.R) && Within(brightest.Green, low.G, high.G) && Within(brightest.Blue, low.B, high.B),
                $"vertex {i} ({x:F1},{y:F1}) after {after}: {brightest} vs {low} to {high}");
            Assert.True(Math.Min(brightest.Red, Math.Min(brightest.Green, brightest.Blue)) >= atLeast, $"vertex {i}: {brightest}");
            tested++;
        }
        Assert.True(tested >= 3, $"vertices tested: {tested}");
    });

    [Fact]
    [Trait("Decision", "ED-248")] // between strikes only the faint ambient pool at the knob; nothing anywhere else (the old streaks are gone)
    public void Between_strikes_only_the_ambient_light_shows_at_the_knob() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effect = Effect(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude));
        var seed = effect.StrikeSeed;
        // a gap of over 2.1 s: 1.85 s after its strike the glow-out is over and the next leader has not left the knob
        var k = Enumerable.Range(1, 50).First(k => Strike.StrikeAt(k + 1, seed) - Strike.StrikeAt(k, seed) > 2.1);
        var quiet = Strike.StrikeAt(k, seed) + 1.85;
        var ops = new StrikeChannels(seed).Scene(effect.StrikeGeometryFor(new Size(260, 34)), quiet, StrikeTint.Claude);
        var wash = Assert.Single(ops);
        Assert.Equal(StrikeOpKind.Wash, wash.Kind);
        Assert.Equal(Strike.Ambient(quiet), wash.Alpha, 12);
        Assert.Equal(0.9 * 34 * 1.2, wash.Rx, 9);
        using var initial=Frame(effect,0);
        var baseTime=effect.StrikeOffset; var starts=new StrikeStarts(seed); var windows=starts.Windows(baseTime,baseTime+60,true);
        var clean=windows.Items.Take(windows.Count).First(w=>w.To-w.From>0.02);
        var pageTime=(clean.From+clean.To)/2-baseTime; // K-162/K-165: the interior, never the inclusive visible-end boundary.
        using var drawn = Frame(effect, pageTime);
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude));
        // OD-0078 (required with OD-0090): the bubbles the last strikes touched have burst and are gone until their next rise
        var (_, _, popped) = StrikeFixtures.WithBursts(ops, effect.StrikeGeometryFor(new Size(260, 34)), seed, effect.Bubbles(260, 34).Seed, 260, quiet, StrikeTint.Claude);
        using var bare = StrikeFixtures.Liquid(spec, new Size(260, 34), 260, quiet, LiquidFlow.At(quiet, true), null, 1, effect.Bubbles(260, 34), null, null, popped);
        using var expected=MixTrackFixtures.Draw(effect.Look,effect.StrikeGeometryFor(new Size(260,34)),seed,effect.Bubbles(260,34).Seed,pageTime,baseTime,previousFrame:0);
        Assert.True(MaxDelta(drawn,expected,0,260,0,34)<=1); // SPEC K-111/K-127/B-08: independent page-clock pool, exact scene.
        var idle=new StrikeStorm(seed).Resolve(effect.StrikeGeometryFor(new Size(260,34)),pageTime+baseTime,pageTime,1,0);
        Assert.Equal(0,idle.Masks.Count); Assert.Equal(1,idle.LightCount); Assert.Equal(Strike.Ambient(pageTime),idle.Light[0].Alpha,12);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-136")] // EffortSlider.swift:1151-1201; DEVIATION OD-0073: Strike replaces the streaks and bolts at every effort
    [Trait("Req", "PR-COMPOSER-134")] // EffortSlider.swift:1099-1132, 1343-1346: the streak traits, their cache and their random go with them
    public void The_source_streaks_and_bolts_are_gone()
    {
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
        foreach (var name in new[] { "DrawStreaks", "DrawBolts", "EdgeFade", "Random" }) Assert.Null(typeof(TrackEffect).GetMethod(name, any));
        foreach (var name in new[] { "Streak", "Traits", "TraitCache" }) Assert.Null(typeof(TrackEffect).GetNestedType(name, any));
        Assert.Null(typeof(TrackEffect).GetField("traits", any));
    }

    [Theory]
    [Trait("Decision", "ED-248")] // OD-0073: Strike over the model's own liquid at every effort, solo and paired; the lead's tint at the knob
    [InlineData(TrackLook.Kind.Fast, false)]
    [InlineData(TrackLook.Kind.Fusion, false)]
    [InlineData(TrackLook.Kind.Fast, true)]
    [InlineData(TrackLook.Kind.Fusion, true)]
    public void Fast_mode_draws_the_strike_over_the_liquid_at_every_effort_and_for_pairs(TrackLook.Kind kind, bool paired) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var pair = paired ? new EffortPairBrand(new HydraPair(ProviderKind.Deepseek) { WorkerProvider = ProviderKind.Claude }) : null;
        var look = new TrackLook(kind, EffortBrand.Deepseek, pair);
        Assert.True(TrackEffect.IsLiquid(look));
        var effect = Effect(look);
        using var initial = Frame(effect,0);
        var t = MixTrackFixtures.NextStrikePageTime(effect);
        using var drawn = Frame(effect, t);
        var geometry = effect.StrikeGeometryFor(new Size(260, 34));
        // the knob's radius, and where the bolt leaves: OD-0090's page puts it 2 px before the knob's centre (236), on a
        // quarter pixel (jewel-knob.html:593 tl.exAt = Math.round((x - 2) * 4) / 4: 234)
        Assert.Equal(new StrikeGeometry(260, 34, 234, EffortSlider.ThumbSize / 2), geometry);
        // SPEC B-01..B-07/C-02 + OD-0093 supersede the A2 production scene. State/math are pinned separately by locked vectors.
        using var expected = MixTrackFixtures.Draw(look,geometry,effect.StrikeSeed,effect.Bubbles(260,34).Seed,t,effect.StrikeOffset,previousFrame:0);
        Assert.True(MaxDelta(drawn, expected, 0, 260, 0, 34) <= 1, "the track draws the scene of its own seed in the lead's tint");
        using var bare = MixTrackFixtures.Draw(look with {TrackLookKind=TrackLook.Kind.Plain},geometry,effect.StrikeSeed,effect.Bubbles(260,34).Seed,t,effect.StrikeOffset);
        Assert.True(MaxDelta(drawn, bare, 150, 238, 4, 30) > 60, "the strike shows");
    });

    [Fact]
    [Trait("Decision", "ED-248")] // the slider: fast at an effort below the top is the liquid with the strike, its fill the brand's (no amber under the liquid)
    public void Below_the_top_fast_mode_is_the_models_liquid() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Assert.True(TrackEffect.IsLiquid(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude)));
        Assert.True(TrackEffect.IsLiquid(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Claude))); // OD-0075: the liquid at every effort level
        Assert.True(TrackEffect.IsFast(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude)));
        Assert.True(TrackEffect.IsFast(new TrackLook(TrackLook.Kind.Fusion, EffortBrand.Claude)));
        Assert.False(TrackEffect.IsFast(new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Claude)));
        Assert.False(TrackEffect.IsFast(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Claude, new EffortPairBrand(new HydraPair(ProviderKind.Claude)))));
    });

    [Fact]
    [Trait("Decision", "ED-248")] // fast mode: currents 1.5x (7 to 4.67 s, 11 to 7.33 s), caustic 6 to 4 s; breathe and bubbles keep their pace
    public void Fast_mode_runs_the_currents_one_and_a_half_times_faster()
    {
        Assert.Equal(new LiquidFlow(10 / 7.0, 10 / 11.0, 10 / 6.0), LiquidFlow.At(10, false));
        Assert.Equal(new LiquidFlow(10 / 4.67, 10 / 7.33, 10 / 4.0), LiquidFlow.At(10, true));
        var (fast, slow, caustic) = LiquidTrack.Offsets(LiquidFlow.At(3.5, true), 300);
        Assert.Equal(3.5 % 4.67 / 4.67 * 300, fast, 6);
        Assert.Equal(3.5 % 7.33 / 7.33 * 300, slow, 6);
        Assert.Equal(300 * LiquidTrack.Alternate(3.5, 4), caustic, 6);
        Assert.Equal(LiquidTrack.Offsets(LiquidFlow.At(3.5, false), 300), (3.5 % 7 / 7 * 300, 3.5 % 11 / 11 * 300, 300 * LiquidTrack.Alternate(3.5, 6)));
        // a pair keeps its track when fast mode is toggled: the currents change pace without a jump
        var pace = new LiquidPace();
        Assert.Equal(LiquidFlow.At(2, false), pace.At(2, false));
        var before = pace.At(5, false);
        var after = pace.At(5, true);
        Assert.Equal(before.Fast, after.Fast, 12); Assert.Equal(before.Slow, after.Slow, 12); Assert.Equal(before.Caustic, after.Caustic, 12);
        Assert.Equal(1 / 4.67, pace.At(6, true).Fast - after.Fast, 12);
        Assert.Equal(1 / 7.33, pace.At(6, true).Slow - after.Slow, 12);
        Assert.Equal(1 / 4.0, pace.At(6, true).Caustic - after.Caustic, 12);
        var back = pace.At(8, false);
        Assert.Equal(after.Fast + 3 / 4.67, back.Fast, 12);                                          // it ran fast from 5 s to 8 s
        Assert.Equal(1 / 7.0, pace.At(9, false).Fast - back.Fast, 12);
    }

    [Fact]
    [Trait("Decision", "ED-248")] // reduce motion: the canvas's still, t = strikeAt(1) + 0.035 (I = .497), over the liquid with every animation stopped
    public void Reduce_motion_draws_one_still_frame_thirty_five_ms_into_the_first_strike() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var look = new TrackLook(TrackLook.Kind.Fusion, EffortBrand.Claude);
        var effect = Effect(look);
        EnvironmentValues.SetAccessibilityReduceMotion(effect, true);
        Assert.Equal(Strike.StrikeAt(1, effect.StrikeSeed) + 0.035, Strike.StillTime(effect.StrikeSeed), 12);
        Assert.Equal(0.497, Strike.Intensity(0.035, Strike.Triple(1, effect.StrikeSeed)), 3);
        using var first = Frame(effect, 0.4);
        using var later = Frame(effect, 7.9);
        Assert.Equal(first.Bytes, later.Bytes);
        var ops = new StrikeChannels(effect.StrikeSeed).Scene(effect.StrikeGeometryFor(new Size(260, 34)), Strike.StillTime(effect.StrikeSeed), StrikeTint.Claude);
        Assert.Equal(2 + 1 + 3 + 5 * 3 + 1, ops.Count);                                              // legacy pure maths retained unchanged
        var geometry=effect.StrikeGeometryFor(new Size(260,34));
        using var still = MixTrackFixtures.Draw(look,geometry,effect.StrikeSeed,effect.Bubbles(260,34).Seed,0,0,true);
        Assert.True(MaxDelta(first, still, 0, 260, 0, 34) <= 1, "the still: the liquid at rest, no bubbles, the strike 35 ms in");
    });

    // ---- the port's frames of the reference tubes (ED-248 evidence); every frame when MARROW_STRIKE_PORT_FRAMES names a folder
    // (and each without its lightning when MARROW_STRIKE_PORT_BARE_FRAMES names another)

    [Fact]
    [Trait("Decision", "ED-248")] // renders the four reference tubes at the reference strip's times (meta.json windows, 1/60 s)
    public void Port_frames_of_the_reference_tubes_render_at_the_strips_times() => headless.OnUiThread(() =>
    {
        var target = Environment.GetEnvironmentVariable("MARROW_STRIKE_PORT_FRAMES");
        var reference = Environment.GetEnvironmentVariable("MARROW_STRIKE_REFERENCE_FRAMES");
        var bare = Environment.GetEnvironmentVariable("MARROW_STRIKE_PORT_BARE_FRAMES");
        var all = !string.IsNullOrEmpty(target);
        var folder = all ? target! : Path.Combine(Path.GetTempPath(), "marrow-strike-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var tube in StrikeFixtures.CanvasLog().Where(t => t.Start > 0))
            {
                Directory.CreateDirectory(Path.Combine(folder, tube.Name));
                var pane = SKColors.Black;
                var first = reference is null ? null : Path.Combine(reference, tube.Name, "f000.png");
                if (first is not null && File.Exists(first)) { using var image = SKBitmap.Decode(first); pane = image.GetPixel(2, 2); }
                else pane = tube.Name.StartsWith("dark", StringComparison.Ordinal) ? new SKColor(20, 29, 39) : new SKColor(233, 238, 243);
                var liquidWidth = tube.Top ? tube.W : 0.76 * 520;
                var bubbles = new BubbleLanes(tube.Seed, tube.Name.Contains("520", StringComparison.Ordinal) ? 520 : 300, tube.H);
                foreach (var i in all ? Enumerable.Range(0, 288) : [0, 24, 25, 28, 30, 60])
                {
                    var t = tube.Start + tube.Off + i / 60.0;
                    using var frame = StrikeFixtures.PortFrame(tube, t, pane, tube.Name.StartsWith("dark", StringComparison.Ordinal), liquidWidth, bubbles);
                    Assert.Equal((tube.Name.Contains("520", StringComparison.Ordinal) ? 536 : 316) * 2, frame.Width);
                    Assert.Equal((int)(tube.H + 16) * 2, frame.Height);
                    using var data = frame.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(folder, tube.Name, $"f{i:000}.png"), data.ToArray());
                    if (string.IsNullOrEmpty(bare)) continue;
                    // the same frame without the lightning: the liquid alone, to measure the light the strike adds
                    Directory.CreateDirectory(Path.Combine(bare, tube.Name));
                    using var liquid = StrikeFixtures.PortFrame(tube, t, pane, tube.Name.StartsWith("dark", StringComparison.Ordinal), liquidWidth, bubbles, lightning: false);
                    using var liquidData = liquid.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(bare, tube.Name, $"f{i:000}.png"), liquidData.ToArray());
                }
            }
        }
        finally { if (!all) Directory.Delete(folder, true); }
    });
}
