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
// on its v22 bubbles, the port's 298 x 34 tube with the knob at 274, 240 s of irregular frames with a few long gaps).
[Collection("Headless")]
public sealed class EffortSliderJewelBurstTests(HeadlessSession headless)
{
    private static readonly StrikeGeometry Tube = new(298, 34, 274, 24);

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
        var seen = new List<BubbleBurst>();
        effect.Bursts.OnBurst = seen.Add;
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
            if (double.IsNaN(first)) first = t;
            last = t;
        }
        var geometry = effect.StrikeGeometryFor(new Size(298, 34));
        var contacts = JewelFixtures.Contacts(geometry, effect.StrikeSeed, effect.Bubbles(298, 34).Seed, 298, first - 6, last);
        Assert.True(contacts.Count > 40, $"the run has contacts ({contacts.Count})");
        Assert.True(contacts.Count == seen.Count && contacts.Zip(seen).All(p => Math.Abs(p.First.T - p.Second.T) < 1e-9 && p.First.Lane == p.Second.I && p.First.Rise == p.Second.Rise),
            $"rule {contacts.Count}: {string.Join(" ", contacts.Take(8))}; track {seen.Count}: {string.Join(" ", seen.Take(8).Select(b => (b.T, b.I, b.Rise)))}");
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
        Assert.Null(BubbleBursts.Ring(burst, 0.85, 1 + BubbleBursts.Life + 1e-9)); // 0.19 s and it is gone
    }

    [Fact]
    [Trait("Decision", "ED-252")] // strike-engine.js orbGeo: the exit is the first vertex at or past the knob's radius less half a pixel
    public void The_bolts_exit_is_where_its_channel_crosses_the_rim_less_half_a_pixel()
    {
        var s = JewelFixtures.Expected.GetProperty("signal");
        var seed = s.GetProperty("seed").GetInt32();
        foreach (var e in s.GetProperty("exits").EnumerateArray())
        {
            var g = new StrikeGeometry(298, 34, 274, e.GetProperty("kr").GetDouble());
            Assert.Equal(e.GetProperty("th").GetDouble(), KnobSignal.ExitOf(g, seed, e.GetProperty("k").GetInt32()), 9);
        }
    }
}
