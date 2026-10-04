// PORT-ONLY CZ-1: the slider's 40x20 Auto capsule, CZ-12.2.2/6; distinct from the reused SettingsSwitch.
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzAutoButton : CzButton
{
    private bool check;
    private readonly CzColor fill, ink;
    public bool Checked { get => check; set { check = value; Refresh(); } }
    public CzAutoButton(string title, bool value) : base(title)
    {
        Width = 40; Height = 20; Padding = new Thickness(0); CornerRadius = new CornerRadius(10);
        check = value; PressScale = .95;
        fill = new CzColor(Motion, value ? CzTheme.Token(this, "pill") : Colors.Transparent, c => { Background = CzTheme.Brush(c); InvalidateVisual(); });
        ink = new CzColor(Motion, ((SolidColorBrush)CzTheme.Text(this, !value)).Color, c => { if (Child is TextBlock label) label.Foreground = CzTheme.Brush(c); });
        Refresh();
    }
    protected override void Refresh()
    {
        base.Refresh();
        if (fill is null) { Background = check ? CzTheme.Brush(CzTheme.Token(this, "pill")) : Brushes.Transparent; return; }
        fill.To(check ? CzTheme.Token(this, "pill") : Colors.Transparent, CzMotion.Ease(.2));
        Background = CzTheme.Brush(fill.Value);
        ink.To(((SolidColorBrush)CzTheme.Text(this, !check && !IsPointerOver)).Color, CzMotion.Ease(.2));
        if (Child is TextBlock text) text.Foreground = CzTheme.Brush(ink.Value);
    }
    public override void Render(DrawingContext context)
    {
        if (Checked) context.DrawRectangle(Background, null, new RoundedRect(new Rect(Bounds.Size), 10), BoxShadows.Parse("0 1 2 0 #26000000"));
        base.Render(context);
        context.DrawRectangle(null, new Pen(CzTheme.Brush(CzTheme.Token(this, Checked ? "rim" : "div")), Checked ? .5 : 1), new Rect(Bounds.Size).Deflate(.5), 9.5, 9.5);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new CzTogglePeer(this);
}
