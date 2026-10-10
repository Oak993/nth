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

    private void Front(Avalonia.Controls.Window window) => native.OnUi(() =>
        WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("NATIVE_PRECONDITION: the test window has no HWND."), native.ThreadId));

    private void StillFront(Avalonia.Controls.Window window, string measured) => native.OnUi(() =>
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (N.GetForegroundWindow() != handle)
            throw new InvalidOperationException($"NATIVE_PRECONDITION: the test window lost the foreground during the measured seconds (foreground 0x{N.GetForegroundWindow():X}, window 0x{handle:X}); {measured}.");
    });

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
    // at the frame clock's 60 a second and the gem flows. The timings are written to jewel-native.txt (the run's data folder).
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
            File.WriteAllText(Record("jewel-native.txt"), report + Environment.NewLine);
            Assert.True(Gem(centre), report);
            Assert.True(JewelKnobOperation.CpuFrames == cpu0, report); // a GPU window draws the knob on the GPU
            Assert.True(gpuFrames * 1000.0 / span >= 55, report);
            Assert.True(delivered * 1000.0 / span >= 58, report);
            Assert.InRange(phase, 2.6 * 5 * 0.8, 4.2 * 5 * 1.1);
        }, factType: typeof(EffortSliderNativeFrameTests));
    }

    // OD-0090 / ED-252 (lead's ruling, 2026-10-03): the composer primes the knob's programs after its window's first frame, at
    // idle, so the first open of the effort popover does not wait for their compile. A window hosts the primer (as the
    // composer's model chip does) and an animating liquid track; once primed, the slider opens in a popup from it, as the
    // effort popover does, and its first knob draw on the GPU must fit a 60 Hz frame. Recorded: jewel-warm.txt (the run's data folder).
    [Fact]
    [Trait("Decision", "ED-252")]
    public void The_knob_warm_up_keeps_the_popovers_first_knob_draw_within_a_frame()
    {
        native.Run(_ =>
        {
            var index = 3;
            var (window, frames, host) = native.OnUi(() =>
            {
                Border host = null!;
                var hosted = Host(() => new StackPanel { Children = { (host = new Border { Width = 300, Height = 26, Child = new TrackEffect(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude)) { Width = 300, Height = 26 } }) } });
                GpuWarmup.WhenIdle(host);
                return (hosted.Window, hosted.Frames, host);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (GpuWarmup.StepsDone < GpuWarmup.Steps && DateTime.UtcNow < deadline) Thread.Sleep(20);
            var steps = string.Join(", ", GpuWarmup.Done.Select(d => $"{d.Program} {d.Milliseconds:F1} ms{(d.Forced ? " (forced)" : "")}"));
            Assert.True(GpuWarmup.StepsDone >= GpuWarmup.Steps, $"the warm-up did not finish: {GpuWarmup.StepsDone} steps ({steps})");
            Thread.Sleep(300);
            var slider = native.OnUi(() =>
            {
                var slider = new EffortSlider(4, new Binding<int>(() => index, value => index = value), "Max", true, EffortBrand.Claude) { Width = 320 };
                var popup = new Avalonia.Controls.Primitives.Popup { PlacementTarget = host, Placement = PlacementMode.Bottom, Child = new Border { Width = 340, Height = 60, Child = slider } };
                ((Panel)window.Content!).Children.Add(popup);
                popup.IsOpen = true;
                return slider;
            });
            deadline = DateTime.UtcNow.AddSeconds(10);
            while (JewelKnobOperation.GpuFrames < 30 && DateTime.UtcNow < deadline) Thread.Sleep(20);
            var report = $"warm-up steps {steps}; the popover's first knob draw {JewelKnobOperation.FirstGpuRenderMs:F2} ms, the longest after it " +
                $"{JewelKnobOperation.MaxGpuRenderMs:F2} ms over {JewelKnobOperation.GpuFrames} GPU knob frames ({JewelKnobOperation.CpuFrames} on the CPU)";
            File.WriteAllText(Record("jewel-warm.txt"), report + Environment.NewLine);
            Assert.True(JewelKnobOperation.GpuFrames >= 30, report);
            Assert.True(JewelKnobOperation.FirstGpuRenderMs < 1000.0 / 60, report);
        }, factType: typeof(EffortSliderNativeFrameTests));
    }

    // The warm-up's own frames: the window's frame gaps over its first 5 s with a liquid track animating throughout (the worst
    // case: the idle fallback finds no quiet frame and steps at its 3 s cap), with the primer and without it; and with the
    // track animating for 2 s only (the fallback waits for it to stop). Recorded in jewel-warm-frames.txt (the run's data folder).
    [Fact]
    [Trait("Decision", "ED-252")]
    public void The_knob_warm_up_waits_for_the_first_frame_and_its_frames_are_measured() => WarmFrames(true);

    [Fact]
    [Trait("Decision", "ED-252")]
    public void Without_the_warm_up_the_windows_frames_are_measured_the_same_way() => WarmFrames(false);

    [Fact]
    [Trait("Decision", "ED-252")]
    public void The_knob_warm_up_waits_until_the_animation_stops() => WarmFrames(true, stopAfter: 2000);

    private void WarmFrames(bool warm, int stopAfter = 0, [System.Runtime.CompilerServices.CallerMemberName] string fact = "")
    {
        native.Run(_ =>
        {
            var shown = Environment.TickCount64;
            var (window, frames, host) = native.OnUi(() =>
            {
                Border host = null!;
                var hosted = Host(() => host = new Border { Width = 300, Height = 26, Child = new TrackEffect(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude)) { Width = 300, Height = 26 } });
                if (warm) GpuWarmup.WhenIdle(host);
                return (hosted.Window, hosted.Frames, host);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            long stopped = 0, stoppedAt = 0;
            if (stopAfter > 0)
            {
                Thread.Sleep(stopAfter);
                // the track leaves the window: its frame requests go with it, nothing animates any more
                (stopped, stoppedAt) = native.OnUi(() => { host.Child = null; return (Environment.TickCount64, (long)frames.Stamps.Count); });
                Thread.Sleep(5000 - stopAfter);
            }
            else Thread.Sleep(5000);
            var (gaps, count, longest) = native.OnUi(() =>
            {
                var stamps = stopAfter > 0 ? frames.Stamps.Take((int)stoppedAt).ToList() : frames.Stamps; // the animating frames
                var longest = stamps.Zip(stamps.Skip(1), (a, b) => (Gap: b - a, At: b)).OrderByDescending(x => x.Gap).Take(3).ToArray();
                return (frames.GapHistogram(0), stamps.Count, string.Join(" ", longest.Select(x => $"{x.Gap:F0}@{x.At:F0}")));
            });
            var first = native.OnUi(() => frames.Stamps.Count > 0 ? frames.Stamps[0] : -1);
            var line = $"warm-up {(warm ? "on" : "off")}{(stopAfter > 0 ? $", the track stopped after {stopAfter} ms (the frames while it animated)" : "")}: " +
                $"{count} frames{(stopAfter > 0 ? "" : " in 5 s")}, gaps ms {gaps}; the longest (ms@ms since the frame clock started) {longest}" +
                (warm ? $"; its steps {string.Join(", ", GpuWarmup.Done.Select(d => $"{d.Program} {d.Milliseconds:F1} ms{(d.Forced ? " (forced)" : "")}"))}, the first {GpuWarmup.FirstStepTicks - shown} ms after the window was made" : "") +
                (stopAfter > 0 ? $", {GpuWarmup.FirstStepTicks - stopped} ms after the track stopped" : "");
            File.AppendAllText(Record("jewel-warm-frames.txt"), line + Environment.NewLine);
            if (warm)
            {
                Assert.True(GpuWarmup.StepsDone == GpuWarmup.Steps, line); // one program a frame
                Assert.True(first >= 0 && GpuWarmup.FirstStepTicks > shown, line); // after the window's first frame
                if (stopAfter > 0)
                {
                    Assert.True(GpuWarmup.FirstStepTicks > stopped, line);   // nothing compiled while the track animated
                    Assert.True(GpuWarmup.Done.All(d => !d.Forced), line);   // each in a quiet frame, within the cap
                }
                else Assert.True(GpuWarmup.Done.All(d => d.Forced), line);   // never quiet: stepped at the cap
            }
            else Assert.Equal(0, GpuWarmup.StepsDone);
        }, fact, typeof(EffortSliderNativeFrameTests));
    }

    // ED-252 (lead's ruling): ANGLE's program binaries kept on disk, so only the first launch on a machine compiles the knob's
    // programs. Both launches share one folder under the run's data folder (never the user's). Recorded: jewel-cache.txt.
    private static string SharedProgramCache() => Path.Combine(RunFolder(), "jewel-gpu-programs");

    [Fact]
    [Trait("Decision", "ED-248")]
    public void A_steady_jewel_slider_delivers_no_frame_clock_callbacks_on_main()
    {
        native.Run(_ =>
        {
            var (window, source, slider) = native.OnUi(() =>
            {
                var window = new Avalonia.Controls.Window { Width = 380, Height = 110, ShowInTaskbar = false };
                native.Register(window);
                window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
                var source = new StrikeMeasurementFrameSource(new AvaloniaFrameSource(window));
                FrameClock.Source.SetProcessDefault(source);
                var selected = 3;
                var slider = new EffortSlider(4, new Binding<int>(() => selected, value => selected = value), "Max", true, EffortBrand.Claude) { Width = 298 };
                window.Content = slider; window.Show(); window.Activate();
                return (window, source, slider);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            Thread.Sleep(5000);
            var before = native.OnUi(() => source.MainFrames);
            var gpu = JewelKnobOperation.GpuFrames;
            Thread.Sleep(3000);
            Assert.True(JewelKnobOperation.GpuFrames > gpu, "the GPU knob must keep animating");
            var delivered = native.OnUi(() => source.MainFrames) - before;
            File.WriteAllText(Record("strike-main-sources.json"), System.Text.Json.JsonSerializer.Serialize(
                source.RenderSources.Select(s => new { periodMs = s.RefreshPeriod?.TotalMilliseconds, ticks = s.Ticks, refresh = s.Refresh })));

            Assert.True(delivered == 0, $"steady slider delivered {delivered} main callbacks in three seconds");
        }, factType: typeof(EffortSliderNativeFrameTests));
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Both_retained_slider_sources_keep_sixty_updates_per_second_without_refresh_drift()
    {
        native.Run(_ =>
        {
            var (window, source, slider) = native.OnUi(() =>
            {
                var window = new Avalonia.Controls.Window { Width = 380, Height = 110, ShowInTaskbar = false };
                native.Register(window);
                window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
                var source = new StrikeMeasurementFrameSource(new AvaloniaFrameSource(window));
                FrameClock.Source.SetProcessDefault(source);
                var selected = 3;
                var slider = new EffortSlider(4, new Binding<int>(() => selected, value => selected = value), "Max", true, EffortBrand.Claude) { Width = 298 };
                window.Content = slider; window.Show(); window.Activate();
                return (window, source, slider);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            // T44's ambient policy runs the track only while its window is the foreground one, uncovered and used within 20 s; a window that
            // merely reports IsActive may sit in the background (the gate's count was 0). The shared foreground helper brings it to the front or
            // refuses with NATIVE_PRECONDITION; the same check follows the measured seconds, so a lost front is named and not read as a slow track.
            Front(window);
            Thread.Sleep(5000);
            var effect = native.OnUi(() => Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>()));
            var before = effect.Draws;
            var gpu = JewelKnobOperation.GpuFrames;
            Thread.Sleep(3000);
            var count = effect.Draws - before;
            var jewel = JewelKnobOperation.GpuFrames - gpu;
            StillFront(window, $"count {count}, jewel {jewel}");
            File.WriteAllText(Record("strike-native-cadence.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                count, jewel, sources = source.RenderSources.Select(s => new { periodMs = s.RefreshPeriod?.TotalMilliseconds, ticks = s.Ticks, refresh = s.Refresh })
            }));
            Assert.InRange(count, 174, 183);
            Assert.InRange(jewel, 174, 183);
        }, factType: typeof(EffortSliderNativeFrameTests));
    }
    // ST-0 diagnostic under shared-PC load; the lead's quiet-window gate remains separate.
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Strike_round_three_records_sixty_seconds_with_server_gc_on_the_real_compositor()
    {
        native.Run(_ =>
        {
            Assert.True(System.Runtime.GCSettings.IsServerGC, "the child must enable server GC before runtime startup");
            Assert.Null(Environment.GetEnvironmentVariable("DOTNET_GCHeapAffinitizeMask"));
            var (window, source, slider) = native.OnUi(() =>
            {
                var window = new Avalonia.Controls.Window { Width = 380, Height = 110, ShowInTaskbar = false };
                native.Register(window);
                window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
                var source = new StrikeMeasurementFrameSource(new AvaloniaFrameSource(window));
                FrameClock.Source.SetProcessDefault(source);
                var selected = 3;
                var slider = new EffortSlider(4, new Binding<int>(() => selected, value => selected = value), "Max", true, EffortBrand.Claude) { Width = 298 };
                window.Content = slider; window.Show(); window.Activate();
                return (window, source, slider);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the measurement window never became active");
            Thread.Sleep(5000);
            var probe = new StrikeTickMeasurement();
            var before = native.OnUi(() =>
            {
                probe.Effect = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
                Assert.NotEmpty(source.RenderSources);
                var visual = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(slider)!.Compositor.CreateCustomVisual(probe);
                Avalonia.Rendering.Composition.ElementComposition.SetElementChildVisual(slider, visual);
                probe.Active = window.IsActive;
                window.PropertyChanged += (_, change) => { if (change.Property == WindowBase.IsActiveProperty) probe.Active = window.IsActive; };
                probe.On = true;
                visual.SendHandlerMessage(StrikeTickMeasurement.Start);
                return (UiBytes: GC.GetAllocatedBytesForCurrentThread(), Main: source.MainFrames,
                    Records: probe.Effect.Recordings, Thread: Environment.CurrentManagedThreadId);
            });
            var collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            var gpu = JewelKnobOperation.GpuFrames;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Thread.Sleep(60000);
            probe.On = false;
            var seconds = watch.Elapsed.TotalSeconds;
            var after = native.OnUi(() => (UiBytes: GC.GetAllocatedBytesForCurrentThread(), Main: source.MainFrames, Records: probe.Effect.Recordings, Active: window.IsActive));
            var n = probe.Count;
            Assert.True(n > 600, "the real compositor did not deliver measurement ticks");
            Assert.NotEqual(before.Thread, probe.Thread);
            Assert.True(JewelKnobOperation.GpuFrames > gpu, "the knob must use the real GPU");
            var draws = probe.Draws[n - 1] - probe.Draws[0];
            var intervals = Enumerable.Range(1, n - 1).Select(i => probe.Bytes[i] - probe.Bytes[i - 1]).OrderDescending().ToArray();
            var drawn = intervals.Take((int)draws).Order().ToArray();
            var quiet = intervals.Skip((int)draws).Order().ToArray();
            var gaps = new List<double>();
            var last = probe.Stamps[0];
            for (var i = 1; i < n; i++) if (probe.Draws[i] != probe.Draws[i - 1])
            {
                gaps.Add(System.Diagnostics.Stopwatch.GetElapsedTime(last, probe.Stamps[i]).TotalMilliseconds); last = probe.Stamps[i];
            }
            gaps.Add(System.Diagnostics.Stopwatch.GetElapsedTime(last, probe.Stamps[n - 1]).TotalMilliseconds);
            var report = new
            {
                gate = "INFORMATIVE_UNDER_SHARED_PC_LOAD", serverGc = System.Runtime.GCSettings.IsServerGC,
                seconds, ticks = n, draws, updatesPerSecond = draws / seconds,
                inactiveTicks = probe.InactiveTicks, activeAtEnd = after.Active,
                sources = source.RenderSources.Select(s => new { ticks = s.Ticks, refresh = s.Refresh, periodMs = s.RefreshPeriod?.TotalMilliseconds }),
                mainFrames = after.Main - before.Main, uiBytes = after.UiBytes - before.UiBytes,
                uiBytesPerSecond = (after.UiBytes - before.UiBytes) / seconds, recordings = after.Records - before.Records,
                renderThread = probe.Thread, uiThread = before.Thread,
                drawnMedianBytes = drawn.Length == 0 ? 0 : drawn[drawn.Length / 2],
                quietMedianBytes = quiet.Length == 0 ? 0 : quiet[quiet.Length / 2],
                longestObservedContentGapMs = gaps.Count == 0 ? 0 : gaps.Max(),
                collections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collections[i]).ToArray(),
                gpuKnobFrames = JewelKnobOperation.GpuFrames - gpu,
                quietWindow = false, nativeLockHeld = true,
                presentationCapture = "not measured; compositor update gaps are not DXGI present intervals"
            };
            var encoded = System.Text.Json.JsonSerializer.SerializeToElement(report);
            File.WriteAllText(Record("strike-st0-gate-failures.json"), System.Text.Json.JsonSerializer.Serialize(StrikeMeasurementGate.Failures(encoded)));
            File.WriteAllText(Record("strike-st0-load.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }, factType: typeof(EffortSliderNativeFrameTests));
    }

    // the run's data folder (it outlives the native child, whose own temp folder is removed with it)
    private static string RunFolder()
    {
        var data = Marrow.Testing.TestDataDirectory.Read()!;
        // a native child's data folder is <the run's data folder>\WindowManagerNative\<nonce>\data
        return WindowManagerNativeChildProtocol.IsChild ? Path.GetFullPath(Path.Combine(data, "..", "..", "..")) : data;
    }

    // a native record, kept in the run's data folder
    private static string Record(string name) => Path.Combine(RunFolder(), name);

    private void Launch(string what)
    {
        native.Run(_ =>
        {
            GpuProgramCache.FolderOverride = SharedProgramCache();
            var (window, _) = native.OnUi(() =>
            {
                Border host = null!;
                var hosted = Host(() => host = new Border { Width = 300, Height = 26 });
                GpuWarmup.WhenIdle(host);
                return (hosted.Window, host);
            });
            N.Until(() => native.OnUi(() => window.IsActive), "the test window never became active");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (GpuWarmup.StepsDone < GpuWarmup.Steps && DateTime.UtcNow < deadline) Thread.Sleep(20);
            Thread.Sleep(200);
            var line = $"{what}: cache {GpuProgramCache.State ?? "on"} ({GpuProgramCache.Folder}); entries written {GpuProgramCache.Writes}, found {GpuProgramCache.Hits}, " +
                $"missing {GpuProgramCache.Misses}; warm-up steps {string.Join(", ", GpuWarmup.Done.Select(d => $"{d.Program} {d.Milliseconds:F1} ms{(d.Forced ? " (forced)" : "")}"))}";
            File.AppendAllText(Record("jewel-cache.txt"), line + Environment.NewLine);
            Assert.True(GpuProgramCache.State is null, line);
            Assert.True(GpuWarmup.StepsDone == GpuWarmup.Steps, line);
            if (what == "first launch") Assert.True(GpuProgramCache.Writes > 0, line);
            else
            {
                Assert.True(GpuProgramCache.Hits > 0, line);
                Assert.True(GpuWarmup.Done.All(d => d.Milliseconds < 1000.0 / 60), line); // nothing compiled on the render thread
            }
        }, what == "first launch" ? nameof(The_knob_programs_are_compiled_on_a_first_launch_and_kept) : nameof(On_a_second_launch_the_knob_programs_come_from_the_cache),
            typeof(EffortSliderNativeFrameTests));
    }

    [Fact]
    [Trait("Decision", "ED-252")]
    public void The_knob_programs_are_compiled_on_a_first_launch_and_kept()
    {
        if (!WindowManagerNativeChildProtocol.IsChild && Directory.Exists(SharedProgramCache())) Directory.Delete(SharedProgramCache(), true);
        Launch("first launch");
    }

    [Fact]
    [Trait("Decision", "ED-252")]
    public void On_a_second_launch_the_knob_programs_come_from_the_cache()
    {
        if (!WindowManagerNativeChildProtocol.IsChild)
        {
            // the first launch, in a process of its own, fills the folder this launch reads
            if (Directory.Exists(SharedProgramCache())) Directory.Delete(SharedProgramCache(), true);
            WindowManagerNativeChildHost.RunParent(WindowManagerNativeChildProtocol.FactName(nameof(The_knob_programs_are_compiled_on_a_first_launch_and_kept), typeof(EffortSliderNativeFrameTests)));
        }
        Launch("second launch");
    }
}
