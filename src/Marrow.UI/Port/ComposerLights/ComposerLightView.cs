// PORT-ONLY, owner-ruled addition (OD-0083, OD-0085, OD-0089; ED-251): the light around a composer, drawn on the render
// thread. The view hands an immutable snapshot of what it should show (the settings, the composer's state, the theme,
// the composer's rectangle, reduce motion) to a composition visual; the visual's handler runs the light's animator on the
// compositor's frame clock and draws it with one shader per light, requesting frames only while something moves (a
// turning beam or aura, a fade). Under reduce motion it draws one still and asks for no frames. The same control serves
// every composer (the app's settings) and the Settings page's live preview (any settings value, saved or not).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Styling;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;

namespace Marrow.UI.Port.ComposerLights;

/// <summary>Everything the render thread needs for one composer's light, as one immutable value.</summary>
public sealed record ComposerLightSnapshot(
    ComposerLightSettings Settings, ComposerLightPhase Phase, bool Dark, Rect Composer, bool ReduceMotion,
    ComposerLightPalette Palette, double? FadeBottom, TimeProvider Clock)
{
    /// <summary>How far the light reaches outside the composer for these settings (px): the Uiverse halo, Pulse Outside's haloes.</summary>
    public double Reach
    {
        get
        {
            var a = Settings.Aura; var b = Settings.Beam;
            var aura = a.HaloSize + 3 * a.HaloBlur + 4;
            var dark = b.Theme == BeamTheme.Auto ? Dark : b.Theme == BeamTheme.Dark;
            var beam = b.Type == BeamType.PulseOutside ? 30 + 3 * Math.Max(b.BloomBlur ?? BorderBeamMath.GlowRadius(dark ? 22.5 : 15, b.GlowSize), b.CoreBlur ?? 6) + 4 : 2;
            return Math.Max(aura, beam);
        }
    }
}

/// <summary>The composition handler: the light's animator and renderer, on the render thread.</summary>
internal sealed class ComposerLightHandler : CompositionCustomVisualHandler
{
    private ComposerLightSnapshot? snapshot;
    private ComposerLightAnimator? light;
    private readonly ComposerLightRenderer renderer = new();
    private bool frameRequested;
    private int stopped;
    private double reach;
    private bool software;

    /// <summary>
    /// ENGINEERING DECISION (ED-251): with no GPU (Skia's software rasterizer), the light is drawn as one still, as under
    /// reduce motion: its shaders cost 15 to 60 ms a frame there, which would make typing stutter. Tests that need the
    /// motion on the headless (software) renderer turn this on.
    /// </summary>
    internal static volatile bool AnimateWithoutGpu;

    /// <summary>The animator (tests read it).</summary>
    public ComposerLightAnimator? Light => Volatile.Read(ref light);

    /// <summary>Frames drawn so far (tests).</summary>
    public int Frames => Volatile.Read(ref frames);
    private int frames;

    /// <summary>True while the handler asks the compositor for frames.</summary>
    public bool IsAnimating => Volatile.Read(ref animating) != 0;
    private int animating;

    public void Stop() => Interlocked.Exchange(ref stopped, 1);

    private static double Seconds(TimeProvider clock)
    {
        using var scope = SwiftClock.Provider.Override(clock);
        return SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond;
    }

    public override void OnMessage(object message)
    {
        if (Volatile.Read(ref stopped) != 0 || message is not ComposerLightSnapshot next) return;
        var now = Seconds(next.Clock);
        var size = next.Composer.Size;
        if (light is null)
            Volatile.Write(ref light, new ComposerLightAnimator(next.Settings, next.Phase, next.Dark, size.Width, size.Height, next.ReduceMotion, next.Palette));
        else
        {
            light.SetReduceMotion(next.ReduceMotion || software, now);
            light.SetSize(size.Width, size.Height, now);
            light.SetDark(next.Dark, now);
            light.SetPalette(next.Palette, now);
            light.SetPhase(next.Phase, now);
            light.SetSettings(next.Settings, now);
        }
        // the reach covers what the light drew before and what it draws now, through a fade
        reach = Math.Max(next.Reach, snapshot?.Reach ?? 0);
        snapshot = next;
        Step(now);
    }

    public override void OnAnimationFrameUpdate()
    {
        frameRequested = false;
        if (Volatile.Read(ref stopped) != 0 || snapshot is null) return;
        Step(Seconds(snapshot.Clock));
    }

    private void Step(double now)
    {
        if (light is null) return;
        var more = light.Advance(now);
        if (!more && snapshot is not null) reach = snapshot.Reach;
        Volatile.Write(ref animating, more ? 1 : 0);
        Invalidate();
        if (more && !frameRequested)
        {
            frameRequested = true;
            RegisterForNextAnimationFrameUpdate();
        }
    }

    public override Rect GetRenderBounds() => snapshot is { } s ? s.Composer.Inflate(reach) : default;

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (Volatile.Read(ref stopped) != 0 || snapshot is not { } s || light is null) return;
        if (!light.AuraVisible && light.DrawnCount == 0) return;
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease();
        if (lease.GrContext is null && !AnimateWithoutGpu && !software)
        {
            software = true;
            light.SetReduceMotion(true, Seconds(s.Clock));
        }
        var canvas = lease.SkCanvas;
        var m = canvas.TotalMatrix;
        var scale = (float)Math.Max(1, Math.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY));
        canvas.Save();
        canvas.Translate((float)s.Composer.X, (float)s.Composer.Y);
        renderer.Draw(canvas, lease.GrContext, light, scale, (float)lease.CurrentOpacity,
            s.FadeBottom is { } f ? (float)(f - s.Composer.Y) : null);
        canvas.Restore();
        Interlocked.Increment(ref frames);
    }
}

/// <summary>
/// The composer light (OD-0089). Place it over the composer's glass and under its content; give it the
/// <see cref="Settings"/> (the app's, or any value for a preview), the composer's <see cref="Phase"/> and, when the view
/// is larger than the composer, the composer's rectangle in the view (<see cref="ComposerRect"/>; empty: the view's own
/// bounds). It follows the theme and reduce motion by itself.
/// </summary>
public sealed class ComposerLightView : Control
{
    public static readonly StyledProperty<ComposerLightSettings> SettingsProperty =
        AvaloniaProperty.Register<ComposerLightView, ComposerLightSettings>(nameof(Settings), ComposerLightSettings.Default);
    public static readonly StyledProperty<ComposerLightPhase> PhaseProperty =
        AvaloniaProperty.Register<ComposerLightView, ComposerLightPhase>(nameof(Phase), ComposerLightPhase.Rest);
    public static readonly StyledProperty<ComposerLightPalette> PaletteProperty =
        AvaloniaProperty.Register<ComposerLightView, ComposerLightPalette>(nameof(Palette), ComposerLightPalette.Default);
    public static readonly StyledProperty<Rect> ComposerRectProperty =
        AvaloniaProperty.Register<ComposerLightView, Rect>(nameof(ComposerRect));
    public static readonly StyledProperty<double?> FadeBottomProperty =
        AvaloniaProperty.Register<ComposerLightView, double?>(nameof(FadeBottom));

    private CompositionCustomVisual? visual;
    private ComposerLightHandler? handler;
    private ComposerLightSnapshot? sent;

    public ComposerLightView()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        Focusable = false;
        AttachedToVisualTree += (_, _) => Install();
        DetachedFromVisualTree += (_, _) => Remove();
        ActualThemeVariantChanged += (_, _) => Send();
    }

    /// <summary>The light's settings: the app's own, or a preview's value.</summary>
    public ComposerLightSettings Settings { get => GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }

    /// <summary>What the composer is doing: at rest, typing, running a turn.</summary>
    public ComposerLightPhase Phase { get => GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }

    /// <summary>The colours the Uiverse light's Maker and Accent schemes take.</summary>
    public ComposerLightPalette Palette { get => GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }

    /// <summary>The composer's rectangle in this view; empty for the view's own bounds.</summary>
    public Rect ComposerRect { get => GetValue(ComposerRectProperty); set => SetValue(ComposerRectProperty, value); }

    /// <summary>Where (view px) the light outside the composer has faded out (the window's bottom edge); null for none.</summary>
    public double? FadeBottom { get => GetValue(FadeBottomProperty); set => SetValue(FadeBottomProperty, value); }

    internal ComposerLightHandler? Handler => handler;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SettingsProperty || change.Property == PhaseProperty || change.Property == PaletteProperty
            || change.Property == ComposerRectProperty || change.Property == FadeBottomProperty
            || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty || change.Property == BoundsProperty)
            Send();
    }

    private void Install()
    {
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        handler = new ComposerLightHandler();
        visual = host.Compositor.CreateCustomVisual(handler);
        visual.ClipToBounds = false;
        ElementComposition.SetElementChildVisual(this, visual);
        sent = null;
        Send();
    }

    private void Remove()
    {
        handler?.Stop();
        ElementComposition.SetElementChildVisual(this, null);
        visual = null;
        handler = null;
        sent = null;
    }

    private void Send()
    {
        if (visual is null) return;
        var composer = ComposerRect.Width > 0 && ComposerRect.Height > 0 ? ComposerRect : new Rect(Bounds.Size);
        var snapshot = new ComposerLightSnapshot(Settings, Phase, ActualThemeVariant == ThemeVariant.Dark, composer,
            EnvironmentValues.GetAccessibilityReduceMotion(this), Palette, FadeBottom, SwiftClock.Provider.Current);
        if (snapshot == sent) return;
        sent = snapshot;
        visual.Size = new Vector(Bounds.Width, Bounds.Height);
        visual.SendHandlerMessage(snapshot);
    }
}
