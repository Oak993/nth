using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// OD-0078 / OD-0084, required by the owner with OD-0090 (2026-10-03): every bubble the strike touches bursts on the spot, on
// every maker, visible on pale liquids. The engine's contact rule (strike-engine.js events / near / process / contact) was
// recorded from the locked engine itself (tests/fixtures/jewel-knob/expected.mjs burstRun: the engine's A2 strikes at strikeAt
// on its v22 bubbles, the port's 298 x 34 tube with the knob at 274, 240 s of irregular frames with a few long gaps). The
// bolt leaves where the page's slider puts it for a knob at 274: tl.exAt = Math.round((274 - 2) * 4) / 4 = 272
// (jewel-knob.html:593, run by expected.mjs), recorded as the signal's geometry.ex.
[Collection("Headless")]
public sealed class EffortSliderJewelBurstTests(HeadlessSession headless)
{
    private static double PageExit => JewelFixtures.Expected.GetProperty("signal").GetProperty("geometry").GetProperty("ex").GetDouble();
    private static StrikeGeometry Tube => new(298, 34, PageExit, 24);

    [Theory]
    [Trait("Decision", "ED-252")] // the port's bursts are the locked engine's: the same bubbles, the same rises, at the same instants
    [InlineData(0)]
    [InlineData(1)]
    public void Every_burst_is_the_locked_engines(int run)
    {
        var r = JewelFixtures.Expected.GetProperty("bursts")[run];
        int strikeSeed = r.GetProperty("strikeSeed").GetInt32(), bubbleSeed = r.GetProperty("bubbleSeed").GetInt32();
        var bursts = new BubbleBursts();
        var seen = new List<BubbleBurst>();
        bursts.OnBurst = seen.Add;
        var channels = new StrikeChannels(strikeSeed);
        foreach (var t in r.GetProperty("frames").EnumerateArray()) bursts.Process(channels, Tube, strikeSeed, bubbleSeed, 298, t.GetDouble());
        var expected = r.GetProperty("bursts").EnumerateArray().ToArray();
        Assert.True(expected.Length > 100, "the run touches many bubbles");
        Assert.Equal(expected.Length, seen.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            var (e, b) = (expected[i], seen[i]);
            Assert.True(e.GetProperty("k").GetInt32() == b.K && e.GetProperty("i").GetInt32() == b.I && e.GetProperty("rise").GetInt32() == b.Rise,
                $"burst {i}: engine k {e.GetProperty("k")} lane {e.GetProperty("i")} rise {e.GetProperty("rise")}, port k {b.K} lane {b.I} rise {b.Rise}");
            Assert.Equal(e.GetProperty("t").GetDouble(), b.T, 9);
            // where it was: the engine eases the rise through a 1024-step table, the port exactly (under 1e-3 px apart)
            Assert.True(Math.Abs(e.GetProperty("x").GetDouble() - b.X) < 1e-3 && Math.Abs(e.GetProperty("y").GetDouble() - b.Y) < 1e-3, $"burst {i} at {b.X},{b.Y}");
            Assert.True(Math.Abs(e.GetProperty("r").GetDouble() - b.R) < 1e-3, $"burst {i} radius {b.R}");
            // its number and its turn, which place the ring's arcs and the glints (contact(): rot = (n % 628) / 100)
            Assert.True(e.GetProperty("n").GetInt32() == b.N && Math.Abs(e.GetProperty("rot").GetDouble() - b.Rot) < 1e-12,
                $"burst {i}: engine n {e.GetProperty("n")} rot {e.GetProperty("rot")}, port n {b.N} rot {b.Rot}");
        }
    }

    [Theory]
    [Trait("Decision", "ED-252")] // zero misses: over a long run of irregular frames on the slider's own track, every contact is a burst, on a pale maker and a dark one
    [InlineData(EffortBrand.Silver)]
    [InlineData(EffortBrand.Claude)]
    public void On_the_track_every_contact_is_a_burst(EffortBrand brand) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effect = new TrackEffect(new TrackLook(TrackLook.Kind.Fast, brand)) { TrackWidth = 298 };
        effect.Measure(new Size(298, 34)); effect.Arrange(new Rect(0, 0, 298, 34));
        var seen = new List<StrikeBurst>(); var expected=new List<StrikeBurst>();
        var geometry=effect.StrikeGeometryFor(new Size(298,34)); var storm=new StrikeStorm(effect.StrikeSeed); var clock=new StrikeFastClock();
        var lanes=new StrikeBubbleLanes($"{effect.Bubbles(298,34).Seed},9,3,1",298,34); var oracle=new StrikeContacts(effect.StrikeSeed,lanes,clock); var ink=StrikeInk.Of(JewelLiquid.Of(brand));
        var time = typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var x = 777u;
        double Next() { x = unchecked(x * 1103515245 + 12345); return (x >> 8) / 16777216.0; }
        double t = 0, first = double.NaN, last = 0;
        using var bitmap = new RenderTargetBitmap(new PixelSize(298, 34));
        while (t < 90)
        {
            t += Next() < 0.01 ? 0.5 : 0.01 + 0.06 * Next();
            time.SetValue(effect, t);
            using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var frame));
            seen.AddRange(frame.MixContacts!.NewBursts.Take(frame.MixContacts.NewCount));
            var f=storm.Resolve(geometry,t+effect.StrikeOffset,t,1,ink.Amount); oracle.Process(geometry,storm.Plans,f,t,effect.StrikeOffset,ink.Amount);
            expected.AddRange(oracle.NewBursts.Take(oracle.NewCount));
            if (double.IsNaN(first)) first = t;
            last = t;
        }
        Assert.True(expected.Count > 40, $"the run has contacts ({expected.Count})");
        Assert.True(expected.Count == seen.Count && expected.Zip(seen).All(p => Math.Abs(p.First.T - p.Second.T) < 1e-9 && p.First.Lane == p.Second.Lane && p.First.Rise == p.Second.Rise),
            $"rule {expected.Count}; track {seen.Count}"); // SPEC B-05/B-07 replaces A2 centre contacts.
        Assert.Equal(expected,seen); // stronger: every burst field/order, beyond the retained 1e-9/key assertion.
    });

    [Fact]
    [Trait("Decision", "ED-252")] // drawBubbles' burst on a pale liquid: the ring's neutral arcs (+1.4 px, .5 al ink) under the white ones, and none at ink 0
    public void A_burst_on_a_pale_liquid_has_its_neutral_ring_and_on_a_dark_one_none()
    {
        var burst = new BubbleBurst(1, 30, 17, 2, 0.8, 1.1, 123456, 0, 0, 0);
        var ring = BubbleBursts.Ring(burst, 0.85, 1.05)!.Value;
        Assert.Equal(5 + 123456 % 3, ring.Arcs);
        SKBitmap Draw(double ink)
        {
            var bitmap = new SKBitmap(new SKImageInfo(60, 34, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(0xE7, 0xE9, 0xF1)); // OpenAI's light stop
            BubbleBursts.DrawRings(canvas, [burst], 0.85, 1.05, ink);
            return bitmap;
        }
        using var pale = Draw(1);
        using var plain = Draw(0);
        using var none = Draw(0.009);
        int darker = 0;
        for (var y = 0; y < 34; y++)
            for (var x = 0; x < 60; x++)
            {
                Assert.Equal(plain.GetPixel(x, y), none.GetPixel(x, y));
                SKColor a = plain.GetPixel(x, y), b = pale.GetPixel(x, y);
                if (b.Red + b.Green + b.Blue < a.Red + a.Green + a.Blue - 20) darker++;
            }
        Assert.True(darker > 10, $"the neutral ring shows on the silver liquid ({darker} px)");
        // just outside the white arc, inside the neutral one (0.7 px wider each side): the liquid under rgba(20,22,30, .5 al)
        using var fine = new SKBitmap(new SKImageInfo(240, 136, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(fine))
        {
            canvas.Clear(new SKColor(0xE7, 0xE9, 0xF1));
            canvas.Scale(4);
            BubbleBursts.DrawRings(canvas, [burst], 0.85, 1.05, 1);
        }
        var mid = ring.Start + (ring.Span - ring.Gap) / 2;
        var at = ring.Radius + ring.Width / 2 + 0.45;
        var px = fine.GetPixel((int)Math.Floor((30 + at * Math.Cos(mid)) * 4), (int)Math.Floor((17 + at * Math.Sin(mid)) * 4));
        var alpha = Math.Floor(ring.Alpha * 0.5 * 1000 + 0.5) / 1000;
        Assert.Equal(0xE7 * (1 - alpha) + 20 * alpha, px.Red, 4.0);
        Assert.Equal(0xF1 * (1 - alpha) + 30 * alpha, px.Blue, 4.0);
        Assert.Null(BubbleBursts.Ring(burst, 0.85, 1 + BubbleBursts.Life + 1e-9)); // 0.19 s and it is gone
    }

    [Fact]
    [Trait("Decision", "ED-252")] // contact(): a popped bubble's rise ends there, it is not drawn again until its next rise
    public void A_popped_bubble_is_gone_from_the_liquid() => headless.OnUiThread(() =>
    {
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude));
        var lanes = new BubbleLanes(11, 298, 34);
        const double t = 4.2;
        var (lane, now) = Enumerable.Range(0, lanes.Count).Select(i => (i, lanes.At(i, t))).First(p => p.Item2 is { } n && LiquidTrack.BubbleAt(n.Rise, 34, n.Progress).Opacity > 0.4);
        var (centre, radius, _) = LiquidTrack.BubbleAt(now!.Value.Rise, 34, now.Value.Progress);
        SKBitmap Draw(IReadOnlyCollection<(int, int)>? popped)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(298, 34));
            using (var context = bitmap.CreateDrawingContext())
                LiquidTrack.Draw(context, new Size(298, 34), 298, t, spec, LiquidTrack.GradientFor(spec, 298, Avalonia.Styling.ThemeVariant.Dark), lanes, false,
                    Avalonia.Styling.ThemeVariant.Dark, LiquidFlow.At(t, true), null, 0, null, popped);
            using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
            return SKBitmap.Decode(stream);
        }
        using var whole = Draw(null);
        using var popped = Draw([(lane, now.Value.K)]);
        using var other = Draw([(lane, now.Value.K + 1)]); // its next rise popped: this one still shows
        int differ = 0, same = 0;
        for (var y = (int)(centre.Y - radius); y <= (int)(centre.Y + radius); y++)
            for (var x = (int)(centre.X - radius); x <= (int)(centre.X + radius); x++)
            {
                if (x < 0 || y < 0 || x >= 298 || y >= 34) continue;
                if (whole.GetPixel(x, y) != popped.GetPixel(x, y)) differ++;
                if (whole.GetPixel(x, y) == other.GetPixel(x, y)) same++;
            }
        Assert.True(differ > 0, $"bubble {lane} at {centre} is gone when popped");
        Assert.True(same > 0 && differ > 0);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // drawBubbles fills a burst's glints as one path: where two overlap the light is the same, never added
    public void A_bursts_glints_are_filled_once()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(40, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            Strike.Render(canvas, [StrikeOp.Discs([10, 10, 4, 14, 10, 4, 30, 10, 3], new SKColor(255, 222, 204), 0.5)]);
        }
        var single = bitmap.GetPixel(30, 10).Alpha;
        Assert.InRange(single, 120, 135);                 // 0.5 of the way
        Assert.Equal(single, bitmap.GetPixel(12, 10).Alpha); // inside both discs: the same, not 0.75
    }

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js orbGeo: the exit is the first vertex at or past the knob's radius less half a pixel
    public void The_bolts_exit_is_where_its_channel_crosses_the_rim_less_half_a_pixel()
    {
        var s = JewelFixtures.Expected.GetProperty("signal");
        var seed = s.GetProperty("seed").GetInt32();
        foreach (var e in s.GetProperty("exits").EnumerateArray())
        {
            var g = new StrikeGeometry(298, 34, PageExit, e.GetProperty("kr").GetDouble());
            Assert.Equal(e.GetProperty("th").GetDouble(), KnobSignal.ExitOf(g, seed, e.GetProperty("k").GetInt32()), 9);
        }
    }
}
