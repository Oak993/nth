// PORT-ONLY: SK-5, PLAN 2.1 lock/unlock order. Enumeration includes hidden/system entries, never follows a link.
namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreTrees(IStoreProtection protection)
{
    public static IEnumerable<string> Entries(string folder)
    {
        if ((File.GetAttributes(folder) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory) yield break;
        foreach (var path in Directory.EnumerateFileSystemEntries(folder))
        {
            yield return path;
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
                foreach (var child in Entries(path)) yield return child;
        }
    }

    public void Lock(string folder)
    {
        StorePaths.EnsureAncestors(folder);
        var entries = Entries(folder).Prepend(folder).ToArray();
        // Inspect the entire tree before changing anything; a reparse point is never locked or followed.
        if (entries.Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("A locked store tree cannot contain a reparse point.");
        foreach (var path in entries)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReadOnly)) == 0) File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
        }
        protection.Lock(folder);
    }

    public void Unlock(string folder)
    {
        StorePaths.EnsureAncestors(Path.GetDirectoryName(folder)!);
        if (!Path.Exists(folder)) return;
        var entries = Entries(folder).Prepend(folder).ToArray();
        // Every ACL first, before any attribute change, so inherited WriteAttributes deny entries are gone.
        foreach (var path in entries)
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) protection.Unlock(path);
        foreach (var path in entries)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) == 0 && (attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }
}
