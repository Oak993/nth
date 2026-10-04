// PORT-ONLY CZ-3 (customization-SPEC CZ-4.5.1; DESIGN gen.py .czmc, cz.js:155-190): the preview model switch's chip. 28 high, radius 14, padding 0 10, gap 7: the maker's
// dot, its name at 13 (a crossfading text whose width eases 0.3 s ez), and a 7 x 5 chevron that turns 180 degrees while the menu is open. The menu button pattern.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewSwitchChip : CzButton
{
    private sealed class Peer(PreviewSwitchChip owner) : ControlAutomationPeer(owner), IExpandCollapseProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        public bool ShowsMenu => true;
        public ExpandCollapseState ExpandCollapseState => owner.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() => owner.Toggled?.Invoke(true);
        public void Collapse() => owner.Toggled?.Invoke(false);
        public void Invoke() => owner.Activate();
    }

    private readonly CzNumber labelWidth;
    internal CzCrossfadeText ValueLabel { get; }
    internal CzChevron Chevron { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
    internal PreviewDot Dot { get; }

    /// <summary>Whether the menu is open: the fill stays on its hover value and the chevron is turned.</summary>
    internal bool IsOpen { get; private set; }

    /// <summary>The menu was asked open (true) or shut (false) by an assistive technology.</summary>
    internal Action<bool>? Toggled { get; set; }

    internal PreviewSwitchChip(string makerId) : base("Preview model")
    {
        Height = 28;
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(10, 0);
        PressScale = .97;
        var text = PreviewMakers.For(makerId).Name;
        Dot = new PreviewDot(Colour(makerId));
        ValueLabel = new CzCrossfadeText(text) { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        labelWidth = new CzNumber(Motion, 0, v => ValueLabel.Width = v);
        labelWidth.Jump(NaturalWidth(text));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Dot);
        row.Children.Add(ValueLabel);
        row.Children.Add(Chevron);
        Child = row;
        Describe(makerId);
        ActualThemeVariantChanged += (_, _) => Dot.Set(Colour(current));
        AttachedToVisualTree += (_, _) => Dot.Set(Colour(current));
    }

    private string current = "";

    private Color Colour(string makerId)
    {
        current = makerId;
        return Color.Parse(PreviewMakers.Dot(makerId, CzTheme.Dark(this)));
    }

    private void Describe(string makerId)
    {
        AutomationProperties.SetName(this, PreviewMakers.ChipName(makerId));
        ToolTip.SetTip(this, PreviewMakers.For(makerId).Name);
    }

    /// <summary>The maker changed: the name crossfades, the width eases to it over 0.3 s ez, the dot to its colour over 0.35 s.</summary>
    internal void Show(string makerId)
    {
        var text = PreviewMakers.For(makerId).Name;
        ValueLabel.SetText(text);
        labelWidth.To(NaturalWidth(text), CzMotion.Ez(.3));
        Dot.Ease(Colour(makerId));
        Describe(makerId);
    }

    internal void SetOpen(bool open)
    {
        IsOpen = open;
        Chevron.SetOpen(open);
        Refresh();
    }

    // the text's width at the chip's size, measured as it is drawn
    private double NaturalWidth(string text)
    {
        var probe = new TextBlock { Text = text, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 13 };
        probe.Measure(Size.Infinity);
        return Math.Ceiling(probe.DesiredSize.Width);
    }

    protected override void Refresh()
    {
        base.Refresh();
        if (IsOpen) Background = CzTheme.Brush(CzTheme.Token(this, "caph"));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
}
