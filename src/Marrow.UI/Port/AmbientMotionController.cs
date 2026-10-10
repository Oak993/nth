using Avalonia.Rendering.Composition;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

// Registration belongs to the compositor thread. Wakes (input, availability) cross through the UI thread, where
// SendHandlerMessage is legal. ED-NEW-frame-pacing: no timer. A moving source asks for every refresh and Pace decides at
// each whether it is one of the 60 Hz slots (AmbientRefreshPacer); a stopped source (unseen, or idle past the ease) asks
// for nothing until a wake.
internal sealed class AmbientMotionController : IDisposable
{
    private readonly AmbientWindowState window;
    private readonly TimeProvider time;
    private readonly TopLevel? inputRoot;
    private CompositionCustomVisual? visual;
    private Action? wake;
    private AmbientRefreshPacer pacer = new();
    private int disposed;
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
        window.Changed += Pulse;
    }
    /// <summary>A render-thread source: its slots are those of every other ambient source of the same compositor.</summary>
    internal void Bind(CompositionCustomVisual visual) { this.visual = visual; pacer = AmbientRefreshPacer.For(visual.Compositor); }
    /// <summary>A main-thread source (frames from FrameClock.Source): its own pacer, its frame's time as the refresh's identity.</summary>
    internal void Bind(Action wake) => this.wake = wake;
    /// <summary>Tests: the pacer this source asks.</summary>
    internal AmbientRefreshPacer Pacer { get => pacer; set => pacer = value; }
    private void PointerInput(object? sender, PointerEventArgs args) => window.Touch(Now);
    private void KeyInput(object? sender, KeyEventArgs args) => window.Touch(Now);
    internal void Sample(double now, bool interaction) => Clock.Sample(now, window.LastInput, window.Available, interaction);

    /// <summary>
    /// Whether a frame may be asked for now: false once the motion has stopped (unavailable, or idle past the ease), when
    /// only a wake (input, availability) brings it back. Which of the refreshes asked for advance it is <see cref="Pace"/>'s.
    /// </summary>
    internal bool Request(double now, bool interaction)
    {
        Sample(now, interaction);
        return !Stopped(now, interaction);
    }

    private bool Stopped(double now, bool interaction) =>
        Volatile.Read(ref disposed) != 0 || double.IsPositiveInfinity(Clock.RequestDelay(now, window.LastInput, window.Available, interaction));

    internal enum Paced { Stop, Wait, Go }

    /// <summary>
    /// At a refresh tick (<paramref name="tick"/>: its identity, the same for every source in it): Go advances the motion
    /// now; Wait asks for the next refresh and draws nothing (this refresh belongs to a 60 Hz slot that already had its
    /// update); Stop asks for nothing. An interaction is not paced (it runs at the display's rate), but its refreshes are counted.
    /// </summary>
    internal Paced Pace(long tick, double now, bool interaction)
    {
        Sample(now, interaction);
        if (Stopped(now, interaction)) return Paced.Stop;
        if (!pacer.Beat(tick, now) && !interaction) return Paced.Wait;
        Clock.Requested(now);
        return Paced.Go;
    }

    private void Pulse()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        visual?.SendHandlerMessage(this);
        wake?.Invoke();
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        window.Changed -= Pulse;
        inputRoot?.RemoveHandler(InputElement.PointerMovedEvent, PointerInput);
        inputRoot?.RemoveHandler(InputElement.PointerPressedEvent, PointerInput);
        inputRoot?.RemoveHandler(InputElement.PointerWheelChangedEvent, PointerInput);
        inputRoot?.RemoveHandler(InputElement.KeyDownEvent, KeyInput);
        window.Dispose(); visual = null; wake = null;
    }
}
