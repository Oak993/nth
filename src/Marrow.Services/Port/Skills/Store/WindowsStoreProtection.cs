// PORT-ONLY: SK-5 real Windows ACL and alternate-data-stream implementation, PLAN 2.1.
namespace Marrow.Services.Port.Skills.Store;

public sealed class WindowsStoreProtection : IStoreProtection
{
    public void Lock(string folder) => throw new NotImplementedException();
    public bool IsLocked(string path) => throw new NotImplementedException();
    public bool HasNamedStreams(string path) => throw new NotImplementedException();
    public void Unlock(string path) => throw new NotImplementedException();
}
