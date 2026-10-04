// PORT-ONLY CZ-1: small switch and Auto capsule toggle pattern.
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
namespace Marrow.UI.Port.Settings;

public sealed class CzTogglePeer(CzAutoButton owner) : ControlAutomationPeer(owner), IToggleProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;
    public ToggleState ToggleState => owner.Checked ? ToggleState.On : ToggleState.Off;
    public void Toggle() => owner.Activate();
}
