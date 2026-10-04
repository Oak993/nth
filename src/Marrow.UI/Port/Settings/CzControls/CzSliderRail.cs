// PORT-ONLY CZ-1: pointer capture and focusable slider drawing, CZ-12.2.1/5.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
namespace Marrow.UI.Port.Settings;

public sealed class CzSliderRail : Control, ICustomHitTest
{
    public CzSlider Slider { get; }
    private readonly CzNumber position, knobScale;
    private bool dragging, keyboardFocus, placed;
    public CzSliderRail(CzSlider slider, string title, string detail)
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Slider = slider; Width = 150; Height = 22; Focusable = true;
        AutomationProperties.SetName(this, title); AutomationProperties.SetHelpText(this, detail);
        var motion = new CzMotion(this);
        position = new CzNumber(motion, 0, _ => InvalidateVisual()); knobScale = new CzNumber(motion, 1, _ => InvalidateVisual());
    }
    internal void Move(double fraction, bool direct)
    {
        if (direct || !placed) position.Jump(fraction); else position.To(fraction, CzMotion.Ez(.32));
        placed = true;
    }
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (!e.Handled && Slider.Key(e.Key)) e.Handled = true; }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragging = true; e.Pointer.Capture(this); Focus(NavigationMethod.Pointer); Slider.SetFromPointer(e.GetPosition(this).X, Bounds.Width);
        knobScale.To(1.1, CzMotion.Ease(.15)); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (dragging) Slider.SetFromPointer(e.GetPosition(this).X, Bounds.Width); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); dragging = false; e.Pointer.Capture(null); knobScale.To(IsPointerOver ? 1.06 : 1, CzMotion.Ease(.15)); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); dragging = false; knobScale.To(1, CzMotion.Ease(.15)); }
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); if (!dragging) knobScale.To(1.06, CzMotion.Ease(.15)); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (!dragging) knobScale.To(1, CzMotion.Ease(.15)); }
    protected override void OnGotFocus(FocusChangedEventArgs e) { base.OnGotFocus(e); keyboardFocus = e.NavigationMethod != NavigationMethod.Pointer; InvalidateVisual(); }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); keyboardFocus = false; InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        var travel = Math.Max(1, Bounds.Width - 18); var x = 9 + position.Value * travel;
        context.DrawRectangle(CzTheme.Brush(CzTheme.Token(this, "trk")), null, new Rect(9, 9, travel, 4), 2, 2);
        context.DrawRectangle(CzTheme.Brush(CzTheme.Token(this, "trkfill")), null, new Rect(9, 9, position.Value * travel, 4), 2, 2);
        var knob = new Rect(x - 9 * knobScale.Value, 11 - 7 * knobScale.Value, 18 * knobScale.Value, 14 * knobScale.Value);
        context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(31, 0, 0, 0)), .5), new RoundedRect(knob, 7), BoxShadows.Parse("0 1 3 0 #4D000000"));
        if (keyboardFocus) context.DrawRectangle(null, new Pen(CzTheme.Brush(CzTheme.Accent(this)), 2), knob.Inflate(1), 8, 8);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new CzSliderPeer(this);
}
