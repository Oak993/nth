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
using Marrow.Core.Models;
using Marrow.Core.Port;
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
            // later shadow first (a box-shadow list paints its first shadow on top). s.lift's opacity is the element's: CSS
            // composites its two shadows together and then draws the group at the lift's opacity, lift (a over b), not
            // (lift a) over (lift b)
            if (dark) Shadow(2, 6, new SKColor(0, 0, 0), 0.24); else Shadow(2, 6, new SKColor(20, 32, 50), 0.2);
            using (var group = new SKPaint { Color = SKColors.Black.WithAlpha((byte)Math.Round(lift * 255)) })
            {
                canvas.SaveLayer(group);
                Shadow(2, 5, new SKColor(0, 0, 0), 0.2);
                Shadow(7, 18, new SKColor(0, 0, 0), 0.34);
                canvas.Restore();
            }
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
    [Trait("Req", "PR-COMPOSER-125")] // EffortSlider.swift:801-818,833-836 as amended by OD-0090 (grip, springs, release on a lost capture)
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
    [Trait("Decision", "ED-252")] // ED-252: the knob's signal is scaled by the fast effect's opacity (the page has no fast toggle; the port's crossfades the effect in over :799 .smooth(0.3))
    public void Turning_fast_mode_on_lights_the_knob_only_as_far_as_the_bolt_has_faded_in() => headless.OnUiThread(() =>
    {
        // under reduce motion the knob reads the strike's still moment, 35 ms into strike 1 (charge 1 and a flare), whatever
        // the effect's clock: so the frame inside the 0.3 s fade is lit for every seed
        using var rig = JewelFixtures.Rig(index: 1, reduceMotion: true);
        for (var i = 0; i < 4; i++) rig.Frame();
        Assert.Equal(0, rig.Slider.Signal.RawCharge);
        rig.Slider.UpdateLook(true, "Title", null);
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e => TrackEffect.IsFast(e.Look));
        Assert.Equal(0, effect.Opacity);
        rig.Frame(0.1);
        var opacity = effect.Opacity;
        Assert.InRange(opacity, 0.01, 0.99); // the bolt partly faded in
        var still = new KnobSignal();
        still.ReadMix(effect.StrikeGeometryFor(effect.Bounds.Size),new StrikePlans(effect.StrikeSeed),StrikeMix.Timing(1,effect.StrikeSeed).At+0.035,0); // SPEC A-21/B-04/K-173
        Assert.Equal(1, still.RawCharge);
        Assert.True(still.RawFlare > 0);
        Assert.True(Math.Abs(still.RawCharge * opacity - rig.Slider.Signal.RawCharge) < 1e-12, $"the bolt at {opacity}: the knob's charge {rig.Slider.Signal.RawCharge}, not {still.RawCharge * opacity}");
        Assert.True(Math.Abs(still.RawFlare * opacity - rig.Slider.Signal.RawFlare) < 1e-12, $"the bolt at {opacity}: the knob's flare {rig.Slider.Signal.RawFlare}, not {still.RawFlare * opacity}");
    });
    // ---- under reduce motion the knob's loop runs only while the light has somewhere to go (ED-252): the springs, a colour
    // fade, and the shaped light on its way to what the bolt shows. It comes to the strike's still moment (35 ms into strike
    // 1: charge 1, the flare of that moment) as the tube draws it, and only then asks for no more frames.

    // the flare the strike's still moment gives this effect's seed (the knob reads that moment under reduce motion)
    private static double StillFlare(TrackEffect effect)
    {
        var still = new KnobSignal();
        still.ReadMix(effect.StrikeGeometryFor(effect.Bounds.Size),new StrikePlans(effect.StrikeSeed),StrikeMix.Timing(1,effect.StrikeSeed).At+0.035,0); // SPEC A-21/B-04/K-173
        Assert.Equal(1, still.RawCharge);
        Assert.True(still.RawFlare > 0.1, $"the still moment's flare is {still.RawFlare}");
        return still.RawFlare;
    }

    // 60 Hz frames until nothing is waiting for one (the knob has stopped asking and so has everything else), at most ten seconds
    private static int RunUntilIdle(JewelRig rig)
    {
        var frames = 0;
        while (rig.Frames.Pending > 0 && frames < 600) { rig.Frame(); frames++; }
        Assert.Equal(0, rig.Frames.Pending);
        return frames;
    }

    private static void AssertStillLight(JewelRig rig, TrackEffect effect, string when)
    {
        var flare = StillFlare(effect);
        var signal = rig.Slider.Signal;
        Assert.True(effect.Opacity == 1 && signal.RawCharge == 1 && signal.Charge == 1 && Math.Abs(signal.Flare - flare) < 1e-3,
            $"{when}: the bolt is in (opacity {effect.Opacity}) and the knob has stopped asking for frames, reading charge {signal.RawCharge} (shaped {signal.Charge}) and flare {signal.RawFlare} (shaped {signal.Flare}); the still moment is charge 1, flare {flare}");
    }

    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: under reduce motion fast mode turned on fades the effect in over 0.3 s and the knob follows the bolt in, to the still moment's light (charge 1, its flare), and only then asks for no frames
    public void Under_reduce_motion_turning_fast_mode_on_brings_the_knob_to_the_still_moments_light_before_it_idles() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 1, reduceMotion: true);
        for (var i = 0; i < 4; i++) rig.Frame();
        Assert.Equal(0, rig.Frames.Pending); // dark and still: no frames
        rig.Slider.UpdateLook(true, "Title", null);
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e => TrackEffect.IsFast(e.Look));
        RunUntilIdle(rig);
        AssertStillLight(rig, effect, "a fast look fading in");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: a pair's effect stays when fast mode turns on (the look changes, not the effect), so the bolt is in at once; the knob's flare still rises through shape()'s two stages before the knob idles
    public void Under_reduce_motion_turning_fast_mode_on_over_a_pair_brings_the_knob_to_the_still_moments_light_before_it_idles() => headless.OnUiThread(() =>
    {
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Deepseek) { WorkerProvider = ProviderKind.Claude });
        using var rig = JewelFixtures.Rig(index: 1, reduceMotion: true, pair: pair);
        for (var i = 0; i < 4; i++) rig.Frame();
        var before = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single();
        Assert.False(TrackEffect.IsFast(before.Look));
        Assert.Equal(0, rig.Frames.Pending);
        rig.Slider.UpdateLook(true, "Title", pair);
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single();
        Assert.Same(before, effect); // no new effect, no fade: the bolt is fully in at once
        Assert.True(TrackEffect.IsFast(effect.Look));
        Assert.Equal(1, effect.Opacity);
        RunUntilIdle(rig);
        AssertStillLight(rig, effect, "a fast look on a kept pair effect");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: a pair joining while fast mode is on is a new effect fading in over the old (the effect's identity changes, the fast look does not): the knob follows the new bolt in to the still moment's light before it idles
    public void Under_reduce_motion_a_pair_joining_in_fast_mode_brings_the_knob_to_the_new_bolts_light_before_it_idles() => headless.OnUiThread(() =>
    {
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Deepseek) { WorkerProvider = ProviderKind.Claude });
        using var rig = JewelFixtures.Rig(index: 1, fast: true, reduceMotion: true);
        for (var i = 0; i < 4; i++) rig.Frame();
        var old = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single();
        Assert.Equal(0, rig.Frames.Pending);
        rig.Slider.UpdateLook(true, "Title", pair);
        Assert.Equal(2, rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Count()); // the old one fading out, the new one fading in
        RunUntilIdle(rig);
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single();
        Assert.NotSame(old, effect);
        AssertStillLight(rig, effect, "a new fast effect fading in over the old");
    });

    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: under reduce motion fast mode turned off lets the light go (the charge's 0.65 s settle, the flare's hold and fall) and then the knob asks for no frames
    public void Under_reduce_motion_turning_fast_mode_off_lets_the_light_go_before_the_knob_idles() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 1, fast: true, reduceMotion: true);
        for (var i = 0; i < 4; i++) rig.Frame();
        Assert.Equal(0, rig.Frames.Pending);
        Assert.Equal(1, rig.Slider.Signal.Charge);
        Assert.True(rig.Slider.Signal.Flare > 0.1);
        rig.Slider.UpdateLook(false, "Title", null);
        var frames = RunUntilIdle(rig);
        Assert.InRange(frames, 39, 600); // the charge's settle takes 0.65 s
        Assert.Equal(0, rig.Slider.Signal.Charge);
        Assert.Equal(0, rig.Slider.Signal.Flare);
        // the bolt's effect has faded out and gone; the plain liquid that came in under it (OD-0075: liquid at every effort level) stays
        Assert.False(TrackEffect.IsFast(Assert.Single(rig.Slider.GetVisualDescendants().OfType<TrackEffect>()).Look));
    });
    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: under reduce motion, turning fast mode off drops the knob's signal at once (the fading bolt is not read) and the knob lets go through shape(), staying at least as lit as the bolt still drawn (the still moment at the bolt's opacity)
    public void Under_reduce_motion_turning_fast_mode_off_drops_the_signal_at_once_and_the_knob_stays_as_lit_as_the_bolt_still_fading() => headless.OnUiThread(() =>
    {
        // under reduce motion the still moment is read, so what the bolt would give the knob is its opacity times the still
        // moment's charge 1 and flare. Without reduce motion "as lit as the bolt" does not hold: a strike inside the fade is
        // drawn by the bolt and not read by the knob (A_strike_inside_the_fade_out_of_fast_mode_does_not_charge_or_flare_the_knob,
        // as ED-252 decides), so the bolt can flare above the knob's falling light (review round 9: 0.3811 against 0.3183)
        using var rig = JewelFixtures.Rig(index: 1, fast: true, reduceMotion: true);
        for (var i = 0; i < 4; i++) rig.Frame();
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single();
        var flare = StillFlare(effect);
        Assert.Equal(1, rig.Slider.Signal.Charge);
        Assert.True(Math.Abs(rig.Slider.Signal.Flare - flare) < 1e-3);
        rig.Slider.UpdateLook(false, "Title", null);
        Assert.Contains(effect, rig.Slider.GetVisualDescendants().OfType<TrackEffect>()); // leaving: still drawn, fading out (the plain liquid fades in beside it, OD-0075)
        var seen = 0;
        double first = 0;
        for (var i = 0; i < 40 && effect.GetVisualParent() is not null; i++)
        {
            rig.Frame();
            if (effect.GetVisualParent() is null) break;
            var opacity = effect.Opacity;
            if (seen++ == 0) first = opacity;
            Assert.True(rig.Slider.Signal.RawCharge == 0 && rig.Slider.Signal.RawFlare == 0, $"frame {seen}: the bolt is drawn at {opacity} and the knob reads charge {rig.Slider.Signal.RawCharge}, flare {rig.Slider.Signal.RawFlare}: the signal drops at once");
            Assert.True(rig.Slider.Signal.Charge >= opacity - 1e-9 && rig.Slider.Signal.Flare >= opacity * flare - 1e-9,
                $"frame {seen}: the bolt is drawn at {opacity}, the knob is lit to charge {rig.Slider.Signal.Charge}, flare {rig.Slider.Signal.Flare}: darker than the bolt it leaves");
        }
        Assert.True(seen >= 8 && first > 0.5, $"the bolt was drawn on {seen} frames of the knob's letting go, at {first} on the first"); // the premise: the fade is under way, not over
    });

    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: a strike landing inside the 0.3 s the bolt fades out after fast mode is turned off does not reach the knob (it is not read once the effect has left)
    public void A_strike_inside_the_fade_out_of_fast_mode_does_not_charge_or_flare_the_knob() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 1, fast: true);
        rig.Frame();
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single();
        var now = effect.TimeAt(SwiftDate.Now);
        var k = Enumerable.Range(0, 40).First(j => Strike.StrikeAt(j, effect.StrikeSeed) > now + 0.5);
        var strikes = Strike.StrikeAt(k, effect.StrikeSeed);
        rig.Frame(strikes - 0.1 - now); // 0.1 s before the strike: its leader (0.14 s) and charge are still to come
        rig.Slider.UpdateLook(false, "Title", null);
        Assert.Contains(effect, rig.Slider.GetVisualDescendants().OfType<TrackEffect>()); // still drawn, fading out (the plain liquid fades in beside it, OD-0075)
        var drawn = 0; // frames on which the tube still draws the strike's bolt (its leader and its flash), at a visible opacity
        for (var i = 0; i < 14; i++)
        {
            rig.Frame();
            var a = effect.TimeAt(SwiftDate.Now) - strikes;
            if (effect.GetVisualParent() is { } && a > -Strike.Lead && a < 0.2 && effect.Opacity > 0.15) drawn++;
            Assert.True(rig.Slider.Signal.RawCharge == 0 && rig.Slider.Signal.RawFlare == 0,
                $"{a:F3} s from the strike, the bolt at {effect.Opacity}: the knob reads charge {rig.Slider.Signal.RawCharge}, flare {rig.Slider.Signal.RawFlare}");
        }
        Assert.True(drawn >= 3, $"the bolt's strike was drawn on {drawn} frames of the fade"); // the premise: the strike is inside the fade
    });

    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: under reduce motion the light is still moving while the fast effect it reads is not yet fully in, even where the shaped signal has nothing to settle (the effect's opacity is 0 until its first step)
    public void Under_reduce_motion_the_light_moves_while_the_fast_effect_is_not_fully_in() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 1, reduceMotion: true);
        for (var i = 0; i < 4; i++) rig.Frame();
        Assert.False(rig.Slider.LightMoves); // dark, no fast effect: at rest
        rig.Slider.UpdateLook(true, "Title", null);
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e => TrackEffect.IsFast(e.Look));
        Assert.Equal(0, effect.Opacity);
        Assert.True(rig.Slider.Signal.Settled, "the signal has nothing to settle: it still reads the dark");
        Assert.True(rig.Slider.LightMoves, "but the bolt about to fade in is the light's next value");
        RunUntilIdle(rig);
        Assert.False(rig.Slider.LightMoves); // lit and in: at rest again
        Assert.True(rig.Slider.Signal.Settled);
        effect.Opacity = 0.7; // the bolt not fully in (the signal, settled on the last frame's opacity, reads it at the next one)
        Assert.True(rig.Slider.Signal.Settled);
        Assert.True(rig.Slider.LightMoves);
        effect.Opacity = 0.99; // nearly in: its next value is still not the one read
        Assert.True(rig.Slider.Signal.Settled);
        Assert.True(rig.Slider.LightMoves);
        effect.Opacity = 1;
        Assert.False(rig.Slider.LightMoves);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // ED-252: under reduce motion the knob's loop runs while its light has somewhere to go; a pair's effect without fast mode draws no bolt (the signal reads none), so its 0.3 s fade in is not light on its way to the knob
    public void Under_reduce_motion_a_pair_effect_fading_in_without_fast_mode_does_not_keep_the_knob_asking_for_frames() => headless.OnUiThread(() =>
    {
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Deepseek) { WorkerProvider = ProviderKind.Claude });
        using var rig = JewelFixtures.Rig(index: 1, reduceMotion: true);
        for (var i = 0; i < 4; i++) rig.Frame();
        Assert.Equal(0, rig.Frames.Pending); // dark and still: no frames
        Assert.False(TrackEffect.IsFast(Assert.Single(rig.Slider.GetVisualDescendants().OfType<TrackEffect>()).Look)); // the plain liquid (OD-0075), no bolt
        var requests = new KnobFrameRequests(rig.Frames, rig.Slider); // the slider's own requests counted, every request passed on
        using var counted = Marrow.UI.Port.FrameClock.Source.Override(requests);
        rig.Slider.UpdateLook(false, "Title", pair); // a pair joins, fast mode off
        var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().Single(e => e.Look.Pair is not null);
        Assert.False(TrackEffect.IsFast(effect.Look));
        Assert.Equal(0, effect.Opacity); // a new effect, fading in
        Assert.Equal(1, requests.Count); // the look's change asks the knob for a frame
        Assert.False(rig.Slider.LightMoves);
        var fading = 0;
        for (var i = 0; i < 60 && rig.Frames.Pending > 0; i++)
        {
            rig.Frame();
            if (effect.Opacity >= 1) continue;
            fading++;
            Assert.False(rig.Slider.LightMoves, $"the pair's effect at {effect.Opacity}: no bolt, so no light on its way to the knob");
        }
        Assert.Equal(0, rig.Frames.Pending);
        Assert.Equal(1, effect.Opacity);
        Assert.True(fading >= 10, $"the premise: the effect faded in over {fading} frames");
        Assert.True(requests.Count == 1, $"the knob asked for {requests.Count} frames while a pair's effect without a bolt faded in over {fading}: it draws the look's change and idles");
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
        using var at = Draw(0.01);
        using var faint = Draw(0.05);
        int Differ(SKBitmap a, SKBitmap b) { var n = 0; for (var y = 0; y < 34; y++) for (var x = 0; x < 298; x++) if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++; return n; }
        Assert.Equal(0, Differ(none, below));
        Assert.True(Differ(none, at) > 0, "an ink of exactly 0.01 blends the pale-liquid look in (the page returns only below it: k < 0.01)");
        Assert.True(Differ(none, faint) > 0, "an ink of 0.05 blends the pale-liquid look into the bubbles");
    });
}
