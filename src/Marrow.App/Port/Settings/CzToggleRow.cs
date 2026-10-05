// PORT-ONLY CZ-4 (customization-SPEC CZ-12.3, CZ-12.2.7): a row holding the port's small switch (SettingsSwitch, behind CzSwitch's name and dimming), bound to the setting it edits. A dimmed switch is inert and not focusable; it can be
// shown on and inert (Hold colours still under Mono).
using Marrow.App.Views.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class CzToggleRow
{
    internal CzRow Row { get; }
    internal CzSwitch Switch { get; }

    internal CzToggleRow(string title, string detail, Func<bool> get, Action<bool> set)
    {
        Switch = new CzSwitch(new SettingsSwitch(new Binding<bool>(get, set)), title, detail);
        Row = new CzRow(title, detail, Switch, dimmed => Switch.Dimmed = dimmed);
    }

    /// <summary>Shows what the setting says now (the switch follows its binding by itself).</summary>
    internal void Apply(string detail, bool dimmed = false)
    {
        Row.SetDetail(detail);
        Row.Dimmed = dimmed;
    }
}
