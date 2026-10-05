using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Fixtures;
public sealed class OfferInstallLock : IInstallLock
{
    private readonly HashSet<string> locked = new(StringComparer.Ordinal);
    public void Lock(string path) => locked.Add(path);
    public void Unlock(string path) => locked.Remove(path);
    public bool IsLocked(string path) => locked.Contains(path);
    public void Renamed(string oldPath, string newPath) { if (locked.Remove(oldPath)) locked.Add(newPath); }
}
