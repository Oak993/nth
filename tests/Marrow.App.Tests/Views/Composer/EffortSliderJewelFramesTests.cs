using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// OD-0090: the port's frames of the Jewel knob slider for the side-by-side with the locked page (headless Chrome), and the
// knob's measured frame cost. MARROW_JEWEL_FRAMES_DIR names the folder (E:/Droppy/work/design/jewel-knob-locked/port-evidence);
// unset, a throwaway folder under the run's data dir is used. Frames: the 298 px slider in the app's popover (four stops,
// at Max), on the page's pane colour, at 1x: at rest, held (pressed on the knob, 0.6 s), charging and at a strike's swell
// in fast mode, for several makers, dark and light, at Max (the liquid at the knob's end, as the page's copy shows it).
[Collection("Headless")]
public sealed class EffortSliderJewelFramesTests(HeadlessSession headless)
{
    private static string Folder()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_JEWEL_FRAMES_DIR") is { Length: > 0 } given
            ? given
            : Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? Path.GetTempPath(), "jewel-frames");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Save(JewelRig rig, Border host, string path)
    {
        Dispatcher.UIThread.RunJobs();
        var size = new PixelSize((int)host.Bounds.Width, (int)host.Bounds.Height);
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(host);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }

    [Theory]
    [Trait("Decision", "ED-252")]
    [InlineData("dark")]
    [InlineData("light")]
    public void Port_frames_render_for_the_side_by_side(string appearance) => headless.OnUiThread(() =>
    {
        var folder = Folder();
        var written = 0;
        JewelKnobRenderer.SupersampleOnCpu = true; // the frames show the GPU's 2x path (the headless renderer is the CPU's)
        try
        {
        foreach (var (maker, brand) in new[] { ("Claude", EffortBrand.Claude), ("OpenAI", EffortBrand.Silver), ("DeepSeek", EffortBrand.Deepseek), ("Gemini", EffortBrand.Antigravity),
            ("Grok", EffortBrand.Grok), ("Devin", EffortBrand.Devin), ("Copilot", EffortBrand.Copilot) })
        {
            foreach (var state in new[] { "rest", "held", "charge", "strike" })
            {
                var fast = state is "charge" or "strike";
                using var rig = JewelFixtures.Rig(index: 3, fast: fast, brand: brand);
                rig.Window.Height = 88; // the slider at (20, 20) in a 338 x 88 frame
                rig.Window.RequestedThemeVariant = appearance == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                var host = (Border)rig.Window.Content!;
                host.Background = new SolidColorBrush(appearance == "dark" ? Color.Parse("#141d27") : Color.Parse("#e9eef3")); // the page's --pane base
                for (var i = 0; i < 30; i++) rig.Frame();
                if (state == "held") { rig.Down(rig.Slider.KnobX + 6); for (var i = 0; i < 36; i++) rig.Frame(); }
                if (fast)
                {
                    var effect = rig.Slider.GetVisualDescendants().OfType<TrackEffect>().First();
                    var seed = effect.StrikeSeed;
                    // like the page's shots (shoot.cjs): charging, its raw charge past half; or the strike's swell at its peak
                    var at=effect.StrikeFrameTime; var first=(int)Math.Floor(at/2.4)-1;
                    var plan=Enumerable.Range(first,12).Select(k=>StrikeMix.Timing(k,seed)).First(p=>p.At-p.Lead-p.Charge>at&&p.Charge>0);
                    var leader = plan.At-plan.Lead;
                    var target = (state == "charge" ? leader-plan.Charge*0.3 : plan.At)-effect.StrikeOffset; // SPEC A-21/B-09
                    while (effect.TimeAt(SwiftDate.Now) < target - 1.0 / 60) rig.Frame();
                    rig.Frame(Math.Max(0, target - effect.TimeAt(SwiftDate.Now)));
                    if (state == "charge") Assert.True(rig.Slider.Signal.Phase == KnobPhase.Charge && rig.Slider.Signal.RawCharge > 0.5, $"charge {rig.Slider.Signal.RawCharge}");
                    else
                    {
                        var peak = rig.Slider.Signal.Flare;
                        for (var i = 0; i < 60; i++) { rig.Frame(); if (rig.Slider.Signal.Flare < peak) break; peak = rig.Slider.Signal.Flare; }
                    }
                }
                Save(rig, host, Path.Combine(folder, $"port-{maker}-{appearance}-{state}.png"));
                written++;
            }
        }
        }
        finally { JewelKnobRenderer.SupersampleOnCpu = false; }
        Assert.Equal(28, written);
    });

    [Fact]
    [Trait("Decision", "ED-252")] // OD-0090: a bounded per-frame cost (measured on the CPU raster backend, the headless one)
    public void The_knobs_frame_cost_is_measured_and_bounded() => headless.OnUiThread(() =>
    {
        var lines = new List<string>();
        foreach (var (dpr, cpu) in new[] { (1.0, false), (1.0, true), (1.5, false), (1.5, true), (2.0, false), (2.0, true) })
        {
            var (size, perPx) = JewelOrb.RenderSize(48, dpr);
            var orb = new JewelOrb(JewelMaterial.For(EffortBrand.Claude));
            var u = new JewelUniforms();
            u.SetPage(48, 0.26, 190, 298, 34, new SKColor(0x14, 0x1d, 0x27), new SKColor(0x11, 0x18, 0x20), [SKColors.Red, SKColors.Red, SKColors.Red, SKColors.Red, SKColors.Red]);
            var px = (int)Math.Ceiling(48 * dpr) + 80;
            using var bitmap = new SKBitmap(new SKImageInfo(px, px, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            canvas.Scale((float)dpr);
            var renderer = JewelKnobRenderer.For(null, size);
            orb.Frame(0, 0, 0, Math.PI, false, size, perPx, true, u);
            u.Values[110] = JewelOrb.Supersampled(48, dpr) ? 2 : 1;
            // the CPU renderer's own path (JewelCpuKnob: 48 px at most, the gem at 30 Hz, the middle sample, stepping down), or
            // the GPU path's look (2x, three samples, every frame) drawn on the CPU for reference
            var knob = new JewelCpuKnob();
            void Draw() { if (cpu) knob.Draw(canvas, u.Values, new SKRect(20, 20, 68, 68), 1); else renderer.Draw(canvas, u.Values, new SKRect(20, 20, 68, 68), 1); }
            Draw();
            var draw = new List<double>();
            var watch = new Stopwatch();
            for (var i = 1; i <= 120; i++)
            {
                orb.Frame(i / 60.0, i % 40 < 10 ? 0.58 : 0, i % 40 is > 5 and < 10 ? 0.24 : 0, Math.PI, false, size, perPx, true, u);
                u.Values[110] = JewelOrb.Supersampled(48, dpr) ? 2 : 1;
                watch.Restart();
                Draw();
                draw.Add(watch.Elapsed.TotalMilliseconds);
            }
            draw.Sort();
            lines.Add(cpu
                ? $"dpr {dpr} CPU renderer's knob (JewelCpuKnob: {knob.RenderedSize} px, step {knob.Level}, the gem every other frame, middle sample): per frame p50 {draw[60]:F2} ms, p95 {draw[114]:F2} ms, max {draw[^1]:F2} ms"
                : $"dpr {dpr} GPU path's look (render {size} px, three samples, every frame) drawn on the CPU, for reference: per frame p50 {draw[60]:F2} ms, p95 {draw[114]:F2} ms, max {draw[^1]:F2} ms");
        }
        // the UI thread's share: a slider frame (springs, signal, orb uniforms, layout) in fast mode
        using (var rig = JewelFixtures.Rig(index: 2, fast: true))
        {
            for (var i = 0; i < 30; i++) rig.Frame();
            var watch = Stopwatch.StartNew();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 120; i++) rig.Slider.Advance(1.0 / 60, 1 + i / 60.0);
            var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 120.0;
            lines.Add($"UI thread, a slider frame (fast mode, no rendering): {watch.Elapsed.TotalMilliseconds / 120:F3} ms, {bytes:F0} B allocated");
        }
        File.WriteAllLines(Path.Combine(Folder(), "frame-cost.txt"), lines);
        // generous (the PC is loaded by other runs): the CPU fallback's knob at 1x stays within a 60 Hz frame
        var p50 = double.Parse(lines[1].Split("p50 ")[1].Split(" ms")[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(p50 < 16, string.Join(" | ", lines));
        // what the CPU renderer draws: a 2x frame (below 128 device px) at its 1x size, the ball's radius for that size
        var check = new JewelUniforms();
        new JewelOrb(JewelMaterial.For(EffortBrand.Claude)).Frame(0, 0, 0, Math.PI, false, 96, 2, true, check);
        check.Values[110] = 2;
        Assert.Equal((48, 1.0), JewelKnobRenderer.CpuSize(check.Values));
        JewelUniforms.Resize(check.Values, 48, 1);
        var at48 = new JewelUniforms();
        new JewelOrb(JewelMaterial.For(EffortBrand.Claude)).Frame(0, 0, 0, Math.PI, false, 48, 1, true, at48);
        Assert.Equal(at48.Kn.BallRadius, check.Kn.BallRadius, 6);
        Assert.Equal(at48.Fx.Px, check.Fx.Px, 6);
    });
}
