// PORT-ONLY: SK-5, PLAN 2.1 GC: one immediate deletion attempt and no following reparse points.
namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreDeletion : IStoreDeletion
{
    public static StoreDeletion Instance { get; } = new();

    public void Delete(string folder)
    {
        StorePaths.EnsureAncestors(Path.GetDirectoryName(folder)!);
        if (!Path.Exists(folder)) return;
        var attributes = File.GetAttributes(folder);
        if ((attributes & FileAttributes.Directory) == 0) File.Delete(folder);
        else if ((attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(folder);
        else
        {
            // Do not use recursive deletion on attacker-planted links. Remove each leaf explicitly.
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder)) Delete(entry);
            Directory.Delete(folder);
        }
    }
}
