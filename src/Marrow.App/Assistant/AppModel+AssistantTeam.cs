// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 2.1 "Sidebar", 9.4; ED-NEW-dextra-as17-6, -7). No Swift twin.
// The app's side of the Asturs: opening one from the sidebar (its own thread, else the Team tab), and asking the home's Team tab to show one ("Show in Team"). The request is kept until the home takes
// it, so it works whether the home is already on screen or is built for it.
using Marrow.App.Assistant;

namespace Marrow.App;

public sealed partial class AppModel
{
    private string? pendingTeamFocus;

    /// <summary>The assistant a sidebar row asked the home's Team tab to show; the home takes it with <see cref="TakeTeamFocus"/>.</summary>
    public string? PendingTeamFocus => pendingTeamFocus;

    /// <summary>Raised when a request for the Team tab is waiting.</summary>
    public event Action? TeamFocusRequested;

    /// <summary>Opens the home with the Team tab showing this assistant.</summary>
    public void ShowTeamOf(string assistantId)
    {
        pendingTeamFocus = assistantId;
        ShowAssistantHome();
        TeamFocusRequested?.Invoke();
    }

    /// <summary>Takes the waiting request, if there is one.</summary>
    public string? TakeTeamFocus()
    {
        var id = pendingTeamFocus;
        pendingTeamFocus = null;
        return id;
    }

    /// <summary>An Astur's row was pressed: its own thread opens in the main area, or, with none yet, the Team tab shows it.</summary>
    public void OpenAstur(string assistantId)
    {
        if (AssistantHost.Profiles().FirstOrDefault(p => p.Id == assistantId) is { HomeThread: { } thread } && Thread(thread) is not null)
        {
            SelectedThreadID = thread;
            return;
        }
        ShowTeamOf(assistantId);
    }
}
