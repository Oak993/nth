// PORT-ONLY: SK-5 (DL-5), installed items, damage/Repair, leases, GC and teardown. Provider wiring belongs to SK-9/SK-11/SK-13.
using Marrow.Core.Port.Skills.State;

namespace Marrow.Services.Port.Skills.Store;

public sealed class SkillsStore
{
    public SkillsStore(StorePaths paths, IEnumerable<StoreItem> items, AddonsState state, IStoreProtection? protection = null,
        IStoreDeletion? deletion = null, IStoreRepairDownloader? downloader = null, IStoreRepairInstaller? installer = null) => throw new NotImplementedException();
    public AddonsState State => throw new NotImplementedException();
    public StoreLease? Acquire(string id) => throw new NotImplementedException();
    public StoreLease? AcquireGeneration(string folder, StoreManifest manifest) => throw new NotImplementedException();
    public void VerifyAfterAppUpdate() => throw new NotImplementedException();
    public Task<bool> RepairAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public void Retire(string folder) => throw new NotImplementedException();
    public void Collect() => throw new NotImplementedException();
    public void Teardown() => throw new NotImplementedException();
}
