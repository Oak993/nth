using Avalonia;
using Avalonia.Media.Imaging;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :1039-1335 (TrackEffect).
// DEVIATION OD-0048 / OD-0051 (owner-approved, locked 2026-10-02): at maximum effort and for every pair the track is a
// glass tube of living liquid; the reference is the locked canvas (E:\Droppy\work\design\effort-liquid-approved-v15.dc.html).
[Collection("Headless")]
public sealed class EffortSliderLiquidTests(HeadlessSession headless)
{
    private static TrackLook Solo(EffortBrand brand) => new(TrackLook.Kind.Supercharged, brand);
    private static EffortPairBrand Pair(ProviderKind lead, ProviderKind heads) => new(new HydraPair(lead) { WorkerProvider = heads });

    private static TrackEffect Effect(TrackLook look, double width = 240, double height = 34)
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effect = new TrackEffect(look);
        effect.Measure(new Size(width, height)); effect.Arrange(new Rect(0, 0, width, height));
        return effect;
    }

    // one track, drawn at a moment (each track has its own bubbles, so a comparison draws the same track)
    private static SKBitmap Frame(TrackEffect effect, double seconds)
    {
        var (width, height) = (effect.Bounds.Width, effect.Bounds.Height);
        typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(effect, seconds);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)width, (int)height));
        using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static SKBitmap Liquid(LiquidSpec spec, double seconds)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(240, 34));
        using (var context = bitmap.CreateDrawingContext())
            LiquidTrack.Draw(context, new Size(240, 34), seconds, spec, [], false, Avalonia.Styling.ThemeVariant.Light);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static Avalonia.Media.Color Resolved(SwiftColor color) => color.Resolve(Avalonia.Styling.ThemeVariant.Light);

    private static int MaxDelta(SKBitmap a, SKBitmap b, int fromX, int toX)
    {
        var max = 0;
        for (var y = 4; y < a.Height - 4; y++)
            for (var x = fromX; x < toX; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                max = Math.Max(max, Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue))));
            }
        return max;
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-138")] // EffortSlider.swift:1054-1057 (which tracks draw an effect); DEVIATION OD-0051
    [InlineData(TrackLook.Kind.Plain, false, false)]
    [InlineData(TrackLook.Kind.Fast, false, false)]
    [InlineData(TrackLook.Kind.Supercharged, false, true)]
    [InlineData(TrackLook.Kind.Fusion, false, true)]
    [InlineData(TrackLook.Kind.Plain, true, true)]
    [InlineData(TrackLook.Kind.Fast, true, true)]
    public void Maximum_effort_and_every_pair_draw_the_liquid(TrackLook.Kind kind, bool paired, bool liquid) =>
        Assert.Equal(liquid, TrackEffect.IsLiquid(new TrackLook(kind, EffortBrand.Claude, paired ? Pair(ProviderKind.Claude, ProviderKind.Deepseek) : null)));

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: one provider's liquid is its three stops, dark to light along the track
    public void A_solo_track_is_its_brands_three_stops_end_to_end()
    {
        var spec = LiquidSpec.For(Solo(EffortBrand.Cursor));
        var stops = spec.Stops();
        Assert.Equal(EffortBrand.Cursor.LiquidStops.Select(Resolved), stops.Select(s => Resolved(s.Color)));
        Assert.Equal(new[] { 0.0, 0.5, 1.0 }, stops.Select(s => s.At));
        Assert.Null(spec.OilRange());
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961; DEVIATION OD-0048: workers left, the lead at the knob, 1.6 x the stretch
    public void A_pair_puts_the_heads_left_and_the_lead_at_the_knob_end_with_more_room()
    {
        var look = new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Claude, Pair(ProviderKind.Claude, ProviderKind.Deepseek));
        Assert.Equal(EffortBrand.Deepseek, look.Pair!.HeadsBrand);
        var spec = LiquidSpec.For(look);
        Assert.Equal(2, spec.Segments.Count);
        Assert.Equal(EffortBrand.Deepseek.LiquidStops.Select(Resolved), spec.Segments[0].Colors.Select(Resolved));
        Assert.Equal(EffortBrand.Claude.LiquidStops.Select(Resolved), spec.Segments[1].Colors.Select(Resolved));
        Assert.Equal(1, spec.Segments[0].Weight);
        Assert.Equal(LiquidSpec.LeadShare, spec.Segments[1].Weight);
        Assert.Equal(1.6, LiquidSpec.LeadShare);
        // each colour held over the middle third of its own stretch: heads 0..1/2.6, lead 1/2.6..1
        var stops = spec.Stops();
        Assert.Equal(6, stops.Count);
        var split = 1 / 2.6;
        Assert.Equal(split / 3, stops[0].At, 9);
        Assert.Equal(split / 2, stops[1].At, 9);
        Assert.Equal(split * 2 / 3, stops[2].At, 9);
        Assert.Equal(split + (1 - split) / 3, stops[3].At, 9);
        Assert.Equal(split + (1 - split) / 2, stops[4].At, 9);
        Assert.Equal(1 - (1 - split) / 3, stops[5].At, 9);
        Assert.True(stops.Zip(stops.Skip(1)).All(p => p.First.At < p.Second.At));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961: heads at home are the roster colours
    public void Heads_at_home_bring_the_roster_colours()
    {
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Claude));
        Assert.Null(pair.HeadsBrand);
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Claude, pair));
        Assert.Equal(pair.Colors.Select(Resolved), spec.Segments[0].Colors.Select(Resolved));
        Assert.False(spec.Segments[0].Oil);
        Assert.Equal(EffortBrand.Claude.LiquidStops.Select(Resolved), spec.Segments[1].Colors.Select(Resolved));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: Grok's obsidian carries the oil sheen, only over its own stretch
    public void The_oil_sheen_covers_only_grok()
    {
        Assert.Equal((0.0, 1.0), LiquidSpec.For(Solo(EffortBrand.Grok)).OilRange());
        Assert.Equal((0.0, 1 / 2.6), LiquidSpec.For(new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Claude, Pair(ProviderKind.Claude, ProviderKind.Grok))).OilRange());
        var leadGrok = LiquidSpec.For(new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Grok, Pair(ProviderKind.Grok, ProviderKind.Claude))).OilRange();
        Assert.NotNull(leadGrok);
        Assert.Equal(1 / 2.6, leadGrok.Value.From, 9);
        Assert.Equal(1.0, leadGrok.Value.To, 9);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: the tube is one SkSL pass, like the app's glass
    public void The_liquid_shader_compiles() => Assert.NotNull(LiquidTrack.CompiledEffect);

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // EffortSlider.swift:1054-1056; DEVIATION OD-0048: the blend shows heads left, lead right
    public void A_paired_tube_shows_the_heads_left_and_the_lead_at_the_knob() => headless.OnUiThread(() =>
    {
        var look = new TrackLook(TrackLook.Kind.Plain, EffortBrand.Claude, Pair(ProviderKind.Claude, ProviderKind.Deepseek));
        using var frame = Frame(Effect(look), 1.0);
        SKColor left = frame.GetPixel(30, 17), right = frame.GetPixel(200, 17);
        Assert.True(left.Blue > left.Red + 60, $"heads end {left}");     // Deepseek blue
        Assert.True(right.Red > right.Blue + 60, $"lead end {right}");   // Claude orange
        Assert.Equal(255, frame.GetPixel(120, 17).Alpha);
        Assert.Equal(0, frame.GetPixel(0, 0).Alpha); // outside the rounded capsule
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: deterministic frames that move with time
    public void Frames_are_deterministic_and_the_liquid_moves() => headless.OnUiThread(() =>
    {
        var effect = Effect(Solo(EffortBrand.Deepseek));
        using var first = Frame(effect, 2.0);
        using var again = Frame(effect, 2.0);
        using var later = Frame(effect, 3.5);
        Assert.Equal(first.Bytes, again.Bytes);
        Assert.True(MaxDelta(first, later, 20, 220) > 6);
    });

    [Theory]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: currents are functions of continuous time, so no loop ever jumps
    [InlineData(0.5)]
    [InlineData(37.25)]
    [InlineData(600.0)]
    public void One_frame_later_the_liquid_has_barely_moved(double seconds) => headless.OnUiThread(() =>
    {
        // the liquid alone (a bubble's own glide is checked in A_bubble_is_invisible_where_its_path_restarts)
        var spec = LiquidSpec.For(Solo(EffortBrand.Claude));
        using var now = Liquid(spec, seconds);
        using var next = Liquid(spec, seconds + 1 / 60.0);
        var delta = MaxDelta(now, next, 20, 220);
        Assert.True(delta <= 12, $"a frame step must not jump ({delta})");
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: a light liquid keeps a readable dark bubble rim
    public void A_silver_tube_renders_light() => headless.OnUiThread(() =>
    {
        using var frame = Frame(Effect(Solo(EffortBrand.Silver)), 1.0);
        var c = frame.GetPixel(120, 17);
        Assert.True(c.Red > 200 && c.Green > 200 && c.Blue > 200, c.ToString());
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: each track has its own seeded bubbles, mostly tiny, a few medium
    public void Bubbles_are_seeded_varied_and_mostly_tiny()
    {
        var a = LiquidTrack.Bubbles(3, 300, 10);
        Assert.Equal(a, LiquidTrack.Bubbles(3, 300, 10));
        Assert.NotEqual(a, LiquidTrack.Bubbles(4, 300, 10));
        Assert.Equal(10, a.Count); // one per 30 points
        Assert.Equal(4, LiquidTrack.Bubbles(1, 40, 10).Count);
        Assert.Equal(24, LiquidTrack.Bubbles(1, 2000, 10).Count);
        var many = Enumerable.Range(0, 40).SelectMany(seed => LiquidTrack.Bubbles(seed, 720, 10)).ToArray();
        var tiny = many.Count(b => b.Size < 3); var medium = many.Count(b => b.Size >= 5);
        Assert.True(tiny > many.Length / 2, $"tiny {tiny} of {many.Length}");
        Assert.InRange(medium, 1, many.Length / 5);
        Assert.All(many, b => Assert.InRange(b.Size, 1.4, 8));
        // the paths differ: some drift right with the current, some rise to either side
        Assert.Contains(many, b => b.P3.X > 50);
        Assert.Contains(many, b => b.P1.X < 0);
        Assert.Contains(many, b => b.P1.X > 0 && b.P3.X < 50);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: a bubble fades in and out, so its restart never shows
    public void A_bubble_is_invisible_where_its_path_restarts()
    {
        var b = LiquidTrack.Bubbles(9, 300, 10)[0];
        var start = 3 * b.Duration - b.Offset + 1e-9; // phase 0 of a later cycle
        Assert.True(LiquidTrack.BubbleAt(b, 10, start).Opacity < 1e-6);
        Assert.True(LiquidTrack.BubbleAt(b, 10, start - 0.001).Opacity < 0.01); // the end of the cycle before
        Assert.Equal(b.Opacity, LiquidTrack.BubbleAt(b, 10, start + b.Duration * 0.5).Opacity, 9);
        var (centre, radius, _) = LiquidTrack.BubbleAt(b, 10, start);
        Assert.Equal(b.X, centre.X, 3);
        Assert.Equal(10 - b.Bottom, centre.Y, 3);
        Assert.Equal(b.Size * b.S0 / 2, radius, 3);
        var end = LiquidTrack.BubbleAt(b, 10, start + b.Duration * 0.9999).Centre;
        Assert.Equal(b.X + b.P3.X, end.X, 1);
        Assert.Equal(10 - b.Bottom + b.P3.Y, end.Y, 1);
    }
}
