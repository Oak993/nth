using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for "the render server plays it alone: no timer, no view update and no main-thread drawing"
// (DroppyCode/App/Views/Composer/ComposerWorkingDots.swift:5-11, :152-190; windows_path of PR-COMPOSER-102 / -103 and
// UI-COMPOSER-017: "it must not tick on the UI thread"). Closes STOP cloud-verify #3 (ComposerWorkingDotsCompositorTests.cs:17-27),
// where render-thread ownership rested on an ENGINEERING DECISION because the headless renderer draws on the UI thread.
// Here the field plays on a real Win32 window on the production compositor:
//   - SwiftClock's provider counts the threads that read it. DotFieldCompositionFrame.Sample reads the clock its field captured,
//     so every frame the render server samples (OnRender, and the animation-frame callback's RequestFrameIfNeeded) names its
//     thread; once the field is laid out, nothing on the UI thread reads that clock while it plays;
//   - a probe on the window's CompositingRenderer sees every UI-thread render update and what it redrew: none redraws the field;
//   - no layout pass, no property change on the field or a dot, no frame request on the frame clock, no new DispatcherTimer;
//   - meanwhile the frames the render server drew advance through the pulse, and so do the real screen pixels of a dot.
// Needs a free interactive desktop; the runner takes native.lock around the run.
[Collection("WindowManagerNative")]
public sealed class ComposerWorkingDotsNativeTests(WindowManagerNativeSession native)
{
    private const double FieldHeight = 18.4; // the bottom 40 percent of the 46 pt pill (ComposerView.swift:661-675)

    private static void Themeless(AvaloniaWindow window) =>
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });

    private AvaloniaWindow Show(Control content, PixelPoint at, double width, double height)
    {
        var window = new AvaloniaWindow
        {
            WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = at, Width = width, Height = height,
            Background = Brushes.Black, Content = content,
        };
        Themeless(window);
        native.Register(window);
        window.Show();
        return window;
    }

    private static List<Avalonia.Threading.DispatcherTimer> Timers()
    {
        var snapshot = typeof(Dispatcher).GetMethod("SnapshotTimersForUnitTests", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(Dispatcher), "SnapshotTimersForUnitTests");
        return (List<Avalonia.Threading.DispatcherTimer>)snapshot.Invoke(snapshot.IsStatic ? null : Dispatcher.UIThread, null)!;
    }

    // The screen's brightest luma in a 5 x 5 block around a point of a control (a 2 pt dot lies inside it at any scale).
    private static double Peak(PixelPoint center)
    {
        var grab = ScreenGrab.Of(center.X - 2, center.Y - 2, 5, 5);
        var peak = 0.0;
        for (var y = 0; y < 5; y++)
            for (var x = 0; x < 5; x++) peak = Math.Max(peak, grab.Luma(x, y));
        return peak;
    }

    private static PixelPoint Dot(Control field, int row, int column) =>
        field.PointToScreen(new Point(4 + column * 8, field.Bounds.Height - (4 + row * 8)));

    [Fact]
    [Trait("Req", "PR-COMPOSER-102")] // windows_path: it must not tick on the UI thread
    [Trait("Req", "PR-COMPOSER-103")] // ComposerWorkingDots.swift:5-11 the render server plays it alone
    [Trait("Req", "UI-COMPOSER-017")] // record notes: no timer, no view update and no main-thread drawing
    public void The_dot_field_plays_on_the_render_thread_while_the_UI_thread_stays_idle() => native.Run(subject =>
    {
        var clock = new ThreadRecordingTimeProvider();
        var frames = new ManualFrameSource();
        var (window, dots, field, uiThread) = native.OnUi(() =>
        {
            subject.Main.Hide();
            subject.Settings.Hide();
            SwiftClock.Provider.SetProcessDefault(clock); // read by the field when it lays out, and captured into each frame it sends
            FrameClock.Source.SetProcessDefault(frames); // a frame request from anything would wait here
            var dots = new ComposerWorkingDots { Width = 400, Height = FieldHeight, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20) };
            var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
            var window = Show(new Panel { Children = { dots } }, new PixelPoint(screen.WorkingArea.X + 120, screen.WorkingArea.Y + 120), 440, 60);
            return (window, dots, (DotFieldControl)dots.Child!, Environment.CurrentManagedThreadId);
        });
        N.Until(() => native.OnUi(() => field.CompositionHandler?.LastFrame is not null), "the render server never drew the dot field", 5000);
        Thread.Sleep(600); // settle: the first layout, the first frames, the window's own first updates

        var (handler, probe, timersBefore, events) = native.OnUi(() =>
        {
            var events = new List<string>();
            dots.PropertyChanged += (_, e) => events.Add("ComposerWorkingDots." + e.Property.Name);
            field.PropertyChanged += (_, e) => events.Add("DotFieldControl." + e.Property.Name);
            foreach (var dot in ((Canvas)field.Layer!).Children) dot.PropertyChanged += (_, e) => events.Add("dot." + e.Property.Name);
            field.LayoutUpdated += (_, _) => events.Add("layout pass");
            var probe = new RendererUpdateProbe(window);
            clock.Reset();
            return (field.CompositionHandler!, probe, Timers(), events);
        });

        // Observe 2.5 s of the pulse from this (driver) thread only: no work is posted to the UI thread meanwhile.
        var drawn = new List<DotFieldCompositionFrame>();
        var pixels = new List<double>();
        var at = native.OnUi(() => Dot(field, 0, 3));
        native.OnUi(() => clock.Reset()); // the read of the dot's place above ran on the UI thread
        var until = DateTime.UtcNow.AddSeconds(2.5);
        while (DateTime.UtcNow < until)
        {
            if (handler.LastFrame is { } frame) drawn.Add(frame);
            pixels.Add(Peak(at));
            Thread.Sleep(50);
        }
        var reads = clock.Reads;

        var (updates, timersAfter, pending, observed, mine) = native.OnUi(() =>
        {
            var updates = probe.Updates;
            probe.Dispose();
            return (updates, Timers(), frames.Pending, events.ToArray(), new HashSet<Visual>(field.GetSelfAndVisualDescendants().Append(dots)));
        });

        // the render server sampled the field, on a thread that is not the UI thread, and the UI thread did not read its clock
        var render = reads.Where(read => read.Key != uiThread).Sum(read => read.Value);
        Assert.True(render >= 60, $"the render server sampled the field {render} times in 2.5 s; reads by thread: {string.Join(", ", reads.Select(read => $"{read.Key}={read.Value}"))}, UI thread {uiThread}");
        Assert.False(reads.ContainsKey(uiThread), $"the UI thread read the field's clock {reads.GetValueOrDefault(uiThread)} times while it played");
        // no UI-thread render update redrew the field or a dot
        Assert.DoesNotContain(updates, update => update.Any(mine.Contains));
        // no layout pass, no property change, no frame request, no new timer
        Assert.Empty(observed);
        Assert.Equal(0, pending);
        Assert.Equal(timersBefore.Count, timersAfter.Count);
        Assert.All(timersAfter, timer => Assert.Contains(timer, timersBefore));
        // ... while the frames the render server drew advanced through the pulse (0.08 to 0.75 over 2.2 s, ComposerWorkingDots.swift:177-189)
        var times = drawn.Select(frame => frame.Now).Distinct().Count();
        Assert.True(times >= 20, $"only {times} distinct frames drawn in 2.5 s");
        var opacities = drawn.Select(frame => frame.OpacityAt(0, 3)).ToArray();
        Assert.True(opacities.Max() - opacities.Min() > 0.4, $"the drawn opacity of a dot ranged {opacities.Min():F3} to {opacities.Max():F3}");
        // ... and so did the dot on the screen
        Assert.True(pixels.Max() - pixels.Min() > 40, $"the dot's screen luma ranged {pixels.Min():F1} to {pixels.Max():F1}");
        GC.KeepAlive(window);
    }, factType: typeof(ComposerWorkingDotsNativeTests));

    [Fact]
    [Trait("Req", "PR-COMPOSER-103")] // ComposerWorkingDots.swift:177-189 one clock origin for every field (CACurrentMediaTime-based beginTime)
    public void Two_fields_on_a_real_window_pulse_in_phase() => native.Run(subject =>
    {
        var (window, host, first) = native.OnUi(() =>
        {
            subject.Main.Hide();
            subject.Settings.Hide();
            var first = new DotFieldControl(new SwiftColor(1, 1, 1), animated: true, sweeps: false) { Width = 160, Height = FieldHeight };
            // side by side, 200 pt apart (a whole number of pixels at every scale Windows offers, and 25 pitches), so a dot of each lies on the same pixel phase
            var host = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20), Spacing = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Children = { first } };
            var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
            return (Show(host, new PixelPoint(screen.WorkingArea.X + 120, screen.WorkingArea.Y + 120), 400, 60), host, first);
        });
        N.Until(() => native.OnUi(() => first.CompositionHandler?.LastFrame is not null), "the render server never drew the first field", 5000);
        Thread.Sleep(700); // the second field is mounted 0.7 s later: a wave timed from each field's own mount would be 0.7 s apart
        var second = native.OnUi(() =>
        {
            var second = new DotFieldControl(new SwiftColor(1, 1, 1), animated: true, sweeps: false) { Width = 160, Height = FieldHeight };
            host.Children.Add(second);
            return second;
        });
        N.Until(() => native.OnUi(() => second.CompositionHandler?.LastFrame is not null), "the render server never drew the second field", 5000);
        Thread.Sleep(300);

        var (a, b) = native.OnUi(() => (first.CompositionHandler!, second.CompositionHandler!));
        // The same dots of both fields on the same screen grab (one composed frame, so one instant), over 2.4 s of the pulse.
        var columns = new[] { 0, 3, 6, 9 };
        var (spots, scale) = native.OnUi(() => (columns.Select(column => (Dot(first, 0, column), Dot(second, 0, column))).ToArray(), window.RenderScaling));
        Console.WriteLine($"ComposerWorkingDotsNativeTests: display scale {scale:0.00}");
        var origin = native.OnUi(() => window.PointToScreen(new Point(0, 0)));
        var size = native.OnUi(() => new PixelSize((int)Math.Ceiling(400 * scale), (int)Math.Ceiling(60 * scale)));
        var pairs = new List<(double A, double B)>();
        var drawnPairs = 0;
        var largestOffset = 0.0;
        var until = DateTime.UtcNow.AddSeconds(2.4);
        while (DateTime.UtcNow < until)
        {
            var grab = ScreenGrab.Of(origin.X, origin.Y, size.Width, size.Height);
            double PeakIn(PixelPoint p)
            {
                var peak = 0.0;
                for (var y = -2; y <= 2; y++)
                    for (var x = -2; x <= 2; x++) peak = Math.Max(peak, grab.Luma(p.X - origin.X + x, p.Y - origin.Y + y));
                return peak;
            }
            foreach (var (top, bottom) in spots) pairs.Add((PeakIn(top), PeakIn(bottom)));
            // the frames the render server drew for both in the same pass (sampled microseconds apart) show the same opacity
            if (a.LastFrame is { } fa && b.LastFrame is { } fb && Math.Abs((fa.Now - fb.Now).TotalMilliseconds) < 2)
            {
                drawnPairs++;
                foreach (var column in columns)
                {
                    Assert.True(Math.Abs(fa.OpacityAt(0, column) - fb.OpacityAt(0, column)) < 0.01, $"column {column}: {fa.OpacityAt(0, column):F3} and {fb.OpacityAt(0, column):F3}");
                    // what a wave timed from the second field's own mount would have drawn instead
                    largestOffset = Math.Max(largestOffset, Math.Abs(fa.OpacityAt(0, column) - (fb with { Now = fb.Now - TimeSpan.FromSeconds(0.7) }).OpacityAt(0, column)));
                }
            }
            Thread.Sleep(60);
        }
        Assert.True(drawnPairs >= 10, $"only {drawnPairs} samples caught both fields drawn in one pass");
        Assert.True(largestOffset > 0.2, $"a 0.7 s phase offset would have moved a dot by {largestOffset:F3} only: the check could not tell");
        // on the screen: the same dots of the two fields are equally bright in every grab, and the pulse moved them
        Assert.All(pairs, pair => Assert.True(Math.Abs(pair.A - pair.B) <= 6, $"dot luma {pair.A:F1} in the first field and {pair.B:F1} in the second"));
        Assert.True(pairs.Max(pair => pair.A) - pairs.Min(pair => pair.A) > 40, $"the dots' luma ranged {pairs.Min(pair => pair.A):F1} to {pairs.Max(pair => pair.A):F1} only");
    }, factType: typeof(ComposerWorkingDotsNativeTests));
}
