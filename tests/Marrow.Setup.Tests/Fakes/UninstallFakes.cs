using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>What the fake Update.exe does with the install root's own removal, which the real one schedules 3 s after it exits.</summary>
public enum RootRemoval
{
    /// <summary>The root is gone when Update.exe exits.</summary>
    AtExit,

    /// <summary>The root goes a moment after Update.exe exits (its delayed <c>cmd</c>).</summary>
    Later,

    /// <summary>The delayed removal never runs: <c>Update.exe</c> is left in an otherwise empty root.</summary>
    Never,

    /// <summary>A file in the root could not be deleted (in use): the root keeps it and <c>Update.exe</c>.</summary>
    FileInUse,
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 10, "Uninstall"; unit U6, ED-NEW-inst-u6-1). A fake <c>Update.exe --uninstall --silent</c>
/// acting out Velopack 1.2.161's uninstall (VELOPACK_CHECK.md check 7; uninstall.rs) on a temporary root: it stops nothing
/// (there is nothing running), deletes the shortcuts into the root when <see cref="RemovesShortcuts"/> (Velopack matches by
/// target), empties the root except Update.exe, deletes its uninstall key from the test registry, then removes the root as
/// <see cref="Removal"/> says, and exits with <see cref="ExitCode"/>. It never starts a process.
/// </summary>
public sealed class FakeUninstallRunner(FakeUserRegistry? registry = null, FakeShellLinks? shell = null) : IVelopackRunner
{
    public List<VelopackCommand> Started { get; } = [];

    public int ExitCode { get; set; }

    public bool Throws { get; set; }

    public bool RemovesShortcuts { get; set; }

    public bool RemovesKey { get; set; } = true;

    public RootRemoval Removal { get; set; } = RootRemoval.AtExit;

    /// <summary>How long after exit a <see cref="RootRemoval.Later"/> root goes.</summary>
    public TimeSpan Later { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Runs before the fake does anything (a test can look at the state Velopack would meet).</summary>
    public Action<VelopackCommand>? Before { get; set; }

    public VelopackRun Start(VelopackCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Throws) throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");
        Started.Add(command);
        Before?.Invoke(command);
        if (ExitCode != 0) return new VelopackRun(4242, Task.FromResult(ExitCode));
        var root = command.InstallRoot;
        if (RemovesShortcuts && shell is not null) new DesktopShortcut(shell, root).RemoveIntoRoot();
        string? held = null;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).ToList())
        {
            if (string.Equals(Path.GetFileName(entry), InstallLayout.UpdateExe, StringComparison.OrdinalIgnoreCase)) continue;
            if (Removal == RootRemoval.FileInUse && held is null && File.Exists(entry))
            {
                held = entry;
                continue;
            }
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
        if (RemovesKey) registry?.RemoveKey(UninstallEntry.KeyPath);
        switch (Removal)
        {
            case RootRemoval.AtExit:
                Directory.Delete(root, recursive: true);
                break;
            case RootRemoval.Later:
                _ = Task.Delay(Later).ContinueWith(_ =>
                {
                    try { Directory.Delete(root, recursive: true); }
                    catch (DirectoryNotFoundException) { } // already removed by the uninstaller's own check
                }, TaskScheduler.Default);
                break;
        }
        return new VelopackRun(4242, Task.FromResult(0));
    }
}

/// <summary>An in-memory Credential Manager: generic credentials by target name.</summary>
public sealed class FakeCredentialStore : ICredentialStore
{
    public HashSet<string> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Targets that refuse to be deleted.</summary>
    public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Filters { get; } = [];

    public IReadOnlyList<string> FindGeneric(string prefix)
    {
        Filters.Add(prefix);
        // CredEnumerateW's filter is not case-sensitive: the fake answers the same way
        return [.. Targets.Where(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];
    }

    public bool DeleteGeneric(string targetName)
    {
        if (Locked.Contains(targetName)) return false;
        Targets.Remove(targetName);
        return true;
    }
}

/// <summary>A running-nth handshake that answers from a script and records what it was asked.</summary>
public sealed class ScriptedRunningNth(RunningNthState answer, RunningNthState afterStop = RunningNthState.Closed) : IRunningNth
{
    public List<string> Calls { get; } = [];

    public RunningNthStatus Status { get; private set; } = RunningNthStatus.Initial;

    public event Action<RunningNthStatus>? StatusChanged;

    public Task<bool> IsNthOpenAsync(CancellationToken cancellationToken = default) => Task.FromResult(answer != RunningNthState.Clear);

    public Task<RunningNthStatus> AskAsync(CancellationToken cancellationToken = default) => Settle("ask", answer);

    public Task<RunningNthStatus> WaitForWorkToFinishAsync(CancellationToken cancellationToken = default) => Settle("wait", RunningNthState.Closed);

    public Task<RunningNthStatus> StopWorkAndQuitAsync(CancellationToken cancellationToken = default) => Settle("stop", afterStop);

    public Task<RunningNthStatus> EndItAsync(CancellationToken cancellationToken = default) => Settle("end", RunningNthState.Closed);

    public Task<RunningNthStatus> KeepWaitingAsync(CancellationToken cancellationToken = default) => Settle("keep", answer);

    public Task CancelAsync()
    {
        Calls.Add("cancel");
        return Task.CompletedTask;
    }

    private Task<RunningNthStatus> Settle(string call, RunningNthState state)
    {
        Calls.Add(call);
        Status = new RunningNthStatus(state, state == RunningNthState.Busy ? [new NthWorkItem("Fix the build", NthWorkKind.Thread, 42)] : [], state == RunningNthState.Busy ? 1 : 0);
        StatusChanged?.Invoke(Status);
        return Task.FromResult(Status);
    }
}

/// <summary>An install hold that a test can deny, and that counts how often it was let go.</summary>
public sealed class FakeHold : IDisposable
{
    public int Disposals { get; private set; }

    public void Dispose() => Disposals++;
}
