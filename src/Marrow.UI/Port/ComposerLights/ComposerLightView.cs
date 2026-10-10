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
using Marrow.Core.Port.Voice.Light;

namespace Marrow.UI.Port.ComposerLights;

/// <summary>Everything the render thread needs for one composer's light, as one immutable value.</summary>
public sealed record ComposerLightSnapshot(
    ComposerLightSettings Settings, ComposerLightPhase Phase, bool Dark, Rect Composer, bool ReduceMotion,
    ComposerLightPalette Palette, double? FadeBottom, TimeProvider Clock, double VoiceLight = 1);

/// <summary>The view left the tree: the handler stops and releases its bakes on the render thread.</summary>
internal sealed class ComposerLightStop
{
    public static readonly ComposerLightStop Instance = new();
    private ComposerLightStop() { }
}

/// <summary>The composition handler: the light's animator and renderer, on the render thread.</summary>
internal sealed class ComposerLightHandler(AmbientMotionController? ambient = null) : CompositionCustomVisualHandler
{
    private double interactionUntil;
    private bool Interaction(double now) => now < interactionUntil || voice.Moving;
    private ComposerLightSnapshot? snapshot;
    private ComposerLightAnimator? light;
    private readonly ComposerLightRenderer renderer = new();
    private bool frameRequested;
    private bool drawnSinceRequest = true;
    private bool wantsFrames;
    private int stopped;
    private double reach = 2, drawnReach = 2;
    private Rect invalidatedBounds;
    private void Dirty()
    {
        var bounds = GetRenderBounds();
        // A changed content bound must refresh Avalonia's cached bounds before partial damage is usable.
        if (bounds != invalidatedBounds) { invalidatedBounds = bounds; Invalidate(); }
        else Invalidate(bounds);
    }
    private bool software;
    private readonly VoiceLightFollower voice = new();
    private double? lastVoiceNow;

    /// <summary>
    /// ENGINEERING DECISION (ED-251): with no GPU (Skia's software rasterizer), the light is drawn as one still, as under
    /// reduce motion: its shaders cost 15 to 60 ms a frame there, which would make typing stutter. Re-checked on every
    /// frame drawn (a device change redraws), so a GPU that comes back brings the motion back. Tests that need the motion
    /// on the headless (software) renderer turn this on.
    /// </summary>
    internal static volatile bool AnimateWithoutGpu;

    /// <summary>The animator (tests read it).</summary>
    public ComposerLightAnimator? Light => Volatile.Read(ref light);

    /// <summary>Frames drawn so far (tests).</summary>
    public int Frames => Volatile.Read(ref frames);
    private int frames;

    /// <summary>True while the handler has asked the compositor for a frame.</summary>
    public bool IsAnimating => Volatile.Read(ref animating) != 0;
    private int animating;

    /// <summary>The light's level while you speak as the render thread has it (1: the light as it is; V-13.4).</summary>
    public double VoiceLight => voice.Value;

    /// <summary>True once the handler has stopped and released its renderer.</summary>
    public bool IsStopped => Volatile.Read(ref stopped) != 0;
    internal double AmbientSpeed => ambient?.Clock.Speed ?? 1;

    public void Stop() => Interlocked.Exchange(ref stopped, 1);

    // SwiftClock's reading of a provider, without the ambient override's allocation (this runs every frame)
    private static double Seconds(TimeProvider clock) => clock.GetTimestamp() / (double)clock.TimestampFrequency;

    public override void OnMessage(object message)
    {
        if (ReferenceEquals(message, ambient))
        {
            if (snapshot is { } current) { var pulseNow = Seconds(current.Clock); ambient?.Sample(pulseNow, Interaction(pulseNow)); }
            Request(); return;
        }
        if (message is ComposerLightStop)
        {
            Stop();
            renderer.Dispose();
            Volatile.Write(ref animating, 0);
            return;
        }
        if (IsStopped || message is not ComposerLightSnapshot next) return;
        var now = Seconds(next.Clock);
        if (snapshot is not null) interactionUntil = now + .9;
        var size = next.Composer.Size;
        if (light is null) voice.Jump(next.VoiceLight);                  // a first frame never starts from full: the level is the host's
        if (light is null)
            Volatile.Write(ref light, new ComposerLightAnimator(next.Settings, next.Phase, next.Dark, size.Width, size.Height, next.ReduceMotion || software, next.Palette));
        else
        {
            light.SetReduceMotion(next.ReduceMotion || software, now);
            light.SetSize(size.Width, size.Height, now);
            light.SetDark(next.Dark, now);
            light.SetPalette(next.Palette, now);
            light.SetPhase(next.Phase, now);
            light.SetSettings(next.Settings, now);
        }
        snapshot = next;
        Step(now);
    }

    public override void OnAnimationFrameUpdate()
    {
        using var work = AuroraBootTrace.Begin("Composer light: compositor frame");
        frameRequested = false;
        Volatile.Write(ref animating, 0);
        if (IsStopped || snapshot is null) return;
        ambient?.Sample(Seconds(snapshot.Clock), Interaction(Seconds(snapshot.Clock)));
        if (ambient is not null && !ambient.Available) return;
        // ED-NEW-frame-pacing: the light moves at the 60 Hz slots of the refreshes, with every other ambient source
        if (ambient is not null && ambient.Pace(CompositionNow.Ticks, Seconds(snapshot.Clock), Interaction(Seconds(snapshot.Clock))) == AmbientMotionController.Paced.Wait)
        {
            frameRequested = true;
            Volatile.Write(ref animating, 1);
            RegisterForNextAnimationFrameUpdate();
            return;
        }
        Step(Seconds(snapshot.Clock));
    }

    private void Step(double now)
    {
        if (light is null || snapshot is null) return;
        ambient?.Sample(now, Interaction(now));
        // V-13.4: vis = follow(vis, target, dt, 0.2, 0.17) on the frame clock; reduce motion jumps
        // the follower moves by the frame it is in: a light that stood still (or a level that did not move) has no frame to measure from, so its first step after a change is 0 s
        var voiceDt = voice.Moving && lastVoiceNow is { } previous ? Math.Max(0, now - previous) : 0;
        lastVoiceNow = now;
        voice.Step(snapshot.VoiceLight, voiceDt, snapshot.ReduceMotion || software);   // with no GPU the light is a still (ED-251): its level jumps as well
        if (voice.Value <= 0)
        {
            // at 0 the light draws nothing, asks for no frames (unless the level is on its way back) and its motion clock stands still (nothing is advanced; the clock caps a step at 50 ms, so it goes on from where it stopped)
            wantsFrames = voice.Moving;
            Dirty();
            if (wantsFrames && drawnSinceRequest) Request();
            return;
        }
        wantsFrames = light.Advance(now, ambient?.Clock.Speed ?? 1) || renderer.Settling || voice.Moving;
        reach = Reach(light);
        Dirty();
        // the board's port notes: frames only while something moves, and only while it is seen. A frame is asked for
        // again only once the last one was drawn; a light nobody draws (a hidden chat column) stops, and OnRender
        // resumes it when it is drawn again.
        if (wantsFrames && drawnSinceRequest) Request();
    }

    private void Request()
    {
        if (frameRequested || IsStopped || snapshot is null || !wantsFrames) return;
        var now = Seconds(snapshot.Clock);
        if (ambient is not null && !ambient.Request(now, Interaction(now)))
        {
            Volatile.Write(ref animating, ambient.Available && ambient.Clock.Moving ? 1 : 0); return;
        }
        using var request = AuroraBootTrace.Begin("Composer light: frame request");
        frameRequested = true;
        drawnSinceRequest = false;
        Volatile.Write(ref animating, 1);
        RegisterForNextAnimationFrameUpdate();
    }

    // how far this frame's light reaches outside the composer: the Uiverse halo while it shows, Pulse Outside's haloes
    private static double Reach(ComposerLightAnimator light)
    {
        var r = 2.0;
        if (light.AuraVisible) r = Math.Max(r, light.Aura.HaloSize + 3 * light.Aura.HaloBlur + 4);
        for (var i = 0; i < light.DrawnCount; i++)
        {
            var f = light.Slots[light.Drawn[i]];
            if (f.Mode == 3) r = Math.Max(r, 30 + 3 * Math.Max(f.OuterBloomSigma, f.CoreSigma) + 4);
        }
        return r;
    }

    // this frame's reach and the last drawn one, so a shrinking light clears what it drew
    public override Rect GetRenderBounds() => snapshot is { } s && s.Composer.Width > 0 && s.Composer.Height > 0 ? s.Composer.Inflate(Math.Max(reach, drawnReach)) : default;

    public override void OnRender(ImmediateDrawingContext context)
    {
        using var work = AuroraBootTrace.Begin("Composer light: compositor draw");
        if (IsStopped || snapshot is not { } s || light is null) return;
        drawnSinceRequest = true;
        drawnReach = reach;
        if (voice.Value > 0 && (light.AuraVisible || light.DrawnCount > 0))
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var now = Seconds(s.Clock);
            var noGpu = lease.GrContext is null && !AnimateWithoutGpu;
            if (noGpu != software)
            {
                software = noGpu;
                light.SetReduceMotion(s.ReduceMotion || software, now);
            }
            var canvas = lease.SkCanvas;
            var m = canvas.TotalMatrix;
            var scale = (float)Math.Max(1, Math.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY));
            canvas.Save();
            canvas.Translate((float)s.Composer.X, (float)s.Composer.Y);
            renderer.Draw(canvas, lease.GrContext, light, scale, now, (float)(lease.CurrentOpacity * voice.Value),
                s.FadeBottom is { } f ? (float)(f - s.Composer.Y) : null);
            canvas.Restore();
            Interlocked.Increment(ref frames);
            if (renderer.Settling) wantsFrames = true;
        }
        if (wantsFrames) Request();
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
    public static readonly StyledProperty<double> VoiceLightProperty =
        AvaloniaProperty.Register<ComposerLightView, double>(nameof(VoiceLight), 1, coerce: static (_, value) => double.IsNaN(value) ? 1 : Math.Clamp(value, 0, 1));

    private CompositionCustomVisual? visual;
    private ComposerLightHandler? handler;
    private ComposerLightSnapshot? sent;
    private AmbientMotionController? ambient;

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

    /// <summary>
    /// The light's level while you speak, 0 to 1 (V-13.4): 1 is the light as it is (the default); the host sets <c>VoiceLightTarget.For(setting, state)</c> (0, 0.35 or 1) and
    /// the light follows it on the render thread (0.2 s up, 0.17 s down), its whole output multiplied by the level; at 0 it draws nothing, asks for no frames and its motion
    /// clock stands still until the level returns. Under reduce motion the level jumps.
    /// </summary>
    public double VoiceLight { get => GetValue(VoiceLightProperty); set => SetValue(VoiceLightProperty, value); }

    internal ComposerLightHandler? Handler => handler;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SettingsProperty || change.Property == PhaseProperty || change.Property == PaletteProperty
            || change.Property == ComposerRectProperty || change.Property == FadeBottomProperty || change.Property == VoiceLightProperty
            || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty || change.Property == BoundsProperty)
            Send();
    }

    private void Install()
    {
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        var availability = AmbientWindowState.Acquire(this);
        ambient = availability is null ? null : new(availability);
        handler = new ComposerLightHandler(ambient);
        visual = host.Compositor.CreateCustomVisual(handler);
        ambient?.Bind(visual);
        visual.ClipToBounds = false;
        ElementComposition.SetElementChildVisual(this, visual);
        sent = null;
        Send();
    }

    private void Remove()
    {
        // the handler releases its bakes and shaders on the render thread, where they were made
        visual?.SendHandlerMessage(ComposerLightStop.Instance);
        ambient?.Dispose(); ambient = null;
        ElementComposition.SetElementChildVisual(this, null);
        visual = null;
        handler = null;
        sent = null;
    }

    private void Send()
    {
        if (visual is null) return;
        var composer = ComposerRect.Width > 0 && ComposerRect.Height > 0 ? ComposerRect : new Rect(Bounds.Size);
        // not laid out yet: nothing to light (a 0 x 0 composer is never sent)
        if (!(composer.Width > 0) || !(composer.Height > 0)) return;
        var snapshot = new ComposerLightSnapshot(Settings, Phase, ActualThemeVariant == ThemeVariant.Dark, composer,
            EnvironmentValues.GetAccessibilityReduceMotion(this), Palette, FadeBottom, SwiftClock.Provider.Current, VoiceLight);
        if (snapshot == sent) return;
        sent = snapshot;
        visual.Size = new Vector(Bounds.Width, Bounds.Height);
        visual.SendHandlerMessage(snapshot);
    }
}
