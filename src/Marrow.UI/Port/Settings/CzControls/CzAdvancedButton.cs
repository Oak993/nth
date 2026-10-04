// PORT-ONLY CZ-1: Show/Hide crossfade, turning chevron, expand/collapse (CZ-12.7).
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzAdvancedButton : CzButton
{
    public CzCrossfadeText Label { get; }
    public CzChevron Chevron { get; }
    private bool open;
    public bool Open
    {
        get => open;
        set { if (open == value) return; open = value; Label.SetText(value ? "Hide" : "Show"); Chevron.SetOpen(value); OpenChanged?.Invoke(value); }
    }
    public event Action<bool>? OpenChanged;
    public CzAdvancedButton(string title = "Advanced", string detail = "") : base(title, detail)
    {
        Padding = new Thickness(12, 0, 9, 0);
        Label = new CzCrossfadeText("Show") { Width = 30, FontSize = 12, FontWeight = FontWeight.Medium };
        Chevron = new CzChevron { VerticalAlignment = VerticalAlignment.Center, Opacity = .75 };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Label); line.Children.Add(Chevron); Child = line; Invoked += () => Open = !Open;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new CzAdvancedPeer(this);
}
