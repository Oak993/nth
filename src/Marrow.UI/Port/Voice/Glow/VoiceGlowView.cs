// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13): the voice glow as a control, after VoiceOrbView.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Marrow.Core.Port;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>
/// The voice glow (voice-glow 0.2.1 through the design's voice-glow.js): a soft light that rises from the bottom of the composer with the voice and sweeps side to side while the
/// words are finished. Place it over the composer's glass and light and under its content, as large as the composer's rounded shape; it draws inside that shape (corner
/// <see cref="Radius"/>, the composer's 22), follows the theme and reduce motion by itself, and asks for frames only while it is <see cref="Active"/> or fading.
/// <see cref="Active"/> is Listening or Processing, <see cref="Processing"/> is Processing, and the raw level the session reads goes to <see cref="Level"/>.
/// </summary>
public sealed class VoiceGlowView : Control
{
    public static readonly StyledProperty<bool> ActiveProperty = AvaloniaProperty.Register<VoiceGlowView, bool>(nameof(Active));
    public static readonly StyledProperty<bool> ProcessingProperty = AvaloniaProperty.Register<VoiceGlowView, bool>(nameof(Processing));
    public static readonly StyledProperty<double> RadiusProperty = AvaloniaProperty.Register<VoiceGlowView, double>(nameof(Radius), VoiceGlowInput.ComposerRadius);

    private CompositionCustomVisual? visual;
    private VoiceGlowHandler? handler;
    private VoiceGlowSnapshot? sent;
    private ManualFrameSource? manualFrames;
    private double? manualOrigin;
    private TimeProvider? manualClock;

    public VoiceGlowView()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        Focusable = false;
        AttachedToVisualTree += (_, _) => Install();
        DetachedFromVisualTree += (_, _) => Remove();
        ActualThemeVariantChanged += (_, _) => Send();
    }

    /// <summary>The glow is shown: Listening or Processing (V-13.3). A change fades it in (0.6 s) or out (0.5 s).</summary>
    public bool Active { get => GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    /// <summary>The sweep: Processing (V-13.3). The glow sinks away and a tighter blob sweeps side to side along the bottom edge.</summary>
    public bool Processing { get => GetValue(ProcessingProperty); set => SetValue(ProcessingProperty, value); }

    /// <summary>The corner of the shape the glow is drawn in (the composer's 22).</summary>
    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    /// <summary>
    /// The raw level <c>all</c> (V-5.3, unsmoothed, 0 to 1): the host writes it as it arrives, from any thread; the render thread reads the latest each frame and the driver smooths
    /// it. Writing it sends no message.
    /// </summary>
    public VoiceGlowLevel Level { get; } = new();

    internal VoiceGlowHandler? Handler => handler;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActiveProperty || change.Property == ProcessingProperty || change.Property == RadiusProperty || change.Property == BoundsProperty
            || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty)
            Send();
    }

    private void Install()
    {
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        manualFrames = FrameClock.Source.Current as ManualFrameSource;
        var clock = SwiftClock.Provider.Current;
        manualClock = manualFrames is null ? null : clock;
        manualOrigin = manualClock is null ? null : clock.GetTimestamp() / (double)clock.TimestampFrequency;
        if (manualFrames is not null) manualFrames.FrameCompleted += OnManualFrame;
        handler = new VoiceGlowHandler();
        visual = host.Compositor.CreateCustomVisual(handler);
        visual.ClipToBounds = false;
        ElementComposition.SetElementChildVisual(this, visual);
        sent = null;
        Send();
    }

    private void Remove()
    {
        if (manualFrames is not null) manualFrames.FrameCompleted -= OnManualFrame;
        manualFrames = null;
        manualOrigin = null;
        manualClock = null;
        // the handler releases its surface on the render thread, where it was made
        visual?.SendHandlerMessage(VoiceGlowStop.Instance);
        ElementComposition.SetElementChildVisual(this, null);
        visual = null;
        handler = null;
        sent = null;
    }

    // Freeze the level after the frame's voice callbacks, rather than reading a later mutable audio sample at paint time.
    private void OnManualFrame()
    {
        if (visual is null || manualClock is not { } clock) return;
        var input = new VoiceGlowInput(Active, Processing, new VoiceGlowLevel { Value = Level.Value },
            ActualThemeVariant == ThemeVariant.Dark, EnvironmentValues.GetAccessibilityReduceMotion(this),
            Bounds.Width, Bounds.Height, Radius);
        visual.SendHandlerMessage(new VoiceGlowTick(input, clock.GetTimestamp() / (double)clock.TimestampFrequency));
    }

    private void Send()
    {
        if (visual is null) return;
        var snapshot = new VoiceGlowSnapshot(
            new VoiceGlowInput(Active, Processing, manualFrames is null ? Level : new VoiceGlowLevel { Value = Level.Value }, ActualThemeVariant == ThemeVariant.Dark, EnvironmentValues.GetAccessibilityReduceMotion(this), Bounds.Width, Bounds.Height, Radius),
            manualClock ?? SwiftClock.Provider.Current, manualOrigin);
        if (snapshot == sent) return;
        sent = snapshot;
        visual.Size = new Vector(Bounds.Width, Bounds.Height);
        visual.SendHandlerMessage(snapshot);
    }
}
