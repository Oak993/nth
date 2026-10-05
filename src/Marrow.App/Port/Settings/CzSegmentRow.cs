// PORT-ONLY CZ-4 (customization-SPEC CZ-12.1): a row holding one small segmented control (a radio group: one tab stop, arrows move the pick), wired to the setting it edits.
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class CzSegmentRow
{
    private bool showing;

    internal CzRow Row { get; }
    internal CzSegmentedControl Control { get; }

    /// <param name="picked">The user picked an option (its id); an echo of the setting's own change is not reported.</param>
    internal CzSegmentRow(string title, string detail, IReadOnlyList<(string Id, string Text)> options, string selected, Action<string> picked)
    {
        Control = new CzSegmentedControl(title, options, selected, small: true);
        Row = new CzRow(title, detail, Control, dimmed => Control.Dimmed = dimmed);
        Control.SelectionChanged += id => { if (!showing) picked(id); };
    }

    /// <summary>Shows what the setting says now.</summary>
    internal void Apply(string selected, string detail)
    {
        showing = true;
        try { Control.Show(selected); }
        finally { showing = false; }
        Row.SetDetail(detail);
    }
}
