// PORT-ONLY CZ-4 (customization-SPEC CZ-12.8; DESIGN gen.py ROW(), `.czf > .in > .dv + row`): one place in a card: the divider above a row and the row, in a fold that opens and shuts (0.34 s, opacity 0.24 s after 0.08 s; shut 0.14 s)
// with the content clipped while it moves and inert once shut. The card (CzRowStack) decides which dividers show.
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class CzRowSlot : Decorator
{
    private readonly CzFoldRow fold;

    internal CzDivider Divider { get; }
    internal Control Row { get; }

    /// <summary>A caption line: it has no divider above it and the row after it has none either (cz.js:608-610).</summary>
    internal bool IsCaption { get; }

    /// <summary>Raised when the slot opens or shuts, so the card can lay its dividers out again.</summary>
    internal event Action? OpenChanged;

    internal CzRowSlot(Control row, bool caption = false, bool open = true, bool dividerShown = false)
    {
        Row = row;
        IsCaption = caption;
        Divider = new CzDivider(dividerShown);
        fold = new CzFoldRow(new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Children = { Divider, row } }, open);
        Child = fold;
    }

    /// <summary>Whether the row is shown (open) or folded shut.</summary>
    internal bool Open
    {
        get => fold.Open;
        set
        {
            if (fold.Open == value) return;
            fold.Open = value;
            OpenChanged?.Invoke();
        }
    }

    /// <summary>How open the fold is now: 0 shut, 1 open.</summary>
    internal double Fraction => fold.Fraction;

    /// <summary>How opaque the slot is now (CZ-12.8: 0.24 s after 0.08 s opening, 0.14 s shutting).</summary>
    internal double Fade => fold.Opacity;
}
