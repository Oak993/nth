using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

// OD-0136: the nth icon B and its boot motion. The drawing is checked against Chrome's rendering of the design's own sources
// (tests/fixtures/nth: the master svg and the boot stage's settled tile, copied from work/design/nth-brand/nth-brand.html; how the
// oracle PNGs were made is in its manifest.json), and the motion against the page's keyframes and their timing functions.
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

    // ---- the motion against the keyframes ----

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void Silver_gathers_over_six_tenths_from_a_soft_blur_and_four_fifths_scale_and_is_opaque_by_fifty_five_percent()
    {
        var start = NthMotion.Boot(0, false);
        Assert.Equal(0, start.GatherOpacity); Assert.Equal(.8, start.GatherScale, 12); Assert.Equal(NthMark.GatherSigma, start.GatherBlur, 6);
        Assert.Equal(14 * 240 / 243.9, NthMark.GatherSigma, 4); // blur(14px) on the page's 243.9 px stage tile
        Assert.Equal(1, NthMotion.Boot(.33, false).GatherOpacity, 9);
        Assert.True(NthMotion.Boot(.2, false).GatherOpacity < 1);
        var mid = NthMotion.Boot(.3, false);
        Assert.Equal(.8 + .2 * NthMotion.Ease(.5, .2, .8, .2, 1), mid.GatherScale, 12); // one cubic-bezier(.2,.8,.2,1) over the whole .6 s
        var done = NthMotion.Boot(.6, false);
        Assert.Equal((1, 1, 0), (done.GatherOpacity, done.GatherScale, done.GatherBlur));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_n_draws_from_thirty_eight_hundredths_over_seventy_two_hundredths_in_a_symmetric_ease()
    {
        Assert.Equal(0, NthMotion.Boot(.38, false).Draw);
        Assert.Equal(.5, NthMotion.Boot(.38 + .36, false).Draw, 6); // cubic-bezier(.65,0,.35,1) is point-symmetric about its middle
        Assert.True(NthMotion.Boot(.5, false).Draw < .1); // slow in
        Assert.Equal(1, NthMotion.Boot(1.1, false).Draw);
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_bead_drops_in_at_one_second_with_one_overshoot_and_rests_at_one_and_a_half()
    {
        Assert.Equal(0, NthMotion.Boot(.999, false).BeadOpacity);
        var first = NthMotion.Boot(1.0, false);
        Assert.Equal((0, -36, .3), (first.BeadOpacity, first.BeadLift, first.BeadScale));
        var rest = NthMotion.Boot(1.5, false);
        Assert.Equal((1, 0, 1), (rest.BeadOpacity, rest.BeadLift, rest.BeadScale));
        // cubic-bezier(.34,1.56,.64,1) overshoots once: the scale rises past 1 and comes back, one peak, never below 1 after it
        var scales = Enumerable.Range(0, 501).Select(i => NthMotion.Boot(1.0 + i * .001, false).BeadScale).ToArray();
        var peak = Array.IndexOf(scales, scales.Max());
        Assert.InRange(scales[peak], 1.05, 1.3);
        Assert.All(scales.Take(peak), (s, i) => Assert.True(s <= scales[i + 1] + 1e-12));
        Assert.All(scales.Skip(peak).Zip(scales.Skip(peak + 1)), pair => Assert.True(pair.Second <= pair.First + 1e-12));
        Assert.All(scales.Skip(peak), s => Assert.True(s >= 1 - 1e-12));
        Assert.Contains(Enumerable.Range(0, 501), i => NthMotion.Boot(1.0 + i * .001, false).BeadLift > 0); // it passes its rest once
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void Light_sweeps_once_from_one_eighteen_and_the_tile_breathes_once_to_one_point_zero_three_five()
    {
        Assert.Equal(0, NthMotion.Boot(1.18, false).Sweep);
        var mid = NthMotion.Boot(1.48, false);
        Assert.Equal(NthSweep.Hot, mid.SweepKind);
        Assert.Equal(NthMotion.Ease(.5, .45, 0, .2, 1), mid.Sweep, 9);
        Assert.Equal(1, NthMotion.Boot(1.78, false).Sweep);
        Assert.Equal(1, NthMotion.Boot(1.36, false).Breath);
        Assert.Equal(1.035, NthMotion.Boot(1.36 + .45 * .5, false).Breath, 9);
        Assert.Equal(1, NthMotion.Boot(NthMotion.Settled, false).Breath, 9);
        Assert.True(NthMotion.Boot(1.5, false).Breath is > 1 and < 1.035);
        Assert.Equal(NthPose.Still, NthMotion.Boot(NthMotion.Settled, false) with { Sweep = 0, SweepKind = NthSweep.Soft });
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_long_hold_repeats_the_soft_sweep_slowly_and_reduced_motion_is_the_finished_mark()
    {
        foreach (var t in new[] { 2.0, 3.0, 4.18, 5.38, 7.0 }) Assert.False(NthMotion.Boot(t, false).Sweep is > 0 and < 1);
        var hold = NthMotion.Boot(4.78, false);
        Assert.Equal(NthSweep.Soft, hold.SweepKind); Assert.Equal(NthMotion.Ease(.5, .45, 0, .2, 1), hold.Sweep, 9);
        Assert.Equal(NthMotion.Ease(.5, .45, 0, .2, 1), NthMotion.Boot(7.78, false).Sweep, 9); // every 3 s
        foreach (var t in new[] { 0, .2, .8, 1.2, 1.5, 5.0 }) Assert.Equal(NthPose.Still, NthMotion.Boot(t, true));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_empty_chat_tile_sweeps_once_as_the_prompt_fades_in_then_stays_still()
    {
        Assert.Equal(NthPose.Still, NthMotion.Hero(0, false));
        Assert.Equal(NthPose.Still, NthMotion.Hero(.3, false));
        Assert.Equal(.3, NthMotion.HeroSweepDelay); Assert.Equal(.9, NthMotion.HeroDone, 9); // sweepOnce's .6 s pass, after the prompt's .3 s fade-in
        var mid = NthMotion.Hero(.6, false);
        Assert.Equal(NthSweep.Soft, mid.SweepKind); Assert.Equal(NthMotion.Ease(.5, .45, 0, .2, 1), mid.Sweep, 9);
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
    [Trait("Decision", "OD-0136")]
    public void The_boot_tile_forms_its_n_then_its_bead_and_settles_on_the_stage_tile()
    {
        static SKColor At(SKSurface s, (int X, int Y) p) => Pixel(s, p.X, p.Y);
        using (var early = Boot(.3)) { Assert.False(White(At(early, Stage(70, 150)))); Assert.False(White(At(early, Stage(186, 74)))); } // the tile, no mark yet
        using (var drawing = Boot(.8)) { Assert.True(White(At(drawing, Stage(70, 180)))); Assert.False(White(At(drawing, Stage(158, 190)))); Assert.False(White(At(drawing, Stage(186, 74)))); }
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
    [Trait("Decision", "OD-0136")]
    public void The_boot_tile_gathers_in_from_a_blur_and_the_hot_sweep_brightens_it_once()
    {
        // On a black desktop: at .12 s the tile is smaller, fading in and blurred, so light spills above its edge; at .6 s it is sharp.
        using var gathering = Boot(.12, desktop: 0);
        using var gathered = Boot(.6, desktop: 0);
        var pose = NthMotion.Boot(.12, false);
        Assert.True(pose.GatherBlur > 3 && pose.GatherOpacity is > .5 and < 1);
        var top = Stage(128, 8, pose.GatherScale).Y; // the gathering tile's top edge, mid-width
        var settledTop = Stage(128, 8).Y;
        Assert.True(Pixel(gathering, 660, top - 6).Red > 15, "no blur above the gathering tile's edge");
        Assert.True(Pixel(gathered, 660, settledTop - 6).Red <= 6, "the gathered tile's edge is not sharp");
        Assert.True(Pixel(gathering, 660, top + 6).Red < Pixel(gathered, 660, settledTop + 6).Red);
        // the hot band's centre crosses the tile's centre when the sweep is half done
        var half = Enumerable.Range(0, 601).Select(i => 1.18 + i * .001).MinBy(t => Math.Abs(NthMotion.Boot(t, false).Sweep - .5));
        using var sweeping = Boot(half);
        using var after = Boot(1.9);
        var centre = Stage(128, 128);
        Assert.True(Pixel(sweeping, centre.X, centre.Y).Red > Pixel(after, centre.X, centre.Y).Red + 60);
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
