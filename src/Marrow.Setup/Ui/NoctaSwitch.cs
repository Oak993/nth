using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.Setup.Motion;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Ui;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 8a; the locked Nocta switch, toggles design 7, as the canvas copies it: <c>.tg.t-noc</c>;
/// unit U2, ED-NEW-inst-u2-1). 46 × 26 DIP: a black track with its reflection and, when on, a soft aura behind the knob;
/// a 22 DIP dark dome knob with a highlight, a rim and a small lume that glows when on and is a faint ring when off. The
/// knob travels 20 DIP on an interruptible spring (response 0.38 s, damping 0.82): a tap mid-flight turns it around from
/// where it is with its speed. The on and off layers cross-fade (0.28 s); hover rings the knob and deepens its shadow; a
/// press swells it to 1.08 (spring 0.22 s). Calm: only the layers fade (0.14 s) and the knob is placed at once (the
/// canvas's Calm). A contrast theme draws an outline with a plain knob, the highlight colour when on. The toggle state
/// changes in the same frame as the tap, and Narrator sees a toggle named by its row.
/// </summary>
public sealed class NoctaSwitch : Control, IMotion
{
    public const double TrackWidth = 46, TrackHeight = 26, Knob = 22, Travel = 20;

    private static readonly CubicBezier Fade = new(.3, .7, .3, 1);
    private readonly Spring position;
    private readonly Spring swell = new(0.22, 0.9, 1, 0.0005);
    private readonly Tween on;
    private readonly Tween hover = new(0);
    private readonly Func<bool> calm;
    private ScreenTokens tokens = ScreenTokens.Dark;
    private Animator? animator;
    private bool pointerFocus;
    private bool down;

    public NoctaSwitch(bool isOn, Func<bool> calm)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        IsOn = isOn;
        position = new Spring(0.38, 0.82, isOn ? 1 : 0, 0.0005);
        on = new Tween(isOn ? 1 : 0);
        Width = TrackWidth;
        Height = TrackHeight;
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
        Cursor = new Cursor(StandardCursorType.Hand);
        ScreenTokens.Watch(this, value =>
        {
            tokens = value;
            InvalidateVisual();
        });
        SetupTheme.BindColor(this, FocusColorProperty, SetupTheme.Focus);
    }

    public static readonly StyledProperty<Color> FocusColorProperty = AvaloniaProperty.Register<NoctaSwitch, Color>(nameof(FocusColor));

    public Color FocusColor
    {
        get => GetValue(FocusColorProperty);
        set => SetValue(FocusColorProperty, value);
    }

    public bool IsOn { get; private set; }

    /// <summary>The knob's place: 0 off, 1 on (it moves on its spring).</summary>
    public double KnobPosition => position.Value;

    /// <summary>The knob's speed in positions per second.</summary>
    public double KnobVelocity => position.Velocity;

    public bool ShowsFocusRing => IsFocused && !pointerFocus;

    /// <summary>Raised when a tap, Space or Narrator changes the state (not when <see cref="Set"/> is called).</summary>
    public event Action<bool>? Toggled;

    public void Attach(Animator frames) => animator = frames;

    /// <summary>Flips the switch as a tap does.</summary>
    public void Toggle()
    {
        if (!IsEffectivelyEnabled) return;
        Set(!IsOn);
        Toggled?.Invoke(IsOn);
    }

    /// <summary>Sets the state; <paramref name="animate"/> false places it at once (a screen set up before it shows).</summary>
    public void Set(bool value, bool animate = true)
    {
        var old = IsOn;
        IsOn = value;
        if (!animate || animator is null)
        {
            position.Snap(value ? 1 : 0);
            on.Snap(value ? 1 : 0);
        }
        else if (calm())
        {
            position.Snap(value ? 1 : 0);
            on.Retarget(value ? 1 : 0, 0.14, CubicBezier.Ease);
        }
        else
        {
            position.Retarget(value ? 1 : 0);
            on.Retarget(value ? 1 : 0, 0.28, Fade);
        }
        if (old != value && ControlAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, old ? ToggleState.On : ToggleState.Off, value ? ToggleState.On : ToggleState.Off);
        InvalidateVisual();
        animator?.Start(this);
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = position.Advance(seconds);
        moving |= swell.Advance(seconds);
        moving |= on.Advance(seconds);
        moving |= hover.Advance(seconds);
        InvalidateVisual();
        return moving;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SwitchPeer(this);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        hover.Retarget(1, calm() ? 0.14 : 0.28, Fade);
        animator?.Start(this);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hover.Retarget(0, calm() ? 0.14 : 0.28, Fade);
        if (down)
        {
            down = false;
            swell.Retarget(1);
        }
        animator?.Start(this);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pointerFocus = true;
        Focus(NavigationMethod.Pointer);
        down = true;
        if (!calm()) swell.Retarget(1.08);
        animator?.Start(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var wasDown = down;
        down = false;
        swell.Retarget(1);
        animator?.Start(this);
        var point = e.GetPosition(this);
        if (wasDown && point.X >= -6 && point.Y >= -8 && point.X <= Bounds.Width + 6 && point.Y <= Bounds.Height + 8) Toggle();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Space) return;
        Toggle();
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (e.NavigationMethod == NavigationMethod.Pointer) pointerFocus = true;
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        pointerFocus = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var t = tokens;
        var track = new RoundedRect(new Rect(0, 0, TrackWidth, TrackHeight), TrackHeight / 2);
        var x = 2 + Travel * position.Value;
        var knob = new Rect(x, 2, Knob, Knob);
        if (t.Contrast)
        {
            using (context.PushOpacity(on.Value)) context.DrawRectangle(new SolidColorBrush(t.SwitchOnFill), null, track);
            using (context.PushOpacity(1 - on.Value)) context.DrawRectangle(null, new Pen(new SolidColorBrush(t.SwitchOutline), 1.5), track.Deflate(0.75, 0.75));
            var knobInk = SetupPalette.Lerp(t.SwitchKnob, t.SwitchKnobOn, on.Value);
            context.DrawEllipse(new SolidColorBrush(knobInk), null, knob.Center, Knob / 2, Knob / 2);
            if (ShowsFocusRing) context.DrawRectangle(null, new Pen(new SolidColorBrush(FocusColor), 2), new RoundedRect(new Rect(-3, -3, TrackWidth + 6, TrackHeight + 6), 16));
            return;
        }
        var accent = t.SwitchAccent;
        // the track: black glass, its reflection, the aura when on
        context.DrawRectangle(Vertical((SetupPalette.Hex(0x09090A), 0), (SetupPalette.Hex(0x141416), .55), (SetupPalette.Hex(0x0A0A0B), 1)), null, track,
            ShadowLayer.Many(new(0, 0, 0, .5, W(.09), true), new(0, 1, 0, 0, W(.06), true), new(0, -1, 2, 0, B(.6), true), new(0, 1, 2, 0, B(.5))));
        if (on.Value > 0.001)
            using (context.PushOpacity(on.Value))
                context.DrawRectangle(Radial(.74, .5, .48, .95, (A(accent, .30), 0), (A(accent, 0), .78)), null, track);
        context.DrawRectangle(Vertical((W(.07), 0), (W(0), .4), (W(0), .8), (W(.03), 1)), null, track);
        // the knob: shadow, then the swollen dome with its parts
        var round = new RoundedRect(knob, Knob / 2);
        context.DrawRectangle(Brushes.Transparent, null, round, ShadowLayer.Many(new(0, 1, 1, 0, B(.8)), new(0, 3, 7, 0, B(.6))));
        if (hover.Value > 0.001)
            using (context.PushOpacity(hover.Value))
                context.DrawRectangle(Brushes.Transparent, null, round, ShadowLayer.Many(new(0, 1, 1.5, 0, B(.8)), new(0, 5, 11, 0, B(.6))));
        var s = swell.Value;
        using (context.PushTransform(Matrix.CreateTranslation(-knob.Center.X, -knob.Center.Y) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(knob.Center.X, knob.Center.Y)))
        using (context.PushGeometryClip(new EllipseGeometry(knob)))
        {
            // dome: circle at 50% 112%, farthest corner (26.98 DIP on a 22 DIP knob)
            context.DrawRectangle(RadialAbs(knob, 11, 24.64, 26.98, (SetupPalette.Hex(0x3A3D44), 0), (SetupPalette.Hex(0x16171A), .46), (SetupPalette.Hex(0x060607), .82)), null, knob);
            if (on.Value > 0.001)
                using (context.PushOpacity(on.Value))
                    context.DrawRectangle(RadialAbs(knob, 11, 11.88, 16.19, (A(accent, .42), 0), (A(accent, .10), .5), (A(accent, 0), .75)), null, knob);
            var lume = new Rect(knob.X + 8.5, knob.Y + 8.5, 5, 5);
            if (on.Value < 0.999)
                using (context.PushOpacity(1 - on.Value))
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(W(.28)), .8), lume.Center, 2.1, 2.1);
            if (on.Value > 0.001)
                using (context.PushOpacity(on.Value))
                {
                    context.DrawRectangle(Brushes.Transparent, null, new RoundedRect(lume, 2.5), ShadowLayer.Many(new(0, 0, 3, 0, accent), new(0, 0, 8, 0, A(accent, .6))));
                    context.DrawEllipse(RadialAbs(lume, 2.5, 2.5, 3.54, (Colors.White, 0), (accent, .7)), null, lume.Center, 2.5, 2.5);
                }
            // the soft highlight: left and right 21 %, top 6 %, 27 % tall
            var soft = new Rect(knob.X + Knob * .21, knob.Y + Knob * .06, Knob * .58, Knob * .27);
            context.DrawEllipse(Vertical((W(.66), 0), (W(.14), .62), (W(0), 1)), null, soft.Center, soft.Width / 2, soft.Height / 2);
            context.DrawRectangle(Brushes.Transparent, null, round, ShadowLayer.Many(new(0, 0, 0, .5, W(.2), true), new(0, -1.5, 2, 0, W(.13), true), new(0, 1, 1, 0, B(.5), true)));
            if (hover.Value > 0.001)
                using (context.PushOpacity(hover.Value))
                    context.DrawRectangle(Brushes.Transparent, null, round, ShadowLayer.Many(new ShadowLayer(0, 0, 0, .8, W(.4), true)));
        }
        if (ShowsFocusRing) context.DrawRectangle(null, new Pen(new SolidColorBrush(FocusColor), 2), new RoundedRect(new Rect(-4, -4, TrackWidth + 8, TrackHeight + 8), 17));
    }

    private static Color W(double a) => SetupPalette.Rgba(255, 255, 255, a);

    private static Color B(double a) => SetupPalette.Rgba(0, 0, 0, a);

    private static Color A(Color color, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);

    private static LinearGradientBrush Vertical(params (Color Color, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        foreach (var (color, at) in stops) brush.GradientStops.Add(new GradientStop(color, at));
        return brush;
    }

    private static RadialGradientBrush Radial(double cx, double cy, double rx, double ry, params (Color Color, double At)[] stops)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(cx, cy, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(cx, cy, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(rx, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(ry, RelativeUnit.Relative),
        };
        foreach (var (color, at) in stops) brush.GradientStops.Add(new GradientStop(color, at));
        return brush;
    }

    /// <summary>A circular gradient centred at (<paramref name="cx"/>, <paramref name="cy"/>) DIP inside <paramref name="box"/>, of <paramref name="radius"/> DIP.</summary>
    private static RadialGradientBrush RadialAbs(Rect box, double cx, double cy, double radius, params (Color Color, double At)[] stops) =>
        Radial(cx / box.Width, cy / box.Height, radius / box.Width, radius / box.Height, stops);

    private sealed class SwitchPeer(NoctaSwitch owner) : ControlAutomationPeer(owner), IToggleProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;

        public ToggleState ToggleState => owner.IsOn ? ToggleState.On : ToggleState.Off;

        public void Toggle() => owner.Toggle();
    }
}
