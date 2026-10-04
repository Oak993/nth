// PORT-ONLY CZ-1: 28px row, name and animated checked indicator (CZ-12.12.2).
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzMenuItem : CzButton
{
    public CzMenuButton Menu { get; }
    public string Id { get; }
    public TextBlock Label { get; }
    public CzCheckMark Check { get; }
    public bool Checked { get; private set; }
    public CzMenuItem(CzMenuButton menu, string id, string text) : base(text)
    {
        Menu = menu; Id = id; Height = 28; CornerRadius = new CornerRadius(7); Padding = new Thickness(8, 0);
        HorizontalAlignment = HorizontalAlignment.Stretch; ChromeFill = false; PressScale = 1;
        Label = new TextBlock { Text = text, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 13, Foreground = CzTheme.Text(this) };
        Check = new CzCheckMark { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Check); line.Children.Add(Label); Child = line;
        Invoked += () => Menu.Select(Id);
    }
    public void SetChecked(bool value) { Checked = value; Check.SetChecked(value); }
    protected override void Refresh() { base.Refresh(); if (Label is not null) Label.Foreground = CzTheme.Text(this); Background = IsPressed ? CzTheme.Brush(CzTheme.Token(this, "prs")) : IsPointerOver ? CzTheme.Brush(CzTheme.Token(this, "hov")) : Brushes.Transparent; }
    protected override AutomationPeer OnCreateAutomationPeer() => new CzMenuItemPeer(this);
}
