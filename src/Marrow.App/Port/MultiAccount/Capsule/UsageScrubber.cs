// PORT-ONLY: multi-account step 3a (OD-0115, OD-0119). The "Usage left" scrubber of the glow card (multi-account.html .czsl): what is left of the previewed account in whole percents. A 4 px track filled in the usage light's own
// colours, a 14 px white knob. Pointer drag, arrows (1), PageUp/PageDown (10), Home (0), End (100). Every touch holds the preview's slow run for four seconds.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.UI.Port;

namespace Marrow.App.Port.MultiAccount.Capsule;

internal sealed class UsageScrubber : RangeBase
{
    public UsageScrubber()
    {
        Minimum = 0;
        Maximum = 100;
        SmallChange = 1;
        LargeChange = 10;
        Value = 100;
        Focusable = true;
        Height = 16;
        MinWidth = 60;
        Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, "Usage left, for the preview");
        held = new AnimatedScalar(this, 1, _ => InvalidateVisual());
        PropertyChanged += (_, e) => { if (e.Property == ValueProperty) InvalidateVisual(); };
    }

    private readonly AnimatedScalar held;
    private bool dragging;
    private bool keyboardFocus;

    /// <summary>Raised on every touch of the pointer or keyboard (not by the run).</summary>
    public event Action? Touched;

    public bool IsHeld => dragging;

    /// <summary>"100%": the number beside the scrubber.</summary>
    public string ValueText => Math.Round(Value).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>"100% left": the words for a screen reader.</summary>
    public string SpokenText => ValueText + " left";

    public void Set(double percent) => Value = Math.Max(0, Math.Min(100, Math.Round(percent)));

    protected override Size MeasureOverride(Size availableSize) => new(double.IsFinite(availableSize.Width) ? availableSize.Width : 120, 16);

    private void At(PointerEventArgs e)
    {
        var x = e.GetPosition(this).X;
        Set(Bounds.Width <= 0 ? Value : x / Bounds.Width * 100);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        dragging = true;
        Touched?.Invoke();
        At(e);
        held.Set(1.15, Animation.EaseOut(duration: .12));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging) return;
        Touched?.Invoke();
        At(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragging) return;
        dragging = false;
        e.Pointer.Capture(null);
        Touched?.Invoke();
        held.Set(1, Animation.EaseOut(duration: .12));
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (dragging) { dragging = false; held.Set(1, Animation.EaseOut(duration: .12)); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double? next = e.Key switch
        {
            Key.Right or Key.Up => Value + 1,
            Key.Left or Key.Down => Value - 1,
            Key.PageUp => Value + 10,
            Key.PageDown => Value - 10,
            Key.Home => 0,
            Key.End => 100,
            _ => null,
        };
        if (next is null) return;
        Touched?.Invoke();
        Set(next.Value);
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        keyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        keyboardFocus = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var dark = Marrow.UI.Port.PlainControls.IsDark(ActualThemeVariant);
        var t = CapsuleTokens.For(dark);
        var w = Bounds.Width;
        var share = Math.Clamp(Value / 100, 0, 1);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, 16));
        context.DrawRectangle(new SolidColorBrush(t.Track), null, new RoundedRect(new Rect(0, 6, w, 4), 2));
        if (share > 0)
        {
            var fill = new Rect(0, 6, w * share, 4);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
                GradientStops = [new GradientStop(CapsuleTokens.Of(UsageLight.Color(.05, dark)), 0), new GradientStop(CapsuleTokens.Of(UsageLight.Color(.25, dark)), .25), new GradientStop(CapsuleTokens.Of(UsageLight.Color(.5, dark)), .5), new GradientStop(CapsuleTokens.Of(UsageLight.Color(1, dark)), 1)],
            };
            context.DrawRectangle(brush, null, new RoundedRect(fill, 2));
        }
        var centre = new Point(w * share, 8);
        var radius = 7 * held.Value;
        if (keyboardFocus) context.DrawEllipse(null, new Pen(new SolidColorBrush(t.FocusRing), 2), centre, radius + 1, radius + 1);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(77, 0, 0, 0)), null, new Point(centre.X, centre.Y + 1), radius + .5, radius + .5);
        context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)), .5), centre, radius, radius);
    }
}
