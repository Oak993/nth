using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.ComposerLights;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the composer light's renderer, drawn on the playground's reference card
// (beam-fix/pill.js framing). Expectations follow the playground: Border beam lives inside the composer (its slot clips to
// the composer's corner radius), the Uiverse light's halo outside it, Off draws nothing (logic.js panes: "Nothing is drawn").
public sealed class ComposerLightRendererTests
{
    [Fact]
    [Trait("Decision", "ED-251")] // the board's port notes: one SkSL shader per light and frame (and Large/Small's bake); all compile
    public void Every_shader_compiles()
    {
        Assert.NotNull(ComposerLightShaders.Rotate);
        Assert.NotNull(ComposerLightShaders.RotateBake);
        Assert.NotNull(ComposerLightShaders.Moving);
        Assert.NotNull(ComposerLightShaders.Aura);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // evidence: the frames of a reference job list (MARROW_COMPOSER_LIGHT_JOBS), written to MARROW_COMPOSER_LIGHT_EVIDENCE
    public void Evidence_frames_of_the_reference_jobs()
    {
        var jobs = Environment.GetEnvironmentVariable("MARROW_COMPOSER_LIGHT_JOBS");
        if (string.IsNullOrEmpty(jobs)) return;
        foreach (var job in ComposerLightJob.Load(jobs))
        {
            var dark = job.Th == "dark";
            var light = ComposerLightRig.Light(job.Settings(), dark, job.Phase);
            light.Still(job.T, job.TH ?? job.T, job.AuraP ?? 0, job.AuraBp ?? 0, 0);
            using var frame = ComposerLightRig.Render(light, dark);
            ComposerLightRig.Evidence(frame, job.Name.Replace("ref_", "port_", StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0083 "no per-frame full-size blur": while the composer grows the bakes wait (drawn analytically, the old blob fields stretched); 150 ms still, they follow the size once
    public void Bakes_follow_a_size_change_once_it_is_still()
    {
        using var renderer = new ComposerLightRenderer();
        var light = ComposerLightRig.Light(ComposerLightSettings.Default, dark: true);
        light.Still(0.35, 0.35, 0, 0, 0);
        using (ComposerLightRig.Render(light, true, renderer: renderer, now: 0)) { }
        var first = renderer.BakeCount;
        Assert.Equal(3, first);                                        // the inset shadow, the bloom's edge, the blob fields
        for (var i = 1; i <= 10; i++)
        {
            light.SetSize(820, 52 + i, i / 60.0);
            light.Still(0.35, 0.35, 0, 0, i / 60.0);
            using var moving = ComposerLightRig.Render(light, true, renderer: renderer, h: 52 + i, now: i / 60.0);
            Assert.True(renderer.Settling);
            if (i == 10)
            {
                using var fresh = ComposerLightRig.Render(light, true, h: 62);
                var (max, off) = ComposerLightFixtures.Compare(fresh, moving);
                Assert.True(max <= 40 && off <= 0.5, $"the analytic frame differs by {max} on {off:F2} % of pixels");
            }
        }
        Assert.Equal(first, renderer.BakeCount);                       // nothing baked while it moved
        using var settled = ComposerLightRig.Render(light, true, renderer: renderer, h: 62, now: 10 / 60.0 + 0.2);
        Assert.Equal(first + 3, renderer.BakeCount);
        Assert.False(renderer.Settling);
        using var reference = ComposerLightRig.Render(light, true, h: 62);
        Assert.Equal(0, ComposerLightFixtures.Compare(reference, settled).Max);   // the settled bake is the size's own
    }

    // the share of the outline the ray from c through p leaves the 820 x 52 (r 22) box at, found on the outline itself
    // (BorderBeamMath.PerimeterAt, beam.js bbPerim) by the nearest angle: where beam.js's conics put their stops
    private static double RayShare(double cx, double cy, double px, double py)
    {
        var want = Math.Atan2(px - cx, cy - py);
        double best = 0, err = double.MaxValue;
        for (var i = 0; i < 200000; i++)
        {
            var u = i / 200000.0;
            var (x, y) = BorderBeamMath.PerimeterAt(820, 52, 22, u);
            var d = Math.Abs(Math.IEEERemainder(Math.Atan2(x - cx, cy - y) - want, 2 * Math.PI));
            if (d < err) { err = d; best = u; }
        }
        return best;
    }

    [Theory]
    [Trait("Decision", "ED-251")] // beam.js bbInnerPieces / bbRingConic: the inner wash's ends are conics about (H/2, H/2) and (W - H/2, H/2); the ring's about the centre; the straight middle is linear in x
    [InlineData(24, 26, 0)]        // left end, centre row: the ray from (26, 26) leaves at the left edge
    [InlineData(10, 40, 0)]
    [InlineData(800, 12, 1)]       // right end
    [InlineData(300, 10, 2)]       // the straight top: (x - W/2) / L
    [InlineData(500, 45, 3)]       // the straight bottom
    [InlineData(5, 5, 4)]          // a ring pixel: the ray from the centre
    [InlineData(100, 20, 4)]       // inside, as the bloom's samples read it: still the ray from the centre
    [InlineData(700, 40, 4)]
    public void The_outline_share_is_where_the_playground_puts_its_stops(int x, int y, int kind)
    {
        var source = ComposerLightShaders.BeamUniforms + """
            uniform shader shadowTex;
            uniform shader bloomTex;
            uniform shader profiles;
            """ + ComposerLightShaders.BeamFunctions + """
            half4 main(float2 p) { return half4(half(uInner(p)), half(uCentre(p)), 0.0, 1.0); }
            """;
        using var effect = SKRuntimeEffect.CreateShader(source, out var error) ?? throw new InvalidOperationException(error);
        using var u = new SKRuntimeEffectUniforms(effect);
        u["box"] = new SKSize(820, 52);
        u["geo"] = new[] { 22f, 21f, 1f, 0f };
        u["win"] = new[] { 0f, 22f, 1f, 0f };
        using var nothing = SKShader.CreateColor(SKColors.Transparent);
        using var c = new SKRuntimeEffectChildren(effect) { ["shadowTex"] = nothing, ["bloomTex"] = nothing, ["profiles"] = nothing };
        using var shader = effect.ToShader(u, c);
        var info = new SKImageInfo(820, 52, SKColorType.RgbaF32, SKAlphaType.Unpremul);
        using var surface = SKSurface.Create(info);
        using (var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src }) surface.Canvas.DrawRect(new SKRect(0, 0, 820, 52), paint);
        var pixels = new float[820 * 52 * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { surface.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0); }
        finally { handle.Free(); }
        double inner = pixels[(y * 820 + x) * 4], centre = pixels[(y * 820 + x) * 4 + 1];
        double px = x + 0.5, py = y + 0.5, L = BorderBeamMath.PerimeterLength(820, 52, 22);
        double expected = kind switch
        {
            0 => RayShare(26, 26, px, py),
            1 => RayShare(820 - 26, 26, px, py),
            2 => BorderBeamMath.Frac((px - 410) / L),
            3 => ((410 - 22) + Math.PI * 22 + (52 - 44) + (820 - 22 - px)) / L,
            _ => RayShare(410, 26, px, py),
        };
        Assert.Equal(expected, kind == 4 ? centre : inner, 2);          // half precision: 1/100 of the turn
    }

    [Theory]
    [Trait("Decision", "ED-251")] // REVIEW: beamSlots() (logic.js): a structural change goes into the slot faded out longest, styled by its own key's sheet at once. A reused slot must never draw the blob field of the structure it held before (only a size change may stretch a settling bake), or the colours pop when the bake lands 150 ms in
    [InlineData(BeamType.Large, true)]
    [InlineData(BeamType.Small, false)]
    public void A_reused_slot_never_draws_the_structure_it_held_before(BeamType type, bool dark)
    {
        using var renderer = new ComposerLightRenderer();
        var b0 = BorderBeamOptions.Default.WithType(type);
        var light = new ComposerLightAnimator(ComposerLightSettings.Default with { Beam = b0 }, ComposerLightPhase.Rest, dark, 820, 52, false);
        var t = 0.0;
        void Draw(double now) { light.Advance(now); using (ComposerLightRig.Render(light, dark, renderer: renderer, now: now)) { } }
        Draw(0);
        // Ocean and Sunset take the two fresh slots; Forest reuses the first (it held Colorful's blobs), Candy the second
        foreach (var palette in new[] { BeamPalette.Ocean, BeamPalette.Sunset, BeamPalette.Forest, BeamPalette.Candy })
        {
            t += 1;
            for (var s = t - 0.95; s < t; s += 0.05) light.Advance(s);
            light.SetSettings(ComposerLightSettings.Default with { Beam = b0 with { Palette = palette } }, t);
            foreach (var dt in new[] { 0.02, 0.1, 0.14 })
            {
                light.Advance(t + dt);
                using var drawn = ComposerLightRig.Render(light, dark, renderer: renderer, now: t + dt);
                using var correct = ComposerLightRig.Render(light, dark);                       // a renderer with every bake its key's own
                var (max, off) = ComposerLightFixtures.Compare(correct, drawn);
                Assert.True(max <= 8, $"{palette} at +{dt} s: {max} levels off on {off:F2} % of pixels (the slot's previous structure)");
            }
        }
    }
}
