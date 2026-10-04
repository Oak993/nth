// PORT-ONLY CZ-1: invoke semantics for the custom capsule.
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
namespace Marrow.UI.Port.Settings;

public sealed class CzButtonPeer(CzButton owner) : ControlAutomationPeer(owner), IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    public void Invoke() => owner.Activate();
}
