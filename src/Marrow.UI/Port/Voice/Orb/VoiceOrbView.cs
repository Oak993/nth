// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-8, voice-SPEC V-11): the voice orb as a control (V-11.3), after ComposerLightView.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;

namespace Marrow.UI.Port.Voice.Orb;

/// <summary>
/// The voice orb: a composition custom visual <see cref="OrbScene.ViewPx"/> (72) view pixels square, centred on its slot, drawing the ball at the slot diameter. The
/// host hands it what the orb is doing (<see cref="Mode"/>, <see cref="Hovered"/>, <see cref="Pressed"/>, <see cref="Bands"/>, <see cref="SlotDiameter"/>, the chosen
/// <see cref="Kind"/>); the motion, the swap crossfade and the frames run on the render thread. It follows the theme and reduce motion by itself.
/// </summary>
public sealed class VoiceOrbView : Control
{
    public static readonly StyledProperty<VoiceOrbKind> KindProperty =
        AvaloniaProperty.Register<VoiceOrbView, VoiceOrbKind>(nameof(Kind), VoiceOrbKind.Siri);
    public static readonly StyledProperty<OrbMotionMode> ModeProperty =
        AvaloniaProperty.Register<VoiceOrbView, OrbMotionMode>(nameof(Mode), OrbMotionMode.Rest);
    public static readonly StyledProperty<bool> HoveredProperty = AvaloniaProperty.Register<VoiceOrbView, bool>(nameof(Hovered));
    public static readonly StyledProperty<bool> PressedProperty = AvaloniaProperty.Register<VoiceOrbView, bool>(nameof(Pressed));
    public static readonly StyledProperty<OrbBands> BandsProperty = AvaloniaProperty.Register<VoiceOrbView, OrbBands>(nameof(Bands));
    public static readonly StyledProperty<double> SlotDiameterProperty = AvaloniaProperty.Register<VoiceOrbView, double>(nameof(SlotDiameter), 32);

    private CompositionCustomVisual? visual;
    private VoiceOrbHandler? handler;
    private VoiceOrbSnapshot? sent;

    public VoiceOrbView()
    {
        Width = OrbScene.ViewPx;
        Height = OrbScene.ViewPx;
        IsHitTestVisible = false;
        ClipToBounds = false;
        Focusable = false;
        AttachedToVisualTree += (_, _) => Install();
        DetachedFromVisualTree += (_, _) => Remove();
        ActualThemeVariantChanged += (_, _) => Send();
    }

    /// <summary>The chosen orb (<c>voiceOrb.orb</c>); a change crossfades (V-11.5).</summary>
    public VoiceOrbKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public OrbMotionMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }

    public bool Hovered { get => GetValue(HoveredProperty); set => SetValue(HoveredProperty, value); }

    public bool Pressed { get => GetValue(PressedProperty); set => SetValue(PressedProperty, value); }

    /// <summary>The smoothed bands (V-5.5); the orb takes them while Listening only.</summary>
    public OrbBands Bands { get => GetValue(BandsProperty); set => SetValue(BandsProperty, value); }

    /// <summary>The slot diameter the ball springs to (V-11.4).</summary>
    public double SlotDiameter { get => GetValue(SlotDiameterProperty); set => SetValue(SlotDiameterProperty, value); }

    internal VoiceOrbHandler? Handler => handler;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty || change.Property == ModeProperty || change.Property == HoveredProperty || change.Property == PressedProperty
            || change.Property == BandsProperty || change.Property == SlotDiameterProperty
            || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty)
            Send();
    }

    private void Install()
    {
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        handler = new VoiceOrbHandler();
        visual = host.Compositor.CreateCustomVisual(handler);
        visual.ClipToBounds = false;
        ElementComposition.SetElementChildVisual(this, visual);
        sent = null;
        Send();
    }

    private void Remove()
    {
        // the handler releases its surface on the render thread, where it was made
        visual?.SendHandlerMessage(VoiceOrbStop.Instance);
        ElementComposition.SetElementChildVisual(this, null);
        visual = null;
        handler = null;
        sent = null;
    }

    private void Send()
    {
        if (visual is null) return;
        var snapshot = new VoiceOrbSnapshot(
            new OrbSceneInput(Kind, Mode, Hovered, Pressed, Bands, SlotDiameter, ActualThemeVariant == ThemeVariant.Dark, EnvironmentValues.GetAccessibilityReduceMotion(this)),
            SwiftClock.Provider.Current);
        if (snapshot == sent) return;
        sent = snapshot;
        visual.Size = new Vector(OrbScene.ViewPx, OrbScene.ViewPx);
        visual.SendHandlerMessage(snapshot);
    }
}
