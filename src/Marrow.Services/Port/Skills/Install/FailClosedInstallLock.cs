// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

internal sealed class FailClosedInstallLock : IInstallLock
{
    public void Lock(string path) => throw new InvalidOperationException("Installer lock seam is not bound.");
    public void Unlock(string path) => throw new InvalidOperationException("Installer lock seam is not bound.");
    public bool IsLocked(string path) => throw new InvalidOperationException("Installer lock seam is not bound.");
}
