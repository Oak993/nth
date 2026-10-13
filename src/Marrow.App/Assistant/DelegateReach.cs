// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3 "globally, per project or per chat. It is off until the owner turns it on (first run, 2.6, or the chat's
// menu)"; unit AS-22; ED-NEW-dextra-as22-9). No Swift twin.
// Where the delegate answers: a chat's own choice from its menu beats everything; without one, "Everywhere" covers every chat and "This project
// only" the project the owner named (or, until he names one, the project Dextra's home thread works in). With everything at its default the delegate
// answers nowhere. Pure over the settings.
using Marrow.Core.Port;

namespace Marrow.App.Assistant;

public static class DelegateReach
{
    /// <summary>Whether the delegate answers in a chat of <paramref name="project"/>. <paramref name="homeProject"/> is the project "This project only" means until a project is named.</summary>
    public static bool IsOn(DextraPreferences preferences, Guid chat, Guid project, Guid? homeProject)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        foreach (var choice in preferences.DelegateChats)
            if (choice.Chat == chat) return choice.On;
        return preferences.Delegate switch
        {
            DelegateScope.Everywhere => true,
            DelegateScope.ThisProject => (preferences.DelegateProject ?? homeProject) is { } named && named == project,
            _ => false,
        };
    }

    /// <summary>The chat's own choice, or null when it made none (the scope decides).</summary>
    public static bool? ChatChoice(DextraPreferences preferences, Guid chat)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        foreach (var choice in preferences.DelegateChats)
            if (choice.Chat == chat) return choice.On;
        return null;
    }
}
