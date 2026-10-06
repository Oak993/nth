using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

// PORT-ONLY: OD-0113 replaces the icon at the three ED-097 loading sites. No per-turn animation is added.
public sealed class AuroraLoader : Control
{
    private static readonly HashSet<AuroraLoader> mounted = [];
    private readonly AuroraLoaderKind kind;
    private CompositionCustomVisual? visual;
    private TimeProvider? clock;
    private long started;
    private long? finished;
    private AuroraLoaderSnapshot? sent;

    public AuroraLoader(AuroraLoaderKind kind)
    {
        this.kind = kind;
        Height = kind == AuroraLoaderKind.CurtainFold ? 48 : kind == AuroraLoaderKind.FlowArc ? 40 : 88;
        if (kind != AuroraLoaderKind.AuroraSheen) Width = Height;
        IsHitTestVisible = false; Focusable = false;
        AutomationProperties.SetName(this, "Loading");
        AttachedToVisualTree += (_, _) => Install();
        DetachedFromVisualTree += (_, _) => Remove();
        ActualThemeVariantChanged += (_, _) => Send();
    }

    public AuroraLoaderKind Kind => kind;
    // Queried on the UI thread by the application's idle GPU warm-up guard.
    public static bool AnyAnimating()
    {
        foreach (var loader in mounted)
            if (loader.IsEffectivelyVisible && loader.clock is { } time &&
                (loader.finished is not { } end || time.GetElapsedTime(end).TotalSeconds < .15)) return true;
        return false;
    }
    public AuroraLoaderTelemetry? Telemetry { get; init; }
    public double CurrentOpacity => clock is null ? 0 : AuroraLoaderMotion.Visibility(clock.GetElapsedTime(started).TotalSeconds);
    public void BeginWait()
    {
        clock ??= SwiftClock.Provider.Current;
        started = clock.GetTimestamp(); finished = null; Send();
    }
    public void Complete() { if (clock is not null && finished is null) { finished = clock.GetTimestamp(); Send(); } }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) Send();
    }

    private void Install()
    {
        mounted.Add(this);
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        if (clock is null) BeginWait();
        visual = host.Compositor.CreateCustomVisual(new AuroraLoaderHandler());
        visual.ClipToBounds = true;
        ElementComposition.SetElementChildVisual(this, visual);
        Send();
    }

    private void Send()
    {
        if (visual is null || clock is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var snapshot = new AuroraLoaderSnapshot(kind, Bounds.Size, PlainControls.IsDark(ActualThemeVariant),
            EnvironmentValues.GetAccessibilityReduceMotion(this), clock, started, finished, Telemetry);
        if (snapshot == sent) return;
        sent = snapshot; visual.Size = new Vector(Bounds.Width, Bounds.Height); visual.SendHandlerMessage(snapshot);
    }

    private void Remove()
    {
        mounted.Remove(this);
        visual?.SendHandlerMessage(false);
        ElementComposition.SetElementChildVisual(this, null);
        visual = null; sent = null; // The same wait survives a temporary detach while its host rebuilds chrome.
    }
}
