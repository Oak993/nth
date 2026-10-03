using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

/// <summary>ED-252: a GPU program cache folder of its own (GpuProgramCache's folder and root pointed at it, restored on
/// Dispose), with the cache's private entry points (ANGLE's blob-cache callbacks) at hand.</summary>
public sealed class GpuProgramCacheFolder : IDisposable
{
    private static readonly System.Reflection.FieldInfo folderField = typeof(GpuProgramCache).GetField("folder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    private readonly object? saved = folderField.GetValue(null);
    private readonly nint key = System.Runtime.InteropServices.Marshal.AllocHGlobal(8), value = System.Runtime.InteropServices.Marshal.AllocHGlobal(64);
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "jewel-cache-" + Guid.NewGuid().ToString("N"));
    public string Folder => Path.Combine(Root, "0123456789abcdef01234567");

    public GpuProgramCacheFolder()
    {
        Directory.CreateDirectory(Folder);
        System.Runtime.InteropServices.Marshal.Copy(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, key, 8);
        System.Runtime.InteropServices.Marshal.Copy(new byte[64], 0, value, 64);
        folderField.SetValue(null, Folder);
        GpuProgramCache.FolderOverride = Root;
    }

    private static object? Call(string name, params object[] args) =>
        typeof(GpuProgramCache).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, args);

    public string FileFor() => (string)Call("FileFor", key, (nint)8)!;
    public nint Take(bool buffer, int size) => (nint)Call("Take", key, (nint)8, buffer ? value : (nint)0, (nint)size)!;
    public byte[] Taken(int count) { var bytes = new byte[count]; System.Runtime.InteropServices.Marshal.Copy(value, bytes, 0, count); return bytes; }

    public void Put(byte[] bytes)
    {
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, value, bytes.Length);
        Call("Put", key, (nint)8, value, (nint)bytes.Length);
    }

    public void Dispose()
    {
        folderField.SetValue(null, saved);
        GpuProgramCache.FolderOverride = null;
        System.Runtime.InteropServices.Marshal.FreeHGlobal(key);
        System.Runtime.InteropServices.Marshal.FreeHGlobal(value);
        Directory.Delete(Root, recursive: true);
    }
}
