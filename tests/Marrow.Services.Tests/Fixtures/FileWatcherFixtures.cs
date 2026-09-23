using System.Globalization;
using Marrow.Core.Port;
using Marrow.Services.Port;

namespace Marrow.Services.Tests.Fixtures;

// Helpers for Port/FileWatcherTests.cs (R-TEST-3: helper types live in Fixtures/, never inside a test class).
// Not port code.

/// <summary>
/// A real temporary folder under MARROW_PORT_DATA_DIR (R-TEST-9: no file-system abstraction, nothing outside the
/// throwaway data folder). Deleted, read-only flags cleared first, when disposed.
/// </summary>
public sealed class ScratchFolder : IDisposable
{
    public ScratchFolder()
    {
        var dataDirectory = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        Root = dataDirectory.TrimEnd('\\', '/') + "\\FileWatcherTests\\" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(Root);
    }

    /// <summary>The folder's full path, with backslashes and no trailing separator.</summary>
    public string Root { get; }

    /// <summary><see cref="Root"/> joined with <paramref name="parts"/> by backslashes.</summary>
    public string Child(params string[] parts) => Root + "\\" + string.Join('\\', parts);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Already gone (a test deleted its root) or still held: the data folder is thrown away anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// What a watch handed its handler: every call, whether each ran where its source delivers (ED-116) outside any Swift
/// task, the paths in arrival order, and whether any batch said everything may have changed. A log for
/// <c>FileWatcher.WatchTree</c> is built with the SerialQueue the watch delivers on and records with <see cref="Record"/>;
/// a log for <c>FileWatcher.WatchDirectory</c> is built without one and records with <see cref="RecordCall"/>.
/// </summary>
public sealed class WatchLog(SerialQueue? queue = null)
{
    private readonly Lock gate = new();
    private readonly List<string> order = [];
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private int calls;
    private bool everything;
    private bool offTargetOrInTask;
    private bool emptyBatch;

    /// <summary>The handler for <c>FileWatcher.WatchTree</c>: checks <c>queue.IsCurrent &amp;&amp; !SwiftTask.IsInTask</c>.</summary>
    public void Record(FileWatcherBatch batch)
    {
        var onQueueOutsideTask = queue is not null && queue.IsCurrent && !SwiftTask.IsInTask;
        lock (gate)
        {
            calls++;
            if (!onQueueOutsideTask) offTargetOrInTask = true;
            if (batch.Everything) everything = true;
            if (batch.Paths.Count == 0 && !batch.Everything) emptyBatch = true;
            foreach (var path in batch.Paths)
            {
                order.Add(path);
                seen.Add(path);
            }
        }
    }

    /// <summary>The handler for <c>FileWatcher.WatchDirectory</c>: checks <c>MainActor.IsIsolated &amp;&amp; !SwiftTask.IsInTask</c>.</summary>
    public void RecordCall()
    {
        var onMainOutsideTask = queue is null && MainActor.IsIsolated && !SwiftTask.IsInTask;
        lock (gate)
        {
            calls++;
            if (!onMainOutsideTask) offTargetOrInTask = true;
        }
    }

    public int Calls
    {
        get { lock (gate) return calls; }
    }

    public bool Everything
    {
        get { lock (gate) return everything; }
    }

    /// <summary>Every call so far ran on the log's SerialQueue and outside any Swift task (a tree-watch log only).</summary>
    public bool AllOnTheirQueueOutsideAnyTask
    {
        get
        {
            if (queue is null) throw new InvalidOperationException("A directory-watch log has no queue; use AllOnMainOutsideAnyTask.");
            lock (gate) return !offTargetOrInTask;
        }
    }

    /// <summary>Every call so far ran on the main actor and outside any Swift task (a directory-watch log only).</summary>
    public bool AllOnMainOutsideAnyTask
    {
        get
        {
            if (queue is not null) throw new InvalidOperationException("A tree-watch log delivers on its queue; use AllOnTheirQueueOutsideAnyTask.");
            lock (gate) return !offTargetOrInTask;
        }
    }

    /// <summary>Some call carried no path and did not say everything.</summary>
    public bool AnyEmptyBatch
    {
        get { lock (gate) return emptyBatch; }
    }

    public bool Saw(string path)
    {
        lock (gate) return seen.Contains(path);
    }

    public bool SawAll(IEnumerable<string> paths)
    {
        lock (gate) return paths.All(seen.Contains);
    }

    /// <summary>Every delivered path so far, in arrival order (a copy).</summary>
    public IReadOnlyList<string> Paths
    {
        get { lock (gate) return [.. order]; }
    }

    /// <summary>Where <paramref name="path"/> first arrived, counting every delivered path; -1 if never.</summary>
    public int FirstIndex(string path)
    {
        lock (gate) return order.IndexOf(path);
    }

    /// <summary>Drops the paths kept so far (the overflow test's bursts would otherwise pile up).</summary>
    public void ForgetPaths()
    {
        lock (gate)
        {
            order.Clear();
            seen.Clear();
        }
    }
}
