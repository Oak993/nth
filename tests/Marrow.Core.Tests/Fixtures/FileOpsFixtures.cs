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
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root)) root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marrow-port-tests");
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

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(FullPath)) return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(FullPath, "*", SearchOption.AllDirectories))
                File.SetAttributes(entry, FileAttributes.Normal);
            Directory.Delete(FullPath, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
