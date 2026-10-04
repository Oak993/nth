// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

/// <summary>SK-5 can bind the store lock. The default fails closed, including on Linux.</summary>
public interface IInstallLock
{
    void Lock(string path);
    void Unlock(string path);
    bool IsLocked(string path);
    void Renamed(string oldPath, string newPath) { }
}
