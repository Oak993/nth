// Source: DroppyCode/UI/Chrome/HydraPairMark.swift (B3 52bc792), :5-23.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Chrome;

/// <summary>
/// A pair as its two provider marks, the lead's larger and the heads' smaller after an
/// arrow, so every place a pair is drawn says the same thing at the same proportions.
/// </summary>
// :5-23
public sealed class HydraPairMark : Decorator
{
    /// <summary>
    /// The lead provider kind.
    /// </summary>
    // :6
    public static readonly StyledProperty<ProviderKind> LeadProperty =
        AvaloniaProperty.Register<HydraPairMark, ProviderKind>(nameof(Lead));

    /// <summary>
    /// The heads provider kind.
    /// </summary>
    // :7
    public static readonly StyledProperty<ProviderKind> HeadsProperty =
        AvaloniaProperty.Register<HydraPairMark, ProviderKind>(nameof(Heads));

    /// <summary>
    /// The lead size. Defaults to 16.
    /// </summary>
    // :8
    public static readonly StyledProperty<double> LeadSizeProperty =
        AvaloniaProperty.Register<HydraPairMark, double>(nameof(LeadSize), 16);

    public ProviderKind Lead
    {
        get => GetValue(LeadProperty);
        set => SetValue(LeadProperty, value);
    }

    public ProviderKind Heads
    {
        get => GetValue(HeadsProperty);
        set => SetValue(HeadsProperty, value);
    }

    public double LeadSize
    {
        get => GetValue(LeadSizeProperty);
        set => SetValue(LeadSizeProperty, value);
    }

    private bool _built;

    public HydraPairMark(ProviderKind lead, ProviderKind heads, double leadSize = 16)
    {
        Lead = lead;
        Heads = heads;
        LeadSize = leadSize;
        _built = true;
        Rebuild();
    }

    // :10-18 body is re-evaluated from the current lead, heads and leadSize, so a changed value redraws the pair.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_built && (change.Property == LeadProperty || change.Property == HeadsProperty || change.Property == LeadSizeProperty))
            Rebuild();
    }

    private void Rebuild()
    {
        var lead = Lead;
        var heads = Heads;
        var leadSize = LeadSize;
        // :10-39 HStack containing the pair marks
        var spacing = SwiftNumber.Rounded(leadSize * 0.25); // :11

        var hstack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = spacing,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        // :12-13 Lead provider icon at leadSize with primary text color
        var leadIcon = SwiftImage.Named(lead.IconName)
            .Resizable().AspectRatio(ContentMode.Fit) // Components.swift:9-12 ProviderIcon
            .ForegroundStyle(Chrome.PrimaryText);
        leadIcon.Width = leadSize;
        leadIcon.Height = leadSize;
        hstack.Children.Add(leadIcon);

        // :14-16 Arrow at (leadSize * 0.5).rounded() semibold with secondary text color
        var arrowSize = SwiftNumber.Rounded(leadSize * 0.5);
        var arrowFont = SwiftFont.System(arrowSize, SwiftFont.Weight.Semibold);
        var arrow = SwiftImage.SystemName("arrow.right")
            .Font(arrowFont)
            .ForegroundStyle(Chrome.SecondaryText);
        hstack.Children.Add(arrow);

        // :17-18 Heads provider icon at (leadSize * 0.75).rounded() with primary text at 0.8 opacity
        var headsSize = SwiftNumber.Rounded(leadSize * 0.75);
        var headsIcon = SwiftImage.Named(heads.IconName)
            .Resizable().AspectRatio(ContentMode.Fit) // Components.swift:9-12 ProviderIcon
            .ForegroundStyle(Chrome.PrimaryText.Opacity(0.8));
        headsIcon.Width = headsSize;
        headsIcon.Height = headsSize;
        hstack.Children.Add(headsIcon);

        // :20-21 Accessibility element with ignored children and proper label
        AutomationProperties.SetAccessibilityView(hstack, AccessibilityView.Raw); // :20 children: .ignore
        var accessibilityLabel = $"{lead.DisplayName} lead, {heads.DisplayName} heads"; // :21
        AutomationProperties.SetName(this, accessibilityLabel);

        Child = hstack;
    }
}
