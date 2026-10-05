// PORT-ONLY CZ-1: selection container for a required single-selection group.
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
namespace Marrow.UI.Port.Settings;

public sealed class CzRadioGroupPeer(CzRadioGroup owner) : ControlAutomationPeer(owner), ISelectionProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    public bool CanSelectMultiple => false;
    public bool IsSelectionRequired => owner.SelectedId.Length > 0;
    public IReadOnlyList<AutomationPeer> GetSelection() => owner.Buttons.Where(b => b.Picked).Select(b => ControlAutomationPeer.CreatePeerForElement(b)!).ToArray();
}
