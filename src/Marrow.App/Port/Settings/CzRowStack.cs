// PORT-ONLY CZ-4 (customization-SPEC CZ-12.8; DESIGN cz.js card() :591-610): the rows of one card, top to bottom. Its dividers follow the rows that are shown: none above the first shown row, none around a caption, none under a shown caption;
// they fade over 0.3 s (CzDivider) as rows fold in and out.
using Avalonia.Controls;
using Avalonia.Layout;

namespace Marrow.App.Port.Settings;

internal sealed class CzRowStack : StackPanel
{
    private readonly List<CzRowSlot> slots = [];

    internal CzRowStack() => Orientation = Orientation.Vertical;

    internal IReadOnlyList<CzRowSlot> Slots => slots;

    /// <summary>Puts <paramref name="row"/> at the end of the card.</summary>
    internal CzRowSlot Add(Control row, bool caption = false, bool open = true)
    {
        // a row put in place starts with the divider the rule gives it, so building a page fades nothing in
        var first = !slots.Any(s => s.Open);
        var afterCaption = slots.Count > 0 && slots[^1] is { IsCaption: true, Open: true };
        var slot = new CzRowSlot(row, caption, open, dividerShown: !(first || caption || afterCaption));
        slot.OpenChanged += Relayout;
        slots.Add(slot);
        Children.Add(slot);
        return slot;
    }

    /// <summary>The divider rule of the design: the first shown row, a caption, and the row right after a shown caption have none.</summary>
    internal void Relayout()
    {
        var first = true;
        CzRowSlot? previous = null;
        foreach (var slot in slots)
        {
            var afterCaption = previous is { IsCaption: true, Open: true };
            slot.Divider.Shown = !(first || slot.IsCaption || afterCaption);
            if (slot.Open) first = false;
            previous = slot;
        }
    }
}
