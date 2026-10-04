using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Store;
namespace Marrow.Services.Tests.Port.Skills.Pipelines;
internal sealed class PipelineProtection : IStoreProtection, IInstallLock
{
    private readonly HashSet<string> roots = new(StringComparer.Ordinal);
    public void Lock(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        roots.Add(path);
    }
    public void Unlock(string path) => roots.Remove(path);
    public bool IsLocked(string path) => roots.Any(r => path == r || path.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    public bool HasNamedStreams(string path) => false;
    public void Renamed(string oldPath, string newPath) { if (roots.Remove(oldPath)) roots.Add(newPath); }
}
