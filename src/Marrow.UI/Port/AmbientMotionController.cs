using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

// Registration belongs to the compositor thread. Delayed wakes cross through the UI thread,
// where SendHandlerMessage is legal; no frame is registered just to check a deadline.
internal sealed class AmbientMotionController : IDisposable
{
    private readonly AmbientWindowState window;
    private readonly ITimer timer;
    private readonly Action pulse;
    private readonly TimeProvider time;
    private readonly TopLevel? inputRoot;
    private readonly object timerLock = new();
    private CompositionCustomVisual? visual;
    private Action? wake;
    private int pending, disposed;
    internal AmbientMotionClock Clock { get; } = new();
    internal bool Available => window.Available;
    internal double Now => time.GetTimestamp() / (double)time.TimestampFrequency;
    internal AmbientMotionController(AmbientWindowState window, Control? host = null)
    {
        this.window = window;
        time = SwiftClock.Provider.Current;
        if (host is not null && TopLevel.GetTopLevel(host) is { } root && root is not Window)
        {
            inputRoot = root;
            root.AddHandler(InputElement.PointerMovedEvent, PointerInput, RoutingStrategies.Tunnel, true);
            root.AddHandler(InputElement.PointerPressedEvent, PointerInput, RoutingStrategies.Tunnel, true);
            root.AddHandler(InputElement.PointerWheelChangedEvent, PointerInput, RoutingStrategies.Tunnel, true);
            root.AddHandler(InputElement.KeyDownEvent, KeyInput, RoutingStrategies.Tunnel, true);
        }
        pulse = Pulse;
        timer = time.CreateTimer(static state => ((AmbientMotionController)state!).TimerElapsed(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        window.Changed += Pulse;
    }
    internal void Bind(CompositionCustomVisual visual) => this.visual = visual;
    internal void Bind(Action wake) => this.wake = wake;
    private void PointerInput(object? sender, PointerEventArgs args) => window.Touch(Now);
    private void KeyInput(object? sender, KeyEventArgs args) => window.Touch(Now);
    internal void Sample(double now, bool interaction) => Clock.Sample(now, window.LastInput, window.Available, interaction);
    internal bool Request(double now, bool interaction)
    {
        Sample(now, interaction);
        var delay = Clock.RequestDelay(now, window.LastInput, window.Available, interaction);
        if (double.IsPositiveInfinity(delay) || Volatile.Read(ref disposed) != 0) return false;
        if (delay > 0)
        {
            lock (timerLock)
            {
                if (Volatile.Read(ref disposed) == 0 && Interlocked.CompareExchange(ref pending, 1, 0) == 0)
                    timer.Change(TimeSpan.FromSeconds(delay), Timeout.InfiniteTimeSpan);
            }
            return false;
        }
        Clock.Requested(now); return true;
    }
    private void TimerElapsed() { if (Volatile.Read(ref disposed) == 0) Dispatcher.UIThread.Post(pulse, DispatcherPriority.Render); }
    private void Pulse()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Interlocked.Exchange(ref pending, 0);
        visual?.SendHandlerMessage(this);
        wake?.Invoke();
    }
    public void Dispose()
    {
        lock (timerLock) { if (Interlocked.Exchange(ref disposed, 1) != 0) return; timer.Dispose(); }
        window.Changed -= Pulse;
        inputRoot?.RemoveHandler(InputElement.PointerMovedEvent, PointerInput);
        inputRoot?.RemoveHandler(InputElement.PointerPressedEvent, PointerInput);
        inputRoot?.RemoveHandler(InputElement.PointerWheelChangedEvent, PointerInput);
        inputRoot?.RemoveHandler(InputElement.KeyDownEvent, KeyInput);
        window.Dispose(); visual = null; wake = null;
    }
}
