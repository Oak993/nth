// PORT-ONLY CZ-4 (customization-SPEC CZ-12.6; OWNER OD-0099 Look Picks, Q-CZ-3 B; OD-0095): the glass colour picker the Uiverse light's Custom colour wells open. One picker (and one list of recent colours) serves both wells; it is
// presented below the open well as a popover with the Mac's arrow tip through the port's presenter, so Esc closes it before the window (WindowTransients) and a click outside dismisses it.
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class CzWellPicker : Decorator
{
    private readonly Popup popup;
    private CzColorWell? open;

    internal GlassColorPicker Picker { get; } = new();
    internal CzColorWell? OpenWell => open;

    internal CzWellPicker()
    {
        popup = new Popup { Child = new Border { Padding = GlassColorPicker.CardInset, Child = Picker }, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true };
        Child = popup;
        Picker.Changed += (hex, _) => open?.SetColor(hex);
        popup.Closed += (_, _) => open = null; // a click outside: the popover has closed itself
        DetachedFromVisualTree += (_, _) => Close();
    }

    /// <summary>A click on a well: its picker opens below it, or closes if it is the one open; from the other well it moves.</summary>
    internal void Toggle(CzColorWell well)
    {
        if (open == well) { Close(); return; }
        Close(refocus: false);
        open = well;
        popup.PlacementTarget = well;
        Picker.Value = well.Hex;
        Presentation.SetPresented(popup, true, well, () => Close());
    }

    internal void Close(bool refocus = true)
    {
        if (open is not { } well) return;
        open = null;
        Presentation.SetPresented(popup, false, well, () => { });
        if (refocus) well.Focus(NavigationMethod.Directional);
    }
}
