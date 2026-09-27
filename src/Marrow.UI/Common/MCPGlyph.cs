using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.UI.Common;

// Source: DroppyCode/UI/Common/MCPGlyph.swift (B3 52bc792), :4-58.
// Forms (ED-095, ED-098): MCPGlyph is a Decorator; it depends on HydraBreathRingView from HydraGlyph.cs
// for the breathing ring animation. Chrome.primaryText, Chrome.danger, Chrome.warning are in Chrome.cs.
public sealed class MCPGlyph : Decorator
{
    /// <param name="entry">:5 <c>let entry: MCPCatalogEntry</c>.</param>
    /// <param name="size">:6 <c>var size: CGFloat = 20</c>.</param>
    /// <param name="isRunning">:7 <c>var isRunning = false</c>.</param>
    /// <param name="status">:8 <c>var status: ToolCall.Status?</c>.</param>
    // :12-32 body
    public MCPGlyph(MCPCatalogEntry entry, double size = 20, bool isRunning = false, ToolCall.Status? status = null)
    {
        var glyph = EntryColor(entry); // :23, :39 - compute the glyph color
        var stack = new Panel { Width = size, Height = size }; // ZStack

        if (isRunning)
        {
            // :14-17 HydraBreathRing while isRunning
            var ring = new HydraBreathRingView
            {
                Width = size,
                Height = size,
                StrokeColor = SwiftColor.Rgb(entry.ColorValue).Opacity(0.55), // :15 entry.color.opacity(0.55), independently of :23 monochrome foreground
                StrokeWidth = 1.5,
                IsHitTestVisible = false,
            };
            stack.Children.Add(ring);
        }

        // :19-24 Image(entry.asset).resizable().renderingMode(.template)...
        var image = SwiftImage.Named(entry.Asset)
            .Resizable()
            .RenderingMode(ImageRenderingMode.Template)
            .AspectRatio(ContentMode.Fit)
            .ForegroundStyle(glyph);
        image.Width = size;
        image.Height = size;
        stack.Children.Add(image);

        var root = new Panel { Width = size, Height = size }; // :26 .frame(width: size, height: size)
        root.Children.Add(stack);

        // :27-30 .overlay(alignment: .bottomTrailing)
        if (ShouldShowBadge(status))
        {
            var badge = Badge(status!.Value, size); // :29 badgeView(symbol:color:)
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Bottom;
            root.Children.Add(badge);
        }

        Child = root;

        // :32 .accessibilityLabel(Text("\(entry.name) tool"))
        AutomationProperties.SetName(this, $"{entry.Name} tool");
    }

    /// <summary>
    /// The glyph color: Chrome.primaryText if isMonochrome, else the entry's brand colour.
    /// </summary>
    // :23, :39 entry.isMonochrome ? Chrome.primaryText : entry.color
    private static SwiftColor EntryColor(MCPCatalogEntry entry)
    {
        if (entry.IsMonochrome)
        {
            return Chrome.Chrome.PrimaryText;
        }
        return SwiftColor.Rgb(entry.ColorValue); // :95 var color: Color { Color(rgb: colorValue) }
    }

    /// <summary>
    /// Whether to show a badge: only for failed and declined statuses.
    /// </summary>
    private static bool ShouldShowBadge(ToolCall.Status? status) => status is ToolCall.Status.Failed or ToolCall.Status.Declined;

    /// <summary>
    /// The badge as a circle with a symbol and border, offset to the bottom-right corner.
    /// </summary>
    // :43-58 private func badgeView(symbol: String, color: Color) -> some View
    private static Control Badge(ToolCall.Status status, double size)
    {
        // :52-54 badgeSymbol computed property
        (string, SwiftColor) badgeInfo = status switch
        {
            ToolCall.Status.Failed => ("xmark", Chrome.Chrome.Danger),           // :37
            ToolCall.Status.Declined => ("hand.raised.slash", Chrome.Chrome.Warning), // :38
            _ => throw new UnreachableException(),
        };
        var (symbolName, color) = badgeInfo;

        var diameter = Math.Max(8, size * 0.5); // :44

        var circle = new Ellipse { Width = diameter, Height = diameter }; // :46-47 Circle().fill(color)
        circle.Fill = color.Brush(circle);

        var symbol = SwiftImage.SystemName(symbolName).ForegroundStyle(SwiftColor.White); // :48-50
        SwiftImage.SetFont(symbol, SwiftFont.System(diameter * 0.55, SwiftFont.Weight.Heavy)); // :49

        var badge = new Panel { Width = diameter, Height = diameter }; // :52, :59
        badge.Children.Add(circle);
        badge.Children.Add(symbol);

        // :53-54 .overlay { Circle().strokeBorder(...) }
        var strokeWidth = Math.Max(1, diameter * 0.12);
        var ring = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            StrokeThickness = strokeWidth,
            Stroke = SwiftColor.Dynamic(light: SwiftColor.White.Opacity(0.95), dark: SwiftColor.Black.Opacity(0.55)).Brush(badge),
        };
        var withRing = new Panel { Width = diameter, Height = diameter };
        withRing.Children.Add(badge);
        withRing.Children.Add(ring);

        // :56 .offset(x: diameter * 0.3, y: diameter * 0.3)
        withRing.RenderTransform = new TranslateTransform(diameter * 0.3, diameter * 0.3);

        // :57 .transition(.scale.combined(with: .opacity))
        return withRing.Transition(SwiftTransition.Scale().Combined(SwiftTransition.Opacity));
    }
}
