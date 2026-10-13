// PORT-ONLY: the terminal's accessible, selectable and copyable text (TerminalSurface.TextControl). The cell painter draws every glyph and the
// selection; this control is invisible (opacity 0) and serves only its Text (the automation name, SelectedText, Copy, SelectAll), none of which
// reads a text layout (ED-NEW-perf-motion-1).
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Marrow.App.Views.Terminal.Port;

internal sealed class TerminalSemanticText : SelectableTextBlock
{
    // Styled exactly as the SelectableTextBlock it replaces (its theme and the app's SelectableTextBlock styles).
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    /// <summary>How many text layouts were made (for tests).</summary>
    internal int LayoutsMade { get; private set; }

    // TextBlock lays out its whole text in Measure, again in Arrange and on every selection change: for a 500-row scrollback that is every history
    // line shaped twice per output frame, for text nobody sees. The layout is of nothing; the surface sizes this control to the cell grid.
    protected override TextLayout CreateTextLayout(string? text)
    {
        LayoutsMade++;
        return new TextLayout(string.Empty, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground);
    }

    // Invisible (opacity 0): the surface paints the selection highlight and the glyphs on the cell grid.
    protected override void RenderTextLayout(DrawingContext context, Avalonia.Point origin)
    {
    }
}
