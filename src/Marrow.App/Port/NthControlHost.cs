using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App.Port;

// PORT-ONLY: installer handshake. ED-NEW-inst-c7-5 admission; ED-NEW-inst-c7-6 exact-ID retirement.
public sealed class NthControlHost : INthControlHost
{
    private readonly AppShell shell;
    private readonly Task launchReady;
    private readonly int processId;
    private readonly string executable;
    private readonly Dictionary<string, NthInstallerQuit> quits = new(StringComparer.Ordinal);
    private readonly HashSet<string> retired = new(StringComparer.Ordinal);
    private readonly Queue<string> retirementOrder = [];

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
        if (request.Command == NthControlCommand.Status) return new NthControlAnswer(Reply(NthControlReply.Status, work));
        var id = request.Id is { } candidate && Guid.TryParseExact(candidate, "N", out var parsed) ? parsed.ToString("N") : null;
        if (request.Command == NthControlCommand.Withdraw)
        {
            if (id is not null)
            {
                Retire(id);
                if (quits.TryGetValue(id, out var pending)) Withdraw(pending);
            }
            return new NthControlAnswer(Reply(NthControlReply.Ok, []) with { Id = id });
        }
        if (id is null) return new NthControlAnswer(Reply(NthControlReply.Refused, []) with { Reason = "missing-id" });
        if (retired.Contains(id) || quits.ContainsKey(id))
            return new NthControlAnswer(Reply(NthControlReply.Refused, []) with { Reason = "retired-id", Id = id });
        if (shell.HasTerminated) return new NthControlAnswer(Reply(NthControlReply.Ok, []) with { Id = id });
        if (request.Mode == NthQuitMode.IfIdle && work.Count != 0 || shell.Delegate.Model?.HasUndurableInstallerTurns == true)
            return new NthControlAnswer(Reply(NthControlReply.Busy, work) with { Id = id });

        var quit = new NthInstallerQuit { Id = id, NeedsIdle = request.Mode == NthQuitMode.IfIdle };
        quits.Add(id, quit);
        shell.Delegate.Model?.HoldInstallerTurns(quit);
        var merge = work.Any(item => item.Kind == NthWorkKind.Merge);
        return new NthControlAnswer(Reply(NthControlReply.Ok, [], merge ? NthWorkKind.Merge : null) with { Id = id, CommitRequired = true },
            PrepareCommit: token => Prepare(quit, token), Abort: () => MainActor.Post(() => Abort(quit)));
    }

    private void Retire(string id)
    {
        if (!retired.Add(id)) return;
        retirementOrder.Enqueue(id);
        // Outstanding callbacks also retain their own Cancelled bit; pruning the lookup cannot revive one.
        while (retirementOrder.Count > 1024) retired.Remove(retirementOrder.Dequeue());
    }

    private void Withdraw(NthInstallerQuit quit)
    {
        if (quit.Cancelled) return;
        quit.Cancelled = true;
        Retire(quit.Id);
        quits.Remove(quit.Id);
        shell.Delegate.Model?.ReleaseInstallerTurns(quit);
        _ = shell.WithdrawInstallerQuit(quit.Id);
    }

    private void Abort(NthInstallerQuit quit)
    {
        if (!quit.Committed) Withdraw(quit);
    }

    private Task<NthControlAnswer> Prepare(NthInstallerQuit quit, CancellationToken token) => MainActor.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (quit.Cancelled) return new NthControlAnswer(Reply(NthControlReply.Refused, []) with { Reason = "withdrawn", Id = quit.Id });
        var work = RunningWork();
        if (quit.NeedsIdle && work.Count != 0 || shell.Delegate.Model?.HasUndurableInstallerTurns == true)
        {
            Withdraw(quit);
            return new NthControlAnswer(Reply(NthControlReply.Busy, work) with { Id = quit.Id });
        }
        var merge = work.Any(item => item.Kind == NthWorkKind.Merge);
        return new NthControlAnswer(Reply(NthControlReply.Ok, [], merge ? NthWorkKind.Merge : null) with { Id = quit.Id },
            Commit: cancellation => Commit(quit, cancellation), Abort: () => MainActor.Post(() => Abort(quit)));
    });

    private Task<NthControlResponse?> Commit(NthInstallerQuit quit, CancellationToken token) => MainActor.Run<NthControlResponse?>(() =>
    {
        token.ThrowIfCancellationRequested();
        if (quit.Cancelled) return Reply(NthControlReply.Refused, []) with { Reason = "withdrawn", Id = quit.Id };
        var current = RunningWork();
        if (quit.NeedsIdle && current.Count != 0 || shell.Delegate.Model?.HasUndurableInstallerTurns == true)
        { Withdraw(quit); return Reply(NthControlReply.Busy, current) with { Id = quit.Id }; }
        if (!quit.NeedsIdle) shell.Delegate.Model?.CancelInstallerPrivateWork();
        if (shell.TerminateForInstaller(quit.Id, quit)) { quit.Committed = true; return null; }
        Withdraw(quit);
        return Reply(NthControlReply.Refused, []) with { Reason = "pending-quit", Id = quit.Id };
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
