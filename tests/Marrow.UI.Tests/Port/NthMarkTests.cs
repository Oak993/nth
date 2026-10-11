using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

// OD-0136: the nth icon B. The drawing is checked against Chrome's rendering of the design's own sources (tests/fixtures/nth: the
// master svg and the boot stage's settled tile, copied from work/design/nth-brand/nth-brand.html; how the oracle PNGs were made is
// in its manifest.json). OD-0141: the boot motion (the tile's pop, its turning gradient and lifting shadow, the n's trim, the bead's
// spring; no shine) against its own curves, and the painter's frames (ED-NEW-bootpop-1 names the tests it replaced).
public sealed class NthMarkTests
{
    // ---- the drawing against the design (Chrome oracle) ----

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_icon_tile_matches_Chrome_drawing_the_master_svg()
    {
        var oracle = Oracle("chrome/mkB-256.png");
        using var surface = Surface(256, 256);
        using var mark = new NthMark();
        mark.Draw(surface.Canvas, new SKRect(0, 0, 256, 256), NthTileStyle.Icon, NthGlyph.Regular, NthPose.Still);
        AssertNear(oracle, surface, meanBound: 2.5, p99Bound: 12, maxBound: 40);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_boot_stage_tile_and_its_shadow_match_Chrome_drawing_the_design_stage()
    {
        var oracle = Oracle("chrome/stage-244.png");
        using var surface = Surface(360, 400);
        using var mark = new NthMark();
        var tile = new SKRect(58, 40, 58 + 244, 40 + 244);
        var unit = tile.Width / NthMark.TileSide;
        mark.DrawStageShadow(surface.Canvas, tile, 1);
        mark.Draw(surface.Canvas, new SKRect(tile.Left - 8 * unit, tile.Top - 8 * unit, tile.Left + 248 * unit, tile.Top + 248 * unit),
            NthTileStyle.Stage, NthGlyph.Regular, NthPose.Still);
        AssertNear(oracle, surface, meanBound: 2.5, p99Bound: 12, maxBound: 48);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_oracles_are_the_ones_made_from_the_design_sources()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures(), "manifest.json")));
        foreach (var source in manifest.RootElement.GetProperty("sources").EnumerateArray())
            Assert.Equal(source.GetProperty("sha256").GetString(), Hash(Path.Combine(Root(), source.GetProperty("path").GetString()!)));
        foreach (var frame in manifest.RootElement.GetProperty("frames").EnumerateArray())
            Assert.Equal(frame.GetProperty("sha256").GetString(), Hash(Path.Combine(Fixtures(), frame.GetProperty("file").GetString()!)));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_n_draws_from_its_left_foot_up_over_the_arch_and_down_the_right_stem()
    {
        // stroke-dashoffset 100 to 0 on pathLength 100: the visible share runs from the path's start (70,196).
        using var mark = new NthMark();
        SKColor At(double draw, float x, float y)
        {
            using var surface = Surface(256, 256);
            mark.Draw(surface.Canvas, new SKRect(0, 0, 256, 256), NthTileStyle.Icon, NthGlyph.Regular, NthPose.Still with { Draw = draw, BeadOpacity = 0 });
            return Pixel(surface, (int)x, (int)y);
        }
        static bool White(SKColor c) => c.Red > 180 && c.Green > 180 && c.Blue > 180; // gGlyph is white to --s300 #b9c0cd; the tile is graphite
        Assert.False(White(At(0, 70, 190))); // nothing drawn yet: the tile shows through
        Assert.True(White(At(.2, 70, 190))); // a fifth (56 of 282 units): the left stem's foot
        Assert.False(White(At(.2, 108, 82))); // ...not yet the arch's top
        Assert.True(White(At(.5, 108, 82))); // half (141 units: 72 up, then 69 of the arch's 138): up to the arch's top
        Assert.False(White(At(.5, 158, 190))); // ...not the right foot
        Assert.True(White(At(1, 158, 190)));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_thick_cut_and_a_glyph_fitted_off_the_symbol_draw_their_own_geometry()
    {
        using var mark = new NthMark();
        using var surface = Surface(256, 256);
        mark.Draw(surface.Canvas, new SKRect(0, 0, 256, 256), NthTileStyle.Icon, NthGlyph.Small, NthPose.Still);
        Assert.True(Pixel(surface, 50, 150).Red > 200); // mkBs' 36-unit stem reaches x 48 (mkB's 26-unit stem starts at 57)
        Assert.True(Pixel(surface, 194, 50).Red > 200); // its bead, 194,68 r20
        using var moved = Surface(256, 256);
        mark.Draw(moved.Canvas, new SKRect(0, 0, 256, 256), NthTileStyle.Icon, NthGlyph.Small with { Left = 60, BeadX = 200 }, NthPose.Still);
        Assert.True(Pixel(moved, 44, 150).Red > 200);
        Assert.True(Pixel(surface, 44, 150).Red < 120);
    }

    // ---- the boot motion (OD-0141: the tile pops toward the viewer, the n draws, the bead springs in; no shine) ----

    // 600 Hz samples of a value over [from, to].
    private static double[] Samples(Func<double, double> value, double from, double to, double step = 1 / 600.0) =>
        Enumerable.Range(0, (int)Math.Round((to - from) / step) + 1).Select(i => value(from + i * step)).ToArray();

    private static void Rising(double[] values, double slack = 0)
    {
        for (var i = 1; i < values.Length; i++) Assert.True(values[i] >= values[i - 1] - slack, $"sample {i} falls: {values[i - 1]} to {values[i]}");
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void The_tile_pops_from_eighty_six_hundredths_with_one_overshoot_under_two_percent_and_rests_exactly_at_one()
    {
        var start = NthMotion.Boot(0, false);
        Assert.Equal((.86, 0.0, 0.0), (start.TileScale, start.TileOpacity, start.Lift));
        var scales = Samples(t => NthMotion.Boot(t, false).TileScale, 0, NthMotion.TileSeconds);
        var peak = Array.IndexOf(scales, scales.Max());
        Assert.InRange(scales[peak], 1.005, 1.02); // it comes slightly forward, never more than 2% past its size
        Rising(scales[..(peak + 1)]); // one rise to the peak...
        Assert.All(scales[peak..], s => Assert.True(s >= .998, $"the tile sinks back visibly: {s}")); // ...then a settle with no second pop
        foreach (var t in new[] { NthMotion.TileSeconds, 1.0, 1.8, 30.0 }) Assert.Equal(1, NthMotion.Boot(t, false).TileScale);
        // its opacity rises only, from 0 to exactly 1 at .3 s
        var opacities = Samples(t => NthMotion.Boot(t, false).TileOpacity, 0, 1);
        Rising(opacities);
        Assert.True(NthMotion.Boot(.1, false).TileOpacity is > .3 and < 1);
        Assert.Equal(1, NthMotion.Boot(NthMotion.TileFadeSeconds, false).TileOpacity);
        // its shadow lifts with the same spring: lift 0 to exactly 1, a little past 1 with the overshoot
        foreach (var t in Samples(t => t, 0, NthMotion.TileSeconds, .01))
        {
            var p = NthMotion.Boot(t, false);
            Assert.Equal(p.TileScale, NthMotion.TileFrom + (1 - NthMotion.TileFrom) * p.Lift, 12);
        }
        Assert.Equal(1, NthMotion.Boot(NthMotion.TileSeconds, false).Lift);
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void The_n_draws_once_the_tile_is_two_thirds_through_its_spring_rising_smoothly_to_exactly_the_whole_n()
    {
        Assert.InRange(NthMotion.DrawStart / NthMotion.TileSeconds, .6, .7);
        Assert.All(Samples(t => NthMotion.Boot(t, false).Draw, 0, NthMotion.DrawStart), d => Assert.Equal(0, d));
        var draws = Samples(t => NthMotion.Boot(t, false).Draw, 0, 3);
        Rising(draws); // never steps back
        Assert.All(draws, d => Assert.InRange(d, 0, 1));
        // no jump: at 600 Hz no step exceeds three times the trim's mean speed (1 / 0.56 s); the ease's own peak is 2.7 times it
        var bound = 3 / (600 * NthMotion.DrawSeconds);
        for (var i = 1; i < draws.Length; i++) Assert.True(draws[i] - draws[i - 1] < bound, $"the draw jumps at sample {i}: {draws[i] - draws[i - 1]}");
        Assert.True(NthMotion.Boot(NthMotion.DrawStart + .05, false).Draw < .02); // eases in...
        Assert.True(NthMotion.Boot(NthMotion.DrawEnd - .05, false).Draw > .99); // ...and out
        foreach (var t in new[] { NthMotion.DrawEnd, 1.2, 1.8, 30.0 }) Assert.Equal(1, NthMotion.Boot(t, false).Draw);
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void The_bead_springs_in_from_four_tenths_only_after_the_n_is_complete_and_rests_when_the_mark_settles()
    {
        Assert.Equal(NthMotion.DrawEnd, NthMotion.BeadStart);
        foreach (var t in Samples(t => t, 0, 3))
        {
            var p = NthMotion.Boot(t, false);
            if (p.Draw < 1) Assert.Equal(0, p.BeadOpacity); // the dot never shows before the n is whole
        }
        var first = NthMotion.Boot(NthMotion.BeadStart, false);
        Assert.Equal((0.0, .4), (first.BeadOpacity, first.BeadScale));
        var scales = Samples(t => NthMotion.Boot(t, false).BeadScale, NthMotion.BeadStart, NthMotion.Settled);
        var peak = Array.IndexOf(scales, scales.Max());
        Assert.InRange(scales[peak], 1.02, 1.1); // a soft spring: one small overshoot
        Rising(scales[..(peak + 1)]);
        Assert.All(scales[peak..], s => Assert.True(s >= .995));
        var opacities = Samples(t => NthMotion.Boot(t, false).BeadOpacity, NthMotion.BeadStart, 3);
        Rising(opacities);
        Assert.Equal(1, NthMotion.Boot(NthMotion.BeadStart + NthMotion.BeadFadeSeconds, false).BeadOpacity);
        var rest = NthMotion.Boot(NthMotion.Settled, false);
        Assert.Equal((1.0, 1.0), (rest.BeadOpacity, rest.BeadScale));
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void The_mark_settles_within_one_point_four_seconds_then_holds_still_before_the_hand_over_with_no_shine_ever()
    {
        Assert.InRange(NthMotion.Settled, 1.1, 1.4);
        Assert.Equal(NthPose.Still, NthMotion.Boot(NthMotion.Settled, false));
        foreach (var t in Samples(t => t, NthMotion.Settled, 30, 1 / 60.0)) Assert.Equal(NthPose.Still, NthMotion.Boot(t, false)); // no hold sweeps
        Assert.All(Samples(t => NthMotion.Boot(t, false).Sweep, 0, 30, 1 / 60.0), s => Assert.Equal(0, s)); // no light band at any time
        Assert.True(NthMotion.Settled <= AuroraBootSequence.HandOver(0, false) - .3); // a brief hold before the unchanged hand-over
        Assert.Equal(1.8, AuroraBootSequence.HandOver(0, false));
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void The_tile_gradient_turns_from_twenty_two_degrees_off_and_rests_exactly_on_the_icon_gradient_as_the_bead_lands()
    {
        Assert.Equal(-22, NthMotion.Boot(0, false).Light);
        var lights = Samples(t => NthMotion.Boot(t, false).Light, 0, NthMotion.Settled);
        Rising(lights); // one way, toward rest
        for (var i = 1; i < lights.Length; i++) Assert.True(lights[i] - lights[i - 1] < .15, $"the light jumps at sample {i}"); // eased, no jump
        foreach (var t in new[] { NthMotion.Settled, 1.8, 30.0 })
        {
            var light = NthMotion.Boot(t, false).Light;
            Assert.Equal(0, light); Assert.False(double.IsNegative(light)); // exactly the resting angle, not a negative zero
        }
        // Drawn: the settled pose draws exactly the resting stage tile (the one the Chrome oracle checks), byte for byte; a turned
        // gradient moves the corners and leaves the centre, about which it turns, as it is.
        using var mark = new NthMark();
        byte[] Draw(in NthPose pose)
        {
            using var surface = Surface(256, 256);
            mark.Draw(surface.Canvas, new SKRect(0, 0, 256, 256), NthTileStyle.Stage, NthGlyph.Regular, pose);
            using var image = surface.Snapshot();
            using var pixels = image.PeekPixels();
            return pixels.GetPixelSpan().ToArray();
        }
        var still = Draw(NthPose.Still);
        Assert.Equal(still, Draw(NthMotion.Boot(NthMotion.Settled, false)));
        Assert.Equal(still, Draw(NthPose.Still with { Light = 0 }));
        var turned = Draw(NthPose.Still with { Light = -11 });
        static int At(byte[] p, int x, int y) => p[(y * 256 + x) * 4];
        Assert.True(Math.Abs(At(turned, 30, 226) - At(still, 30, 226)) >= 4, "the gradient does not move at the lower left corner");
        Assert.True(Math.Abs(At(turned, 128, 128) - At(still, 128, 128)) <= 2, "the gradient does not turn about the tile's centre");
        Assert.Equal(NthPose.Still, NthMotion.Boot(.5, true)); // reduced motion: the static gradient
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void Reduced_motion_is_the_finished_mark_at_every_moment()
    {
        foreach (var t in Samples(t => t, 0, 5, 1 / 60.0)) Assert.Equal(NthPose.Still, NthMotion.Boot(t, true));
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void The_shadow_lifts_from_close_under_the_tile_to_the_design_box_shadow()
    {
        using var mark = new NthMark();
        var tile = new SKRect(60, 40, 300, 280); // 240 px: symbol units are pixels
        byte Alpha(double lift, int below)
        {
            using var surface = Surface(360, 400);
            mark.DrawStageShadow(surface.Canvas, tile, 1, lift);
            return Pixel(surface, 180, (int)tile.Bottom + below).Alpha;
        }
        Assert.True(Alpha(0, 12) <= 4, "a resting shadow reaches out from under the tile");
        Assert.True(Alpha(1, 12) > Alpha(0, 12) + 15, "the lifted shadow does not fall out from under the tile");
        Assert.True(Alpha(1, 20) > Alpha(.5, 20) && Alpha(.5, 20) > Alpha(0, 20), "the shadow does not grow with the lift");
        Assert.True(Alpha(1.05, 40) >= Alpha(1, 40), "the overshoot does not lift the shadow a little further");
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void A_round_head_leads_the_draw_and_lands_flush_on_the_finished_feet()
    {
        using var mark = new NthMark();
        SKSurface At(double draw)
        {
            var surface = Surface(256, 256);
            mark.Draw(surface.Canvas, new SKRect(0, 0, 256, 256), NthTileStyle.Icon, NthGlyph.Regular, NthPose.Still with { Draw = draw, BeadOpacity = 0 });
            return surface;
        }
        // .15 of the 282-unit path: the head is on the left stem at y 196 - 42.3; a butt end would stop there, the round head
        // reaches 13 units (half the stroke) beyond it.
        using (var rising = At(.15))
        {
            Assert.True(White(Pixel(rising, 70, 196 - 42 - 8)), "no round head beyond the trim");
            Assert.False(White(Pixel(rising, 70, 196 - 42 - 16)), "the head runs beyond half the stroke");
        }
        // just before the end the head is clipped at the feet's line, so nothing hangs below the right foot
        using var nearly = At(.995);
        using var whole = At(1);
        Assert.True(White(Pixel(nearly, 158, 192)));
        SKColor below = Pixel(nearly, 158, 199), finished = Pixel(whole, 158, 199);
        Assert.True(Math.Abs(below.Red - finished.Red) <= 2 && Math.Abs(below.Alpha - finished.Alpha) <= 2, $"{below} below the right foot, finished {finished}");
        Assert.False(White(below));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_empty_chat_tile_sweeps_once_as_the_prompt_fades_in_then_stays_still()
    {
        Assert.Equal(NthPose.Still, NthMotion.Hero(0, false));
        Assert.Equal(NthPose.Still, NthMotion.Hero(.3, false));
        Assert.Equal(.3, NthMotion.HeroSweepDelay); Assert.Equal(.9, NthMotion.HeroDone, 9); // sweepOnce's .6 s pass, after the prompt's .3 s fade-in
        var mid = NthMotion.Hero(.6, false);
        Assert.Equal(NthMotion.Ease(.5, .45, 0, .2, 1), mid.Sweep, 9);
        Assert.Equal(NthPose.Still with { Sweep = mid.Sweep }, mid); // only the sweep moves
        Assert.False(NthMotion.Hero(NthMotion.HeroDone, false).Sweep is > 0 and < 1);
        Assert.False(NthMotion.Hero(30, false).Sweep is > 0 and < 1);
        Assert.Equal(NthPose.Still, NthMotion.Hero(1.6, true));
    }

    // ---- the boot painter draws the mark ----

    private static readonly Rect Window = new(0, 0, 1320, 860);

    private static SKSurface Boot(double seconds, double? ready = null, bool reduce = false, byte desktop = 200)
    {
        var surface = Surface((int)Window.Width, (int)Window.Height);
        surface.Canvas.Clear(new SKColor(desktop, desktop, desktop));
        using var painter = new AuroraBootPainter();
        painter.Draw(surface.Canvas, Window.Size, Window, new AuroraBootSequence(Window, reduce).At(seconds, ready), seconds, false, reduce);
        return surface;
    }

    // Symbol units on the stage tile at rest scale: .88 of 440 centred on the window (the hand-over's unchanged starting rectangle).
    private static (int X, int Y) Stage(double u, double v, double scale = 1)
    {
        var side = .88 * AuroraBootSequence.MarkSize * scale;
        return ((int)(660 - side / 2 + (u - 8) / 240 * side), (int)(430 - side / 2 + (v - 8) / 240 * side));
    }

    private static bool White(SKColor c) => c.Red > 180 && c.Green > 180 && c.Blue > 180;

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void The_boot_tile_forms_its_n_then_its_bead_and_settles_on_the_stage_tile()
    {
        static SKColor At(SKSurface s, (int X, int Y) p) => Pixel(s, p.X, p.Y);
        using (var early = Boot(.3)) { Assert.False(White(At(early, Stage(70, 150)))); Assert.False(White(At(early, Stage(186, 74)))); } // the tile, no mark yet
        Assert.True(NthMotion.Boot(.7, false).Draw is > .3 and < .8);
        using (var drawing = Boot(.7)) { Assert.True(White(At(drawing, Stage(70, 180)))); Assert.False(White(At(drawing, Stage(158, 190)))); Assert.False(White(At(drawing, Stage(186, 74)))); }
        using (var settled = Boot(NthMotion.Settled)) // settled, no readiness yet: no hand-over
        {
            Assert.True(White(At(settled, Stage(70, 150)))); Assert.True(White(At(settled, Stage(158, 150)))); Assert.True(White(At(settled, Stage(186, 74))));
            var centre = At(settled, Stage(128, 128)); // the stage tile's centre, inside the arch: Chrome draws (63,69,81)
            Assert.InRange(centre.Red, 55, 72); Assert.InRange(centre.Green, 61, 78); Assert.InRange(centre.Blue, 73, 90);
            var desktop = Pixel(settled, 20, 20); // far away: only the scrim (light: #080e1c at .46)
            Assert.InRange(desktop.Red, 111, 113); // 200 x .54 + 8 x .46 = 111.7
            var shadow = Pixel(settled, 660, (int)(430 + .88 * 440 / 2 + 20)); // 20 below the tile: its box-shadow on the desktop
            Assert.True(shadow.Red < desktop.Red - 5, $"no tile shadow below the tile: {shadow} against {desktop}");
        }
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void The_boot_tile_pops_in_smaller_and_fainter_and_no_light_ever_crosses_it()
    {
        // On a black desktop: at .05 s the tile is smaller and fading in; by .6 s it is whole at its own size.
        var pose = NthMotion.Boot(.05, false);
        Assert.True(pose.TileScale < .93 && pose.TileOpacity is > .05 and < .9);
        using var popping = Boot(.05, desktop: 0);
        using var popped = Boot(.6, desktop: 0);
        var edge = Stage(128, 12); // just inside the resting tile's top edge
        Assert.True(Pixel(popping, edge.X, edge.Y).Red <= 6, "the popping tile is not smaller than its rest");
        Assert.True(Pixel(popped, edge.X, edge.Y).Red > 30);
        var centre = Stage(128, 128);
        Assert.True(Pixel(popping, centre.X, centre.Y).Red < Pixel(popped, centre.X, centre.Y).Red - 15, "the popping tile is not fainter");
        // From the moment it is opaque to the hand-over the tile's centre never brightens: no band, streak or sweep passes it
        // (the gradient turns about this very point, so it holds its colour while the light moves).
        using var settled = Boot(NthMotion.Settled, desktop: 0);
        var rest = Pixel(settled, centre.X, centre.Y);
        for (var t = NthMotion.TileFadeSeconds; t <= 1.8; t += 1 / 30.0)
        {
            using var frame = Boot(t, desktop: 0);
            var c = Pixel(frame, centre.X, centre.Y);
            Assert.True(Math.Abs(c.Red - rest.Red) <= 3 && Math.Abs(c.Green - rest.Green) <= 3 && Math.Abs(c.Blue - rest.Blue) <= 3, $"at {t:F3} s the centre is {c}, at rest {rest}");
        }
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void Reduced_motion_fades_the_finished_mark_in_and_never_moves_it()
    {
        using var fading = Boot(.15, reduce: true, desktop: 0);
        using var shown = Boot(.6, reduce: true, desktop: 0);
        using var later = Boot(1.0, reduce: true, desktop: 0);
        var stem = Stage(70, 150);
        Assert.True(Pixel(fading, stem.X, stem.Y).Red < Pixel(shown, stem.X, stem.Y).Red - 40);
        Assert.Equal(Pixel(shown, stem.X, stem.Y), Pixel(later, stem.X, stem.Y));
        Assert.True(White(Pixel(shown, stem.X, stem.Y)));
    }

    [Fact]
    [Trait("Decision", "OD-0141")]
    public void Drawing_the_boot_mark_allocates_nothing_per_frame()
    {
        // Every 60 Hz frame of the mark, the hold and the reduced-motion fade, after one warm pass (the painter's caches and the
        // JIT): the drawing creates no managed object (transforms, opacity and the painter's own paints, paths and shaders only).
        var window = new Rect(0, 0, 900, 700);
        using var surface = Surface(900, 700);
        using var painter = new AuroraBootPainter();
        var moving = new AuroraBootSequence(window, false);
        var calm = new AuroraBootSequence(window, true);
        var times = Samples(t => t, 0, 1.8, 1 / 60.0);
        void Pass()
        {
            foreach (var t in times)
            {
                painter.Draw(surface.Canvas, window.Size, window, moving.At(t, null), t, true, false);
                painter.Draw(surface.Canvas, window.Size, window, calm.At(t, null), t, false, true);
            }
        }
        Pass();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Pass();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ---- helpers ----

    private static SKSurface Surface(int width, int height)
    {
        var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))!;
        surface.Canvas.Clear(SKColors.Transparent);
        return surface;
    }

    private static SKColor Pixel(SKSurface surface, int x, int y)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, x, y);
        return bitmap.GetPixel(0, 0);
    }

    private static SKBitmap Oracle(string file) => SKBitmap.Decode(Path.Combine(Fixtures(), file)).Copy(SKColorType.Rgba8888);

    // Premultiplied per-channel differences: their mean, 99th percentile and maximum (antialiased edges and Chrome's dithering
    // differ by a few levels; a wrong colour, gradient or shape moves the mean and the percentile).
    private static void AssertNear(SKBitmap oracle, SKSurface surface, double meanBound, double p99Bound, double maxBound)
    {
        using var drawn = new SKBitmap(new SKImageInfo(oracle.Width, oracle.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.True(surface.ReadPixels(drawn.Info, drawn.GetPixels(), drawn.RowBytes, 0, 0));
        using var expected = new SKBitmap(new SKImageInfo(oracle.Width, oracle.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var image = SKImage.FromBitmap(oracle)) Assert.True(image.ReadPixels(expected.Info, expected.GetPixels(), expected.RowBytes, 0, 0));
        var a = expected.GetPixelSpan(); var b = drawn.GetPixelSpan();
        var differences = new double[oracle.Width * oracle.Height];
        for (var i = 0; i < differences.Length; i++)
        {
            var d = 0;
            for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(a[i * 4 + c] - b[i * 4 + c]));
            differences[i] = d;
        }
        var sorted = differences.Order().ToArray();
        var mean = differences.Average(); var p99 = sorted[(int)(sorted.Length * .99)]; var max = sorted[^1];
        Assert.True(mean <= meanBound && p99 <= p99Bound && max <= maxBound, $"mean {mean:F2} (bound {meanBound}), p99 {p99} (bound {p99Bound}), max {max} (bound {maxBound})");
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Fixtures() => Path.Combine(Root(), "tests/fixtures/nth");
    private static string Root()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "assets-src/brand/nth-icon-b.svg"))) return at.FullName;
        throw new DirectoryNotFoundException("The nth design sources were not found.");
    }
}
