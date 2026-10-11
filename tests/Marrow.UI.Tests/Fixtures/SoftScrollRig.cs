using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

// ED-246: a window with one ScrollViewer over tall content, on the manual frame clock and the manual Swift clock
// (SoftScrollTests). A frame moves both clocks together, as a real refresh does.
public sealed class SoftScrollRig : IDisposable
{
    public readonly ManualFrameSource Frames = new();
    public readonly ManualTimeProvider Time = new();
    private readonly IDisposable frameScope;
    private readonly IDisposable clockScope;
    public readonly Window Window;
    public readonly ScrollViewer Scroll;
    public readonly Button Button = new() { Content = "first", Height = 30 };
    private static int rigs;

    public SoftScrollRig(Action<ScrollViewer>? configure = null, Control? content = null)
    {
        frameScope = FrameClock.Source.Override(Frames);
        clockScope = SwiftClock.Provider.Override(Time);
        // Each rig's clock starts later than the last one's ended, as real time never runs back: the wheel's stream
        // classification (SoftScroll's, one for the process) sees a new stream, not the last test's.
        Time.Advance(TimeSpan.FromSeconds(1000 * Interlocked.Increment(ref rigs)));
        Scroll = new ScrollViewer
        {
            Content = content ?? new StackPanel { Children = { Button, new Border { Height = 2000, Width = 100 } } },
        };
        // ED-NEW-softscroll-1: a plain ScrollViewer, nothing set on it: soft scrolling is every scroll view's default.
        configure?.Invoke(Scroll);
        Window = new Window { Content = Scroll, Width = 300, Height = 300 };
        Window.Show();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    // One display frame: the clock and the frame source move together, as at a real refresh.
    public void Frame(double hz = 240)
    {
        var dt = TimeSpan.FromTicks((long)Math.Round(TimeSpan.TicksPerSecond / hz));
        Time.Advance(dt);
        Frames.Advance(dt);
        Dispatcher.UIThread.RunJobs();
    }

    public List<double> Run(double seconds, double hz = 240) => Trace(seconds, hz).Select(f => f.Y).ToList();

    // Frames for `seconds`: each frame's Swift-clock time and offset.
    public List<(double T, double Y)> Trace(double seconds, double hz = 240)
    {
        var offsets = new List<(double, double)>();
        for (var k = 0; k < Math.Round(seconds * hz); k++)
        {
            Frame(hz);
            offsets.Add((Seconds, Scroll.Offset.Y));
        }
        return offsets;
    }

    // Wheel events at their own times (seconds from now) between display frames at `hz`, the frames running on until
    // `until`: each frame's time (from now) and offset. Events land on the Swift clock exactly when they are given, as
    // a device's reports do, not on the frame grid.
    public List<(double T, double Y)> Play(IEnumerable<(double T, Vector Delta)> events, double hz, double until, Point? at = null)
    {
        var start = Seconds;
        var queue = new Queue<(double T, Vector Delta)>(events.OrderBy(e => e.T));
        var trace = new List<(double, double)>();
        var dt = TimeSpan.FromTicks((long)Math.Round(TimeSpan.TicksPerSecond / hz));
        for (var k = 1; k * dt.TotalSeconds <= until + 1e-9; k++)
        {
            var frameAt = start + k * dt.TotalSeconds;
            while (queue.Count > 0 && start + queue.Peek().T <= frameAt + 1e-12)
            {
                var (t, delta) = queue.Dequeue();
                AdvanceClockTo(start + t);
                Wheel(delta, at);
            }
            AdvanceClockTo(frameAt);
            Frames.Advance(dt);
            Dispatcher.UIThread.RunJobs();
            trace.Add((Seconds - start, Scroll.Offset.Y));
        }
        return trace;
    }

    private void AdvanceClockTo(double seconds)
    {
        var ticks = (long)Math.Round(seconds * TimeSpan.TicksPerSecond) - SwiftClock.Now.Ticks;
        if (ticks > 0) Time.Advance(TimeSpan.FromTicks(ticks));
    }

    public void Wheel(Vector delta, Point? at = null, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseWheel(at ?? new Point(250, 150), delta, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var physical = Enum.TryParse<PhysicalKey>(key.ToString(), out var parsed) ? parsed : PhysicalKey.None;
        Window.KeyPress(key, modifiers, physical, null);
        Window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    public double Seconds => SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond;

    public void Dispose()
    {
        Window.Close();
        Dispatcher.UIThread.RunJobs();
        clockScope.Dispose();
        frameScope.Dispose();
    }
}
