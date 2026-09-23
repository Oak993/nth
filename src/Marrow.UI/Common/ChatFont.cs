using Marrow.UI.Port;

// Source: DroppyCode/UI/Common/ChatFont.swift (B2 1fbf81e), :1-11.
namespace Marrow.UI.Common;

// SwiftUI Font is represented by SwiftFont (ED-091). This extension of the library Font type
// lives in the Swift file's twin as a static helper (R-NAME-4).
public static class ChatFont
{
    // :6-9
    public static SwiftFont Chat(
        SwiftFont.TextStyle style,
        SwiftFont.Weight? weight = null,
        SwiftFont.Design design = SwiftFont.Design.Default)
    {
        var resolvedWeight = weight ?? (style == SwiftFont.TextStyle.Headline ? SwiftFont.Weight.Semibold : null);
        var font = SwiftFont.System(style, design: design);
        return resolvedWeight is { } value ? font.FontWeight(value) : font;
    }
}
