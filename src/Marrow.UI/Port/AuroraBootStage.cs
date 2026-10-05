using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

// OD-0113: B replaces Comet at the existing boot entry point. The UI drives the HWND/content clip;
// the expensive drawing runs in a composition custom visual, using the imported AuroraMark.
public sealed class AuroraBootStage : Control
{
    private AuroraBootSequence sequence;
    private readonly bool reduceMotion;
    private readonly Action<TimeSpan> tick;
    private IFrameSource? source;
    private TimeProvider? clock;
    private long started;
    private double? readyAt;
    private bool waiting, shown, closed;
    private CompositionCustomVisual? visual;

    public AuroraBootStage(Rect window, bool reduceMotion)
    {
        WindowFrame = window; this.reduceMotion = reduceMotion;
        sequence = new AuroraBootSequence(window, reduceMotion);
        Frame = sequence.At(0, null); tick = OnFrame;
        IsHitTestVisible = false; Focusable = false; AutomationProperties.SetName(this, "Loading");
        AttachedToVisualTree += (_, _) =>
        {
            clock = SwiftClock.Provider.Current; started = clock.GetTimestamp();
            source = FrameClock.Source.Current;
            if (ElementComposition.GetElementVisual(this) is { } host)
            {
                visual = host.Compositor.CreateCustomVisual(new AuroraBootHandler());
                ElementComposition.SetElementChildVisual(this, visual);
            }
            Send(); Request();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (waiting) source?.CancelFrame(tick);
            waiting = false; source = null;
            visual?.SendHandlerMessage(false); visual = null;
            ElementComposition.SetElementChildVisual(this, null);
        };
        ActualThemeVariantChanged += (_, _) => Send();
    }

    public Rect WindowFrame { get; private set; }
    public double Elapsed { get; private set; }
    public double? ReadyAt => readyAt;
    public AuroraBootFrame Frame { get; private set; }
    public AuroraLoaderTelemetry? Telemetry { get; set; }
    public event EventHandler? FrameChanged;
    public event EventHandler? MainWindowShown;
    public event EventHandler? Closed;
    public void Ready() { readyAt ??= clock?.GetElapsedTime(started).TotalSeconds ?? 0; Send(); }
    public void SetWindowFrame(Rect window)
    {
        WindowFrame = window; sequence = new AuroraBootSequence(window, reduceMotion);
        Frame = sequence.At(Elapsed, readyAt); Send();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) Send();
    }
    private void Send()
    {
        if (visual is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        visual.Size = new Vector(Bounds.Width, Bounds.Height);
        visual.SendHandlerMessage(new AuroraBootSnapshot(Bounds.Size, WindowFrame, Frame, Elapsed, PlainControls.IsDark(ActualThemeVariant), reduceMotion, Telemetry, clock ?? TimeProvider.System, started, readyAt));
    }
    private void Request()
    {
        if (waiting || closed || source is null) return;
        waiting = true; source.RequestFrame(tick);
    }
    private void OnFrame(TimeSpan _)
    {
        using var work = AuroraBootTrace.Begin("Boot: UI frame");
        waiting = false;
        if (closed || clock is null || source is null) return;
        Elapsed = clock.GetElapsedTime(started).TotalSeconds;
        Frame = sequence.At(Elapsed, readyAt);
        FrameChanged?.Invoke(this, EventArgs.Empty); Send();
        if (Frame.MainWindowShown && !shown) { shown = true; MainWindowShown?.Invoke(this, EventArgs.Empty); }
        if (Frame.StageClosed) { closed = true; Closed?.Invoke(this, EventArgs.Empty); }
        else Request();
    }
}
