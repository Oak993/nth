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

// VT-8, V-T54 (voice-SPEC V-11.3; GR-1, ED-252), native: the two voice orb programs are registered with GpuWarmup, and on a real Win32 window's GPU context (ANGLE D3D11)
// the warm-up runs them, and the orb shader compiles and draws what voice-orbs.js draws in Chrome on the same backend: frames rendered on the GPU through the real
// OrbRenderer are read back and compared with tests/fixtures/voice/ref-orbs (<= 2/255 at >= 99.5 % of pixels, V-11.7's bar). A program that failed to compile on
// the GPU draws nothing and fails the comparison. Recorded: voice-orb-gpu.txt (the run's data folder). Windows review of VT-8 (unit/integ-vt8).
[Collection("WindowManagerNative")]
[Trait("Spec", "V-T54")]
public sealed class VoiceOrbNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    [Trait("Decision", "ED-252")]
    public void The_orb_programs_warm_up_and_draw_the_reference_frames_on_the_windows_gpu()
    {
        native.Run(_ =>
        {
            VoiceWarmup.Register();
            try
            {
                var probe = new OrbGpuProbeHandler();
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
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (GpuWarmup.StepsDone < GpuWarmup.Steps && DateTime.UtcNow < deadline) Thread.Sleep(20);
                var steps = string.Join(", ", GpuWarmup.Done.Select(d => $"{d.Program} {d.Milliseconds:F1} ms{(d.Forced ? " (forced)" : "")}"));
                var (frames, gpu) = probe.Result.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                var lines = new List<string> { $"GPU backend {gpu}; warm-up steps {steps}" };
                var failures = new List<string>();
                for (var i = 0; i < frames.Length; i++)
                {
                    var name = OrbGpuProbeHandler.Jobs[i].Name;
                    using var frame = frames[i];
                    if (frame is null) { failures.Add(name + ": no GPU frame"); continue; }
                    using var reference = SKBitmap.Decode(Path.Combine(ReferenceFolder(), name + ".png"));
                    var (within, max) = Compare(reference, frame, 2);
                    var line = $"{name}: {within:P2} of pixels within 2/255, largest difference {max}";
                    lines.Add(line);
                    if (within < 0.995) failures.Add(line);
                }
                File.WriteAllText(Record("voice-orb-gpu.txt"), string.Join(Environment.NewLine, lines) + Environment.NewLine);
                Console.WriteLine(string.Join(Environment.NewLine, lines));
                Assert.True(GpuWarmup.StepsDone >= GpuWarmup.Steps, $"the warm-up did not finish: {GpuWarmup.StepsDone} of {GpuWarmup.Steps} steps ({steps})");
                Assert.Contains(GpuWarmup.Done, d => d.Program == "voice-orb-siri");
                Assert.Contains(GpuWarmup.Done, d => d.Program == "voice-orb-membrane");
                Assert.NotEqual("none", gpu);
                Assert.True(failures.Count == 0, string.Join("; ", failures));
            }
            finally { VoiceWarmup.Unregister(); }
        }, factType: typeof(VoiceOrbNativeTests));
    }

    private static (double Within, int Max) Compare(SKBitmap a, SKBitmap b, int tolerance)
    {
        var within = 0; var max = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                var d = Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));
                max = Math.Max(max, d);
                if (d <= tolerance) within++;
            }
        return (within / (double)(a.Width * a.Height), max);
    }

    private static string ReferenceFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "tests", "fixtures", "voice", "ref-orbs");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests/fixtures/voice/ref-orbs was not found above " + AppContext.BaseDirectory);
    }

    // the run's data folder (it outlives the native child, whose own temp folder is removed with it)
    private static string Record(string name)
    {
        var data = Marrow.Testing.TestDataDirectory.Read()!;
        var folder = WindowManagerNativeChildProtocol.IsChild ? Path.GetFullPath(Path.Combine(data, "..", "..", "..")) : data;
        return Path.Combine(folder, name);
    }
}
