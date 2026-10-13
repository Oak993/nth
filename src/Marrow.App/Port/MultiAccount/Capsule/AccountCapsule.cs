// PORT-ONLY: multi-account step 3a (OD-0115 composer=A). THE capsule: one control for the plain account capsule and the twelve pill styles, used by the composer and by the Customization previews alike.
// It is a view over what the foundations give (CapsuleContent): it shows, it eases the usage light over 0.6 s, it loops where a style moves, and it raises Clicked. It decides nothing (the popover is the next unit's).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Capsule;

internal sealed class AccountCapsule : Panel
{
    private static readonly Animation LightCurve = Animation.TimingCurve(.25, .1, .25, 1, duration: .6); // CSS ease, 0.6 s: --uc, --uc2
    private static readonly Animation GaugeCurve = Animation.TimingCurve(.32, .72, 0, 1, duration: .9); // the page's --ez, 0.9 s: --u
    private static readonly Animation ArcCurve = Animation.TimingCurve(.32, .72, 0, 1, duration: .7);
    private static readonly Animation GlowCurve = Animation.TimingCurve(.25, .1, .25, 1, duration: .4);
    private static readonly Animation PopCurve = Animation.TimingCurve(.32, .72, 0, 1, duration: .45);
    private static readonly Animation PressCurve = Animation.TimingCurve(.25, .1, .25, 1, duration: .15);

    private readonly string style;
    private readonly bool plain;
    private readonly CapsulePaintState state;
    private readonly CapsuleFace face;
    private readonly Border? glow;
    private readonly AnimatedScalar colour;
    private readonly AnimatedScalar fill;
    private readonly AnimatedScalar arc;
    private readonly AnimatedScalar pop;
    private readonly AnimatedScalar glowBlur;
    private readonly AnimatedScalar glowOpacity;
    private readonly AnimatedScalar press;
    private (byte R, byte G, byte B) fromLight, toLight, fromNext, toNext;
    private CapsuleContent? content;
    private double left = 1;
    private int current = -1;
    private AnimationTimeline? loop;
    private ContinuousInstant? origin;
    private double phase;
    private bool onScreen = true;

    public AccountCapsule(string style)
    {
        this.style = style;
        plain = style == "plain";
        state = new CapsulePaintState { Style = style, Width = plain ? 34 : CapsuleStyles.Width(style) };
        face = new CapsuleFace(this);
        var start = UsageLight.Color(1, false);
        fromLight = toLight = fromNext = toNext = start;
        state.Light = state.Next = start;
        if (style is "11" or "11o")
        {
            glow = new Border { CornerRadius = new CornerRadius(11), IsHitTestVisible = false, Opacity = 0 };
            Children.Add(glow);
        }
        Control body = face;
        if (style is not ("7" or "11" or "11o")) body = face.ChromeGlassCapsule(); // the base is the app's own glass capsule; 7, 11 and 11o paint their own ground
        Children.Add(body);
        ClipToBounds = false;
        colour = new AnimatedScalar(this, 1, p => { Blend(p); Repaint(); });
        fill = new AnimatedScalar(this, 0, v => { state.Fill = v; Repaint(); });
        arc = new AnimatedScalar(this, 0, v => { state.Arc = v; Repaint(); });
        pop = new AnimatedScalar(this, 1, v => { state.Pop = v; Repaint(); });
        glowBlur = new AnimatedScalar(this, 0, _ => UpdateGlowLayer());
        glowOpacity = new AnimatedScalar(this, 0, _ => UpdateGlowLayer());
        press = new AnimatedScalar(this, 1, v => RenderTransform = v == 1 ? null : new ScaleTransform(v, v));
        RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        face.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsPointerOverProperty || e.Property == Avalonia.Controls.Button.IsPressedProperty) { UpdateGlow(true); UpdatePress(); Repaint(); }
            else if (e.Property == InputElement.IsKeyboardFocusWithinProperty) Repaint();
        };
        face.Click += (_, _) => Clicked?.Invoke();
        face.GotFocus += (_, e) => { state.KeyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional; Repaint(); };
        face.LostFocus += (_, _) => { state.KeyboardFocus = false; Repaint(); };
        ActualThemeVariantChanged += (_, _) => Recolour();
        AttachedToVisualTree += (_, _) => { state.Dark = Dark; Recolour(); EnsureLoop(); };
        DetachedFromVisualTree += (_, _) => StopLoop();
        // a capsule that is scrolled out of view does not loop (a frame request nobody sees is a wake-up for nothing)
        EffectiveViewportChanged += (_, e) =>
        {
            var seen = e.EffectiveViewport.Intersects(new Rect(Bounds.Size)) || Bounds.Width <= 0;
            if (seen == onScreen) return;
            onScreen = seen;
            EnsureLoop();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == EnvironmentValues.AccessibilityReduceMotionProperty)
            {
                if (ReduceMotion) foreach (var running in new[] { colour, fill, arc, pop, glowBlur, glowOpacity, press }) running.Finish(); // a run that began before it is not left to finish by frames (OD-0076)
                UpdateGlow(false); EnsureLoop(); Repaint();
            }
        };
    }

    /// <summary>Raised when the capsule is pressed (pointer or keyboard). The capsule decides nothing: whoever holds it opens the popover.</summary>
    public event Action? Clicked;

    internal CapsuleFace Face => face;

    private bool Dark => PlainControls.IsDark(ActualThemeVariant);

    /// <summary>Whether movement is stilled: the system's or the window's Reduce Motion (OD-0076). A capsule never cuts: with it, changes crossfade and nothing loops.</summary>
    private bool ReduceMotion => CapsuleMotion.Reduce(this);

    // ---- what a test can read

    public string Style => style;
    public string Mono => state.Mono;
    public (byte R, byte G, byte B) LightNow => state.Light;
    public (byte R, byte G, byte B) NextNow => state.Next;
    public IReadOnlyList<StepMark> Marks => state.Marks;
    public bool Hot => state.Hot;
    public double ArcNow => state.Arc;
    public double FillNow => state.Fill;
    public double Phase => phase;
    public bool IsLooping => loop is not null;

    /// <summary>ED-NEW-perf-idle-3: raised on the UI thread each time the capsule repaints (the plain outline of a linked account paints from this capsule's eased values and repaints with it).</summary>
    internal event Action? Repainted;
    public bool HasGlow => glow is not null;
    public double GlowOpacityNow => glowOpacity.Value;
    public double GlowBlurNow => glowBlur.Value;
    public string? NameBelow => state.NameBelow;
    public string? TipText { get; private set; }

    internal Size FaceSize => new(state.Width, CapsulePainter.Height);

    // ---- showing a thread

    public void Apply(CapsuleContent next, bool animate = true)
    {
        content = next;
        var view = next.Pill;
        state.View = view;
        state.Mono = next.Mono;
        state.Hot = next.Hot;
        state.Dark = Dark;
        state.Handshake = view?.Handshake ?? false;
        state.Still = ReduceMotion;
        var changedStep = view is not null && current >= 0 && view.Cur != current;
        current = view?.Cur ?? -1;
        if (plain)
        {
            state.Width = Math.Max(34, CapsulePainter.TextWidth(next.Mono, 11) + 16);
            state.Marks = [];
            left = 1 - Math.Min(1, next.Use);
            arc.Set(CapsuleLayout.Arc(next.Use), animate ? Ease(ArcCurve) : null);
            TipText = null;
        }
        else
        {
            state.Marks = CapsuleLayout.Roles(view!);
            var after = CapsuleLayout.NextIndex(view!);
            state.NextMono = after is { } index ? view!.Steps[index].Mono : null;
            state.PeekShown = after is not null;
            left = CapsuleLayout.Left(view!, null);
            state.NameBelow = style == "2" ? view!.Name : null;
            TipText = style == "2" ? null : CapsuleLayout.NameText(style, view!);
            fill.Set(CapsuleLayout.Fill(view!), animate ? Ease(GaugeCurve) : null);
            if (changedStep && animate)
            {
                pop.Set(0, null);
                pop.Set(1, ReduceMotion ? Animation.EaseOut(duration: .26) : PopCurve);
            }
        }
        ToolTip.SetTip(face, TipText);
        AutomationProperties.SetName(face, next.Aria);
        Retarget(animate);
        UpdateGlow(animate);
        InvalidateMeasure();
        face.InvalidateMeasure();
        EnsureLoop();
        Repaint();
    }

    private Animation? Ease(Animation curve) => FrameClock.IsBound && !ReduceMotion ? curve : null;

    // ---- the usage light: one colour per percent, eased over 0.6 s

    private (byte R, byte G, byte B) Target(double value) => UsageLight.Color(value, Dark);

    private void Retarget(bool animate)
    {
        var nextLeft = content?.Pill is { } view ? CapsuleLayout.NextLeft(view) : left;
        fromLight = state.Light;
        fromNext = state.Next;
        toLight = Target(left);
        toNext = Target(nextLeft);
        colour.Set(0, null);
        colour.Set(1, animate && toLight != fromLight || animate && toNext != fromNext ? Ease(LightCurve) : null);
        if (!animate) { colour.Set(1, null); }
    }

    private void Recolour()
    {
        state.Dark = Dark;
        if (content is not null) Retarget(true);
        Repaint();
    }

    private void Blend(double p)
    {
        state.Light = Lerp(fromLight, toLight, p);
        state.Next = Lerp(fromNext, toNext, p);
        UpdateGlowLayer();
    }

    private static (byte, byte, byte) Lerp((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double p) =>
        ((byte)Math.Round(a.R + (b.R - a.R) * p), (byte)Math.Round(a.G + (b.G - a.G) * p), (byte)Math.Round(a.B + (b.B - a.B) * p));

    // ---- the glow rim (11, 11o): a blurred copy of the rim behind the capsule

    private void UpdateGlow(bool animate)
    {
        if (glow is null) return;
        var boost = CapsuleLayout.GlowBoost(left);
        var hover = face.IsPointerOver;
        var handshake = state.Handshake;
        double opacity = 0, blur = 0;
        if (hover) { opacity = .85 * boost; blur = 9; }
        if (handshake) { opacity = .55 * boost; blur = hover ? 9 : 6; }
        if (hover && face.IsPressed) blur = 2;
        if (ReduceMotion && (hover || handshake)) blur = 7;
        var curve = animate ? Ease(GlowCurve) : null;
        glowOpacity.Set(opacity, curve);
        glowBlur.Set(blur, curve);
    }

    private void UpdateGlowLayer()
    {
        if (glow is null || glowOpacity is null || glowBlur is null) return; // the scalars call this while they are being made
        var (first, second) = CapsulePainter.GlowColors(state);
        glow.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
            GradientStops = [new GradientStop(first, 0), new GradientStop(second, 1)],
        };
        glow.Opacity = glowOpacity.Value;
        glow.Effect = glowBlur.Value > 0.01 ? new BlurEffect { Radius = glowBlur.Value } : null;
    }

    private void UpdatePress() => press.Set(face.IsPressed && face.IsPointerOver ? .95 : 1, Ease(PressCurve));

    // ---- the loops: only a style that moves, and never under Reduce Motion

    private bool NeedsLoop =>
        !plain && content?.Pill is not null && (style is "5" or "8" or "6" || state.Marks.Any(m => m.Stand) || style == "10" && state.Handshake);

    private void EnsureLoop()
    {
        var need = NeedsLoop && onScreen && !ReduceMotion && FrameClock.IsBound && VisualRoot is not null;
        if (!need) { StopLoop(); if (ReduceMotion) { phase = 0; state.Phase = 0; } return; }
        if (loop is not null) return;
        origin = SwiftClock.Now;
        // ED-NEW-perf-idle-3: a draw loop at 60 a second, not a frame request at every display refresh for as long as the style
        // moves: the timeline holds its request back to the next slot (1.5 refreshes before) and parks while no window of the
        // app can be seen. The phase is a function of the clock, so the first frame after a park draws where it has got to.
        loop = new AnimationTimeline(() => new AnimationTimelineSchedule(1.0 / 60), _ => Tick());
        loop.Appear();
    }

    private void Tick()
    {
        if (origin is not { } start) return;
        phase = (SwiftClock.Now - start).TotalSeconds;
        state.Phase = phase;
        Repaint();
        EnsureLoop();
    }

    private void StopLoop()
    {
        if (loop is not { } running) return;
        loop = null;
        running.Disappear();
    }

    private void Repaint()
    {
        face.InvalidateVisual();
        Repainted?.Invoke();
    }

    internal void Paint(DrawingContext context) => CapsulePainter.Paint(context, state);
}
