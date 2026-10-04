// PORT-ONLY: SK-5 (DL-5), PLAN 2.1 Windows-only ACL and named-stream boundary. No portable successful default.
namespace Marrow.Services.Port.Skills.Store;

public interface IStoreProtection
{
    void Lock(string folder);
    bool IsLocked(string path);
    bool HasNamedStreams(string path);
    void Unlock(string path);
}
