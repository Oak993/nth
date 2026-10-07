using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Custom;

/// <summary>Copies user input without running it. Rejects reparse points throughout traversal and publishes beside the registry by rename.</summary>
public sealed class DiskCustomSourceFiles : ICustomSourceFiles
{
    public Task<IReadOnlyDictionary<string, byte[]>> ReadFolder(string path, CancellationToken cancel) => Task.Run<IReadOnlyDictionary<string, byte[]>>(() => ReadTree(Path.GetFullPath(path), cancel), cancel);
    public Task<byte[]> ReadZip(string path, CancellationToken cancel) => Task.Run(() => ReadBounded(Path.GetFullPath(path), cancel), cancel);
    public byte[]? ReadRegistry(string root)
    {
        var path = Path.Combine(root, "registry.json");
        StorePaths.EnsureAncestors(path);
        return File.Exists(path) ? ReadBounded(path, CancellationToken.None) : null;
    }
    public IReadOnlyDictionary<string, byte[]> ReadInstalled(string root, string id) => ReadTree(Item(root, id), CancellationToken.None);
    public void Publish(string root, string id, IReadOnlyDictionary<string, byte[]> files)
    {
        var target = Item(root, id);
        StorePaths.EnsureAncestors(target);
        var staging = target + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (relative, bytes) in CustomSourcePaths.Validate(files))
            {
                var path = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                StorePaths.EnsureAncestors(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            Directory.Move(staging, target);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    public void Delete(string root, string id)
    {
        var path = Item(root, id);
        StorePaths.EnsureAncestors(path);
        if (Directory.Exists(path))
        {
            _ = ReadTree(path, CancellationToken.None); // refuse traversal through a changed link
            Directory.Delete(path, true);
        }
    }
    public void SaveRegistry(string root, byte[] bytes)
    {
        var target = Path.Combine(root, "registry.json");
        StorePaths.EnsureAncestors(target);
        Directory.CreateDirectory(root);
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Item(string root, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid own item id.");
        return Path.Combine(root, "items", id);
    }
    private static Dictionary<string, byte[]> ReadTree(string root, CancellationToken cancel)
    {
        StorePaths.EnsureAncestors(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(root);
        long size = 0;
        var entries = 0;
        while (queue.TryDequeue(out var folder))
        {
            cancel.ThrowIfCancellationRequested();
            StorePaths.EnsureAncestors(folder);
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                if (++entries > CustomSourcePaths.MaxFiles * 2) throw new InvalidDataException("Custom source exceeds the traversal limit.");
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                CustomSourcePaths.Require(relative);
                StorePaths.EnsureAncestors(path);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Custom folders cannot contain links.");
                if ((attributes & FileAttributes.Directory) != 0) { queue.Enqueue(path); continue; }
                var length = new FileInfo(path).Length;
                size = checked(size + length);
                if (size > CustomSourcePaths.MaxBytes || result.Count >= CustomSourcePaths.MaxFiles) throw new InvalidDataException("Custom source exceeds the file or byte limit.");
                result.Add(relative, ReadBounded(path, cancel));
            }
        }
        return CustomSourcePaths.Validate(result);
    }
    private static byte[] ReadBounded(string path, CancellationToken cancel)
    {
        StorePaths.EnsureAncestors(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            if (output.Length + read > CustomSourcePaths.MaxBytes) throw new InvalidDataException("Custom source exceeds the byte limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
