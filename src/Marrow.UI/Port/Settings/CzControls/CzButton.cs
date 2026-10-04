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
        Refresh(); PropertyChanged += (_, e) => { if (e.Property.Name == "ActualThemeVariant") Refresh(); };
    }
    protected virtual void Refresh()
    {
        if (ChromeFill) Background = CzTheme.Brush(CzTheme.Token(this, IsPointerOver ? "caph" : "cap"));
        if (Child is TextBlock text) text.Foreground = CzTheme.Text(this);
        InvalidateVisual();
    }
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
