using System;
using Avalonia;
using Avalonia.Controls;

namespace Marrow.App.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/FloatingSidebarPanel.swift (B2 1fbf81e), :1-127.
// Skeleton (c100 group 4): the view, its inputs and its static sizes are declared; body (:27-105), onDrag (:107-113),
// onDragEnd (:115-119) and clamp (:121-126) are left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; CGSize/CGPoint are Avalonia
// Size/Point; `@Environment(AppModel.self)` is read from the environment, not passed in.

/// <summary>:9-127 <c>struct FloatingSidebarPanel: View</c>: the sidebar's thread list as a floating glass panel.</summary>
public sealed class FloatingSidebarPanel : Decorator
{
    // :17 static let defaultSize = CGSize(width: 300, height: 560)
    public static Size DefaultSize { get; } = new(300, 560);
    // :18 static let minSize = CGSize(width: 240, height: 320)
    public static Size MinSize { get; } = new(240, 320);
    // :19 static let margin: CGFloat = 12
    public const double FloatingSidebarPanelMargin = 12; // Swift: margin (clashes with Layoutable.Margin)
    // :20 static let stripHeight: CGFloat = 44
    public const double StripHeight = 44;

    // :13-15 let area, let canClose, let close; :27-105 body
    public FloatingSidebarPanel(Size area, bool canClose, Action close) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/FloatingSidebarPanel.swift:27");

    // :121-126 private static func clamp(_:size:in:) -> CGPoint
    private static Point Clamp(Point origin, Size size, Size @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Sidebar/FloatingSidebarPanel.swift:121");
}
