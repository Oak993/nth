using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using SkiaSharp;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-launch-fix (A): the glass programs a launch could otherwise link on the render thread after the hand-over (the material's
// effect whole and faded, open and under the hand-over's clip, a lone piece with its inside drawn apart and a capsule), made on a worker
// once for each build and GPU, and never waited for. That a worker's draw of a variant finds the program a window's draw linked is a
// native fact (ED-NEW-launch-fix (A): every variant drawn against a cache filled by real launches was a hit); these pin what the variants
// draw and when a pass runs.
[Collection("Headless")]
public sealed class GlassVariantPreparationTests(HeadlessSession headless)
{
    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void Every_number_of_pieces_is_drawn_whole_and_faded_open_and_under_the_hand_overs_clip()
    {
        var variants = GlassVariantPreparation.Variants;
        for (var count = 1; count <= GlassProgramVariants.Counts; count++)
            foreach (var faded in new[] { false, true })
                foreach (var clipped in new[] { false, true })
                {
                    Assert.Contains(new GlassVariantPreparation.Variant(count, faded, clipped, false), variants);
                    if (count == 1) Assert.Contains(new GlassVariantPreparation.Variant(1, faded, clipped, true), variants); // a lone capsule: no inside of its own
                }
        Assert.Equal(4 * GlassProgramVariants.Counts + 4, variants.Count);
        Assert.Equal(variants.Count, variants.Select(v => v.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(8, GlassProgramVariants.Counts); // the material's merged container programs (GlassMaterial.WarmShapes)
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void The_variants_draw_and_are_not_in_the_warm_up_a_first_run_waits_for()
    {
        Assert.Equal(GlassVariantPreparation.Variants.Select(v => v.Name), GlassVariantPreparation.Programs.Select(p => p.Name));
        Assert.All(GlassVariantPreparation.Programs, program => Assert.False(program.IsWork));
        Assert.DoesNotContain(GpuWarmup.Registered, program => program.Name.StartsWith("glass ", StringComparison.Ordinal));
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void A_variant_draws_the_glass_faded_when_faded_and_only_inside_the_hand_overs_clip_when_clipped() => headless.OnUiThread(() =>
    {
        var whole = Drawn(new(1, false, false, false));
        var faded = Drawn(new(1, true, false, false));
        var clipped = Drawn(new(1, false, true, false));
        var backdrop = new SKColor(24, 35, 58);
        // (45, 100): on the piece (40..300, 30..230), outside the clip (60..250, 50..200); (100, 120): on both
        Assert.NotEqual(backdrop, whole[(45, 100)]);
        Assert.Equal(backdrop, clipped[(45, 100)]);
        Assert.NotEqual(backdrop, clipped[(100, 120)]);
        Assert.Equal(whole[(100, 120)], clipped[(100, 120)]);
        Assert.NotEqual(whole[(100, 120)], faded[(100, 120)]);
        Assert.NotEqual(backdrop, faded[(100, 120)]);
    });

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public void Every_variant_draws_into_its_target() => headless.OnUiThread(() =>
    {
        foreach (var variant in GlassVariantPreparation.Variants)
            Assert.NotEqual(new SKColor(24, 35, 58), Drawn(variant)[(100, 56)]); // on every piece (a capsule is 40..160, 30..62) and inside the clip
    });

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public async Task A_pass_draws_every_variant_once_for_a_build_and_a_GPU_and_then_launches_do_nothing()
    {
        using var cache = new GpuProgramCacheFolder();
        var runner = GlassVariantPreparation.Runner;
        var runs = new List<string[]>();
        GlassVariantPreparation.Runner = programs => { runs.Add([.. programs.Select(p => p.Name)]); return Task.CompletedTask; };
        try
        {
            await GlassVariantPreparation.Prepare();
            Assert.Equal([.. GlassVariantPreparation.Programs.Select(p => p.Name)], Assert.Single(runs));
            Assert.Equal([GpuProgramCache.Build, Path.GetFileName(cache.Folder), GlassVariantPreparation.Variants.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                File.ReadAllLines(Path.Combine(cache.Root, GlassVariantPreparation.Marker)));
            await GlassVariantPreparation.Prepare();
            Assert.Single(runs); // done for this build and this GPU's folder

            File.WriteAllLines(Path.Combine(cache.Root, GlassVariantPreparation.Marker), ["another build", Path.GetFileName(cache.Folder), "36"]);
            await GlassVariantPreparation.Prepare();
            Assert.Equal(2, runs.Count); // a new build draws them again (found in the cache, unless its shaders changed)
        }
        finally { GlassVariantPreparation.Runner = runner; }
    }

    [Fact, Trait("Decision", "ED-NEW-launchfix-1")] // traceability alias of ED-NEW-launch-fix
    public async Task A_pass_that_fails_writes_no_marker_and_throws_nothing()
    {
        using var cache = new GpuProgramCacheFolder();
        var runner = GlassVariantPreparation.Runner;
        GlassVariantPreparation.Runner = _ => Task.FromException(new InvalidOperationException("no context"));
        try
        {
            await GlassVariantPreparation.Prepare();
            Assert.False(File.Exists(Path.Combine(cache.Root, GlassVariantPreparation.Marker)));
        }
        finally { GlassVariantPreparation.Runner = runner; }
    }

    // the target's pixels after the variant's draw (a CPU raster: no GPU here)
    private static Dictionary<(int, int), SKColor> Drawn(GlassVariantPreparation.Variant variant)
    {
        var inspect = GpuWarmup.WarmTarget.Inspect;
        var pixels = new Dictionary<(int, int), SKColor>();
        GpuWarmup.WarmTarget.Inspect = surface =>
        {
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            foreach (var at in new[] { (45, 100), (100, 120), (100, 56) }) pixels[at] = bitmap.GetPixel(at.Item1, at.Item2);
        };
        try { GlassVariantPreparation.Draw(new GpuWarmContext(null, 1), variant); }
        finally { GpuWarmup.WarmTarget.Inspect = inspect; }
        return pixels;
    }
}
