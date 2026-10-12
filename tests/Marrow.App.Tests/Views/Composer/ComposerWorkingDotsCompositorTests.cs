using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift (B3 52bc792), :5-11, :124-190.
// STOP cloud-verify #3 (2026-10-04): OD-0098 closes PR-COMPOSER-104 (no frame shows reduced motion). The reference video shows the one-line composer working with its dot
// field (2-chat-use.mov, CATALOG scene 29, still C 92.00); ComposerWorkingDotsFrameTests compares it dot by dot (the geometry, the three rows, the wave and its phase against
// the Swift curve) and found the third row clipped at the field's bounds (fixed). PR-COMPOSER-102, PR-COMPOSER-103 and UI-COMPOSER-017 also carry "no main-thread drawing"
// (windows_path: "it must not tick on the UI thread"), which is not a capture clause and which the headless renderer cannot show: it needs a native
// (Win32 compositor) test that the draw runs off the UI thread. CLOSED (rec-composer): ComposerWorkingDotsNativeTests plays the field on a real window and finds every
// render-server sample off the UI thread, no UI-thread render update, layout pass, property change, frame request or new timer, and two fields in phase on the screen.
// The literals these classes assert from the Swift: ComposerWorkingDotsTests, ...FieldTests,
// ...PillTests (the mounted pill at one and five lines), ...ReviewTests and this class assert every literal (2 point dots, 8 point pitch, three rows, the
// 0.08/0.75/0.08 keyframe at 0.35, 2.2 s, 0.045 and 0.09 delays, the shared clock origin, the static 0.28). "No timer, view update or main-thread drawing" is
// asserted as observed (no frame subscription, no property change, no active timer while the field plays) and rests, for the thread the draw runs on,
// on Avalonia's behaviour that a CompositionCustomVisualHandler (DotFieldCompositionHandler) runs on the render thread, which ComposerWorkingDotsNativeTests observes on a real window.
[Collection("Headless")]
public sealed class ComposerWorkingDotsCompositorTests(HeadlessSession headless)
{
    private static object Read(object owner, string name)
    {
        var value = owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(owner);
        Assert.NotNull(value);
        return value;
    }
    private static double Opacity(DotFieldControl control, int row, int column)
    {
        var frame = Read(control, "LastRenderedFrame");
        return Assert.IsType<double>(frame.GetType().GetMethod("OpacityAt")!.Invoke(frame, [row, column]));
    }
    private static void Render(Avalonia.Controls.Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var pixels = window.CaptureRenderedFrame();
        Assert.NotNull(pixels);
    }
    private static Avalonia.Controls.Window Show(Control control, double width = 32, double height = 24)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Background = Brushes.Black, Content = control };
        window.Show();
        Render(window);
        return window;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-103")] // :5-11 render server alone; source scan complements actual output and UI property/frame observations.
    public void The_animation_path_has_no_UI_frame_timer_or_Avalonia_animation_clock()
    {
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(SourceDirectory(), "../../../.."));
        var folder = System.IO.Path.Combine(root, "src/Marrow.App/Views/Composer");
        var control = System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "ComposerWorkingDots.cs"));
        var handler = System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "DotFieldCompositionHandler.cs"));
        var frame = System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "DotFieldCompositionFrame.cs"));
        Assert.DoesNotContain("DisplayLink", control, StringComparison.Ordinal);
        Assert.DoesNotContain("FrameClock.", control, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestAnimationFrame", control, StringComparison.Ordinal);
        Assert.Contains(": CompositionCustomVisualHandler", handler, StringComparison.Ordinal);
        Assert.Contains("override void OnAnimationFrameUpdate", handler, StringComparison.Ordinal);
        Assert.Contains("RegisterForNextAnimationFrameUpdate()", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Timer", handler, StringComparison.Ordinal);
        // ED-NEW-lean-9 (pacing): the frame never names the compositor's time; the handler names it once, as the shared 60 Hz pacer's
        // tick identity, with the clock's (SwiftClock) seconds as the pacer's time; the draw's time is the clock's (the frame's ClockNow).
        Assert.DoesNotContain("CompositionNow", frame, StringComparison.Ordinal);
        Assert.Equal(1, handler.Split("CompositionNow").Length - 1);
        Assert.Contains("pacer(CompositionNow.Ticks, paced.ClockNow().Ticks / (double)TimeSpan.TicksPerSecond)", handler, StringComparison.Ordinal);
        Assert.Contains("SwiftClock.Now", frame, StringComparison.Ordinal);
    }
    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "") => System.IO.Path.GetDirectoryName(source)!;
    [Theory]
    [InlineData(1.385)]
    [InlineData(2.485)]
    [Trait("Req", "PR-COMPOSER-103")] // :179-185 midpoint of each unequal segment uses CA ease-out, then ease-in (not linear).
    public void The_composited_pulse_uses_the_sources_two_cubic_timing_functions(double time) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var field = new DotFieldControl(new SwiftColor(1), true, false);
        var window = Show(field);
        try
        {
            Assert.Equal(0, frames.Pending);
            runtime.Advance(TimeSpan.FromSeconds(time)); Render(window);
            Assert.InRange(Opacity(field, 0, 0), 0.537, 0.540); // Cubic .684643 / .315357 of .67, respectively.
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
    [Fact]
    [Trait("Req", "PR-COMPOSER-103")] // :5-11 no UI frame/property subscription; :177-190 phase/keyframes. Headless forces UI rendering, so actual render-thread ownership needs a production-renderer proof.
    public void Two_fields_draw_on_the_compositor_in_phase_without_any_UI_frame_subscription() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var first = new DotFieldControl(new SwiftColor(1, 0, 0), true, false);
        var window = Show(first);
        try
        {
            Assert.Equal(0, frames.Pending); // Baseline defect: the old renderer schedules UI-thread DisplayLink ticks.
            Assert.IsType<CompositionCustomVisual>(ElementComposition.GetElementChildVisual(first));
            runtime.Advance(TimeSpan.FromSeconds(1.77));
            Render(window);
            Assert.Equal(0.75, Opacity(first, 0, 0), 8);
            Assert.Same(runtime.Time, Read(Read(first, "LastRenderedFrame"), "Clock"));
            var second = new DotFieldControl(new SwiftColor(1, 0, 0), true, false);
            var host = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            first.Width = second.Width = 16;
            second.Margin = new Thickness(24, 0, 0, 0); // the field's extra column past its width spills beyond its bounds, as the replicator's does (the frame of C 92.000 shows the third row above its field): the two are kept clear of each other
            window.Content = null;
            host.Children.Add(first); host.Children.Add(second); window.Content = host; window.Width = 80;
            Render(window);
            Assert.Equal(Opacity(first, 0, 0), Opacity(second, 0, 0), 8);
            var properties = 0;
            first.PropertyChanged += (_, _) => properties++;
            foreach (var dot in Assert.IsType<Canvas>(first.Layer).Children) dot.PropertyChanged += (_, _) => properties++;
            runtime.Advance(TimeSpan.FromSeconds(0.045));
            Render(window);
            Assert.Equal(0.75, Opacity(first, 0, 1), 8);
            Assert.True(Opacity(first, 0, 0) < Opacity(first, 0, 1));
            runtime.Advance(TimeSpan.FromSeconds(0.045));
            Render(window);
            Assert.Equal(0.5, Opacity(first, 1, 0), 8); // .09 row delay, faded by 2/3.
            runtime.Advance(TimeSpan.FromSeconds(1.34));
            Render(window);
            Assert.Equal(0.08, Opacity(first, 0, 0), 8); // Shared 1 + 2.2, next cycle.
            runtime.Advance(TimeSpan.FromSeconds(22.77));
            Render(window);
            Assert.Equal(0.75, Opacity(first, 0, 0), 8);
            Assert.Same(runtime.Time, Read(Read(first, "LastRenderedFrame"), "Clock"));
            Assert.Equal(Opacity(first, 0, 0), Opacity(second, 0, 0), 8);
            Assert.Equal(0, properties);
            Assert.Equal(0, frames.Pending);
            Assert.Equal(0, runtime.Time.ActiveTimers);
            using var capture = window.CaptureRenderedFrame();
            using var stream = new System.IO.MemoryStream();
            capture!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            using var pixels = SKBitmap.Decode(stream.ToArray());
            Assert.True(pixels.GetPixel(3, 19).Red > 100); // Real composited dot, not just a state sampler.
            Assert.Equal(pixels.GetPixel(3, 19), pixels.GetPixel(43, 19));
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-103")] // :158-175 one sweep, every row in phase, all delayed columns retained until complete.
    public void Sweep_composites_one_pass_and_stops_after_the_final_delayed_column() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var field = new DotFieldControl(new SwiftColor(1), true, true);
        var window = Show(field, 16, 40);
        try
        {
            Assert.Equal(0, frames.Pending);
            runtime.Advance(TimeSpan.FromSeconds(0.32)); Render(window);
            Assert.Equal(0.7, Opacity(field, 0, 0), 8);
            Assert.Equal(Opacity(field, 0, 0), Opacity(field, 4, 0), 8);
            Assert.True(Opacity(field, 0, 1) < Opacity(field, 0, 0));
            runtime.Advance(TimeSpan.FromSeconds(0.022)); Render(window);
            Assert.Equal(0.7, Opacity(field, 0, 1), 8);
            runtime.Advance(TimeSpan.FromSeconds(0.458)); Render(window); // Base ends at .8; last column still paints.
            Assert.Equal(0, Opacity(field, 0, 0), 8);
            Assert.True(Opacity(field, 0, 2) > 0);
            runtime.Advance(TimeSpan.FromSeconds(0.044)); Render(window);
            Assert.Equal(0, Opacity(field, 0, 2), 8);
            Assert.False(Assert.IsType<bool>(Read(Read(field, "CompositionHandler"), "IsAnimating")));
            Assert.Equal(0, frames.Pending);
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-103")] // :104-111,113-122,152-190 motion reinstalls; color/theme never restarts; window reattachment installs anew.
    [Trait("Req", "PR-COMPOSER-104")] // :13-19,154-156 static .28 and removal of animated pulse.
    public void Motion_and_attachment_stop_compositor_work_while_color_changes_keep_the_wave_origin() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var field = new DotFieldControl(new SwiftColor(1, 0, 0), true, true);
        var window = Show(field, 16, 24);
        try
        {
            var visual = ElementComposition.GetElementChildVisual(field);
            var handler = Read(field, "CompositionHandler");
            runtime.Advance(TimeSpan.FromSeconds(0.32)); Render(window);
            field.UpdateColor(new SwiftColor(0, 0, 1)); field.OnAppearanceChanged(); Render(window);
            Assert.Same(visual, ElementComposition.GetElementChildVisual(field));
            Assert.Equal(0.7, Opacity(field, 0, 0), 8);
            Assert.Equal(Colors.Blue, Assert.IsAssignableFrom<ISolidColorBrush>(Read(Read(field, "LastRenderedFrame"), "Brush")).Color);
            field.UpdateAnimated(false); Render(window);
            Assert.False(Assert.IsType<bool>(Read(handler, "IsAnimating")));
            Assert.False(Assert.IsType<CompositionCustomVisual>(visual).Visible);
            Assert.All(Assert.IsType<Canvas>(field.Layer).Children, dot => Assert.Equal(0, dot.Opacity));
            runtime.Advance(TimeSpan.FromSeconds(1));
            field.UpdateAnimated(true); Render(window);
            Assert.Same(visual, ElementComposition.GetElementChildVisual(field));
            Assert.Equal(0, Opacity(field, 0, 0), 8);
            runtime.Advance(TimeSpan.FromSeconds(0.32)); Render(window);
            Assert.Equal(0.7, Opacity(field, 0, 0), 8);
            window.Content = null; Render(window);
            Assert.Null(ElementComposition.GetElementChildVisual(field));
            Assert.True(Assert.IsType<bool>(Read(handler, "IsStopped")));
            window.Content = field; Render(window);
            Assert.NotSame(visual, ElementComposition.GetElementChildVisual(field));
            Assert.Equal(0, Opacity(field, 0, 0), 8);
            runtime.Advance(TimeSpan.FromSeconds(0.32)); Render(window);
            Assert.Equal(0.7, Opacity(field, 0, 0), 8);
            Assert.Equal(0, frames.Pending);
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
