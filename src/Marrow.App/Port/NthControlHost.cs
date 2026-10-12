using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a, unit U4 (ED-NEW-inst-u4-1). The app's side of the handshake with nth
// Setup. Velopack's Setup.exe stops every process in the install root when it updates, so the setup asks first, over the
// control pipe (Core/Support/NthControlServer.cs listens, this decides). Three things are asked:
//   status    who answers and what it is doing; changes nothing. The setup reads pid and exe from it to tell an nth inside the
//             folder it is about to replace from a portable or development copy elsewhere.
//   quit      if-idle: only when no thread is working (and no team merge is going out); otherwise "busy" with the work listed,
//             nothing changed. stop: now, the way closing the main window does (AppShell.Terminate: a merge is waited for, up
//             to AppDelegate.swift:44-54's four minutes; then applicationWillTerminate saves the library, every thread's
//             unsaved state and the settings, and stops the sessions, which is what makes a stop clean).
//   withdraw  the installer gave up: a quit it started that is still waiting on a merge is called off.
// "Running" is the app's own word for it: a thread whose runtime is not idle (ThreadRuntime.IsRunning, helpers and Hydra heads
// included, each its own thread), and a runtime whose team merge is going out (IsHydraMerging).

/// <summary>The app's answers to the installer's control pipe. Everything but <see cref="HandleAsync"/> runs on the main actor.</summary>
public sealed class NthControlHost : INthControlHost
{
    private readonly AppShell shell;
    private readonly Task launchReady;
    private readonly int processId;
    private readonly string executable;

    /// <param name="shell">The shell that quits the app (the same one the main window's close control uses).</param>
    /// <param name="launchReady">Completes when the app has finished launching; a request before then waits for it.</param>
    /// <param name="processId">This process (the test's own id by default).</param>
    /// <param name="executable">This process's file, full path.</param>
    public NthControlHost(AppShell shell, Task? launchReady = null, int? processId = null, string? executable = null)
    {
        this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
        this.launchReady = launchReady ?? Task.CompletedTask;
        this.processId = processId ?? Environment.ProcessId;
        this.executable = executable ?? Environment.ProcessPath ?? string.Empty;
    }

    public async Task<NthControlAnswer> HandleAsync(NthControlRequest request, CancellationToken cancellationToken)
    {
        await launchReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await MainActor.Run(() => Answer(request)).ConfigureAwait(false);
    }

    /// <summary>Decides one request. On the main actor.</summary>
    internal NthControlAnswer Answer(NthControlRequest request)
    {
        var work = RunningWork();
        switch (request.Command)
        {
            case NthControlCommand.Status:
                return new NthControlAnswer(Reply(NthControlReply.Status, work));
            case NthControlCommand.Withdraw:
                _ = shell.WithdrawInstallerQuit();
                return new NthControlAnswer(Reply(NthControlReply.Ok, []));
            default:
                if (request.Mode == NthQuitMode.IfIdle && work.Count > 0 && !shell.HasTerminated)
                    return new NthControlAnswer(Reply(NthControlReply.Busy, work));
                var merge = work.Any(item => item.Kind == NthWorkKind.Merge);
                return new NthControlAnswer(
                    Reply(NthControlReply.Ok, [], merge ? NthWorkKind.Merge : null),
                    () => MainActor.Post(shell.TerminateForInstaller));
        }
    }

    private NthControlResponse Reply(NthControlReply reply, IReadOnlyList<NthWorkItem> work, NthWorkKind? deferred = null) =>
        new(reply, processId, executable, work, work.Count, deferred);

    /// <summary>What is running now, longest first (ties by name, so a list does not shuffle between two asks).</summary>
    internal IReadOnlyList<NthWorkItem> RunningWork()
    {
        if (shell.Delegate.Model is not { } model) return [];
        var now = SwiftDate.Now;
        var items = new List<NthWorkItem>();
        foreach (var runtime in model.LiveRuntimes)
        {
            if (runtime.IsRunning) items.Add(Item(runtime, NthWorkKind.Thread, runtime.TurnStartedAt, now));
            if (runtime.IsHydraMerging) items.Add(Item(runtime, NthWorkKind.Merge, runtime.HydraMergeStartedAt, now));
        }
        return [.. items.OrderByDescending(item => item.Seconds).ThenBy(item => item.Name, StringComparer.Ordinal)];
    }

    private static NthWorkItem Item(ThreadRuntime runtime, NthWorkKind kind, DateTimeOffset? started, DateTimeOffset now)
    {
        var name = NthControl.CleanName(runtime.Thread?.Title ?? string.Empty);
        var seconds = started is { } since ? (int)Math.Clamp((now - since).TotalSeconds, 0, int.MaxValue) : 0;
        return new NthWorkItem(name.Length == 0 ? NthControl.UnnamedWork : name, kind, seconds);
    }
}
