// PORT-ONLY CZ-1: Advanced expand/collapse pattern (CZ-15).
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
namespace Marrow.UI.Port.Settings;

public sealed class CzAdvancedPeer(CzAdvancedButton owner) : ControlAutomationPeer(owner), IExpandCollapseProvider, IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    public ExpandCollapseState ExpandCollapseState => owner.Open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    public bool ShowsMenu => false;
    public void Expand() { if (owner.IsEffectivelyEnabled) owner.Open = true; }
    public void Collapse() { if (owner.IsEffectivelyEnabled) owner.Open = false; }
    public void Invoke() => owner.Activate();
}
