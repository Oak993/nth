using System.Diagnostics;
using Marrow.Core.Support;

namespace Marrow.Setup.Tests.Fixtures;

/// <summary>
/// A temporary stand-in for <c>%LOCALAPPDATA%</c> and <c>%TEMP%</c> (two folders under a fresh folder in the real temp folder),
/// for the leftover sweeps (nth installer PLAN.md 5, unit U9). Nothing outside it is touched. <see cref="Hold"/> keeps a file
/// open without sharing, which is what a running engine, a loaded library or another setup's claim looks like to a sweep.
/// Disposing releases every hold and deletes the whole area, read-only files included.
/// </summary>
public sealed class SweepArea : IDisposable
{
    private readonly List<FileStream> holds = [];

    public SweepArea()
    {
        Base = Path.Combine(Path.GetTempPath(), "nth-tests", "sweep-" + Guid.NewGuid().ToString("N"));
        LocalAppData = Directory.CreateDirectory(Path.Combine(Base, "local")).FullName;
        Temp = Directory.CreateDirectory(Path.Combine(Base, "temp")).FullName;
    }

    /// <summary>The area itself; nothing a sweep may touch lies outside <see cref="LocalAppData"/> and <see cref="Temp"/> inside it.</summary>
    public string Base { get; }

    /// <summary>Stands in for <c>%LOCALAPPDATA%</c>.</summary>
    public string LocalAppData { get; }

    /// <summary>Stands in for <c>%TEMP%</c>.</summary>
    public string Temp { get; }

    /// <summary><c>nth Setup</c> under <see cref="LocalAppData"/>.</summary>
    public string SetupRoot => SetupFolders.Root(LocalAppData);

    /// <summary><c>nth Uninstall</c> under <see cref="Temp"/>.</summary>
    public string UninstallTemp => SetupFolders.UninstallTemp(Temp);

    /// <summary>Writes a file (making its folders) and returns its full path.</summary>
    public string Put(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Writes a file under <c>nth Setup</c> from path parts.</summary>
    public string PutInSetup(params string[] parts) => Put(Path.Combine([SetupRoot, .. parts]));

    /// <summary>Opens a file for writing with no sharing until the area is disposed (or <see cref="Release"/>): to a sweep it is in use.</summary>
    public FileStream Hold(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        holds.Add(stream);
        return stream;
    }

    /// <summary>Lets go of a hold.</summary>
    public void Release(FileStream stream)
    {
        holds.Remove(stream);
        stream.Dispose();
    }

    /// <summary>A directory junction at <paramref name="link"/> pointing to <paramref name="target"/> (no privilege needed).</summary>
    public static void MakeJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && Directory.Exists(link), "the test could not make a directory junction: " + said);
    }

    public void Dispose()
    {
        foreach (var stream in holds) stream.Dispose();
        holds.Clear();
        if (!Directory.Exists(Base)) return;
        foreach (var junction in Directory.EnumerateDirectories(Base, "*", SearchOption.AllDirectories).Where(d => new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList())
            if (Directory.Exists(junction)) Directory.Delete(junction, recursive: false); // the link, never what it points at
        foreach (var file in Directory.EnumerateFiles(Base, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Base, recursive: true);
    }
}
