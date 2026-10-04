// PORT-ONLY CZ-1: checked radio option, CZ-15.
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
namespace Marrow.UI.Port.Settings;

public sealed class CzRadioButtonPeer(CzRadioButton owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.RadioButton;
    public bool IsSelected => owner.Picked;
    public ISelectionProvider? SelectionContainer => ControlAutomationPeer.CreatePeerForElement(owner.Group) as ISelectionProvider;
    public void AddToSelection() => Select();
    public void RemoveFromSelection() { } // Required radio selection cannot be removed without another pick.
    public void Select() => owner.Group.Select(owner.Id);
    public void Invoke() => Select();
}
