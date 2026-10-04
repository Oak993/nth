// PORT-ONLY CZ-1: radio option, also draws a palette swatch when configured (CZ-12.5).
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public class CzRadioButton : CzButton
{
    public CzRadioGroup Group { get; }
    public string Id { get; }
    public IBrush? SwatchFill { get; set; }
    private bool picked;
    private readonly CzNumber ringOpacity, ringScale, dotScale;
    private readonly CzColor labelColor;
    public bool Picked
    {
        get => picked;
        internal set
        {
            picked = value;
            // The constructor's first placement is settled; subsequent selection transitions follow CZ-12.5.
            ringOpacity?.To(value ? 1 : 0, CzMotion.Ease(.18));
            ringScale?.To(value ? 1 : .8, CzMotion.Ez(.24));
            Refresh();
        }
    }
    public CzRadioButton(CzRadioGroup group, string id, string text) : base(text)
    {
        Group = group; Id = id; ChromeFill = false; Background = Brushes.Transparent;
        ringOpacity = new CzNumber(Motion, 0, _ => InvalidateVisual()); ringScale = new CzNumber(Motion, .8, _ => InvalidateVisual());
        dotScale = new CzNumber(Motion, 1, _ => InvalidateVisual());
        labelColor = new CzColor(Motion, ((SolidColorBrush)CzTheme.Text(this, true)).Color, c => { if (Child is TextBlock text) text.Foreground = CzTheme.Brush(c); });
        Invoked += () => Group.Select(Id);
    }
    protected override void Refresh()
    {
        base.Refresh();
        if (Child is TextBlock text)
        {
            if (labelColor is null) text.Foreground = CzTheme.Text(this, !Picked);
            else { labelColor.To(((SolidColorBrush)CzTheme.Text(this, !Picked && !IsPointerOver)).Color, CzMotion.Ease(.15)); text.Foreground = CzTheme.Brush(labelColor.Value); }
        }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (SwatchFill is null) return;
        var centre = new Point(12, 12);
        var scale = dotScale.Value;
        context.DrawEllipse(SwatchFill, new Pen(CzTheme.Brush(CzTheme.Token(this, "rim")), .5), centre, 8 * scale, 8 * scale);
        var ring = CzTheme.Token(this, "ring");
        ring = Color.FromArgb((byte)Math.Round(ring.A * ringOpacity.Value), ring.R, ring.G, ring.B);
        context.DrawEllipse(null, new Pen(CzTheme.Brush(ring), 1.5), centre, 11.25 * ringScale.Value, 11.25 * ringScale.Value);
    }
    protected override void OnPointerEntered(Avalonia.Input.PointerEventArgs e) { base.OnPointerEntered(e); if (SwatchFill is not null) dotScale.To(1.12, CzMotion.Ease(.12)); }
    protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e) { base.OnPointerExited(e); if (SwatchFill is not null) dotScale.To(1, CzMotion.Ease(.12)); }
    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e) { base.OnPointerPressed(e); if (SwatchFill is not null && IsEffectivelyEnabled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) dotScale.To(.86, CzMotion.Ease(.08)); }
    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Handled && Picked && IsEffectivelyEnabled) Focus(Avalonia.Input.NavigationMethod.Pointer);
        if (SwatchFill is not null) dotScale.To(IsPointerOver ? 1.12 : 1, CzMotion.Ease(.12));
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new CzRadioButtonPeer(this);
}
