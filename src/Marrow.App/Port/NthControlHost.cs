using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App.Port;

// PORT-ONLY: installer handshake; ED-NEW-inst-c7-5 reserves admission and checks idle twice.
public sealed class NthControlHost : INthControlHost
{
    private readonly AppShell shell;
    private readonly Task launchReady;
    private readonly int processId;
    private readonly string executable;
    private readonly HashSet<NthInstallerQuit> idleQuits = [];

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

    internal NthControlAnswer Answer(NthControlRequest request)
    {
        var work = RunningWork();
        switch (request.Command)
        {
            case NthControlCommand.Status:
                return new NthControlAnswer(Reply(NthControlReply.Status, work));
            case NthControlCommand.Withdraw:
                foreach (var quit in idleQuits.ToArray()) Abort(quit);
                _ = shell.WithdrawInstallerQuit();
                return new NthControlAnswer(Reply(NthControlReply.Ok, []));
            default:
                if (!shell.HasTerminated)
                {
                    if (request.Mode == NthQuitMode.IfIdle && work.Count > 0 || shell.Delegate.Model?.HasUndurableInstallerTurns == true)
                        return new NthControlAnswer(Reply(NthControlReply.Busy, work));
                    var quit = new NthInstallerQuit { NeedsIdle = request.Mode == NthQuitMode.IfIdle };
                    idleQuits.Add(quit);
                    shell.Delegate.Model?.HoldInstallerTurns(quit);
                    var deferred = work.Any(item => item.Kind == NthWorkKind.Merge) ? NthWorkKind.Merge : (NthWorkKind?)null;
                    return new NthControlAnswer(Reply(NthControlReply.Ok, [], deferred) with { CommitRequired = true },
                        PrepareCommit: token => Prepare(quit, token), Abort: () => MainActor.Post(() => Abort(quit)));
                }
                var merge = work.Any(item => item.Kind == NthWorkKind.Merge);
                return new NthControlAnswer(Reply(NthControlReply.Ok, [], merge ? NthWorkKind.Merge : null),
                    () => MainActor.Post(shell.TerminateForInstaller));
        }
    }

    private void Abort(NthInstallerQuit quit)
    {
        if (quit.Committed || quit.Cancelled) return;
        quit.Cancelled = true;
        idleQuits.Remove(quit);
        shell.Delegate.Model?.ReleaseInstallerTurns(quit);
    }

    private Task<NthControlAnswer> Prepare(NthInstallerQuit quit, CancellationToken token) => MainActor.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (quit.Cancelled) return new NthControlAnswer(Reply(NthControlReply.Refused, []) with { Reason = "withdrawn" });
            var work = RunningWork();
            if (quit.NeedsIdle && work.Count != 0 || shell.Delegate.Model?.HasUndurableInstallerTurns == true)
            {
                Abort(quit);
                return new NthControlAnswer(Reply(NthControlReply.Busy, work));
            }
            var deferred = work.Any(item => item.Kind == NthWorkKind.Merge) ? NthWorkKind.Merge : (NthWorkKind?)null;
            return new NthControlAnswer(Reply(NthControlReply.Ok, [], deferred), Commit: cancellation => MainActor.Run<NthControlResponse?>(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (quit.Cancelled) return Reply(NthControlReply.Refused, []) with { Reason = "withdrawn" };
                var current = RunningWork();
                if (quit.NeedsIdle && current.Count != 0 || shell.Delegate.Model?.HasUndurableInstallerTurns == true)
                { Abort(quit); return Reply(NthControlReply.Busy, current); }
                quit.Committed = true;
                if (!quit.NeedsIdle) shell.Delegate.Model?.CancelInstallerPrivateWork();
                shell.TerminateForInstaller();
                return null;
            }), Abort: () => MainActor.Post(() => Abort(quit)));
        });

    private NthControlResponse Reply(NthControlReply reply, IReadOnlyList<NthWorkItem> work, NthWorkKind? deferred = null) =>
        new(reply, processId, executable, work, work.Count, deferred);

    internal IReadOnlyList<NthWorkItem> RunningWork()
    {
        if (shell.Delegate.Model is not { } model) return [];
        var now = SwiftDate.Now;
        var items = new List<NthWorkItem>();
        var running = new HashSet<Guid>();
        foreach (var runtime in model.LiveRuntimes)
        {
            if (runtime.IsRunning)
            {
                running.Add(runtime.ThreadID);
                items.Add(Item(runtime.Thread?.Title, NthWorkKind.Thread, runtime.TurnStartedAt, now));
            }
            if (runtime.IsHydraMerging) items.Add(Item(runtime.Thread?.Title, NthWorkKind.Merge, runtime.HydraMergeStartedAt, now));
        }
        foreach (var pending in model.PendingInstallerTurns)
            if (!running.Contains(pending.Thread)) items.Add(Item(model.Thread(pending.Thread)?.Title, NthWorkKind.Thread, pending.Started, now));
        return [.. items.OrderByDescending(item => item.Seconds).ThenBy(item => item.Name, StringComparer.Ordinal)];
    }

    private static NthWorkItem Item(string? title, NthWorkKind kind, DateTimeOffset? started, DateTimeOffset now)
    {
        var name = NthControl.CleanName(title ?? string.Empty);
        var seconds = started is { } since ? (int)Math.Clamp((now - since).TotalSeconds, 0, int.MaxValue) : 0;
        return new NthWorkItem(name.Length == 0 ? NthControl.UnnamedWork : name, kind, seconds);
    }
}
