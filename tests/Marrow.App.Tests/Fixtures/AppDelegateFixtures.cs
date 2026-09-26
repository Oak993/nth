using System.Collections.Concurrent;
using System.Reflection;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.Services.Store;

namespace Marrow.App.Tests.Fixtures;

// Helpers for AppDelegateTests (App/AppDelegate.swift). Helper types live here, never inside a test class (R-TEST-3).

/// <summary>The NSApplication that asked applicationShouldTerminate(_:) (AppDelegate.swift:44): records every
/// reply(toApplicationShouldTerminate:) it is given (:51).</summary>
public sealed class RecordingTerminationSender : ITerminationSender
{
    public List<bool> Replies { get; } = [];

    public void Reply(bool toApplicationShouldTerminate) => Replies.Add(toApplicationShouldTerminate);
}

/// <summary>
/// A server process the MCP hub tracks (MCPHub.swift:26 track, :367-376 terminateAll), recording what the quit path
/// does to it, in <see cref="Calls"/> and, as "name:call", in a <c>log</c> several processes may share.
/// <see cref="ExitsOnTerminate"/> false stands for a server that ignores the graceful step; the kill always ends it.
/// <c>onTerminate</c> runs inside the graceful step, so a test can look at the world at that instant.
/// </summary>
public sealed class RecordingServerProcess(string name, List<string>? log = null, Action? onTerminate = null) : IProcessControl
{
    public List<string> Calls { get; } = [];

    public bool ExitsOnTerminate { get; init; } = true;

    public bool IsRunning { get; private set; } = true;

    private void Note(string call)
    {
        Calls.Add(call);
        log?.Add(name + ":" + call);
    }

    public void Terminate()
    {
        Note("terminate");
        onTerminate?.Invoke();
        if (ExitsOnTerminate) IsRunning = false;
    }

    public void KillTree()
    {
        Note("killTree");
        IsRunning = false;
    }

    public void KillProcess()
    {
        Note("killProcess");
        IsRunning = false;
    }
}

/// <summary>
/// A fresh TokenLedger for one test. TokenLedger.Shared's process default (TokenLedger.cs, `static let shared`) is
/// made once per process, so a test that needs a ledger whose live-spend state it controls makes its own through the
/// private init (TokenLedger.swift:99-103) and puts it in TokenLedger.Shared for its own async flow. Call on main,
/// with Storage.Locations and UserDefaults already bound to throwaway places (the init reads both).
/// </summary>
public static class FreshTokenLedger
{
    public static TokenLedger Make() =>
        (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!;

    /// <summary>TokenLedger.swift:356: the live-spend state file under the store root.</summary>
    public static string LiveSpendFile => Path.Combine(Storage.Root, "token-activity-live-v2.json");
}

/// <summary>
/// The order in which file names in one folder are written, as the file system reports it (ReadDirectoryChangesW
/// through FileSystemWatcher, which raises its events in the order the changes happened). Only the folder itself is
/// watched, not its subfolders.
/// </summary>
public sealed class FolderWriteOrder : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly ConcurrentQueue<string> names = new();
    private readonly SemaphoreSlim arrived = new(0);

    public FolderWriteOrder(string folder)
    {
        watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024,
        };
        watcher.Created += (_, e) => Note(e.Name);
        watcher.Changed += (_, e) => Note(e.Name);
        watcher.Renamed += (_, e) => Note(e.Name);
        watcher.EnableRaisingEvents = true;
    }

    private void Note(string? name)
    {
        if (name is null) return;
        names.Enqueue(name);
        arrived.Release();
    }

    /// <summary>The names seen so far, once every one of <paramref name="expected"/> has been seen at least once; fails
    /// when no further change arrives within <paramref name="bound"/>.</summary>
    public IReadOnlyList<string> WaitFor(TimeSpan bound, params string[] expected)
    {
        while (true)
        {
            var seen = names.ToArray();
            if (expected.All(seen.Contains)) return seen;
            if (!arrived.Wait(bound))
                throw new TimeoutException("Saw only " + string.Join(", ", seen) + "; waiting for " + string.Join(", ", expected) + ".");
        }
    }

    public void Dispose()
    {
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
        arrived.Dispose();
    }
}

/// <summary>
/// Test-only cleanup for UpdateChecker.StartBackgroundChecks(): the loop it spawns has no stop method in production
/// (UpdateChecker.swift:132-150, PR-UPDATE-010), so a test that starts it cancels the TaskHandle directly before its
/// TestRuntime is disposed (the same helper UpdateCheckerSecondTests keeps privately).
/// </summary>
public static class UpdateCheckerLoop
{
    public static void Stop(UpdateChecker checker)
    {
        var field = typeof(UpdateChecker).GetField("backgroundTask", BindingFlags.NonPublic | BindingFlags.Instance)!;
        (field.GetValue(checker) as TaskHandle)?.Cancel();
    }
}
