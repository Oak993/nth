using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Setup.Motion;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Ui;

/// <summary>The canvas's three buttons.</summary>
public enum ButtonKind
{
    /// <summary><c>.b.p</c>: liquid silver (dark) or graphite (light); a screen's one final action.</summary>
    Primary,

    /// <summary><c>.b.s</c>: secondary glass.</summary>
    Secondary,

    /// <summary><c>.b.d</c>: secondary glass with a red label; never the default (Uninstall).</summary>
    Destructive,
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3; the canvas's <c>.b</c> and "Buttons" row; unit U2, ED-NEW-inst-u2-1). 36 DIP tall,
/// radius 18, 15 semibold, at least 132 wide (200 for Welcome's Install, <c>.wide</c>), 22 DIP of padding; the small form
/// (<c>.sm</c>, Options' Change…) is 30 tall, radius 15, 13.5. The primary is a vertical gradient with an inset rim, a
/// soft gloss in its top half and its lift shadow; the secondary a translucent fill with a 1 DIP rim; the destructive one
/// a red label and a red rim. No per-button blur (Avalonia has none). Hover lifts the fill; a press scales the face to
/// 0.97; a keyboard focus draws a 2 DIP ring 2 DIP outside. A change of label cross-fades (0.16 s out, the words swap,
/// 0.24 s in) and, when the button reserves its labels, keeps the width of the longest so nothing beside it moves
/// ("Copy details" to "Copied", "Open nth" to "Opening…"). In a contrast theme it uses Windows' colours: the primary in
/// the highlight colour, the others the window colour with a window-text rim, the highlight on hover.
/// The button's own transform and opacity are left to <see cref="Reveal"/>: everything it animates is inside it.
/// </summary>
public sealed class SetupButton : Border, IMotion
{
    public const double Height36 = 36, SmallHeight = 30, MinWidthDefault = 132, WideMinWidth = 200;

    private readonly Panel face = new();
    private readonly Border fill = new();
    private readonly Border gloss = new() { IsHitTestVisible = false };
    private readonly Border rim = new() { IsHitTestVisible = false };
    private readonly Border hoverLight = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Border ring = new() { BorderThickness = new Thickness(2), Margin = new Thickness(-4), IsHitTestVisible = false, Opacity = 0 };
    private readonly TextBlock label;
    private readonly ReserveBox labelBox;
    private readonly ScaleTransform press = new(1, 1);
    private readonly Tween pressed = new(1);
    private readonly Tween hover = new(0);
    private readonly Spring labelOpacity = new(Reveal.FadeIn, 1, 1, 0.002);
    private readonly Action? run;
    private readonly bool small;
    private ScreenTokens tokens = ScreenTokens.Dark;
    private Animator? animator;
    private string? nextLabel;
    private double labelWait;
    private bool pointerFocus;
    private bool down;

    public SetupButton(string text, ButtonKind kind, Action? onClick = null, bool small = false, bool wide = false)
    {
        run = onClick;
        this.small = small;
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
        Height = small ? SmallHeight : Height36;
        MinWidth = small ? 0 : wide ? WideMinWidth : MinWidthDefault;
        HorizontalAlignment = HorizontalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
        var radius = new CornerRadius((small ? SmallHeight : Height36) / 2);
        fill.CornerRadius = radius;
        rim.CornerRadius = radius;
        hoverLight.CornerRadius = radius;
        ring.CornerRadius = new CornerRadius(radius.TopLeft + 4);
        gloss.Margin = new Thickness(0, 1, 0, 0);
        gloss.VerticalAlignment = VerticalAlignment.Top;
        label = SetupTheme.Text(new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center }, small ? TextRole.SmallButton : TextRole.Button, ink: null);
        labelBox = new ReserveBox(label, small ? TextRole.SmallButton : TextRole.Button) { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        labelBox.Margin = new Thickness(small ? 14 : 22, 0);
        face.Children.Add(fill);
        face.Children.Add(gloss);
        face.Children.Add(hoverLight);
        face.Children.Add(rim);
        face.Children.Add(labelBox);
        face.RenderTransform = press;
        face.RenderTransformOrigin = RelativePoint.Center;
        Child = new Panel { Children = { ring, face } };
        AutomationProperties.SetName(this, text);
        Kind = kind;
        // a larger text size makes the pill taller by what the label's line grew, so the words keep their space above and below
        label.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.LineHeightProperty) Fit();
        };
        ScreenTokens.Watch(this, value =>
        {
            tokens = value;
            Draw();
        });
        SetupTheme.BindColor(this, FocusColorProperty, SetupTheme.Focus);
        SetupTheme.BindColor(this, BadColorProperty, SetupTheme.Bad);
        SizeChanged += (_, _) => Draw();
    }

    public static readonly StyledProperty<Color> FocusColorProperty = AvaloniaProperty.Register<SetupButton, Color>(nameof(FocusColor));
    public static readonly StyledProperty<Color> BadColorProperty = AvaloniaProperty.Register<SetupButton, Color>(nameof(BadColor));

    public Color FocusColor
    {
        get => GetValue(FocusColorProperty);
        set => SetValue(FocusColorProperty, value);
    }

    public Color BadColor
    {
        get => GetValue(BadColorProperty);
        set => SetValue(BadColorProperty, value);
    }

    /// <summary>The words the button shows (or is about to show).</summary>
    public string Label => nextLabel ?? label.Text ?? "";

    /// <summary>The label's drawn opacity (it dips while the words change).</summary>
    public double LabelOpacity => label.Opacity;

    public TextBlock LabelBlock => label;

    public ButtonKind Kind
    {
        get;
        set
        {
            field = value;
            Draw();
        }
    }

    public bool ShowsFocusRing => ring.Opacity > 0;

    /// <summary>Raised when the button is clicked, or Enter or Space is pressed on it, or Narrator invokes it.</summary>
    public event Action? Clicked;

    /// <summary>The frame loop (the window's); without one a change of label is made at once.</summary>
    public void Attach(Animator frames) => animator = frames;

    /// <summary>Keeps the width of the longest of <paramref name="labels"/>, so a change of words never moves the row.</summary>
    public void ReserveLabels(params string[] labels) => labelBox.SetWordings(labels);

    /// <summary>Changes the words with a cross-fade: out (0.16 s), the words swap, in (0.24 s, 60 ms later); its Narrator name follows.</summary>
    public void SetLabel(string text, bool calm = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(Label, text, StringComparison.Ordinal))
        {
            if (nextLabel is not null && string.Equals(label.Text, text, StringComparison.Ordinal))
            {
                nextLabel = null;
                labelOpacity.Response = calm ? Reveal.CalmFade : Reveal.FadeIn;
                labelOpacity.Retarget(1);
                Start();
            }
            return;
        }
        AutomationProperties.SetName(this, text);
        if (animator is null)
        {
            label.Text = text;
            nextLabel = null;
            labelOpacity.Snap(1);
            label.Opacity = 1;
            return;
        }
        nextLabel = text;
        labelWait = 0;
        labelOpacity.Response = calm ? Reveal.CalmFade : Reveal.FadeOut;
        labelOpacity.Retarget(0);
        Start();
        calmLabel = calm;
    }

    private bool calmLabel;

    /// <summary>Changes the words at once (inside a <see cref="Reveal.Apply"/> commit, which fades the whole button).</summary>
    public void SetLabelNow(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        AutomationProperties.SetName(this, text);
        label.Text = text;
        nextLabel = null;
        labelWait = 0;
        labelOpacity.Snap(1);
        label.Opacity = 1;
    }

    /// <summary>Runs the button's action (a click, Enter, Space or Narrator's Invoke).</summary>
    public void Invoke()
    {
        if (!IsEffectivelyEnabled) return;
        run?.Invoke();
        Clicked?.Invoke();
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = pressed.Advance(seconds);
        moving |= hover.Advance(seconds);
        if (labelWait > 0)
        {
            labelWait -= seconds;
            if (labelWait > 0) moving = true;
            else
            {
                labelOpacity.Response = calmLabel ? Reveal.CalmFade : Reveal.FadeIn;
                labelOpacity.Retarget(1);
            }
        }
        moving |= labelOpacity.Advance(seconds);
        if (nextLabel is not null && labelOpacity.Target == 0 && labelOpacity.Value <= Reveal.Gone)
        {
            label.Text = nextLabel;
            nextLabel = null;
            labelOpacity.Snap(0);
            labelWait = calmLabel ? 1e-4 : Reveal.ArrivalDelay;
            moving = true;
        }
        press.ScaleX = press.ScaleY = pressed.Value;
        hoverLight.Opacity = hover.Value;
        label.Opacity = Math.Clamp(labelOpacity.Value, 0, 1);
        return moving || labelWait > 0;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ButtonPeer(this);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        hover.Retarget(1, 0.18, CubicBezier.Ease);
        Start();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hover.Retarget(0, 0.18, CubicBezier.Ease);
        if (down)
        {
            pressed.Retarget(1, 0.22, PressCurve);
            down = false;
        }
        Start();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pointerFocus = true;
        ring.Opacity = 0; // already focused from the keyboard: a press hides the ring (no focus event comes)
        Focus(NavigationMethod.Pointer);
        down = true;
        pressed.Retarget(0.97, 0.22, PressCurve);
        Start();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var wasDown = down;
        down = false;
        pressed.Retarget(1, 0.22, PressCurve);
        Start();
        var point = e.GetPosition(this);
        if (wasDown && point.X >= 0 && point.Y >= 0 && point.X <= Bounds.Width && point.Y <= Bounds.Height) Invoke();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Enter or Key.Space)) return;
        Invoke();
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        // only the keyboard's focus shows the ring (the pointer's and a screen's own choice of focus do not)
        pointerFocus = e.NavigationMethod is not (NavigationMethod.Tab or NavigationMethod.Directional);
        ring.Opacity = pointerFocus ? 0 : 1;
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        ring.Opacity = 0;
        pointerFocus = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FocusColorProperty || change.Property == BadColorProperty || change.Property == IsEnabledProperty) Draw();
    }

    private static CubicBezier PressCurve { get; } = new(.2, .9, .3, 1);

    private void Start() => animator?.Start(this);

    /// <summary>36 DIP (30 small) at the base text size; taller as the label's line grows.</summary>
    private void Fit()
    {
        var role = small ? TextRole.SmallButton : TextRole.Button;
        var rest = small ? SmallHeight : Height36;
        var height = label.LineHeight > 0 ? Math.Max(rest, rest - SetupTheme.Style(role).LineHeight + label.LineHeight) : rest;
        if (Math.Abs(height - Height) < .01) return;
        Height = height;
        var radius = new CornerRadius(height / 2);
        fill.CornerRadius = radius;
        rim.CornerRadius = radius;
        hoverLight.CornerRadius = radius;
        ring.CornerRadius = new CornerRadius(height / 2 + 4);
        Draw();
    }

    private void Draw()
    {
        var t = tokens;
        ring.BorderBrush = new SolidColorBrush(FocusColor);
        var height = double.IsNaN(Height) ? small ? SmallHeight : Height36 : Height;
        switch (Kind)
        {
            case ButtonKind.Primary:
                fill.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(t.PrimaryTop, 0), new GradientStop(t.PrimaryBottom, 1) },
                };
                fill.BoxShadow = ShadowLayer.Many(t.PrimaryShadow);
                rim.BoxShadow = t.Contrast ? default : ShadowLayer.Many(new ShadowLayer(0, 0, 0, .5, t.PrimaryRim, true), new ShadowLayer(0, 1, 0, 0, t.PrimaryRim, true));
                label.Foreground = new SolidColorBrush(t.PrimaryInk);
                gloss.IsVisible = t.PrimaryGloss > 0;
                gloss.Opacity = t.PrimaryGloss;
                hoverLight.Background = new SolidColorBrush(SetupPalette.Rgba(255, 255, 255, t.Contrast ? 0 : .06));
                break;
            default:
                fill.Background = new SolidColorBrush(t.SecondaryFill);
                fill.BoxShadow = default;
                var edge = Kind == ButtonKind.Destructive ? t.DestructiveRim : t.SecondaryRim;
                rim.BoxShadow = t.Contrast
                    ? ShadowLayer.Many(new ShadowLayer(0, 0, 0, 1, edge, true))
                    : ShadowLayer.Many(new ShadowLayer(0, 0, 0, 1, edge, true), new ShadowLayer(0, 1, 0, 0, SetupPalette.Rgba(255, 255, 255, .06), true));
                label.Foreground = new SolidColorBrush(Kind == ButtonKind.Destructive ? BadColor : t.SecondaryInk);
                gloss.IsVisible = false;
                hoverLight.Background = new SolidColorBrush(t.Contrast ? t.SecondaryHover : Over(t.SecondaryHover, t.SecondaryFill));
                break;
        }
        // the gloss: left and right 10 %, 1 DIP from the top, 45 % tall, white .55 to clear
        var width = Bounds.Width > 0 ? Bounds.Width : MinWidth;
        gloss.Margin = new Thickness(width * .1, 1, width * .1, 0);
        gloss.Height = height * .45;
        gloss.CornerRadius = new CornerRadius(height / 2, height / 2, height * .45 * .4, height * .45 * .4);
        gloss.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(SetupPalette.Rgba(255, 255, 255, .55), 0), new GradientStop(SetupPalette.Rgba(255, 255, 255, 0), 1) },
        };
    }

    /// <summary>The hover fill as a layer over the resting one: the difference the canvas's <c>:hover</c> background makes.</summary>
    private static Color Over(Color hover, Color rest)
    {
        // a layer of the hover colour that, laid over the resting fill, gives the hover fill's alpha: r + a(1 - r) = h
        var r = rest.A / 255.0;
        var a = Math.Clamp((hover.A / 255.0 - r) / Math.Max(1e-6, 1 - r), 0, 1);
        return Color.FromArgb((byte)Math.Round(a * 255), hover.R, hover.G, hover.B);
    }

    private sealed class ButtonPeer(SetupButton owner) : ControlAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string? GetNameCore() => AutomationProperties.GetName(owner);

        public void Invoke() => owner.Invoke();
    }
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 8a; the canvas's <c>.lnk</c>; unit U2, ED-NEW-inst-u2-1). A link in the fine print:
/// the link ink (the second ink; the hyperlink colour in a contrast theme), underlined 2 DIP below in the same ink at 40 %,
/// the first ink on hover, a 2 DIP focus ring for the keyboard. A button to Narrator, named by its words. Its words can
/// cross-fade with the line it sits in.
/// </summary>
public sealed class LinkButton : Border
{
    public static readonly StyledProperty<Color> InkProperty = AvaloniaProperty.Register<LinkButton, Color>(nameof(Ink));
    public static readonly StyledProperty<Color> HoverInkProperty = AvaloniaProperty.Register<LinkButton, Color>(nameof(HoverInk));
    public static readonly StyledProperty<Color> FocusColorProperty = AvaloniaProperty.Register<LinkButton, Color>(nameof(FocusColor));

    private readonly TextBlock text;
    private bool hot;
    private bool pointerFocus;

    public LinkButton(string words, TextRole role = TextRole.Hint, Action? onClick = null)
    {
        text = SetupTheme.Text(new TextBlock { Text = words }, role, ink: null);
        Child = text;
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
        Cursor = new Cursor(StandardCursorType.Hand);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        CornerRadius = new CornerRadius(2);
        if (onClick is not null) Clicked += onClick;
        AutomationProperties.SetName(this, words);
        SetupTheme.BindColor(this, InkProperty, SetupTheme.Link);
        SetupTheme.BindColor(this, HoverInkProperty, SetupTheme.Ink1);
        SetupTheme.BindColor(this, FocusColorProperty, SetupTheme.Focus);
    }

    public Color Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    public Color HoverInk
    {
        get => GetValue(HoverInkProperty);
        set => SetValue(HoverInkProperty, value);
    }

    public Color FocusColor
    {
        get => GetValue(FocusColorProperty);
        set => SetValue(FocusColorProperty, value);
    }

    public string Words
    {
        get => text.Text ?? "";
        set
        {
            text.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }

    public TextBlock TextBlock => text;

    public bool ShowsFocusRing => IsFocused && !pointerFocus;

    public event Action? Clicked;

    public void Invoke()
    {
        if (IsEffectivelyEnabled) Clicked?.Invoke();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LinkPeer(this);

    /// <summary>Inside a line of text the link sits on the line's baseline: its own first baseline is reported to the line.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        if (text.TextLayout.TextLines.Count > 0)
        {
            var baseline = text.TextLayout.TextLines[0].Baseline;
            if (Math.Abs(GetValue(TextBlock.BaselineOffsetProperty) - baseline) > .01) SetValue(TextBlock.BaselineOffsetProperty, baseline);
        }
        return size;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        hot = true;
        Draw();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hot = false;
        Draw();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pointerFocus = true;
        Focus(NavigationMethod.Pointer);
        Draw();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var point = e.GetPosition(this);
        if (point.X >= 0 && point.Y >= 0 && point.X <= Bounds.Width && point.Y <= Bounds.Height) Invoke();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Enter or Key.Space)) return;
        Invoke();
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        pointerFocus = e.NavigationMethod == NavigationMethod.Pointer;
        Draw();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        pointerFocus = false;
        Draw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == InkProperty || change.Property == HoverInkProperty || change.Property == FocusColorProperty) Draw();
    }

    private void Draw()
    {
        var ink = hot ? HoverInk : Ink;
        text.Foreground = new SolidColorBrush(ink);
        text.TextDecorations =
        [
            new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Stroke = new SolidColorBrush(Color.FromArgb((byte)Math.Round(ink.A * .4), ink.R, ink.G, ink.B)),
                StrokeThickness = 1,
                StrokeThicknessUnit = TextDecorationUnit.Pixel,
                StrokeOffset = 2,
                StrokeOffsetUnit = TextDecorationUnit.Pixel,
            },
        ];
        BorderBrush = new SolidColorBrush(FocusColor);
        BorderThickness = ShowsFocusRing ? new Thickness(2) : default;
        Padding = ShowsFocusRing ? default : new Thickness(2);
        Margin = new Thickness(outer.Left - 2, outer.Top - 2, outer.Right - 2, outer.Bottom - 2);
    }

    private Thickness outer;

    /// <summary>
    /// The room around the link's words (its margin, as the layout sees it). The link draws its focus ring 2 DIP outside
    /// its words, which it takes back with a -2 DIP margin, so a caller sets the room here rather than through Margin.
    /// </summary>
    public Thickness Outer
    {
        get => outer;
        set
        {
            outer = value;
            Margin = new Thickness(value.Left - 2, value.Top - 2, value.Right - 2, value.Bottom - 2);
        }
    }

    private sealed class LinkPeer(LinkButton owner) : ControlAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string? GetNameCore() => AutomationProperties.GetName(owner);

        public void Invoke() => owner.Invoke();
    }
}
