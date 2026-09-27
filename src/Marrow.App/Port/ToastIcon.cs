// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :15.
// PORT-ONLY ED-220 (2): IconUri must name a shell-readable app icon. The embedded Avalonia PNG is materialized
// by production startup; tests use an in-memory file seam and never touch the installed app's data folder.
namespace Marrow.App.Port;

public interface IToastIconFiles
{
    byte[]? Read(string path);
    void Write(string path, byte[] bytes);
}

public static class ToastIcon
{
    public static string Install(IToastIconFiles files, string path, byte[] png)
    {
        if (files.Read(path) is not { } existing || !existing.AsSpan().SequenceEqual(png)) files.Write(path, png);
        return path;
    }
}

public sealed class WindowsToastIconFiles : IToastIconFiles
{
    public byte[]? Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    public void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
