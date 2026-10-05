// PORT-ONLY CZ-1: drawn capsule primitive (CZ-12.4); no framework-default button appearance.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Marrow.UI.Port.Settings;

public class CzButton : Decorator, ICustomHitTest
{
    private readonly CzNumber scale;
    private readonly CzNumber dimOpacity;
    private readonly CzColor fill, ink;
    private bool filled;
    private bool shownDark;
    public CornerRadius CornerRadius { get; set; }
    public IBrush? Background { get; set; }
    private bool down;
    protected bool IsPressed => down;
    private bool keyboardFocus;
    public CzMotion Motion { get; }
    public event Action? Invoked;
    public double PressScale { get; set; } = .96;
    public bool ChromeFill { get; set; } = true;
    public bool Dimmed { get => !IsEnabled; set { IsEnabled = !value; Focusable = !value; dimOpacity.To(value ? .5 : 1, CzMotion.Ease(.3)); } }
    public CzButton(string name, string detail = "")
    {
        Focusable = true; Height = 26; CornerRadius = new CornerRadius(13); Padding = new Thickness(12, 0);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        AutomationProperties.SetName(this, name); AutomationProperties.SetHelpText(this, detail);
        Child = new TextBlock { Text = name, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 12, FontWeight = FontWeight.Medium,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = CzTheme.Text(this) };
        Motion = new CzMotion(this);
        RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        scale = new CzNumber(Motion, 1, v => RenderTransform = new ScaleTransform(v, v));
        dimOpacity = new CzNumber(Motion, 1, v => Opacity = v);
        fill = new CzColor(Motion, Colors.Transparent, color => { if (ChromeFill) { Background = CzTheme.Brush(color); InvalidateVisual(); } });
        ink = new CzColor(Motion, Colors.Transparent, color => { if (ChromeFill) { ApplyInk(CzTheme.Brush(color)); InvalidateVisual(); } });
        Refresh(); PropertyChanged += (_, e) => { if (e.Property.Name == "ActualThemeVariant") Refresh(); };
    }
    protected virtual void Refresh()
    {
        var dark = CzTheme.Dark(this);
        var themeChange = filled && dark != shownDark;
        shownDark = dark;
        if (ChromeFill)
        {
            Ease(fill, CzTheme.Token(this, IsPointerOver ? "caph" : "cap"), themeChange, CzMotion.Ease(.12));
            Ease(ink, ((SolidColorBrush)CzTheme.Text(this)).Color, themeChange, CzMotion.Ease(.12));
        }
        else if (Child is TextBlock text) text.Foreground = CzTheme.Text(this);
        filled = true;
        InvalidateVisual();
    }
    // CZ-14.1, OD-0076, DESIGN gen.py:298 `.czbtn { transition: background-color .12s ease }`: a hover or press eases the fill over 0.12 s; in a tree that turns over (AppearanceTurn, the Settings window) a theme change
    // eases it with the window's 0.32 s turnover; the first fill, Reduce Motion and an unmarked tree take the colour at once
    // The label takes the same turnover (OD-0076: nothing cuts); a button that fills itself with the tokens is the one whose label this paints
    private void Ease(CzColor color, Color target, bool themeChange, Animation hover)
    {
        if (!filled) color.Jump(target);
        else if (themeChange) { if (AppearanceTurn.Eases(this)) color.To(target, AppearanceTurn.Curve); else color.Jump(target); }
        else color.To(target, hover);
    }

    /// <summary>Paints the label with the eased ink (a theme change eases it); the capsule's label is a line of its own.</summary>
    protected virtual void ApplyInk(IBrush brush) { if (Child is TextBlock text) text.Foreground = brush; }
    public void Activate() { if (IsEffectivelyEnabled) Invoked?.Invoke(); }
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Refresh(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Refresh(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        down = true; Refresh(); Focus(NavigationMethod.Pointer); e.Pointer.Capture(this); scale.To(PressScale, CzMotion.Ease(.12)); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); if (!down) return;
        down = false; Refresh(); e.Pointer.Capture(null); scale.To(1, CzMotion.Ease(.12));
        if (HitTest(e.GetPosition(this))) Activate(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); down = false; Refresh(); scale.To(1, CzMotion.Ease(.12)); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEffectivelyEnabled || e.Handled) return;
        if (e.Key is Key.Enter or Key.Space) { Activate(); e.Handled = true; }
    }
    protected override void OnGotFocus(FocusChangedEventArgs e) { base.OnGotFocus(e); keyboardFocus = e.NavigationMethod != NavigationMethod.Pointer; InvalidateVisual(); }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); keyboardFocus = false; InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Background, null, new Rect(Bounds.Size), CornerRadius.TopLeft, CornerRadius.TopLeft);
        base.Render(context);
        if (keyboardFocus) context.DrawRectangle(null, new Pen(CzTheme.Brush(CzTheme.Accent(this)), 2), new Rect(Bounds.Size).Inflate(2), CornerRadius.TopLeft + 2, CornerRadius.TopLeft + 2);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new CzButtonPeer(this);
}
