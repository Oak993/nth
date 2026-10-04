// PORT-ONLY CZ-1: forwarded toggle pattern; the real small SettingsSwitch owns its state.
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
namespace Marrow.UI.Port.Settings;

public sealed class CzSwitchPeer(CzSwitch owner) : ControlAutomationPeer(owner), IToggleProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;
    public ToggleState ToggleState => owner.Checked ? ToggleState.On : ToggleState.Off;
    public void Toggle() => owner.Toggle();
}
