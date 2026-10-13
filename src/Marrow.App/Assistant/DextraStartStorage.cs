// PORT-ONLY: DESIGN.md 9.1, 15.3; ED-NEW-dextra-open-5.
// Opened on a worker; ownership transfers once to the shared host on MainActor.
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed record DextraStartStorage(IAssistantStorageRoots Roots, AssistantStore Store, AuditLog Audit, AssistantTeam Team) : IDisposable
{
    public static DextraStartStorage Open(BackgroundStartSeams seams, string rootId)
    {
        var roots = seams.Roots();
        var store = AssistantStore.Open(roots, new AssistantStoreOptions { InterruptedMission = MissionJournal.Interrupted });
        AuditLog? audit = null;
        try
        {
            audit = AuditLog.Open(roots, seams.Keys());
            return new(roots, store, audit, AssistantTeam.OpenForHost(store, rootId));
        }
        catch { audit?.Dispose(); store.Dispose(); throw; }
    }
    public void Dispose() { Team.Dispose(); Audit.Dispose(); Store.Dispose(); }
}
