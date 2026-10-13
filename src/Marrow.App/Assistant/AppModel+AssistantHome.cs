// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.1 the sidebar row opens the home, 2.2 the home in the main area, 5.4 "Hide from
// the Assistant"; ED-NEW-dextra-as8-8). No Swift twin.
// The app's side of the Assistant home: the one Dextra host (settings, identity, controller), whether the home is in the
// main area (picking a chat leaves it), and a chat's "Hide from the Assistant".
using Marrow.App.Assistant;
using Marrow.Core.Port;

namespace Marrow.App;

public sealed partial class AppModel
{
    private DextraHost? assistantHost;
    private bool assistantHomeShown;

    /// <summary>Dextra's host: its settings (the "dextra" key), identity and controller. Made on first use.</summary>
    public DextraHost AssistantHost
    {
        get => assistantHost ??= new DextraHost(this, new DextraSettings(UserDefaults.Standard.Current));
        internal set
        {
            if (ReferenceEquals(assistantHost, value)) return;
            assistantHost?.Dispose();
            assistantHost = value;
        }
    }

    /// <summary>The Assistant home is in the main area (the sidebar row is selected). Selecting a chat clears it.</summary>
    public bool AssistantHomeShown
    {
        get => Read(assistantHomeShown);
        set => Set(ref assistantHomeShown, value);
    }

    /// <summary>Opens the home in the main area (the sidebar row's click and its menu's Open).</summary>
    public void ShowAssistantHome()
    {
        SelectedThreadID = null;
        AssistantHomeShown = true;
    }

    /// <summary>
    /// "Hide from the Assistant" (5.4): the chat sends no text to the feed from now on, and drafts of it still waiting lose
    /// their words. A mission chat cannot be hidden (it is the Assistant's own work).
    /// </summary>
    public void SetHiddenFromAssistant(Guid thread, bool hidden)
    {
        if (Thread(thread) is not { } chat || chat.HiddenFromAssistant == hidden || AssistantChats.IsAssistants(chat)) return;
        UpdateThread(thread, t => t with { HiddenFromAssistant = hidden });
        if (hidden) AssistantEvents.Hide(thread);
    }

    private void DisposeAssistantHost()
    {
        assistantHost?.Dispose();
        assistantHost = null;
    }
}
