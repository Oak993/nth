using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// DEVIATION OD-0090 (locked 2026-10-03): the knob is the Violet Ember orb in the Jewel material (knob-orbs-1.js, ported from
// Liquid Orb Editor, MIT, LerSent001). The expected uniforms were recorded from the locked knob-orbs-1.js itself: makeOrb()
// for 'violet-ember' with the Jewel material of Claude, its frame(t, s) run at 60 fps against a recording WebGL2 stub
// (tests/fixtures/jewel-knob/expected.mjs, orbRun): a rest, a charge build, a flare and the charge let go.
public sealed class EffortSliderJewelOrbTests
{
    private static JewelUniforms Run(JsonElement run, bool reduce, int size, double perPx, bool dark, Action<JsonElement, JewelUniforms, JewelOrb> check)
    {
        var orb = new JewelOrb(JewelMaterial.For(EffortBrand.Claude));
        var u = new JewelUniforms();
        var frames = run.GetProperty("frames").EnumerateArray().ToArray();
        // the recorded frames are every 6th of 181; the orb is run at every 60 fps frame in between, as the page ran it
        var all = Enumerable.Range(0, 181).Select(i => i / 60.0).ToArray();
        var next = 0;
        foreach (var t in all)
        {
            var c = t < 0.5 ? 0 : t < 0.7 ? 0.58 * ((t - 0.5) / 0.2) : t < 1.2 ? 0.58 : 0;
            var f = t < 0.7 ? 0 : t < 1.2 ? 0.24 * Math.Exp(-(t - 0.7) / 0.3) : 0;
            orb.Frame(t, c, f, Math.PI * 0.9, reduce, size, perPx, dark, u);
            if (next < frames.Length && Math.Abs(frames[next].GetProperty("t").GetDouble() - t) < 1e-9) { check(frames[next], u, orb); next++; }
            if (next >= frames.Length) break;
        }
        Assert.Equal(frames.Length, next);
        return u;
    }

    private static void Near(double expected, double actual, string what, double tolerance = 2e-5) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance * Math.Max(1, Math.Abs(expected)), $"{what}: page {expected}, port {actual}");

    private static void AssertUniforms(JsonElement frame, JewelUniforms u, bool phase = true)
    {
        var e = frame.GetProperty("u");
        var t = frame.GetProperty("t").GetDouble();
        if (phase) Near(e.GetProperty("uT").GetDouble(), u.Phase, $"phase at {t}");
        var f = e.GetProperty("uF").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var (zoom, warp, ridge, sharp) = u.Fold;
        Near(f[0], zoom, $"zoom at {t}"); Near(f[1], warp, $"warp at {t}"); Near(f[2], ridge, $"ridgeAmt at {t}"); Near(f[3], sharp, $"sharp at {t}");
        Near(f[4], u.Values[8], $"shade at {t}"); Near(f[5], u.Values[9], $"sheen at {t}"); Near(f[6], u.Values[10], $"gloss at {t}");
        Near(f[7], u.Values[11], $"shellMidAlpha at {t}"); Near(f[8], u.Values[12], $"shellEdgeAlpha at {t}"); Near(f[9], u.Exposure, $"exposure at {t}");
        Near(f[10], u.Values[14], $"glassOpacity at {t}"); Near(f[11], u.ContourDeform, $"contourDeform at {t}");
        var c = e.GetProperty("uC").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        for (var i = 0; i < 12; i++)
        {
            var (r, g, b) = u.Color(i);
            Near(c[i * 3], r, $"colour {i} red at {t}"); Near(c[i * 3 + 1], g, $"colour {i} green at {t}"); Near(c[i * 3 + 2], b, $"colour {i} blue at {t}");
        }
        var fx = e.GetProperty("uFx").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Near(fx[0], u.Fx.Activation, $"activation at {t}"); Near(fx[1], u.Fx.Flare, $"flare at {t}"); Near(fx[2], u.Fx.Exit, $"exit at {t}"); Near(fx[3], u.Fx.Px, $"px at {t}");
        var kn = e.GetProperty("uKn").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Near(kn[0], u.Kn.BallRadius, $"ball radius at {t}"); Near(kn[1], u.Kn.Dark, $"dark at {t}"); Near(kn[2], u.Kn.PerPx, $"render px per px at {t}");
        var tint = e.GetProperty("uTint").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Near(tint[0], u.Tint.R, "tint red"); Near(tint[1], u.Tint.G, "tint green"); Near(tint[2], u.Tint.B, "tint blue");
        var size = e.GetProperty("uSize").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert.Equal(size[0], u.Values[0]); Assert.Equal(size[1], u.Values[1]);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: knob-orbs-1.js frame(): activation, integrated phase, state blend, uniforms (48 px at 1x: rendered at 96)
    public void Every_frame_sends_the_shader_what_the_locked_orb_sends()
    {
        var run = JewelFixtures.Expected.GetProperty("orb");
        var (size, perPx) = JewelOrb.RenderSize(48, 1);
        Assert.Equal((96, 2.0), (size, perPx)); // below 128 device px the orb renders at 2x, filtered down 2:1
        Assert.Equal(new[] { 96, 96 }, run.GetProperty("size").EnumerateArray().Select(x => x.GetInt32()));
        Run(run, false, size, perPx, true, (frame, u, _) => AssertUniforms(frame, u));
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: knob-orbs-1.js resize(): at 2x display scale 96 device px, still below 128, so 192 at 4 render px per px
    public void At_twice_the_display_scale_the_knob_still_renders_at_twice_its_pixels()
    {
        var (size, perPx) = JewelOrb.RenderSize(48, 2);
        Assert.Equal((192, 4.0), (size, perPx));
        Assert.Equal((128, 1.0), JewelOrb.RenderSize(128, 1)); // from 128 device px it renders at its own size
        var run = JewelFixtures.Expected.GetProperty("orbDpr2");
        var orb = new JewelOrb(JewelMaterial.For(EffortBrand.Claude));
        var u = new JewelUniforms();
        foreach (var frame in run.GetProperty("frames").EnumerateArray())
        {
            orb.Frame(frame.GetProperty("t").GetDouble(), 0, 0, Math.PI * 0.9, false, size, perPx, true, u);
            AssertUniforms(frame, u);
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: knob-orbs-1.js uKn.y: a light pane turns on the contact line and the brighter hairline
    public void On_a_light_pane_the_orb_is_told_so()
    {
        var run = JewelFixtures.Expected.GetProperty("orbLight");
        var orb = new JewelOrb(JewelMaterial.For(EffortBrand.Claude));
        var u = new JewelUniforms();
        foreach (var frame in run.GetProperty("frames").EnumerateArray())
        {
            orb.Frame(frame.GetProperty("t").GetDouble(), 0, 0, Math.PI * 0.9, false, 96, 2, false, u);
            AssertUniforms(frame, u);
        }
        Assert.Equal(0, u.Kn.Dark);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: knob-variants.js jewel() speeds (Idle 2.6, Thinking 4.2) and knob-orbs-1.js frame(): speed = 2.6 + 1.6 k, phase += dt speed
    public void The_gem_flows_at_two_point_six_at_rest_and_up_to_four_point_two_with_the_charge()
    {
        var orb = new JewelOrb(JewelMaterial.For(EffortBrand.Claude));
        var u = new JewelUniforms();
        orb.Frame(0, 0, 0, Math.PI, false, 96, 2, true, u);
        orb.Frame(1, 0, 0, Math.PI, false, 96, 2, true, u);
        Assert.Equal(2.6, orb.Speed, 12);
        Assert.Equal(0.1 * 2.6, orb.Phase, 12); // one frame moves at most 0.1 s (the orb's own clamp)
        orb.Frame(1.05, 0.5, 0, Math.PI, false, 96, 2, true, u);
        Assert.Equal(3.4, orb.Speed, 12);
        Assert.Equal(0.26 + 0.05 * 3.4, orb.Phase, 12); // integrated: a change of speed moves the phase on from where it is
        orb.Frame(1.1, 1, 0, Math.PI, false, 96, 2, true, u);
        Assert.Equal(4.2, orb.Speed, 12);
        Assert.Equal(0.26 + 0.05 * 3.4 + 0.05 * 4.2, orb.Phase, 12);
        // the charge let go: the activation settles back over 0.65 s (smoothstep), so the speed eases down, never drops
        orb.Frame(1.1 + 0.325, 0, 0, Math.PI, false, 96, 2, true, u);
        Assert.Equal(0.5, orb.Activation, 12);
        Assert.Equal(3.4, orb.Speed, 12);
        orb.Frame(1.1 + 0.7, 0, 0, Math.PI, false, 96, 2, true, u);
        Assert.Equal(0, orb.Activation);
        Assert.Equal(2.6, orb.Speed, 12);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: reduce motion stills the gem's flow, light only (frame(): k = 0, flare 0.6x; the colours still follow the activation)
    public void Reduce_motion_holds_the_gem_still_and_keeps_its_light()
    {
        var run = JewelFixtures.Expected.GetProperty("orbReduce");
        var last = Run(run, true, 96, 2, true, (frame, u, orb) =>
        {
            // the owner's ruling stills the flow, where knob-orbs-1.js lets it creep at 0.35x: everything else as the page
            AssertUniforms(frame, u, phase: false);
            Assert.Equal(0f, u.Phase);
            Assert.Equal(0, orb.Speed);
        });
        Assert.Equal(0f, last.Phase);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: the SkSL orb draws what knob-orbs-1.js's GLSL draws (Chrome's WebGL2 on SwiftShader, tests/fixtures/jewel-knob/orb-pixels.cjs)
    public void The_orb_draws_the_locked_orbs_pixels()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(StrikeFixtures.FixturePath("jewel-knob", "orb-pixels.json")));
        var report = new List<string>();
        foreach (var shot in document.RootElement.GetProperty("shots").EnumerateArray())
        {
            var maker = shot.GetProperty("maker").GetString()!;
            var dark = shot.GetProperty("dark").GetBoolean();
            var last = shot.GetProperty("frame").GetInt32();
            var orb = new JewelOrb(JewelMaterial.For(JewelFixtures.BrandOf(maker)));
            var u = new JewelUniforms();
            for (var i = 0; i <= last; i++)
            {
                var t = i / 60.0;
                var c = t < 0.5 ? 0 : t < 0.7 ? 0.58 * ((t - 0.5) / 0.2) : t < 1.2 ? 0.58 : 0;
                var f = t < 0.7 ? 0 : t < 1.2 ? 0.24 * Math.Exp(-(t - 0.7) / 0.3) : 0;
                orb.Frame(t, c, f, Math.PI * 0.9, false, 96, 2, dark, u);
            }
            var page = Convert.FromBase64String(shot.GetProperty("rgba").GetString()!);
            using var surface = SKSurface.Create(new SKImageInfo(96, 96, SKColorType.Rgba8888, SKAlphaType.Premul));
            JewelKnobRenderer.For(null, 96).Program(surface, 96, u.Values); // the fluid's three samples, then the glass
            using var image = surface.Snapshot();
            using var port = SKBitmap.FromImage(image);
            double sum = 0; int n = 0, far = 0, alphaOff = 0;
            for (var y = 0; y < 96; y++)
                for (var x = 0; x < 96; x++)
                {
                    var o = (y * 96 + x) * 4;
                    var p = port.GetPixel(x, y); // unpremultiplied, as getImageData gives the page's
                    if (Math.Abs(p.Alpha - page[o + 3]) > 8) alphaOff++;
                    if (page[o + 3] < 255 || p.Alpha < 255) continue;
                    var d = Math.Max(Math.Abs(p.Red - page[o]), Math.Max(Math.Abs(p.Green - page[o + 1]), Math.Abs(p.Blue - page[o + 2])));
                    sum += d; n++;
                    if (d > 3) far++;
                }
            var mean = sum / n;
            report.Add($"{maker} {(dark ? "dark" : "light")} {shot.GetProperty("state").GetString()}: mean {mean:F2}, over 3: {far}/{n}, alpha off: {alphaOff}");
            Assert.True(n > 6000, $"{maker}: the ball covers the canvas");
            // measured: one program 0.03-0.05; split into four (the currents kept to 1e-6, the orb in full floats) 0.09-0.17, none
            // over 3, alpha exact (the currents as plain half floats: 0.16-0.26; the orb in half floats: 0.14-0.30)
            Assert.True(mean < 0.2 && far <= n / 1000 && alphaOff <= 2, string.Join("; ", report));
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "jewel-orb-pixels.txt"), report);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the four-program split: each noise current is kept to about a millionth through its half-float surface (a part exact in a half float and the rest)
    public void The_noise_currents_come_through_their_half_float_surface_to_a_millionth()
    {
        var block = CurrentsFrame();
        var reference = Pixels(Whole(), block, SKColorType.RgbaF32);
        var kept = Pixels(JewelKnobShader.Noise, block, SKColorType.RgbaF16); // the knob's own currents surface
        double low = 0, cross = 0, plain = 0;
        var inside = 0;
        for (var i = 0; i < 96 * 96; i++)
        {
            if (reference[i * 4 + 3] != 1) continue; // outside the ball
            inside++;
            low = Math.Max(low, Math.Abs(kept[i * 4] + (kept[i * 4 + 1] - 0.5) / 512.0 - reference[i * 4]));
            cross = Math.Max(cross, Math.Abs(kept[i * 4 + 2] + (kept[i * 4 + 3] - 0.5) / 512.0 - reference[i * 4 + 1]));
            plain = Math.Max(plain, Math.Abs((float)(Half)reference[i * 4] - reference[i * 4]));
        }
        Assert.True(inside > 6000, $"the ball covers {inside} pixels");
        Assert.True(plain > 2e-5, $"precondition: one half float is off by {plain:E2} at most");
        var line = $"the currents come back off by {low:E2} (low) and {cross:E2} (cross) at most over {inside} pixels; one half float: {plain:E2}";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "jewel-currents.txt"), line);
        Assert.True(low < 2e-6 && cross < 2e-6, line);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the fluid program adds each current's two halves back: its samples are those of the currents unsplit
    public void The_fluid_reads_the_currents_back_whole()
    {
        var block = CurrentsFrame();
        using var exact = Surface(Whole(), block, SKColorType.RgbaF32);                  // the currents, full floats
        using var split = Surface(JewelKnobShader.Noise, block, SKColorType.RgbaF16);    // as the knob keeps them
        using var halves = SKSurface.Create(new SKImageInfo(96, 96, SKColorType.RgbaF16, SKAlphaType.Premul))!;
        using (var image = exact.Snapshot()) halves.Canvas.DrawImage(image, 0, 0);      // each current as one half float
        // the fluid over the currents as they are read: the real program over the split pair, a variant reading one value
        // each over the full floats (the reference) and over the single half floats (what a lost half would give)
        var source = JewelKnobShader.FluidSource;
        var read = source.IndexOf("half4 currents = noise.eval(coord);", StringComparison.Ordinal);
        Assert.True(read > 0);
        var from = source.IndexOf(';', read) + 1;
        var to = source.IndexOf(';', source.IndexOf(';', from) + 1) + 1; // the two statements that add the halves back
        using var direct = SKRuntimeEffect.CreateShader(source[..from] + " float lowX = float(currents.r); float crsX = float(currents.g);" + source[to..], out var errors)
            ?? throw new InvalidOperationException(errors);
        var real = Fluid(JewelKnobShader.Fluid, block, split);
        var whole = Fluid(direct, block, exact);
        var single = Fluid(direct, block, halves);
        double off = 0, lost = 0;
        for (var i = 0; i < real.Length; i++)
        {
            off = Math.Max(off, Math.Abs(real[i] - whole[i]));
            lost = Math.Max(lost, Math.Abs(single[i] - whole[i]));
        }
        var line = $"the fluid over the kept currents is off by {off:E2} at most; over single half floats {lost:E2}";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "jewel-fluid-currents.txt"), line);
        Assert.True(lost > 1e-4, "precondition: " + line);
        Assert.True(off < lost / 20, line);
    }

    // a charged frame of Claude's knob at 96 px, the middle dispersion sample
    private static float[] CurrentsFrame()
    {
        var orb = new JewelOrb(JewelMaterial.For(EffortBrand.Claude));
        var u = new JewelUniforms();
        for (var i = 0; i <= 50; i++) orb.Frame(i / 60.0, i < 30 ? 0 : 0.58, 0, Math.PI * 0.9, false, 96, 2, true, u);
        var block = u.Values[..(JewelUniforms.ShaderBytes / sizeof(float))];
        block[79] = 0;
        return block;
    }

    // the noise program cut short: its two currents returned unsplit (into a full-float surface)
    private static SKRuntimeEffect Whole()
    {
        var source = JewelKnobShader.NoiseSource;
        var at = source.IndexOf("float2 crs = lqFbm(", StringComparison.Ordinal);
        Assert.True(at > 0);
        return SKRuntimeEffect.CreateShader(source[..(source.IndexOf(';', at) + 1)] + " return half4(low.x, crs.x, 0.0, 1.0); }", out var errors)
            ?? throw new InvalidOperationException(errors);
    }

    private static SKSurface Surface(SKRuntimeEffect effect, float[] block, SKColorType type, SKSurface? child = null)
    {
        var uniforms = new SKRuntimeEffectUniforms(effect) { ["u"] = block };
        var children = new SKRuntimeEffectChildren(effect);
        using var image = child?.Snapshot();
        using var sampled = image?.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Nearest));
        if (sampled is not null) children["noise"] = sampled;
        using var shader = effect.ToShader(uniforms, children);
        var surface = SKSurface.Create(new SKImageInfo(96, 96, type, SKAlphaType.Premul))!;
        using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawRect(new SKRect(0, 0, 96, 96), paint);
        return surface;
    }

    private static float[] Pixels(SKRuntimeEffect effect, float[] block, SKColorType type)
    {
        using var surface = Surface(effect, block, type);
        return Read(surface);
    }

    private static float[] Fluid(SKRuntimeEffect effect, float[] block, SKSurface currents)
    {
        using var surface = Surface(effect, block, SKColorType.RgbaF32, currents);
        return Read(surface);
    }

    private static float[] Read(SKSurface surface)
    {
        var pixels = new float[96 * 96 * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { Assert.True(surface.ReadPixels(new SKImageInfo(96, 96, SKColorType.RgbaF32, SKAlphaType.Premul), handle.AddrOfPinnedObject(), 96 * 16, 0, 0)); }
        finally { handle.Free(); }
        return pixels;
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: the orb is SkSL runtime effects (no WebGL); ED-233 and the lead's ~50 ms a compile: four programs, each over the last's output at the same pixels
    public void The_knob_programs_are_valid_sksl_with_one_uniform_block()
    {
        foreach (var effect in new[] { JewelKnobShader.Noise, JewelKnobShader.Fluid, JewelKnobShader.Orb, JewelKnobShader.Page })
            Assert.Equal(JewelUniforms.ShaderBytes, effect.UniformSize);
        Assert.Empty(JewelKnobShader.Noise.Children);
        Assert.Equal(new[] { "noise" }, JewelKnobShader.Fluid.Children);
        Assert.Equal(new[] { "fluidR", "fluidG", "fluidB" }, JewelKnobShader.Orb.Children);
        Assert.Equal(new[] { "orbs" }, JewelKnobShader.Page.Children);
        // the four noise passes split two and two: each program runs two of them, once a pixel
        static int Fbm(string source) => System.Text.RegularExpressions.Regex.Matches(source, @"= lqFbm\(|lqRidgeS\(lqFbm\(").Count;
        Assert.Equal(2, Fbm(JewelKnobShader.NoiseSource));
        Assert.Equal(2, Fbm(JewelKnobShader.FluidSource));
        Assert.Equal(0, Fbm(JewelKnobShader.OrbSource) + Fbm(JewelKnobShader.PageSource));
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: the knob's binding of Skia's C API takes the parameter types SkiaSharp's own bindings take
    public void The_native_bindings_match_skiasharps_own()
    {
        var api = typeof(SKCanvas).Assembly.GetType("SkiaSharp.SkiaApi", true)!;
        foreach (var binding in JewelNative.Bindings)
        {
            var theirs = api.GetMethod(binding.Name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Assert.True(theirs is not null, binding.Name);
            var ours = binding.GetParameters();
            var their = theirs!.GetParameters();
            Assert.Equal(their.Length, ours.Length);
            for (var i = 0; i < ours.Length; i++)
            {
                static int Size(Type t) => t.IsByRef ? IntPtr.Size : t.IsPointer || t == typeof(nint) || t.IsArray ? IntPtr.Size : System.Runtime.InteropServices.Marshal.SizeOf(t.IsEnum ? Enum.GetUnderlyingType(t) : t);
                Assert.True(Size(their[i].ParameterType) == Size(ours[i].ParameterType), $"{binding.Name} parameter {i}: {their[i].ParameterType} vs {ours[i].ParameterType}");
            }
        }
    }

    // draws one frame of the knob into a raster canvas, as the render thread does
    internal static SKBitmap Draw(JewelUniforms u, double scale = 1, int pad = 0)
    {
        var px = (int)Math.Ceiling(48 * scale) + pad * 2;
        var bitmap = new SKBitmap(new SKImageInfo(px, px, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(pad, pad);
        canvas.Scale((float)scale);
        JewelKnobRenderer.For(null, (int)u.Values[0]).Draw(canvas, u.Values, new SKRect(0, 0, 48, 48), 1);
        return bitmap;
    }

    private static JewelUniforms Frame(EffortBrand brand, double charge = 0, double flare = 0, bool dark = true)
    {
        var orb = new JewelOrb(JewelMaterial.For(brand));
        var u = new JewelUniforms();
        orb.Frame(0, charge, flare, Math.PI, false, 96, 2, dark, u);
        u.SetPage(48, 0.26, 274, 298, 34, new SKColor(0x14, 0x1d, 0x27), new SKColor(0x11, 0x18, 0x20),
            [JewelColor.Parse("#B85A38"), JewelColor.Parse("#C26250"), JewelColor.Parse("#CC6B47"), JewelColor.Parse("#D67852"), JewelColor.Parse("#E0875F")]);
        return u;
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: the knob is an opaque glass ball in its maker's colour, nothing drawn past its edge
    public void The_knob_draws_a_ball_in_its_makers_colour()
    {
        using var claude = Draw(Frame(EffortBrand.Claude));
        Assert.Equal(255, claude.GetPixel(24, 24).Alpha);   // the centre of the gem
        Assert.Equal(0, claude.GetPixel(0, 0).Alpha);       // the corner, past the circle
        Assert.True(claude.GetPixel(24, 0).Alpha < 255); // the top edge is the anti-aliased limb, not a square
        var (r, g, b) = Mean(claude, 12, 36);
        Assert.True(r > g + 20 && r > b + 20, $"Claude's gem reads terracotta, got {r:F0},{g:F0},{b:F0}");
        using var deepseek = Draw(Frame(EffortBrand.Deepseek));
        var (r2, g2, b2) = Mean(deepseek, 12, 36);
        Assert.True(b2 > r2 + 20 && b2 > g2, $"DeepSeek's gem reads blue, got {r2:F0},{g2:F0},{b2:F0}");
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: the charge gathers light in the core, the flare surges through the body (knob-orbs-1.js MAIN's lightning)
    public void The_charge_and_the_flare_light_the_gem()
    {
        using var rest = Draw(Frame(EffortBrand.Claude));
        using var charged = Draw(Frame(EffortBrand.Claude, charge: 0.58));
        using var flaring = Draw(Frame(EffortBrand.Claude, charge: 0.58, flare: 0.24));
        double L(SKBitmap b) { var (r, g, bl) = Mean(b, 14, 34); return r + g + bl; }
        Assert.True(L(charged) > L(rest) + 10, $"charged {L(charged):F0} over rest {L(rest):F0}");
        Assert.True(L(flaring) > L(charged) + 10, $"flaring {L(flaring):F0} over charged {L(charged):F0}");
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090 (jewel-knob.html --knobShadow, s.lift): a shadow under the knob, a deeper one as it lifts
    public void A_lifted_knob_casts_a_deeper_shadow()
    {
        var u = Frame(EffortBrand.Claude);
        using var resting = Draw(u, pad: 30);
        u.Lift = 1;
        using var lifted = Draw(u, pad: 30);
        var below = (30 + 24, 30 + 48 + 6); // just under the knob
        Assert.True(resting.GetPixel(below.Item1, below.Item2).Alpha > 0, "the resting knob has its shadow");
        Assert.True(lifted.GetPixel(below.Item1, below.Item2).Alpha > resting.GetPixel(below.Item1, below.Item2).Alpha + 10, "the lift deepens it");
        Assert.Equal(0, resting.GetPixel(30 + 24, 30 - 12).Alpha); // nothing above, only below
    }

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: a frame allocates nothing managed on the render thread (one program, kept surfaces, native shaders)
    public void Drawing_a_frame_allocates_nothing()
    {
        var u = Frame(EffortBrand.Claude);
        using var bitmap = new SKBitmap(new SKImageInfo(48, 48, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        var renderer = JewelKnobRenderer.For(null, 96);
        renderer.Draw(canvas, u.Values, new SKRect(0, 0, 48, 48), 1); // warm (the program compiles, the surface exists)
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++) renderer.Draw(canvas, u.Values, new SKRect(0.5f * i, 0, 48 + 0.5f * i, 48), 1);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static (double R, double G, double B) Mean(SKBitmap b, int from, int to)
    {
        double r = 0, g = 0, bl = 0; var n = 0;
        for (var y = from; y < to; y++)
            for (var x = from; x < to; x++) { var p = b.GetPixel(x, y); r += p.Red; g += p.Green; bl += p.Blue; n++; }
        return (r / n, g / n, bl / n);
    }
}
