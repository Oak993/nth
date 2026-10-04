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

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :1039-1335 (TrackEffect).
// DEVIATION OD-0048 / OD-0051 (owner-approved, locked 2026-10-02): at maximum effort and for every pair the track is a
// glass tube of living liquid. The reference is the owner's canvas (copy at
// E:\Droppy\work\design\effort-liquid-v19-gpu.dc.html): its CSS and its WebGL renderer, whose values these tests pin.
[Collection("Headless")]
public sealed class EffortSliderLiquidTests(HeadlessSession headless)
{
    private static TrackLook Solo(EffortBrand brand) => new(TrackLook.Kind.Supercharged, brand);
    private static EffortPairBrand Pair(ProviderKind lead, ProviderKind heads) => new(new HydraPair(lead) { WorkerProvider = heads });
    private static Avalonia.Media.Color Resolved(SwiftColor color) => color.Resolve(ThemeVariant.Light);

    private static TrackEffect Effect(TrackLook look, double width = 240, double height = 34)
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effect = new TrackEffect(look) { TrackWidth = width };
        effect.Measure(new Size(width, height)); effect.Arrange(new Rect(0, 0, width, height));
        return effect;
    }

    private static SKBitmap Decode(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    // one track, drawn at a moment (each track has its own bubbles, so a comparison draws the same track)
    private static SKBitmap Frame(TrackEffect effect, double seconds)
    {
        typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(effect, seconds);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)effect.Bounds.Width, (int)effect.Bounds.Height));
        using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        return Decode(bitmap);
    }

    // the liquid alone, without bubbles
    private static SKBitmap Liquid(LiquidSpec spec, double seconds, double width = 240)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)width, 34));
        using (var context = bitmap.CreateDrawingContext())
            LiquidTrack.Draw(context, new Size(width, 34), width, seconds, spec, LiquidTrack.GradientFor(spec, (int)width, ThemeVariant.Light), null, false, ThemeVariant.Light);
        return Decode(bitmap);
    }

    private static int MaxDelta(SKBitmap a, SKBitmap b, int fromX, int toX)
    {
        var max = 0;
        for (var y = 6; y < a.Height - 6; y++)
            for (var x = fromX; x < toX; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                max = Math.Max(max, Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue))));
            }
        return max;
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-138")] // EffortSlider.swift:1054-1057 (which tracks draw an effect); DEVIATION OD-0051, OD-0073 (fast is Strike over the liquid at every effort)
    [InlineData(TrackLook.Kind.Plain, false, true)] // DEVIATION OD-0075: liquid at every effort level, the plain track too
    [InlineData(TrackLook.Kind.Fast, false, true)]
    [InlineData(TrackLook.Kind.Supercharged, false, true)]
    [InlineData(TrackLook.Kind.Fusion, false, true)]
    [InlineData(TrackLook.Kind.Plain, true, true)]
    [InlineData(TrackLook.Kind.Fast, true, true)]
    public void Maximum_effort_and_every_pair_draw_the_liquid(TrackLook.Kind kind, bool paired, bool liquid) =>
        Assert.Equal(liquid, TrackEffect.IsLiquid(new TrackLook(kind, EffortBrand.Claude, paired ? Pair(ProviderKind.Claude, ProviderKind.Deepseek) : null)));

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: one provider's liquid is its three stops, end to end (canvas fillStyle)
    public void A_solo_track_is_its_brands_three_stops_end_to_end()
    {
        var spec = LiquidSpec.For(Solo(EffortBrand.Cursor));
        Assert.False(spec.IsBlend);
        var stops = spec.Stops();
        Assert.Equal(EffortBrand.Cursor.LiquidStops.Select(Resolved), stops.Select(s => Resolved(s.Color)));
        Assert.Equal(new[] { 0.0, 0.5, 1.0 }, stops.Select(s => s.At));
        Assert.Null(spec.OilRange());
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961; DEVIATION OD-0048: workers left, the lead at the knob, 1.6 x the stretch
    public void A_pair_holds_each_models_heart_colour_over_the_middle_third_of_its_stretch_lead_at_the_knob()
    {
        var look = new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Claude, Pair(ProviderKind.Claude, ProviderKind.Deepseek));
        Assert.Equal(EffortBrand.Deepseek, look.Pair!.HeadsBrand);
        var spec = LiquidSpec.For(look);
        Assert.True(spec.IsBlend);
        Assert.Equal(1, spec.Segments[0].Weight);
        Assert.Equal(LiquidSpec.LeadShare, spec.Segments[1].Weight);
        Assert.Equal(1.6, LiquidSpec.LeadShare);
        Assert.False(spec.Segments[0].Lead);
        Assert.True(spec.Segments[1].Lead);
        // the canvas's blend formula: the heart (middle) stop of each, at 0, the thirds of each stretch, and 1
        var stops = spec.Stops();
        var deepseek = Resolved(EffortBrand.Deepseek.LiquidStops[1]); var claude = Resolved(EffortBrand.Claude.LiquidStops[1]);
        Assert.Equal(new[] { deepseek, deepseek, deepseek, claude, claude, claude }, stops.Select(s => Resolved(s.Color)));
        var split = 1 / 2.6;
        double[] at = [0, split / 3, split * 2 / 3, split + (1 - split) / 3, 1 - (1 - split) / 3, 1];
        for (var i = 0; i < at.Length; i++) Assert.Equal(at[i], stops[i].At, 9);
        // the strands of the first current carry each model's title colour
        Assert.Equal(Resolved(EffortBrand.Deepseek.TitleColor), Resolved(spec.Segments[0].Strand));
        Assert.Equal(Resolved(EffortBrand.Claude.TitleColor), Resolved(spec.Segments[1].Strand));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961: heads at home are the roster colours
    public void Heads_at_home_bring_the_roster_colours_across_their_stretch()
    {
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Claude));
        Assert.Null(pair.HeadsBrand);
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Claude, pair));
        Assert.Equal(pair.Colors.Select(Resolved), spec.Segments[0].Colors.Select(Resolved));
        Assert.False(spec.Segments[0].Oil);
        Assert.Equal([Resolved(EffortBrand.Claude.LiquidStops[1])], spec.Segments[1].Colors.Select(Resolved));
        var stops = spec.Stops();
        Assert.Equal(0, stops[0].At);
        Assert.Equal(1, stops[^1].At);
        Assert.True(stops.Zip(stops.Skip(1)).All(p => p.First.At <= p.Second.At));
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
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: the oil sheen is drawn on Grok's liquid only, over its own stretch
    public void The_oil_layer_is_off_for_every_tube_without_grok()
    {
        var (none, noneMask) = LiquidTrack.Oil(LiquidSpec.For(Solo(EffortBrand.Claude)), 240, 2);
        Assert.Equal(new float[4], none); Assert.Equal(0f, noneMask);
        var (solo, soloMask) = LiquidTrack.Oil(LiquidSpec.For(Solo(EffortBrand.Grok)), 240, 2);
        Assert.Equal(1f, solo[0]); Assert.Equal(0f, solo[1]); Assert.Equal(240f, solo[2]); Assert.Equal(0f, soloMask);
        var (blend, blendMask) = LiquidTrack.Oil(LiquidSpec.For(new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Claude, Pair(ProviderKind.Claude, ProviderKind.Grok))), 260, 2);
        Assert.Equal(1f, blend[0]); Assert.Equal(0f, blend[1]); Assert.Equal((float)((1 / 2.6 + 0.06) * 260), blend[2], 3); Assert.Equal(1f, blendMask);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: the canvas's "in oklab" gradient, sampled at each device pixel's centre
    public void The_gradient_is_interpolated_in_oklab_as_css_does()
    {
        var black = Avalonia.Media.Color.FromRgb(0, 0, 0); var white = Avalonia.Media.Color.FromRgb(255, 255, 255);
        var row = LiquidTrack.GradientRow([(black, 0), (white, 1)], 3);
        // the middle texel is OKLab L = 0.5: linear 0.125, sRGB 99 (an sRGB mix would give 128)
        Assert.InRange(row[1].Red, (byte)98, (byte)100);
        Assert.Equal(row[1].Red, row[1].Green);
        Assert.Equal(row[1].Red, row[1].Blue);
        // the first and last texel centres sit a sixth in from each end
        var single = LiquidTrack.GradientRow([(black, 0), (white, 1)], 1);
        Assert.Equal(row[1], single[0]);
        var red = Avalonia.Media.Color.FromRgb(204, 107, 71);
        Assert.Equal(new SKColor(204, 107, 71), LiquidTrack.GradientRow([(red, 0), (red, 1)], 4)[2]); // round trip exact
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: CSS cubic-bezier timing (ease-in-out and the bubbles' curve)
    public void Cubic_bezier_timing_matches_the_curve()
    {
        // the curve itself, sampled finely: at each x the y of the nearest sample (independent of the solver)
        static double Sampled(double x1, double y1, double x2, double y2, double x)
        {
            double best = 2, y = 0;
            for (var i = 0; i <= 200000; i++)
            {
                var s = i / 200000.0; var u = 1 - s;
                var bx = 3 * u * u * s * x1 + 3 * u * s * s * x2 + s * s * s;
                if (Math.Abs(bx - x) < best) { best = Math.Abs(bx - x); y = 3 * u * u * s * y1 + 3 * u * s * s * y2 + s * s * s; }
            }
            return y;
        }
        foreach (var (x1, y1, x2, y2) in new[] { (0.42, 0.0, 0.58, 1.0), (0.35, 0.1, 0.55, 1.0), (0.25, 0.1, 0.25, 1.0) })
            foreach (var x in new[] { 0.1, 0.25, 0.5, 0.75, 0.9 })
                Assert.Equal(Sampled(x1, y1, x2, y2, x), LiquidTrack.CubicBezier(x1, y1, x2, y2, x), 3);
        Assert.Equal(0, LiquidTrack.CubicBezier(0.42, 0, 0.58, 1, 0));
        Assert.Equal(1, LiquidTrack.CubicBezier(0.42, 0, 0.58, 1, 1));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: the canvas's "breathe" (4.8 s; saturate 1 to 1.12, brightness 1 to 1.06)
    public void The_liquid_breathes_on_a_four_point_eight_second_cycle()
    {
        Assert.Equal((1.0, 1.0), LiquidTrack.Breathe(0));
        var (s, b) = LiquidTrack.Breathe(2.4);
        Assert.Equal(1.12, s, 9); Assert.Equal(1.06, b, 9);
        Assert.Equal(LiquidTrack.Breathe(1.0), LiquidTrack.Breathe(5.8));
        Assert.Equal(LiquidTrack.Breathe(1.0).Saturate, LiquidTrack.Breathe(3.8).Saturate, 9); // the two halves mirror
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
        Assert.True(left.Blue > left.Red + 60, $"heads end {left}");     // DeepSeek blue
        Assert.True(right.Red > right.Blue + 60, $"lead end {right}");   // Claude orange
        Assert.Equal(255, frame.GetPixel(120, 17).Alpha);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: the glass bends both rounded ends inward, so the liquid fills them
    public void The_liquid_reaches_both_rounded_ends_and_nothing_spills_outside_the_capsule() => headless.OnUiThread(() =>
    {
        using var frame = Liquid(LiquidSpec.For(Solo(EffortBrand.Claude)), 1.0);
        // just inside each end, on the middle line: liquid, fully opaque
        Assert.Equal(255, frame.GetPixel(2, 17).Alpha);
        Assert.Equal(255, frame.GetPixel(237, 17).Alpha);
        Assert.True(frame.GetPixel(2, 17).Red > 120, frame.GetPixel(2, 17).ToString());
        Assert.True(frame.GetPixel(237, 17).Red > 120, frame.GetPixel(237, 17).ToString());
        // outside the capsule's corners: nothing
        Assert.Equal(0, frame.GetPixel(0, 0).Alpha);
        Assert.Equal(0, frame.GetPixel(239, 33).Alpha);
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
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: every layer is a function of continuous time, so no loop ever jumps
    [InlineData(0.5)]
    [InlineData(37.25)]
    [InlineData(600.0)]
    public void One_frame_later_the_liquid_has_barely_moved(double seconds) => headless.OnUiThread(() =>
    {
        var spec = LiquidSpec.For(Solo(EffortBrand.Claude));
        using var now = Liquid(spec, seconds);
        using var next = Liquid(spec, seconds + 1 / 60.0);
        var delta = MaxDelta(now, next, 20, 220);
        Assert.True(delta <= 12, $"a frame step must not jump ({delta})");
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: currents and light keep the whole track's scale while the knob moves
    public void The_liquid_keeps_the_tracks_scale_when_the_fill_is_shorter() => headless.OnUiThread(() =>
    {
        var spec = LiquidSpec.For(Solo(EffortBrand.Meta));
        // the same 120 px fill, once as a whole track and once as part of a 300 px track: the currents differ
        using var whole = new RenderTargetBitmap(new PixelSize(120, 34));
        using (var c = whole.CreateDrawingContext())
            LiquidTrack.Draw(c, new Size(120, 34), 120, 3, spec, LiquidTrack.GradientFor(spec, 120, ThemeVariant.Light), null, false, ThemeVariant.Light);
        using var part = new RenderTargetBitmap(new PixelSize(120, 34));
        using (var c = part.CreateDrawingContext())
            LiquidTrack.Draw(c, new Size(120, 34), 300, 3, spec, LiquidTrack.GradientFor(spec, 120, ThemeVariant.Light), null, false, ThemeVariant.Light);
        using var a = Decode(whole); using var b = Decode(part);
        Assert.True(MaxDelta(a, b, 20, 100) > 4);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: the white liquid renders light
    public void A_silver_tube_renders_light() => headless.OnUiThread(() =>
    {
        using var frame = Frame(Effect(Solo(EffortBrand.Silver)), 1.0);
        var c = frame.GetPixel(120, 17);
        Assert.True(c.Red > 200 && c.Green > 200 && c.Blue > 200, c.ToString());
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0052: every rise of every bubble is new, mostly tiny, a few medium
    public void Every_rise_is_new_seeded_and_mostly_tiny()
    {
        var a = LiquidTrack.Rise(3, 0, 0, 9, 300, 34);
        Assert.Equal(a, LiquidTrack.Rise(3, 0, 0, 9, 300, 34));          // seeded: the same rise is the same
        Assert.NotEqual(a, LiquidTrack.Rise(3, 0, 1, 9, 300, 34));       // the next rise of the same bubble differs
        Assert.NotEqual(a, LiquidTrack.Rise(3, 1, 0, 9, 300, 34));       // another bubble differs
        Assert.NotEqual(a, LiquidTrack.Rise(4, 0, 0, 9, 300, 34));       // another track differs
        var many = Enumerable.Range(0, 2000).Select(k => LiquidTrack.Rise(5, k % 9, k / 9, 9, 300, 34)).ToArray();
        Assert.Equal(many.Length, many.Distinct().Count());              // no rise ever repeats
        var tiny = many.Count(b => b.Size < 3); var medium = many.Count(b => b.Size >= 5);
        Assert.InRange(tiny, many.Length * 0.5, many.Length * 0.7);      // mostly tiny (60 %)
        Assert.InRange(medium, many.Length * 0.05, many.Length * 0.15);  // a few medium (10 %)
        Assert.All(many, b => { Assert.InRange(b.Size, 1.4, 8); Assert.InRange(b.Gap, 0, 1.4); Assert.InRange(b.Duration, 1.8, 6.8); });
        Assert.Contains(many, b => b.P3.X >= 50);                        // some drift with the current
        Assert.Contains(many, b => b.P1.X < 0);                          // some rise to the left
        Assert.Contains(many, b => b.P1.X > 0 && b.P3.X < 50);           // some to the right
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0052: each bubble keeps its own stretch, so they stay spread out
    [InlineData(1, 300)]
    [InlineData(7, 224)]   // 7 bubbles: the stride steps off 7 so no two share a stretch
    [InlineData(11, 448)]  // 14 bubbles
    [InlineData(2, 720)]   // 22 bubbles
    public void Each_bubble_keeps_its_own_stretch_of_the_track(int seed, double width)
    {
        var lanes = new BubbleLanes(seed, width, 34);
        var n = lanes.Count;
        Assert.Equal(Math.Clamp((int)Math.Round(width / 32), 4, 24), n); // about one per 32 px
        var owners = new int[n];
        for (var i = 0; i < n; i++)
        {
            // where each rise is centred: a drifting rise starts half its drift early
            var stretches = Enumerable.Range(0, 200).Select(k => LiquidTrack.Rise(seed, i, k, n, width, 34))
                .Select(b => (b.Left + (b.P3.X >= 50 ? b.P3.X / 2 : 0)) / width * 100)
                .Select(c => (int)Math.Floor((c - 3) / 92 * n)).ToArray();
            var home = stretches.GroupBy(s => s).OrderByDescending(g => g.Count()).First();
            Assert.InRange(home.Count(), 140, 200); // about four rises in five in its own stretch, the rest anywhere
            owners[Math.Clamp(home.Key, 0, n - 1)]++;
        }
        Assert.All(owners, count => Assert.Equal(1, count)); // every stretch has exactly one bubble
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0052: a bubble rises, pauses, rises again differently; nothing loops
    public void A_bubble_lives_rise_after_rise_with_pauses_between()
    {
        var lanes = new BubbleLanes(9, 300, 34);
        var seen = new List<LiquidBubble>(); var gaps = 0; LiquidBubble? last = null; var wasNull = false;
        for (var time = 0.0; time < 120; time += 0.01)
        {
            var now = lanes.At(0, time);
            if (now is null) { if (!wasNull) gaps++; wasNull = true; continue; }
            wasNull = false;
            Assert.InRange(now.Value.Progress, 0, 1);
            if (!ReferenceEquals(now.Value.Rise, last))
            {
                // a new rise is first seen at its very start (within one 10 ms step): it never begins part-way through
                if (last is not null) Assert.True(now.Value.Progress <= 0.01 / now.Value.Rise.Duration + 1e-9, $"a rise began at {now.Value.Progress}");
                seen.Add(now.Value.Rise); last = now.Value.Rise;
            }
        }
        Assert.True(seen.Count > 15, $"rises in two minutes: {seen.Count}");
        Assert.Equal(4, new BubbleLanes(1, 40, 34).Count);     // never fewer than 4
        Assert.Equal(9, new BubbleLanes(1, 300, 34).Count);    // about one per 32 px
        Assert.Equal(24, new BubbleLanes(1, 2000, 34).Count);  // never more than 24
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.True(gaps > 5, $"pauses: {gaps}");
        // asking for an earlier moment starts again from the beginning, the same way
        var again = new BubbleLanes(9, 300, 34);
        Assert.Equal(lanes.At(0, 3.0), again.At(0, 3.0));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // DEVIATION OD-0051: the "wander" keyframes; a rise starts and ends invisible
    public void A_rise_follows_the_wander_keyframes_and_starts_and_ends_invisible()
    {
        var b = LiquidTrack.Rise(9, 0, 4, 9, 300, 34);
        Assert.Equal(0, LiquidTrack.BubbleAt(b, 34, 0).Opacity, 9);
        Assert.True(LiquidTrack.BubbleAt(b, 34, 0.9999).Opacity < 0.01);
        Assert.Equal(b.Opacity, LiquidTrack.BubbleAt(b, 34, 0.12).Opacity, 9);  // full from 12 %
        Assert.Equal(b.Opacity, LiquidTrack.BubbleAt(b, 34, 0.5).Opacity, 9);   // held to 70 %
        var (centre, radius, _) = LiquidTrack.BubbleAt(b, 34, 0);
        Assert.Equal(b.Left + b.Outer / 2, centre.X, 6);
        Assert.Equal(34 - b.Bottom - b.Outer / 2, centre.Y, 6);
        Assert.Equal(b.Outer / 2 * b.S0, radius, 6);
        var (at38, r38, _) = LiquidTrack.BubbleAt(b, 34, 0.38);                 // at the first waypoint, scaled s1
        Assert.Equal(b.Left + b.Outer / 2 + b.P1.X, at38.X, 6);
        Assert.Equal(b.Outer / 2 * b.S1, r38, 6);
        var end = LiquidTrack.BubbleAt(b, 34, 1).Centre;
        Assert.Equal(b.Left + b.Outer / 2 + b.P3.X, end.X, 6);
        Assert.Equal(34 - b.Bottom - b.Outer / 2 + b.P3.Y, end.Y, 6);
    }
}
