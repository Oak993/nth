// OD-0099 Look Picks, Q-CZ-3 B: the square and the recent row as one named element with a value (CZ-15: every control has a name).
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace Marrow.UI.Port.Settings.ColorPicker;

internal sealed class PickerSurfacePeer(PickerSurface owner) : ControlAutomationPeer(owner), IValueProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => owner.Kind == PickerSurfaceKind.Recent ? AutomationControlType.List : AutomationControlType.Custom;
    public bool IsReadOnly => true;
    public string? Value => owner.ValueText;
    public void SetValue(string? value) { }
}
