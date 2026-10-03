using System.Text.Json;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Chrome;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// ED-252 review round 6: what the slider itself hands the knob and its track, read from the slider (not from hand-built
// copies of its constants), with the expected values taken from OD-0090's locked page
// (E:/Droppy/work/design/jewel-knob-locked/: jewel-knob.html, knob-orbs-1.js, strike-engine.js, run by node through
// tests/fixtures/jewel-knob/expected.mjs where they compute), or from EffortSlider.swift where the page keeps the source's.
[Collection("Headless")]
public sealed class EffortSliderJewelWiringTests(HeadlessSession headless)
{
    private static JsonElement Slider => JewelFixtures.Expected.GetProperty("slider");

    private static void AssertRun(JewelRig rig, IEnumerable<JsonElement> expected)
    {
        foreach (var e in expected)
        {
            rig.Frame();
            var t = e.GetProperty("t").GetDouble();
            Assert.True(Math.Abs(rig.Slider.KnobX - e.GetProperty("x").GetDouble()) < 1e-3, $"x at {t}: page {e.GetProperty("x")}, port {rig.Slider.KnobX}");
            Assert.True(Math.Abs(rig.Slider.KnobScale - e.GetProperty("scale").GetDouble()) < 1e-5, $"scale at {t}: page {e.GetProperty("scale")}, port {rig.Slider.KnobScale}");
            Assert.Equal(e.GetProperty("index").GetInt32(), rig.Index.Value);
        }
    }

    private static void AssertColour(SKColor expected, float[] values, int at, string what)
    {
        var (r, g, b) = (values[at] * 255, values[at + 1] * 255, values[at + 2] * 255);
        Assert.True(Math.Abs(r - expected.Red) < 1.01 && Math.Abs(g - expected.Green) < 1.01 && Math.Abs(b - expected.Blue) < 1.01,
            $"{what}: expected {expected}, the slider handed {r:F1},{g:F1},{b:F1}");
    }

    [Theory]
    [Trait("Decision", "ED-252")] // jewel-knob.html:23/:37 --scenePane/--sceneEmpty, :126 .thru.r opacity var(--ring, .26); knob-orbs-1.js:954 below 128 device px the orb renders at 2x; the deeper shadow follows the lift (:118, stepSlider :588)
    [InlineData("Dark")]
    [InlineData("Light")]
    public void The_slider_hands_the_knob_the_pages_layers(string variant) => headless.OnUiThread(() =>
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var rig = JewelFixtures.Rig(index: 1, fast: true);
        rig.Window.RequestedThemeVariant = theme;
        rig.Frame(); rig.Frame();
        Assert.Equal(theme, rig.Slider.ActualThemeVariant);
        var v = rig.Slider.Uniforms.Values;
        Assert.Equal(48f, v[3]);                       // the knob's CSS size
        Assert.Equal(0.26f, v[23]);                    // the ring's opacity
        Assert.Equal((float)rig.Slider.KnobX, v[76]);  // the knob's centre on the track, the track's width and height
        Assert.Equal(298f, v[77]);
        Assert.Equal(34f, v[78]);
        var dark = theme == ThemeVariant.Dark;
        AssertColour(dark ? new SKColor(0x14, 0x1d, 0x27) : new SKColor(0xe6, 0xeb, 0xf1), v, 80, "the pane (--scenePane)");
        AssertColour(dark ? new SKColor(0x11, 0x18, 0x20) : new SKColor(0xd1, 0xd8, 0xde), v, 84, "the empty tube (--sceneEmpty)");
        // headless the display scale is 1: the 48 px knob is 48 device px, below 128, so it renders at 2x
        Assert.Equal(1, Avalonia.Controls.TopLevel.GetTopLevel(rig.Slider)!.RenderScaling);
        Assert.Equal(2f, v[110]);
        // the fill the ring refracts runs the liquid's whole length: its first stop at the start, its last at the end
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e => e.Opacity >= 1);
        var stops = LiquidSpec.For(effect.Look).Stops();
        Color first = stops[0].Color.Resolve(theme), last = stops[^1].Color.Resolve(theme);
        AssertColour(new SKColor(first.R, first.G, first.B), v, 88, "the fill at its start");
        AssertColour(new SKColor(last.R, last.G, last.B), v, 104, "the fill at its end");
        // held, the knob lifts and its deeper shadow comes with it
        Assert.Equal(0f, rig.Slider.Uniforms.Lift);
        rig.Down(rig.Slider.KnobX);
        for (var i = 0; i < 6; i++) rig.Frame();
        Assert.True(rig.Slider.LiftAmount > 0.3, $"lifted {rig.Slider.LiftAmount}");
        Assert.Equal((float)rig.Slider.LiftAmount, rig.Slider.Uniforms.Lift);
        rig.Up(rig.Slider.KnobX);
    });

    [Theory]
    [Trait("Decision", "ED-252")] // jewel-knob.html:21/:35 --knobShadow (0 2px 6px rgba(0,0,0,.24) dark, rgba(20,32,50,.2) light), :118 .kw>s.lift (0 7px 18px rgba(0,0,0,.34), 0 2px 5px rgba(0,0,0,.2)) at the lift's opacity; CSS: a blur radius b is a Gaussian of sigma b / 2, drawn outside the knob's circle
    [InlineData(true, 1.0)]
    [InlineData(false, 1.0)]
    [InlineData(true, 0.75)]
    public void The_knobs_shadows_are_the_pages(bool dark, double lift)
    {
        var u = new JewelUniforms();
        u.Values[21] = dark ? 1 : 0;
        u.Lift = (float)lift;
        var box = new SKRect(36, 30, 84, 78);
        using var port = new SKBitmap(new SKImageInfo(120, 130, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(port))
        {
            canvas.Clear(SKColors.Transparent);
            JewelKnobRenderer.For(null, 96).Shadows(canvas, u.Values, box, 1);
        }
        using var page = new SKBitmap(new SKImageInfo(120, 130, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(page))
        using (var circle = new SKPath())
        {
            canvas.Clear(SKColors.Transparent);
            circle.AddOval(box);
            canvas.Save();
            canvas.ClipPath(circle, SKClipOperation.Difference, true);
            void Shadow(double y, double blur, SKColor colour, double alpha)
            {
                using var paint = new SKPaint { IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(blur / 2)), Color = colour.WithAlpha((byte)Math.Round(alpha * 255)) };
                canvas.DrawCircle(box.MidX, box.MidY + (float)y, box.Width / 2, paint);
            }
            // the knob's own (on .kw), then the lift's (s.lift, z-index -1 inside .kw's stacking context: over it), its
            // later shadow first (a box-shadow list paints its first shadow on top)
            if (dark) Shadow(2, 6, new SKColor(0, 0, 0), 0.24); else Shadow(2, 6, new SKColor(20, 32, 50), 0.2);
            Shadow(2, 5, new SKColor(0, 0, 0), 0.2 * lift);
            Shadow(7, 18, new SKColor(0, 0, 0), 0.34 * lift);
            canvas.Restore();
        }
        var differ = 0;
        for (var y = 0; y < port.Height; y++)
            for (var x = 0; x < port.Width; x++)
                if (port.GetPixel(x, y) != page.GetPixel(x, y)) differ++;
        Assert.True(differ == 0, $"{differ} pixels differ from the page's shadows");
    }

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:584 stepSlider: x = min(W - kn/2 + 3, max(kn/2 - 3, target + off)): the near end too
    public void The_knob_may_overshoot_the_start_by_three_pixels() => headless.OnUiThread(() =>
    {
        var run = Slider.GetProperty("nearEnd").EnumerateArray().ToArray();
        Assert.Equal(21, run.Min(e => e.GetProperty("x").GetDouble()), 9); // the page's knob is held 3 px past the first stop
        using var rig = JewelFixtures.Rig(width: 600, index: 3);
        rig.Frame();
        rig.Down(2); // on the track at its start: the target clamps to the first stop, 24
        var least = double.MaxValue;
        foreach (var e in run)
        {
            rig.Frame();
            least = Math.Min(least, rig.Slider.KnobX);
            Assert.True(Math.Abs(rig.Slider.KnobX - e.GetProperty("x").GetDouble()) < 1e-3, $"x at {e.GetProperty("t")}: page {e.GetProperty("x")}, port {rig.Slider.KnobX}");
        }
        Assert.Equal(21, least, 9);
        rig.Up(2);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:541 onKnob = |px - x| <= kn / 2: pressed 23.5 px from the centre of the 48 px knob, it lifts where it is and keeps the grip
    public void A_press_inside_the_knobs_rim_lifts_it_where_it_is() => headless.OnUiThread(() =>
    {
        var run = Slider.GetProperty("knobEdge").EnumerateArray().ToArray();
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        rig.Down(47.5);
        AssertRun(rig, run.Take(20));
        Assert.Equal(24, rig.Slider.KnobX); // no move sideways
        rig.Move(150);
        AssertRun(rig, run.Skip(20));
        Assert.Equal(150 - 23.5, rig.Slider.KnobX, 9); // the grip kept
        rig.Up(150);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:553 lostpointercapture lets go as pointerup does (retarget to the stop, spring 0.32/0.78)
    public void Losing_the_pointer_lets_go_of_the_knob_as_a_release_does() => headless.OnUiThread(() =>
    {
        var run = Slider.GetProperty("trackPress").EnumerateArray().ToArray();
        using var rig = JewelFixtures.Rig();
        rig.Frame();
        IPointer? pointer = null;
        rig.Slider.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        rig.Down(200);
        AssertRun(rig, run.Take(36));
        Assert.True(rig.Slider.IsHeld);
        Assert.Same(rig.Slider, pointer!.Captured);
        pointer.Capture(null); // another element takes the pointer, or the window loses it
        Assert.False(rig.Slider.IsHeld);
        AssertRun(rig, run.Skip(36)); // the page's release, frame by frame
        Assert.Equal(24 + 500.0 / 3, rig.Slider.KnobX, 9);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: under reduce motion the fast look is the strike's still moment (ED-248), lit but not moving, so the knob asks for no frames
    public void Under_reduce_motion_a_lit_fast_knob_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 1, fast: true, reduceMotion: true);
        for (var i = 0; i < 4; i++) rig.Frame();
        Assert.True(rig.Slider.Signal.Flare > 0 || rig.Slider.Signal.Charge > 0, "the still moment is lit");
        Assert.Equal(0, rig.Frames.Pending);
        var phase = rig.Slider.Orb.Phase;
        rig.Frame(0.5);
        Assert.Equal(phase, rig.Slider.Orb.Phase);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js:56-66 frameGate after the knob's loop idled (reduce motion): the last drawn frame is long past, so a press is drawn at once, then the gate's cadence
    public void After_idling_a_press_is_drawn_at_once_and_the_cadence_resumes() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(reduceMotion: true);
        var refresh = 1.0 / 239;
        rig.Frame();
        rig.Down(200); // the knob runs on a 239 Hz display (its refresh measured), is released and settles
        for (var i = 0; i < 60; i++) rig.Frame(refresh);
        rig.Up(200);
        for (var i = 0; i < 2000 && rig.Frames.Pending > 0; i++) rig.Frame(refresh);
        Assert.Equal(0, rig.Frames.Pending); // under reduce motion, settled, it idles
        rig.Frame(0.02); // 20 ms with no frame asked for
        rig.Down(30);
        var steps = new List<int>();
        var last = rig.Slider.KnobX;
        for (var i = 1; i <= 40; i++)
        {
            rig.Frame(refresh);
            if (rig.Slider.KnobX != last) { steps.Add(i); last = rig.Slider.KnobX; }
        }
        // the first frame after the press is drawn (the last drawn is long past), then every 4th refresh, as at 239 Hz
        var gaps = steps.Zip(steps.Skip(1), (a, b) => b - a).Distinct().ToList();
        Assert.True(steps[0] == 1 && gaps.SequenceEqual([4]), $"steps at {string.Join(",", steps)}");
        rig.Up(30);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:770 .fill(stopX <= x ? look.stopColor : Chrome.overlay(0.32)): the stop the knob rests on is passed
    public void The_stop_under_the_knob_takes_the_passed_colour() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 1);
        rig.Frame();
        var theme = rig.Slider.ActualThemeVariant;
        var dots = rig.Slider.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Where(e => e.Width == 5).ToList();
        Assert.Equal(4, dots.Count);
        var passed = new TrackLook(TrackLook.Kind.Plain, EffortBrand.Claude).StopColor.Resolve(theme);
        var ahead = Chrome.Overlay(0.32).Resolve(theme);
        Assert.Equal(new[] { passed, passed, ahead, ahead }, dots.Select(d => ((ISolidColorBrush)d.Fill!).Color).ToArray());
    });

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js:973-988 laneAt: each lane keeps its last four rises, so a look back into them (a contact between two frames) does not start the lane again
    public void A_look_back_into_a_lanes_last_four_rises_does_not_start_it_again()
    {
        var lanes = new BubbleLanes(13, 298, 34);
        var seen = new List<(int Lane, double T, int K, double Progress)>();
        for (var step = 0; step <= 12000; step++)
        {
            var t = step / 100.0;
            for (var i = 0; i < lanes.Count; i++)
                if (lanes.At(i, t) is { } now) seen.Add((i, t, now.K, now.Progress));
        }
        Assert.Equal(0, lanes.Restarts);
        var looked = 0;
        for (var i = 0; i < lanes.Count; i++)
        {
            var newest = seen.Where(s => s.Lane == i).Max(s => s.K);
            foreach (var s in seen.Where(s => s.Lane == i && s.K >= newest - 4 && s.K < newest))
            {
                var again = lanes.At(i, s.T);
                Assert.True(again is { } a && a.K == s.K && a.Progress == s.Progress, $"lane {i} at {s.T}: rise {s.K}");
                looked++;
            }
        }
        Assert.True(looked > 100, $"looked back {looked} times");
        Assert.Equal(0, lanes.Restarts);
        _ = lanes.At(0, 0.5); // further back than its history: the lane starts again
        Assert.Equal(1, lanes.Restarts);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // jewel-knob.html:268 drawBub: below an ink of 0.01 the bubble keeps its own look; at or above it the pale-liquid look blends in by the ink
    public void A_bubble_takes_the_pale_liquid_look_from_an_ink_of_one_hundredth() => headless.OnUiThread(() =>
    {
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Silver));
        var lanes = new BubbleLanes(11, 298, 34);
        const double t = 4.2;
        SKBitmap Draw(double ink)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(298, 34));
            using (var context = bitmap.CreateDrawingContext())
                LiquidTrack.Draw(context, new Size(298, 34), 298, t, spec, LiquidTrack.GradientFor(spec, 298, ThemeVariant.Dark), lanes, false,
                    ThemeVariant.Dark, LiquidFlow.At(t, true), null, ink, null, null);
            using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
            return SKBitmap.Decode(stream);
        }
        using var none = Draw(0);
        using var below = Draw(0.009);
        using var faint = Draw(0.05);
        int Differ(SKBitmap a, SKBitmap b) { var n = 0; for (var y = 0; y < 34; y++) for (var x = 0; x < 298; x++) if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++; return n; }
        Assert.Equal(0, Differ(none, below));
        Assert.True(Differ(none, faint) > 0, "an ink of 0.05 blends the pale-liquid look into the bubbles");
    });
}
