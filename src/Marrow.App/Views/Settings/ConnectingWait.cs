// PORT-ONLY: OD-0136 amendment 2, step 11 "Connecting and search" (ED-NEW-nth-motion). The wait of an MCP server that is
// connecting, waiting for a sign-in or being checked: the 12 px step 1 spinner in the 16 px status slot, with the light passing
// through the label (the label keeps its own text, font and colour). Replaces the 3 x 3 working spinner at those places.
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Views.Settings;

internal static class ConnectingWait
{
    /// <summary>The status slot at work: the spinner at 12 px (the MCP rows' size).</summary>
    public static NthStatusMark Mark() => new()
    {
        Status = NthStatus.Working,
        MarkSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The label with the light through it, in the font and colour the site asks for.</summary>
    public static NthShineText Label(string text, SwiftFont font, SwiftColor color, bool wrap = false)
    {
        var label = new NthShineText { Text = text, TextWrapping = wrap ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap, IsHitTestVisible = false };
        font.Apply(label);
        label.Foreground = color.Brush(label);
        return label;
    }

    /// <summary>The mark and the label side by side, 6 points apart (the old spinner-and-text row).</summary>
    public static StackPanel Row(string text, SwiftFont font, SwiftColor color, bool wrap = false) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children = { Mark(), Label(text, font, color, wrap) },
    };
}
