// PORT-ONLY CZ-4 (customization-SPEC CZ-6.4): whether the Advanced cards are open. The open state is shared by the Border beam's and the Uiverse light's Advanced rows and is session state of the page (DESIGN cz.js:124, 586-593).
namespace Marrow.App.Port.Settings;

internal sealed class CzAdvancedState
{
    private bool open;

    internal bool Open
    {
        get => open;
        set
        {
            if (open == value) return;
            open = value;
            Changed?.Invoke();
        }
    }

    internal event Action? Changed;
}
