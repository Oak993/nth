// PORT-ONLY CZ-1: menu button pattern (CZ-12.12.1).
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
namespace Marrow.UI.Port.Settings;

public sealed class CzMenuButtonPeer(CzMenuButton owner) : ControlAutomationPeer(owner), IExpandCollapseProvider, IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    public bool ShowsMenu => true;
    public ExpandCollapseState ExpandCollapseState => owner.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    public void Expand() => owner.OpenMenu();
    public void Collapse() => owner.CloseMenu();
    public void Invoke() => owner.Activate();
}
