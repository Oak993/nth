using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Marrow.App.Port.MultiAccount.Handshake;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Window.Native;

// PORT-ONLY: MA-4-2, native ANGLE renderer and warmed visible frame budget. This is a test fixture window, never Marrow.
[Collection("WindowManagerNative")]
public sealed class HandshakeNativeFrameTests(WindowManagerNativeSession native)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Filaments_are_drawn_on_the_GPU_after_registered_warmup_within_a_frame()
    {
        native.Run(_ =>
        {
            HandshakeCanvas? view = null; Avalonia.Controls.Window? window = null;
            native.OnUi(() =>
            {
                HandshakeGpuProgram.EnsureRegistered(); window = new Avalonia.Controls.Window { Width = 320, Height = 220, ShowInTaskbar = false };
                native.Register(window); window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(window));
                var host = new Panel(); window.Content = host; window.Show(); window.Activate(); GpuWarmup.WhenIdle(host);
            });
            var warmDeadline = DateTime.UtcNow.AddSeconds(15);
            while (GpuWarmup.StepsDone < GpuWarmup.Steps && DateTime.UtcNow < warmDeadline) Thread.Sleep(20);
            Assert.True(GpuWarmup.StepsDone >= GpuWarmup.Steps, "registered GPU warm-up did not finish");
            native.OnUi(() =>
            {
                view = new HandshakeCanvas { Width = 256, Height = 144 }; ((Panel)window!.Content!).Children.Add(view);
                var frame = HandshakeGeometry.Draw(256, 144, 3500, true, false);
                view.Apply(new HandshakeMotionPlan(HandshakePhase.InSync, 3500, .5, "In sync", true, false, (240, 105, 90), (58, 214, 168), frame), true);
            });
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (HandshakeDrawOperation.GpuFrames < 1 && DateTime.UtcNow < deadline) Thread.Sleep(20);
            Assert.True(HandshakeDrawOperation.GpuFrames > 0, "the Handshake must use the native GPU renderer");
            Assert.True(HandshakeDrawOperation.FirstGpuMilliseconds < 16.67, $"first warmed draw {HandshakeDrawOperation.FirstGpuMilliseconds:F2} ms");
            var running = true; var origin = System.Diagnostics.Stopwatch.GetTimestamp(); double last = -1;
            native.OnUi(() =>
            {
                Action<TimeSpan>? tick = null;
                tick = _ =>
                {
                    if (!running) return;
                    var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(origin).TotalSeconds;
                    if (last < 0 || seconds - last >= 1.0 / 60 - .0005)
                    {
                        last = seconds; var t = 3200 + 500 * (.5 + .5 * Math.Sin(seconds * 2));
                        view!.Apply(new HandshakeMotionPlan(HandshakePhase.InSync, t, .5, "In sync", true, false, (240, 105, 90), (58, 214, 168), HandshakeGeometry.Draw(256, 144, t, true, false)), true);
                    }
                    FrameClock.Source.Current.RequestFrame(tick!);
                };
                FrameClock.Source.Current.RequestFrame(tick);
            });
            Thread.Sleep(1000); var start = HandshakeDrawOperation.GpuFrames; var at = System.Diagnostics.Stopwatch.GetTimestamp(); Thread.Sleep(10000);
            var span = System.Diagnostics.Stopwatch.GetElapsedTime(at).TotalSeconds; var draws = HandshakeDrawOperation.GpuFrames - start; native.OnUi(() => running = false);
            var report = $"GPU draws {draws} in {span:F3}s ({draws / span:F2}fps), first warmed draw {HandshakeDrawOperation.FirstGpuMilliseconds:F3}ms, max {HandshakeDrawOperation.MaxGpuMilliseconds:F3}ms";
            File.WriteAllText(Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, "handshake-native-frame.txt"), report);
            Assert.True(draws / span >= 58, report); Assert.True(HandshakeDrawOperation.MaxGpuMilliseconds < 16.67, report);
        }, factType: typeof(HandshakeNativeFrameTests));
    }
}
