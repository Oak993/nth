// PORT-ONLY: SK-5 a locked verified repair waiting for the old folder's leases/handles, never handed to a session.
namespace Marrow.Services.Port.Skills.Store;

internal sealed record PendingStoreRepair(StoreItem Item, string PreparedFolder);
