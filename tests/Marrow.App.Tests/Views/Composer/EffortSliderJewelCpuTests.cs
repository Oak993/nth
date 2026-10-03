using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// OD-0090 (ED-252): the knob's bounded per-frame cost on the CPU renderer, the software fallback (the headless renderer is
// that one). The page's look there (2x supersample, three samples at the rim, every frame) measured p50 21 and 31 ms a knob
// frame at 1.5x and 2x display scale; on the CPU only the orb renders at 48 px at most whatever the scale, the gem's program
// runs every other frame, the rim takes the middle sample only, and the knob steps its size down when its own program's
// median runs over 7 ms. These go through JewelKnobOperation, as a slider's frame does.
[Collection("Headless")]
public sealed class EffortSliderJewelCpuTests(HeadlessSession headless)
{
    // a frame of the knob made for a display scale (the slider's own way: RenderSize, Frame, the supersample it records)
    private static float[] FrameAt(double scale)
    {
        var (size, perPx) = JewelOrb.RenderSize(EffortSlider.ThumbSize, scale);
        var u = new JewelUniforms();
        new JewelOrb(JewelMaterial.For(EffortBrand.Claude)).Frame(0, 0, 0, Math.PI, false, size, perPx, true, u);
        u.SetPage(48, 0.26, 190, 298, 34, new SKColor(0x14, 0x1d, 0x27), new SKColor(0x11, 0x18, 0x20),
            [SKColors.Red, SKColors.Red, SKColors.Red, SKColors.Red, SKColors.Red]);
        u.Values[110] = JewelOrb.Supersampled(EffortSlider.ThumbSize, scale) ? 2 : 1;
        return u.Values;
    }

    private static void Render(JewelOperationHost host, double scale)
    {
        var px = (int)Math.Ceiling(48 * scale);
        using var bitmap = new RenderTargetBitmap(new PixelSize(px, px), new Vector(96 * scale, 96 * scale));
        bitmap.Render(host);
    }

    private static JewelOperationHost Host(JewelKnobOperation op)
    {
        var host = new JewelOperationHost { Operation = op, Width = 48, Height = 48 };
        host.Measure(new Size(48, 48)); host.Arrange(new Rect(0, 0, 48, 48));
        return host;
    }

    [Theory]
    [Trait("Decision", "ED-252")] // the orb renders at 48 px at most on the CPU (2x display scale: 96 device px, blitted up)
    [InlineData(1.0, 48)]
    [InlineData(1.5, 48)]
    [InlineData(2.0, 48)]
    public void On_the_cpu_the_orb_renders_at_48_px_at_most(double scale, int rendered) => headless.OnUiThread(() =>
    {
        var cpu = new JewelCpuKnob();
        var op = new JewelKnobOperation(new Rect(-40, -40, 128, 128), new Rect(0, 0, 48, 48), FrameAt(scale), cpu);
        Render(Host(op), scale);
        Assert.Equal(rendered, cpu.RenderedSize);
        Assert.Equal(1, cpu.Programs);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the gem's program runs every other frame (30 Hz); the frame between reuses its picture
    public void On_the_cpu_the_gem_is_drawn_every_other_frame() => headless.OnUiThread(() =>
    {
        var cpu = new JewelCpuKnob();
        var op = new JewelKnobOperation(new Rect(-40, -40, 128, 128), new Rect(0, 0, 48, 48), FrameAt(1), cpu);
        var host = Host(op);
        for (var i = 0; i < 8; i++) Render(host, 1);
        Assert.Equal(8, cpu.Frames);
        Assert.Equal(4, cpu.Programs);
        // drawing the same frame again never shrinks it again (the frame's values are not changed by the CPU path)
        Assert.Equal(96f, op.Values[0]);
        Assert.Equal(48, cpu.RenderedSize);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // the CPU takes the rim's middle sample only (u[6].w): the same picture inside the rim band, different on it
    public void On_the_cpu_the_rim_takes_the_middle_sample_only()
    {
        var values = FrameAt(1);
        using var three = Program(values, false);
        using var one = Program(values, true);
        int inside = 0, rim = 0;
        for (var y = 0; y < 96; y++)
            for (var x = 0; x < 96; x++)
            {
                var r = Math.Sqrt(Math.Pow(x + 0.5 - 48, 2) + Math.Pow(y + 0.5 - 48, 2)) / (48 * values[20]);
                if (three.GetPixel(x, y) == one.GetPixel(x, y)) continue;
                if (r < 0.7) inside++; else rim++;
            }
        Assert.Equal(0, inside);
        Assert.True(rim > 50, $"the rim's dispersion differs ({rim} px)");
        // and the knob's own CPU path draws the middle-sample picture: its gem equals the program's at 48 px with the flag
        headless.OnUiThread(() =>
        {
            var cpu = new JewelCpuKnob();
            Render(Host(new JewelKnobOperation(new Rect(-40, -40, 128, 128), new Rect(0, 0, 48, 48), values, cpu)), 1);
            using var drawn = cpu.Picture!.Snapshot();
            using var picture = SKBitmap.FromImage(drawn);
            var at48 = (float[])values.Clone();
            JewelUniforms.Resize(at48, 48, 1);
            using var middle = Program48(at48, true);
            using var all = Program48(at48, false);
            int same = 0, sameAsAll = 0;
            for (var y = 0; y < 48; y++)
                for (var x = 0; x < 48; x++) { if (picture.GetPixel(x, y) == middle.GetPixel(x, y)) same++; if (picture.GetPixel(x, y) == all.GetPixel(x, y)) sameAsAll++; }
            Assert.Equal(48 * 48, same);
            Assert.True(sameAsAll < 48 * 48, "the three-sample picture differs");
        });

        static SKBitmap Program48(float[] values, bool middleOnly)
        {
            var u = (float[])values.Clone();
            u[27] = middleOnly ? 1 : 0;
            using var surface = SKSurface.Create(new SKImageInfo(48, 48, SKColorType.Rgba8888, SKAlphaType.Premul));
            JewelKnobRenderer.For(null, 48).Program(surface, 48, u);
            using var image = surface.Snapshot();
            return SKBitmap.FromImage(image);
        }

        static SKBitmap Program(float[] values, bool middleOnly)
        {
            var u = (float[])values.Clone();
            u[27] = middleOnly ? 1 : 0;
            using var surface = SKSurface.Create(new SKImageInfo(96, 96, SKColorType.Rgba8888, SKAlphaType.Premul));
            JewelKnobRenderer.For(null, 96).Program(surface, 96, u);
            using var image = surface.Snapshot();
            return SKBitmap.FromImage(image);
        }
    }

    [Fact]
    [Trait("Decision", "ED-252")] // the knob measures its own program: a median over 7 ms steps it down (48, 40, 32, 24), under 2.5 ms back up
    public void On_the_cpu_a_slow_program_steps_the_size_down_and_a_fast_one_back_up() => headless.OnUiThread(() =>
    {
        var cpu = new JewelCpuKnob();
        for (var i = 0; i < 15; i++) cpu.Record(9);
        Assert.Equal(0, cpu.Level); // not before 16 runs
        cpu.Record(9);
        Assert.Equal(1, cpu.Level);
        for (var i = 0; i < 16; i++) cpu.Record(6);
        Assert.Equal(1, cpu.Level); // between the thresholds it stays
        for (var i = 0; i < 16; i++) cpu.Record(8);
        Assert.Equal(2, cpu.Level);
        var op = new JewelKnobOperation(new Rect(-40, -40, 128, 128), new Rect(0, 0, 48, 48), FrameAt(2), cpu);
        Render(Host(op), 2);
        Assert.Equal(32, cpu.RenderedSize);
        for (var i = 0; i < 16; i++) cpu.Record(1);
        Assert.Equal(1, cpu.Level);
        for (var i = 0; i < 64; i++) cpu.Record(50);
        Assert.Equal(3, cpu.Level); // never below 24 px
    });

    [Fact]
    [Trait("Decision", "ED-252")] // measured: a CPU knob frame through the operation stays within budget (median of the program, and a frame)
    public void On_the_cpu_a_knob_frame_stays_within_budget() => headless.OnUiThread(() =>
    {
        var lines = new List<string>();
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            var cpu = new JewelCpuKnob();
            var op = new JewelKnobOperation(new Rect(-40, -40, 128, 128), new Rect(0, 0, 48, 48), FrameAt(scale), cpu);
            var host = Host(op);
            Render(host, scale); // warm
            var px = (int)Math.Ceiling(48 * scale);
            using var bitmap = new RenderTargetBitmap(new PixelSize(px, px), new Vector(96 * scale, 96 * scale));
            var times = new List<double>();
            for (var i = 0; i < 60; i++)
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                bitmap.Render(host);
                times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            times.Sort();
            lines.Add($"CPU knob through the operation, {scale}x: frame p50 {times[30]:F2} ms, p95 {times[57]:F2} ms (step {cpu.Level}, {cpu.RenderedSize} px)");
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "jewel-cpu-cost.txt"), lines);
        // generous (the PC is shared with other builds): a frame's median within half a 60 Hz frame
        foreach (var line in lines) Assert.True(double.Parse(line.Split("p50 ")[1].Split(" ms")[0], System.Globalization.CultureInfo.InvariantCulture) < 8, string.Join(" | ", lines));
    });

    [Fact]
    [Trait("Decision", "ED-252")] // a slider's view reuses its operations once Avalonia gives them back (no new operation a frame)
    public void A_sliders_view_reuses_its_operations() => headless.OnUiThread(() =>
    {
        using var rig = JewelFixtures.Rig(index: 2);
        var view = rig.Slider.GetVisualDescendants().OfType<JewelKnobView>().Single();
        for (var i = 0; i < 60; i++) { rig.Frame(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        Assert.InRange(view.OperationsMade, 1, 4);
    });
}
