// PORT-ONLY CZ-1: menuitemradio exposes checked state and the menu-item role.
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
namespace Marrow.UI.Port.Settings;

public sealed class CzMenuItemPeer(CzMenuItem owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.MenuItem;
    public bool IsSelected => owner.Checked;
    public ISelectionProvider? SelectionContainer => null;
    public void Select() => owner.Menu.Select(owner.Id);
    public void AddToSelection() => Select();
    public void RemoveFromSelection() { } // Required single selection is replaced only by another pick.
    public void Invoke() => Select();
}
