using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Rendering.Composition;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// VT-9, V-T60 (voice-SPEC V-13.2, V-19), native: the glow's program is registered with GpuWarmup, and on a real Win32 window's GPU context (ANGLE D3D11) the warm-up runs it and the
// glow draws what voice-glow.js draws in Chrome on the same backend: frames rendered on the GPU through the real VoiceGlowPainter are read back and compared with
// tests/fixtures/voice/ref-glow (the same bar as the CPU comparison, VoiceGlowFidelityTests). A program that failed to compile on the GPU draws nothing and fails the comparison.
// Recorded: voice-glow-gpu.txt (the run's data folder), with the measured time a frame (draw, flush and the GPU's finish, 720 x 96) for the whole glow, for each layer
// alone and for the band drawn as the canvas strokes it and as one analytic pass (what the painter does on a GPU).
[Collection("WindowManagerNative")]
public sealed class VoiceGlowNativeTests(WindowManagerNativeSession native)
{
    private static readonly string[] Names =
    [
        "ref_glow_idle_720x96_dark",
        "ref_glow_level-0.2_480x150_light",
        "ref_glow_level-0.6_720x96_dark",
        "ref_glow_level-1.0_720x96_dark",
        "ref_glow_level-1.0_720x96_light",
        "ref_glow_proc-0.8_720x96_dark",
        "ref_glow_proc-1.35_480x150_light",
    ];

    [Fact]
    [Trait("Decision", "ED-252")]
    public void The_glow_program_warms_up_and_draws_the_reference_frames_on_the_windows_gpu()
    {
        native.Run(_ =>
        {
            VoiceGlowWarmup.Register();
            try
            {
                var probe = new GlowGpuProbeHandler(Names, ReferenceFolder());
                var window = native.OnUi(() =>
                {
                    var host = new Border { Width = 120, Height = 120 };
                    var window = new Avalonia.Controls.Window { Width = 200, Height = 200, ShowInTaskbar = false, Content = new StackPanel { Children = { host } } };
                    native.Register(window);
                    window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
                    GpuWarmup.WhenIdle(host);
                    host.AttachedToVisualTree += (_, _) =>
                    {
                        var visual = ElementComposition.GetElementVisual(host)!.Compositor.CreateCustomVisual(probe);
                        visual.Size = new Avalonia.Vector(1, 1);
                        ElementComposition.SetElementChildVisual(host, visual);
                        visual.SendHandlerMessage(this);
                    };
                    window.Show();
                    window.Activate();
                    return window;
                });
                N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (GpuWarmup.StepsDone < GpuWarmup.Steps && DateTime.UtcNow < deadline) Thread.Sleep(20);
                var steps = string.Join(", ", GpuWarmup.Done.Select(d => $"{d.Program} {d.Milliseconds:F1} ms{(d.Forced ? " (forced)" : "")}"));
                var (frames, gpu, timing) = probe.Result.Task.WaitAsync(TimeSpan.FromSeconds(120)).GetAwaiter().GetResult();
                var lines = new List<string> { $"GPU backend {gpu}; warm-up steps {steps}" };
                lines.AddRange(timing);
                var failures = new List<string>();
                for (var i = 0; i < frames.Length; i++)
                {
                    var name = Names[i];
                    using var frame = frames[i];
                    if (frame is null) { failures.Add(name + ": no GPU frame"); continue; }
                    using var reference = SKBitmap.Decode(Path.Combine(ReferenceFolder(), name + ".png"));
                    var (within8, within16, mean, max) = Compare(reference, frame);
                    var line = $"{name}: within 8/255 {within8:P2}, within 16/255 {within16:P2}, mean {mean:F2}, largest {max}";
                    lines.Add(line);
                    if (within8 < 0.98 || within16 < 0.999 || mean > 2.0 || max > 64) failures.Add(line);
                }
                File.WriteAllText(Record("voice-glow-gpu.txt"), string.Join(Environment.NewLine, lines) + Environment.NewLine);
                Console.WriteLine(string.Join(Environment.NewLine, lines));
                Assert.True(GpuWarmup.StepsDone >= GpuWarmup.Steps, $"the warm-up did not finish: {GpuWarmup.StepsDone} of {GpuWarmup.Steps} steps ({steps})");
                Assert.Contains(GpuWarmup.Done, d => d.Program == "voice-glow");
                Assert.NotEqual("none", gpu);
                Assert.True(failures.Count == 0, string.Join("; ", failures));
            }
            finally { VoiceGlowWarmup.Unregister(); }
        }, factType: typeof(VoiceGlowNativeTests));
    }

    private static (double Within8, double Within16, double Mean, int Max) Compare(SKBitmap a, SKBitmap b)
    {
        int w8 = 0, w16 = 0, max = 0;
        long sum = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                var d = Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));
                max = Math.Max(max, d);
                sum += d;
                if (d <= 8) w8++;
                if (d <= 16) w16++;
            }
        var n = (double)(a.Width * a.Height);
        return (w8 / n, w16 / n, sum / n, max);
    }

    private static string ReferenceFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "tests", "fixtures", "voice", "ref-glow");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests/fixtures/voice/ref-glow was not found above " + AppContext.BaseDirectory);
    }

    // the run's data folder (it outlives the native child, whose own temp folder is removed with it)
    private static string Record(string name)
    {
        var data = Marrow.Testing.TestDataDirectory.Read()!;
        var folder = WindowManagerNativeChildProtocol.IsChild ? Path.GetFullPath(Path.Combine(data, "..", "..", "..")) : data;
        return Path.Combine(folder, name);
    }
}
