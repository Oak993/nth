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
    public string PutInSetup(params string[] parts)
    {
        if (parts.Length >= 2)
        {
            if (parts[0] == SetupFolders.RuntimeFolderName && parts.Length >= 3) MarkOwnedSetup(parts[0], parts[1]);
            else if (parts[0] != SetupFolders.RuntimeFolderName) MarkOwnedSetup(parts[0]);
        }
        return Put(Path.Combine([SetupRoot, .. parts]));
    }

    /// <summary>Seeds provenance only for fixtures expressly representing nth-created units, before payload.</summary>
    public void MarkOwnedSetup(params string[] parts)
    {
        MarkOwnedFolder(SetupRoot);
        if (parts.Length > 1 && parts[0] == SetupFolders.RuntimeFolderName) MarkOwnedFolder(Path.Combine(SetupRoot, parts[0]));
        MarkOwnedFolder(Path.Combine([SetupRoot, .. parts]));
    }

    public string PutInUninstall(string name)
    {
        MarkOwnedFolder(UninstallTemp);
        return Put(Path.Combine(UninstallTemp, name));
    }

    private void MarkOwnedFolder(string directory)
    {
        RequireWithinBase(directory);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".nth-setup-owner"), "nth Setup owned unit v1\n");
    }

    private void RequireWithinBase(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(Base) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture cleanup/write escaped its disposable base.");
    }

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
        if (Directory.Exists(Base)) DeleteDisposableTree(Base, isRoot: true);
    }

    private void DeleteDisposableTree(string directory, bool isRoot = false)
    {
        if (!isRoot) RequireWithinBase(directory);
        else if (Path.GetFullPath(directory) != Path.GetFullPath(Base)) throw new InvalidOperationException("Invalid disposable root.");
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(directory, recursive: false); return; }
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RequireWithinBase(entry);
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0) DeleteDisposableTree(entry);
            else
            {
                if ((attributes & FileAttributes.ReparsePoint) == 0) File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }
        File.SetAttributes(directory, FileAttributes.Normal);
        Directory.Delete(directory, recursive: false);
    }
}