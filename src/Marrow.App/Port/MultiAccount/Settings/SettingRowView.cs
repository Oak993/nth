// PORT-ONLY: T11 step 2. One grouped row of the page, as the Customization page draws its rows (CzRow: a plain title over one grey line, the control on the right): the value as a menu, a switch (the port's own SmallSwitch through
// SettingsSwitch, never a new toggle: OD-0115) or a button. It draws a SettingRow and sends the user's choice back; a dependent row sits indented under its parent. The grey line crossfades when it changes (OD-0076).
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class SettingRowView : Decorator
{
    private readonly CzCrossfadeText value = new("") { FontSize = 13, Secondary = true };
    private readonly CzRow row;
    private readonly Action<string>? chooseCallback;
    private readonly Action<bool>? toggleCallback;
    private SettingRow current;

    /// <param name="initial">The row as it is now.</param>
    /// <param name="read">The row as it is at any later moment (the switch reads it).</param>
    /// <param name="pulse">The page's pulse, read by the switch so it follows the model.</param>
    /// <param name="choose">A menu option was picked (its value).</param>
    /// <param name="toggle">A switch was turned.</param>
    /// <param name="press">A button was pressed.</param>
    internal SettingRowView(SettingRow initial, Func<SettingRow> read, ModelPulse pulse, Action<string>? choose = null, Action<bool>? toggle = null, Action? press = null)
    {
        current = initial;
        chooseCallback = choose;
        toggleCallback = toggle;
        var id = "setting-" + initial.Key;
        switch (initial.Kind)
        {
            case SettingKind.Switch:
                var toggleRow = new CzToggleRow(initial.Title, initial.Description, () => { _ = pulse.Revision; return read().On; }, on => toggle?.Invoke(on));
                row = toggleRow.Row;
                break;
            case SettingKind.Button:
                row = new CzRow(initial.Title, initial.Description, LibraryButtons.Make(initial.ButtonText ?? "", () => press?.Invoke(), ButtonLook.Plain, id + "-button"));
                break;
            default:
                var menu = MenuPopover.Value(initial.Title, id + "-menu", value, () => current.Options, v => choose?.Invoke(v));
                row = new CzRow(initial.Title, initial.Description, menu);
                break;
        }
        Child = initial.Indented ? new Border { Padding = new Thickness(18, 0, 0, 0), Child = row } : row;
        this.Named(initial.Title, id);
        value.SetText(initial.ValueLabel);
        value.Opacity = initial.Unset ? 0.7 : 1;
    }

    internal SettingRow Current => current;

    /// <summary>A menu option was picked (what the menu's item does).</summary>
    internal void Choose(string value) => chooseCallback?.Invoke(value);

    /// <summary>The switch was turned (what the switch does).</summary>
    internal void Toggle(bool on) => toggleCallback?.Invoke(on);

    internal CzRow Row => row;

    internal string ValueShown => value.Text;

    internal void Apply(SettingRow next)
    {
        current = next;
        if (next.Kind == SettingKind.Menu) value.SetText(next.ValueLabel);
        value.Opacity = next.Unset ? 0.7 : 1;
        row.SetDetail(next.Description);
    }
}
