using Avalonia;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence (OD-0055): the effort slider's track effect on a real Win32 window, driven by the real frame clock
// (TopLevel.RequestAnimationFrame through AvaloniaFrameSource, as Program.cs binds it), counted over ten seconds of wall time.
// EffortSlider.swift:1049-1050 TimelineView(.animation(minimumInterval: 1/60 (1/30 for a solo supercharged track), paused:
// reduceMotion || !FrontMonitor.isVisible)); DEVIATION OD-0051: the solo supercharged track is the liquid tube and runs at 60.
// These are the instrumented runs PR-COMPOSER-133's verification_method names; the headless cadence theory carries ED-117/O-34.
// They found and now guard: the liquid's GPU crash (ED-233), the half-rate timeline (ED-232), FrontMonitor missing a window's
// activation, and Reduce Motion not pausing a running timeline.
//
// STOP EffortSlider native #1 - RESOLVED 2026-10-02 by ED-237. On the owner's 240 Hz display Avalonia's render loop went idle
// after a frame that changed nothing on screen, and the next requested frame waited 16-31 ms for its idle wake-up: the
// track got about 48 updates a second. AvaloniaFrameSource now requests a compositor commit with each frame (probe: an
// unchanging window 40 -> 240 frames a second, the liquid tube 81 -> 240, no gap over 15 ms), and the tests require 58.
// A run where the parent cannot pass the foreground to the child (foreground grant=False) cannot bring its window to the front;
// such a failure is environmental and is retried alone (the flake rule).
[Collection("WindowManagerNative")]
public sealed class EffortSliderNativeFrameTests(WindowManagerNativeSession native)
{
    private static readonly FieldInfo TimeField = typeof(TrackEffect).GetField("time", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static double Time(TrackEffect effect) => (double)TimeField.GetValue(effect)!;

    // A theme-less window shows its content through a bare presenter (as WindowManagerNativeTests does).
    // The frame source is bound before the content is built, so every view takes the real one.
    private (Avalonia.Controls.Window Window, CountingFrameSource Frames) Host(Func<Control> content)
    {
        var window = new Avalonia.Controls.Window { Width = 420, Height = 90, ShowInTaskbar = false };
        native.Register(window);
        window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        var frames = new CountingFrameSource(new AvaloniaFrameSource(window));
        FrameClock.Source.SetProcessDefault(frames);
        window.Content = content();
        window.Show();
        window.Activate();
        return (window, frames);
    }

    private int UpdatesOver(CountingFrameSource frames, TimeSpan span)
    {
        var start = native.OnUi(() => frames.Updates);
        Thread.Sleep(span);
        return native.OnUi(() => frames.Updates) - start;
    }

    private void RunsAtSixtyForTenSeconds(TrackLook look, [System.Runtime.CompilerServices.CallerMemberName] string fact = "")
    {
        native.Run(_ =>
        {
            var (window, frames) = native.OnUi(() =>
            {
                TrackEffect effect = null!;
                var hosted = Host(() => effect = new TrackEffect(look) { Width = 300, Height = 26 });
                Watch(hosted.Frames, effect);
                return hosted;
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Thread.Sleep(1000); // first frames, JIT and the window's own appearance
            // The tube is really drawn on the GPU: most points along its middle show a saturated (coloured) pixel.
            var row = native.OnUi(() =>
            {
                var effect = (Control)window.Content!;
                return Enumerable.Range(1, 9).Select(k => effect.PointToScreen(new Avalonia.Point(effect.Bounds.Width * k / 10, effect.Bounds.Height / 2))).ToArray();
            });
            int Coloured() => row.Count(point => ScreenPixels.Saturation(ScreenPixels.At(point.X, point.Y)) > 0.25);
            if (TrackEffect.IsLiquid(look))
            {
                // the first paint waits for the passes' shaders to compile on the GPU
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (Coloured() < 7 && DateTime.UtcNow < deadline) Thread.Sleep(100);
                var colours = string.Join(" ", row.Select(point => ScreenPixels.At(point.X, point.Y)));
                Assert.True(Coloured() >= 7, $"only {Coloured()} of 9 points along the tube are coloured: {colours}");
            }
            var (startUpdates, startFrame) = native.OnUi(() => (frames.Updates, frames.Stamps.Count));
            Thread.Sleep(TimeSpan.FromSeconds(10));
            var (updates, delivered, allowed, span, gaps) = native.OnUi(() =>
            {
                var stamps = frames.Stamps;
                return (frames.Updates - startUpdates, stamps.Count - startFrame, frames.AllowedUpdates(startFrame, 1000.0 / 60),
                    stamps[^1] - stamps[startFrame], frames.GapHistogram(startFrame));
            });
            var diagnosis = $"updates {updates}, frames {delivered} over {span:F0} ms, ED-232 allows {allowed}, gaps ms {gaps}";
            // The frame clock delivers at least 60 frames a second on this display ...
            Assert.True(delivered * 1000.0 / span >= 60, diagnosis);
            // ... and the timeline takes every update its 1/60 interval allows over the frames that came (ED-232), never
            // more: 60 a second (OD-0052: never below 60; ED-237 keeps the frames coming on an unchanged frame).
            Assert.InRange(updates, allowed - 5, allowed + 5); // the timeline reads its clock a little after the frame's stamp
            Assert.True(updates * 1000.0 / span >= 58, diagnosis);
            Assert.True(updates * 1000.0 / span <= 60.5, diagnosis);
        }, fact, typeof(EffortSliderNativeFrameTests));
    }

    private static void Watch(CountingFrameSource frames, TrackEffect effect) => frames.Watch(() => Time(effect));

    [Fact]
    [Trait("Req", "PR-COMPOSER-133")] // EffortSlider.swift:1049; DEVIATION OD-0051 (60, not 30); ED-232, ED-233
    public void A_solo_supercharged_track_advances_sixty_times_a_second_on_a_real_window() =>
        RunsAtSixtyForTenSeconds(new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Purple, null));

    [Fact]
    [Trait("Req", "PR-COMPOSER-133")] // EffortSlider.swift:1049
    public void A_paired_supercharged_track_advances_sixty_times_a_second_on_a_real_window() =>
        RunsAtSixtyForTenSeconds(new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Purple, new EffortPairBrand(new HydraPair(ProviderKind.Codex))));

    [Fact]
    [Trait("Req", "PR-COMPOSER-133")] // EffortSlider.swift:1049
    public void A_fast_track_advances_sixty_times_a_second_on_a_real_window() =>
        RunsAtSixtyForTenSeconds(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Purple, null));

    [Fact]
    [Trait("Req", "PR-COMPOSER-133")] // EffortSlider.swift:1049
    public void A_fusion_track_advances_sixty_times_a_second_on_a_real_window() =>
        RunsAtSixtyForTenSeconds(new TrackLook(TrackLook.Kind.Fusion, EffortBrand.Purple, null));

    [Fact]
    [Trait("Req", "PR-COMPOSER-133")] // EffortSlider.swift:1049-1050 paused: reduceMotion || !FrontMonitor.isVisible
    public void Reduce_motion_a_minimised_window_and_an_inactive_window_each_pause_the_track_and_it_resumes_after()
    {
        native.Run(_ =>
        {
            var (window, frames, other) = native.OnUi(() =>
            {
                TrackEffect effect = null!;
                var hosted = Host(() => effect = new TrackEffect(new TrackLook(TrackLook.Kind.Fusion, EffortBrand.Purple, null)) { Width = 300, Height = 26 });
                Watch(hosted.Frames, effect);
                var other = new Avalonia.Controls.Window { Width = 200, Height = 80, ShowInTaskbar = false };
                native.Register(other);
                return (hosted.Window, hosted.Frames, other);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Thread.Sleep(500);
            Assert.True(UpdatesOver(frames, TimeSpan.FromSeconds(1)) >= 25, "running before any pause");

            // Reduce motion (ED-084: the window's inherited environment value).
            native.OnUi(() => EnvironmentValues.SetAccessibilityReduceMotion(window, true));
            Thread.Sleep(200);
            Assert.Equal(0, UpdatesOver(frames, TimeSpan.FromSeconds(2)));
            native.OnUi(() => EnvironmentValues.SetAccessibilityReduceMotion(window, false));
            Thread.Sleep(200);
            Assert.True(UpdatesOver(frames, TimeSpan.FromSeconds(1)) >= 25, "resumed after reduce motion");

            // Minimised.
            native.OnUi(() => window.WindowState = WindowState.Minimized);
            Thread.Sleep(300);
            Assert.Equal(0, UpdatesOver(frames, TimeSpan.FromSeconds(2)));
            native.OnUi(() => { window.WindowState = WindowState.Normal; window.Activate(); });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never came back");
            Thread.Sleep(200);
            Assert.True(UpdatesOver(frames, TimeSpan.FromSeconds(1)) >= 25, "resumed after restore");

            // Another window in front: the app's only window is no longer active, so nothing of it is in front.
            native.OnUi(() => { other.Show(); other.Activate(); });
            N.Until(() => native.OnUi(() => other.IsActive && !window.IsActive), "the other window never took the front");
            Thread.Sleep(300);
            Assert.Equal(0, UpdatesOver(frames, TimeSpan.FromSeconds(2)));
            native.OnUi(() => { other.Hide(); window.Activate(); });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never came back to the front");
            Thread.Sleep(200);
            Assert.True(UpdatesOver(frames, TimeSpan.FromSeconds(1)) >= 25, "resumed in front");
        }, factType: typeof(EffortSliderNativeFrameTests));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-132")] // EffortSlider.swift:760-767,1024-1026 effectIdentity
    public void With_a_pair_a_kind_change_keeps_the_running_clock_and_without_one_the_new_effect_starts_over()
    {
        native.Run(_ =>
        {
            var selected = 2;
            var pair = new EffortPairBrand(new HydraPair(ProviderKind.Codex));
            var (window, frames, paired) = native.OnUi(() =>
            {
                EffortSlider slider = null!;
                var hosted = Host(() => slider = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "High", false, pair: pair) { Width = 320 });
                return (hosted.Window, hosted.Frames, slider);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Thread.Sleep(1500);
            var (before, clockBefore) = native.OnUi(() =>
            {
                var effect = Assert.Single(paired.GetVisualDescendants().OfType<TrackEffect>());
                return (effect, Time(effect));
            });
            Assert.True(clockBefore > 1, "the pair's clock ran: " + clockBefore);
            native.OnUi(() => { selected = 0; paired.UpdateLook(true, "Low", pair); });
            Thread.Sleep(500);
            native.OnUi(() =>
            {
                // The same effect, its clock carried on through the kind change (no jump back to zero).
                Assert.Same(before, Assert.Single(paired.GetVisualDescendants().OfType<TrackEffect>()));
                Assert.InRange(Time(before), clockBefore + 0.35, clockBefore + 1.2);
            });

            // Solo: the identity is the whole look, so the replacement starts its own clock.
            var (soloWindow, solo) = native.OnUi(() =>
            {
                window.Hide();
                EffortSlider slider = null!;
                var hosted = Host(() => slider = new EffortSlider(3, new Binding<int>(() => selected, _ => { }), "Low", true) { Width = 320 });
                return (hosted.Window, slider);
            });
            N.Until(() => native.OnUi(() => soloWindow.IsActive), "the solo window never became active");
            Thread.Sleep(1500);
            native.OnUi(() => { selected = 2; solo.UpdateLook(true, "High", null); });
            Thread.Sleep(700); // past the 0.3 s crossfade
            native.OnUi(() =>
            {
                var effect = Assert.Single(solo.GetVisualDescendants().OfType<TrackEffect>());
                Assert.InRange(Time(effect), 0, 0.9);
            });
        }, factType: typeof(EffortSliderNativeFrameTests));
    }

    // OD-0090 (ED-252, review): the Jewel knob's SkSL on the real GPU path (ANGLE on D3D11, the app's default backend). The
    // liquid's one-shader form overflowed ANGLE's shader compile and killed the process (ED-233); the knob's program inlines
    // the fluid up to three times. This hosts the real slider (fast mode at Max: the knob, the liquid and the strike) on a
    // real window with the real frame clock, and checks from inside the process (the screen itself cannot be read in every
    // session: the liquid tests' screen-pixel checks fail where the window is not on top): every knob frame was drawn on a
    // GPU context, the program's picture read back from the GPU at the knob's centre is the gem's terracotta, the knob runs
    // at the frame clock's 60 a second and the gem flows. The timings are written to %TEMP%\jewel-native.txt.
    [Fact]
    [Trait("Decision", "ED-252")]
    public void The_jewel_knob_draws_on_the_gpu_of_a_real_window()
    {
        native.Run(_ =>
        {
            var index = 3;
            JewelKnobRenderer.Probe = true;
            var shownAt = System.Diagnostics.Stopwatch.StartNew();
            var (gpu0, cpu0) = (JewelKnobOperation.GpuFrames, JewelKnobOperation.CpuFrames);
            var (window, frames, slider) = native.OnUi(() =>
            {
                EffortSlider slider = null!;
                var hosted = Host(() => slider = new EffortSlider(4, new Binding<int>(() => index, value => index = value), "Max", true, EffortBrand.Claude) { Width = 320 });
                return (hosted.Window, hosted.Frames, slider);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            static bool Gem(long bgra) => bgra >= 0 && ((bgra >> 16) & 0xFF) > (bgra & 0xFF) + 30 && ((bgra >> 16) & 0xFF) > 60; // red over blue: terracotta
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!Gem(JewelKnobRenderer.ProbedCentre) && DateTime.UtcNow < deadline) Thread.Sleep(20);
            var firstGem = shownAt.Elapsed.TotalMilliseconds;
            var centre = JewelKnobRenderer.ProbedCentre;
            var screen = native.OnUi(() => slider.PointToScreen(new Avalonia.Point(slider.KnobX, EffortSlider.ThumbSize / 2)));
            var screenPixel = ScreenPixels.At(screen.X, screen.Y);
            Thread.Sleep(500);
            var (startFrame, gpuStart, phaseStart) = native.OnUi(() => (frames.Stamps.Count, JewelKnobOperation.GpuFrames, slider.Orb.Phase));
            Thread.Sleep(TimeSpan.FromSeconds(5));
            var (delivered, span, gaps, gpuFrames, phase) = native.OnUi(() =>
            {
                var stamps = frames.Stamps;
                return (stamps.Count - startFrame, stamps[^1] - stamps[startFrame], frames.GapHistogram(startFrame),
                    JewelKnobOperation.GpuFrames - gpuStart, slider.Orb.Phase - phaseStart);
            });
            JewelKnobRenderer.Probe = false;
            var state = native.OnUi(() =>
            {
                var view = slider.GetVisualDescendants().OfType<JewelKnobView>().Single();
                return $"knob view recorded {view.Recorded} times, {view.OperationsMade} operations, bounds {view.Bounds}, visible {view.IsEffectivelyVisible}; " +
                    $"slider {slider.Bounds}; window {window.Bounds} state {window.WindowState} active {window.IsActive} scaling {window.RenderScaling}; " +
                    $"track effects {slider.GetVisualDescendants().OfType<TrackEffect>().Count()}; GPU knob frames in all {JewelKnobOperation.GpuFrames - gpu0}, " +
                    $"the last {Environment.TickCount64 - JewelKnobOperation.LastGpuFrameTicks} ms ago; read-backs failed {JewelKnobRenderer.ProbeFailures}; " +
                    $"render-thread error: {JewelKnobOperation.LastError ?? "none"}";
            });
            var report = $"first gem read back from the GPU {firstGem:F0} ms after the window was made (centre BGRA {centre:X8}); knob frames on a GPU context " +
                $"{gpuFrames} in {span:F0} ms ({gpuFrames * 1000.0 / span:F1} a second), on the CPU renderer {JewelKnobOperation.CpuFrames - cpu0}; frames delivered " +
                $"{delivered} ({delivered * 1000.0 / span:F1} a second), gaps ms {gaps}; the gem's phase moved {phase:F2} in 5 s (2.6 a second at rest, " +
                $"4.2 charged); the first knob draw call on the render thread (the programs' compile) {JewelKnobOperation.FirstGpuRenderMs:F1} ms, the longest after it {JewelKnobOperation.MaxGpuRenderMs:F2} ms (each with the test's GPU read-back); the screen at the knob's centre {screenPixel}; {state}";
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "jewel-native.txt"), report + Environment.NewLine);
            Assert.True(Gem(centre), report);
            Assert.True(JewelKnobOperation.CpuFrames == cpu0, report); // a GPU window draws the knob on the GPU
            Assert.True(gpuFrames * 1000.0 / span >= 55, report);
            Assert.True(delivered * 1000.0 / span >= 58, report);
            Assert.InRange(phase, 2.6 * 5 * 0.8, 4.2 * 5 * 1.1);
        }, factType: typeof(EffortSliderNativeFrameTests));
    }
}
