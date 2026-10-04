using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Install;
internal sealed class FakeInstallLock : IInstallLock
{
    public HashSet<string> Locked { get; } = new(StringComparer.Ordinal);
    public void Lock(string path) => Locked.Add(path);
    public void Unlock(string path) => Locked.Remove(path);
    public void Renamed(string oldPath, string newPath) { if (Locked.Remove(oldPath)) Locked.Add(newPath); }
    public bool IsLocked(string path) => Locked.Contains(path);
}
