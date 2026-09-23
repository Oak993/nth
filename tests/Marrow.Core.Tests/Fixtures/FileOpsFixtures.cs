using System.Diagnostics;
using Marrow.Testing;

namespace Marrow.Core.Tests.Fixtures;

// Support for the PA-0015 file-operation tests (FileOpsTests). Helper types live in fixture files, never inside a
// test class (R-TEST-3). Folders are real and live under MARROW_PORT_DATA_DIR (R-TEST-9, R-FS-1): no file-system
// abstraction. A lingering handle is a real FileStream held open by the test (R-TEST-9).

/// <summary>A fresh real folder under MARROW_PORT_DATA_DIR for one test, removed afterwards.</summary>
public sealed class ScratchFolder : IDisposable
{
    public ScratchFolder()
    {
        // R-TEST-9: the variable is set before anything runs. No fallback folder is invented when it is missing.
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("MARROW_PORT_DATA_DIR is not set; R-TEST-9 requires it before any test runs");
        FullPath = System.IO.Path.Combine(root, "FileOpsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    /// <summary>The path of <paramref name="relative"/> inside this folder (not created).</summary>
    public string PathOf(string relative) => System.IO.Path.Combine(FullPath, relative);

    /// <summary>Writes a starting file with the plain BCL call; the file under test is written by FileOps.</summary>
    public string Seed(string relative, byte[] bytes)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>The names of every entry directly inside <paramref name="relative"/>, ordinal order.</summary>
    public string[] Names(string relative = "") =>
        Directory.EnumerateFileSystemEntries(PathOf(relative))
            .Select(p => System.IO.Path.GetFileName(p))
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// A handle like the one a child process leaves behind (PA-0015): open for reading, sharing read only, so a
    /// write, rename, replace or delete of the file fails with a sharing violation until it is disposed.
    /// </summary>
    public static FileStream HoldOpen(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>
    /// A reader that shares everything (read, write and delete), the way Node and git open files. It does not block
    /// an in-place write, so it tells a real replace (the handle keeps the old bytes) from an overwrite (the handle
    /// sees the new ones).
    /// </summary>
    public static FileStream HoldOpenSharingAll(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>Everything <paramref name="stream"/> reads from position 0.</summary>
    public static byte[] ReadFromStart(FileStream stream)
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Makes the folder <paramref name="linkRelative"/> a junction to <paramref name="targetRelative"/> with the
    /// system's <c>mklink /J</c>, which needs no privilege (R-TEST-10: a system binary, no network).
    /// </summary>
    public string Junction(string linkRelative, string targetRelative)
    {
        var link = PathOf(linkRelative);
        var start = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, PathOf(targetRelative) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(ManualClockWait.Guard), "mklink did not finish");
        Assert.True(process.ExitCode == 0, "mklink /J failed: " + output);
        Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), "the link is not a reparse point");
        return link;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(FullPath)) RemoveTree(FullPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Removes a folder without ever following a junction or link inside it: a link is removed as itself, so the
    // clean-up cannot clear attributes on, or delete, anything the link points at.
    private static void RemoveTree(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (attributes.HasFlag(FileAttributes.Directory)) Directory.Delete(entry);
                else File.Delete(entry);
            }
            else if (attributes.HasFlag(FileAttributes.Directory))
            {
                RemoveTree(entry);
            }
            else
            {
                File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }
        File.SetAttributes(directory, FileAttributes.Directory);
        Directory.Delete(directory);
    }
}

/// <summary>
/// Watching a blocking FileOps call that runs on another thread while the test drives the manual clock. The only
/// real-time waits are bounded guards against a hang, never a sleep that stands in for work (R-TEST-5).
/// </summary>
public static class ManualClockWait
{
    public static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    /// <summary>Waits until <paramref name="operation"/> has failed an attempt and armed one retry timer.</summary>
    public static void ForRetryTimer(ManualTimeProvider time, Task operation)
    {
        var seen = SpinWait.SpinUntil(() => time.ActiveTimers == 1 || operation.IsCompleted, Guard);
        Assert.True(seen, "the operation neither finished nor armed a retry timer on the manual clock");
        Assert.False(operation.IsCompleted,
            "the operation finished instead of waiting to retry: " + operation.Exception?.InnerException?.Message);
        Assert.Equal(1, time.ActiveTimers);
    }
}
