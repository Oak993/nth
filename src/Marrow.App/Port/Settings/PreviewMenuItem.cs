// PORT-ONLY CZ-3 (customization-SPEC CZ-4.5.2; DESIGN gen.py .pitem): one maker in the preview model menu. 28 high, radius 7, padding 0 8, gap 8: the check (12 px, on the
// current maker), the maker's dot (10 px), its name (13) and its model id (11, secondary, ellipsised). A menu item radio.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewMenuItem : CzButton
{
    private sealed class Peer(PreviewMenuItem owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.MenuItem;
        public bool IsSelected => owner.Checked;
        public ISelectionProvider? SelectionContainer => null;
        public void Select() => owner.Activate();
        public void AddToSelection() => Select();
        public void RemoveFromSelection() { } // required single selection: only another pick replaces it
        public void Invoke() => owner.Activate();
    }

    internal const double Height28 = 28;
    internal PreviewMaker Maker { get; }
    internal CzCheckMark Check { get; } = new() { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
    internal PreviewDot Dot { get; }
    internal TextBlock NameLabel { get; }
    internal TextBlock ModelLabel { get; }
    internal bool Checked { get; private set; }

    internal PreviewMenuItem(PreviewMaker maker, Action<PreviewMaker> pick) : base(maker.Name)
    {
        Maker = maker;
        Height = Height28;
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(8, 0);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ChromeFill = false;
        PressScale = 1;
        Dot = new PreviewDot(Color.Parse(PreviewMakers.Dot(maker.Id, dark: true)));
        NameLabel = Label(maker.Name, 13, secondary: false);
        ModelLabel = Label(maker.Model, 11, secondary: true);
        ModelLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        // name and model sit on one baseline, 7 apart; the model is the one that gives way (.pname i: min-width 0, ellipsis)
        var names = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(NameLabel, Dock.Left);
        ModelLabel.Margin = new Thickness(7, 0, 0, 0);
        ModelLabel.VerticalAlignment = VerticalAlignment.Bottom;
        NameLabel.VerticalAlignment = VerticalAlignment.Bottom;
        names.Children.Add(NameLabel);
        names.Children.Add(ModelLabel);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), VerticalAlignment = VerticalAlignment.Center };
        Check.Margin = new Thickness(0, 0, 8, 0);
        Dot.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(Dot, 1);
        Grid.SetColumn(names, 2);
        row.Children.Add(Check);
        row.Children.Add(Dot);
        row.Children.Add(names);
        Child = row;
        AutomationProperties.SetName(this, maker.Name);
        AutomationProperties.SetHelpText(this, maker.Model);
        Invoked += () => pick(Maker);
        ActualThemeVariantChanged += (_, _) => Dot.Set(Color.Parse(PreviewMakers.Dot(Maker.Id, CzTheme.Dark(this))));
        AttachedToVisualTree += (_, _) => Dot.Set(Color.Parse(PreviewMakers.Dot(Maker.Id, CzTheme.Dark(this))));
    }

    private TextBlock Label(string text, double size, bool secondary) =>
        new() { Text = text, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = size, Foreground = CzTheme.Text(this, secondary), Focusable = false, IsHitTestVisible = false };

    internal void SetChecked(bool value)
    {
        Checked = value;
        Check.SetChecked(value);
    }

    protected override void Refresh()
    {
        base.Refresh();
        if (NameLabel is not null)
        {
            NameLabel.Foreground = CzTheme.Text(this);
            ModelLabel.Foreground = CzTheme.Text(this, secondary: true);
        }
        Background = IsPressed ? CzTheme.Brush(CzTheme.Token(this, "prs")) : IsPointerOver ? CzTheme.Brush(CzTheme.Token(this, "hov")) : Brushes.Transparent;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
}
