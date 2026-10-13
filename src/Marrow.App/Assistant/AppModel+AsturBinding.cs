// PORT-ONLY: DESIGN.md 2.2, 9.1, 9.3; ED-NEW-dextra-open-5 (LEAD-REVIEW).
// Both starts read the same team. A saved root wins over a newly generated settings identity.
using Marrow.App.Assistant;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App;

public sealed partial class AppModel
{
    private AssistantTeam? ownedAsturs;

    internal AssistantTeam BindAsturs(AssistantStore store)
    {
        if (ownedAsturs is { } existing) return existing;
        var settings = AssistantHost.Settings;
        var team = AssistantTeam.OpenForHost(store, settings.EnsureId().ToString("N"));
        if (team.Root is not { } root || !Guid.TryParse(root.Id, out var id))
        { team.Dispose(); throw new IOException("Dextra's saved root has no usable chat identity."); }
        return AdoptAsturs(team);
    }

    internal AssistantTeam AdoptAsturs(AssistantTeam team)
    {
        if (team.Root is not { } root || !Guid.TryParse(root.Id, out var id))
        { team.Dispose(); throw new IOException("Dextra's saved root has no usable chat identity."); }
        var settings = AssistantHost.Settings;
        var previous = settings.Value.Id;
        AssistantHost.AdoptIdentity(id);
        if (previous is { } old && old != id)
            foreach (var thread in Threads.Where(t => t.AssistantID == old && t.MissionID is null).ToArray())
                UpdateThread(thread.Id, value => value with { AssistantID = id });
        return ownedAsturs = team;
    }

    private void DisposeAsturs() { ownedAsturs?.Dispose(); ownedAsturs = null; }
}
