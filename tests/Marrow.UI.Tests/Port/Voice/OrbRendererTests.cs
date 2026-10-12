using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.UI.Port.Voice.Orb;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// VT-8, V-T51 (V-11.3): the shader runs into a reused offscreen surface at 2x and is filtered down; no per-frame allocation of anything the port owns. The design stops
// supersampling at 128 device pixels (voice-orbs.js:496); the view supersamples at every scale (ED-NEW-voicefix-ui-7: at 200% the straight draw's rim aliased), so the
// rows from 128 expect 2x and the straight draw is pinned on a runtime built without supersampling.
public sealed class OrbRendererTests
{
    private static readonly OrbJob Rest = new("siri-wave", "rest-1.3", 32, "dark");

    [Theory]
    [InlineData(1.0, 72, 2)]
    [InlineData(1.25, 90, 2)]
    [InlineData(1.5, 108, 2)]
    [InlineData(1.75, 126, 2)]
    [InlineData(1.7778, 128, 2)]
    [InlineData(2.0, 144, 2)]
    [InlineData(3.0, 216, 2)]
    [InlineData(0.5, 36, 2)]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    [Trait("Decision", "ED-NEW-reltrace-1")] // ED-NEW-voicefix-ui-7
    public void The_shader_runs_at_2x_at_every_scale(double dpr, int devicePx, int scale)
    {
        var runtime = OrbRig.Runtime(Rest, dpr);
        Assert.Equal(devicePx, runtime.DevicePx);
        Assert.Equal(scale, runtime.RenderScale);
        Assert.Equal(devicePx * scale, runtime.RenderPx);
        runtime.Frame(0, Rest.FrameState);
        Assert.Equal(runtime.RenderPx, runtime.Values[0]);   // the block's size is the render surface's
        Assert.Equal(runtime.RenderPx, runtime.Values[1]);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void One_surface_is_made_and_reused_for_every_frame()
    {
        using var renderer = new OrbRenderer();
        var runtime = OrbRig.Runtime(Rest);
        for (var i = 0; i < 40; i++)
        {
            runtime.Frame(i / 60.0, Rest.FrameState);
            using var frame = OrbRig.Render(runtime, 1, renderer, SKColors.Transparent);
        }
        Assert.Equal(1, renderer.SurfaceCreations);
        Assert.Equal(144, renderer.SurfacePx);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void The_surface_is_remade_only_when_the_render_size_changes()
    {
        using var renderer = new OrbRenderer();
        foreach (var dpr in new[] { 1.0, 1.0, 1.25, 1.25, 1.0 })
        {
            var runtime = OrbRig.Runtime(Rest, dpr);
            runtime.Frame(0, Rest.FrameState);
            using var frame = OrbRig.Render(runtime, dpr, renderer, SKColors.Transparent);
        }
        Assert.Equal(3, renderer.SurfaceCreations);          // 144, 180, 144
        Assert.Equal(144, renderer.SurfacePx);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    [Trait("Decision", "ED-NEW-reltrace-1")] // ED-NEW-voicefix-ui-7
    public void A_runtime_built_without_supersampling_draws_straight_with_no_surface()
    {
        using var renderer = new OrbRenderer();
        var runtime = new VoiceOrbRuntime(Rest.Kind, new VoiceOrbRuntimeOptions(OrbScene.ViewPx, Rest.Ball, 2, Rest.Dark, Supersample: false, Phase: Rest.Phase));
        runtime.Frame(0, Rest.FrameState);
        using var frame = OrbRig.Render(runtime, 2, renderer, SKColors.Transparent);
        Assert.Equal(0, renderer.SurfaceCreations);
        Assert.Equal(144, frame.Width);
        Assert.True(frame.GetPixel(72, 72).Alpha > 250);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void The_2x_frame_is_the_1x_frame_of_twice_the_size_filtered_down()
    {
        // the same block (render size 144) drawn at 2x into the 72 px slot and drawn straight at 144 then averaged 2 x 2
        using var supersampled = OrbRig.Render(Rest, 1);
        var direct = new VoiceOrbRuntime(Rest.Kind, new VoiceOrbRuntimeOptions(OrbScene.ViewPx, Rest.Ball, 2, Rest.Dark, Supersample: false, Phase: Rest.Phase));
        direct.Frame(Rest.T, Rest.FrameState);
        using var full = OrbRig.Render(direct, 2, null, OrbRig.DarkCard);
        using var down = new SKBitmap(new SKImageInfo(72, 72, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < 72; y++)
            for (var x = 0; x < 72; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (var dy = 0; dy < 2; dy++)
                    for (var dx = 0; dx < 2; dx++)
                    {
                        var p = full.GetPixel(2 * x + dx, 2 * y + dy);
                        r += p.Red; g += p.Green; b += p.Blue; a += p.Alpha;
                    }
                down.SetPixel(x, y, new SKColor((byte)((r + 2) / 4), (byte)((g + 2) / 4), (byte)((b + 2) / 4), (byte)((a + 2) / 4)));
            }
        var (within, max) = OrbRig.Compare(supersampled, down, 2);
        Assert.True(within >= 0.999 && max <= 4, $"{within:P2} within 2, largest difference {max}");
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void The_uniform_block_is_one_set_a_frame_and_the_port_adds_no_array_of_its_own()
    {
        var runtime = OrbRig.Runtime(Rest);
        runtime.Frame(0, Rest.FrameState);
        using var uniforms = new SKRuntimeEffectUniforms(OrbShaders.Effect);
        var frame = new float[OrbUniformLayout.FrameFloats];
        for (var i = 0; i < 20; i++) OrbUniformLayout.Write(uniforms, frame, runtime.Values, (float)runtime.Mrw.Lift, (float)runtime.Mrw.Desat, (float)runtime.Mrw.Dark);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++) OrbUniformLayout.Write(uniforms, frame, runtime.Values, (float)runtime.Mrw.Lift, (float)runtime.Mrw.Desat, (float)runtime.Mrw.Dark);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 200;
        Console.WriteLine($"uniform set: {perFrame} B/frame (SkiaSharp copies the array it is given)");
        // SkiaSharp's setter copies the 140 floats (560 bytes) into its own array: that copy is the only allocation
        Assert.True(perFrame <= OrbUniformLayout.FrameFloats * sizeof(float) + 64, $"{perFrame} B");
    }

    [Theory]
    [InlineData("rest-1.3")]
    [InlineData("hover")]
    [InlineData("processing")]
    [InlineData("blocked")]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void The_runtimes_frame_allocates_nothing_in_every_state(string state)
    {
        var job = new OrbJob("siri-wave", state, 32, "dark");
        var runtime = OrbRig.Runtime(job);
        var frameState = job.FrameState;
        var bytes = Marrow.Testing.AllocationProbe.Measure(
            () => { for (var i = 0; i < 20; i++) runtime.Frame(i / 60.0, frameState); },
            () => { for (var i = 20; i < 220; i++) runtime.Frame(i / 60.0, frameState); });
        Assert.Equal(0, bytes);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void While_listening_a_frame_allocates_nothing_either()
    {
        // V-T51 "0 B per frame" while Listening too: VT-4's OrbExportRuntime.ApplyAudioUniforms took the bands with `foreach` over an IReadOnlyList, which boxed an
        // enumerator (32 B a Listening frame); the Windows review (unit/integ-vt8) made it an indexed loop.
        var job = new OrbJob("siri-wave", "listen-speech", 32, "dark");
        var runtime = OrbRig.Runtime(job);
        for (var i = 0; i < 20; i++) runtime.Frame(i / 60.0, job.FrameState);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 20; i < 220; i++) runtime.Frame(i / 60.0, job.FrameState);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void A_frame_drawn_makes_no_surface_bitmap_or_buffer_only_skias_wrappers()
    {
        using var renderer = new OrbRenderer();
        var runtime = OrbRig.Runtime(Rest);
        using var target = SKSurface.Create(new SKImageInfo(72, 72, SKColorType.Rgba8888, SKAlphaType.Premul));
        var state = Rest.FrameState;
        for (var i = 0; i < 4; i++)
        {
            runtime.Frame(i / 60.0, state);
            renderer.Draw(target.Canvas, null, runtime.Values, runtime.Mrw, runtime.DevicePx, runtime.RenderPx, 72);
        }
        const int frames = 20;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < frames; i++)
        {
            runtime.Frame((4 + i) / 60.0, state);
            renderer.Draw(target.Canvas, null, runtime.Values, runtime.Mrw, runtime.DevicePx, runtime.RenderPx, 72);
        }
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / frames;
        Console.WriteLine($"orb draw: {perFrame} B/frame managed");
        // a 144 x 144 RGBA surface is 82 944 bytes of native memory and never managed; the managed side is SkiaSharp's wrappers for the frame's shader and snapshot
        Assert.True(perFrame < 1536, $"a frame allocates {perFrame} B");
        Assert.Equal(1, renderer.SurfaceCreations);
    }

    // V-11.5 / V-T53 on the renderer: the crossfade's mix is the opacity the orb is drawn with, through the 2x surface (below 128 device px) as straight onto the
    // canvas (from 128). Windows review (unit/integ-vt8): a 2x path that dropped the opacity drew both orbs whole during a swap at 100 % and 150 % scaling and survived
    // every other test.
    [Theory]
    [InlineData(1.0, true)]
    [InlineData(1.5, true)]
    [InlineData(2.0, true)]
    [InlineData(2.0, false)]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void The_opacity_scales_the_drawn_orb_on_both_paths(double dpr, bool supersample)
    {
        var runtime = new VoiceOrbRuntime(Rest.Kind, new VoiceOrbRuntimeOptions(OrbScene.ViewPx, Rest.Ball, dpr, Rest.Dark, Supersample: supersample, Phase: Rest.Phase,
            SupersampleBelow: OrbScene.SupersampleBelow));
        runtime.Frame(1.3, Rest.FrameState);
        SKBitmap Draw(float opacity)
        {
            using var renderer = new OrbRenderer();
            var info = new SKImageInfo(runtime.DevicePx, runtime.DevicePx, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Scale((float)(runtime.DevicePx / OrbScene.ViewPx));
            renderer.Draw(surface.Canvas, null, runtime.Values, runtime.Mrw, runtime.DevicePx, runtime.RenderPx, (float)OrbScene.ViewPx, opacity);
            var bitmap = new SKBitmap(info);
            surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
            return bitmap;
        }
        using var whole = Draw(1);
        using var half = Draw(0.5f);
        var inked = 0; var worst = 0;
        for (var y = 0; y < whole.Height; y++)
            for (var x = 0; x < whole.Width; x++)
            {
                var a = whole.GetPixel(x, y).Alpha;
                if (a < 64) continue;
                inked++;
                worst = Math.Max(worst, Math.Abs(half.GetPixel(x, y).Alpha - a / 2));
            }
        Assert.True(inked > 100, $"the orb drew {inked} opaque pixels");
        Assert.True(worst <= 2, $"at opacity 0.5 a pixel's alpha is {worst}/255 away from half the whole draw's");
    }
}
