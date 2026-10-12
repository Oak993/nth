using Marrow.Core.Support;

namespace Marrow.Setup.Support;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3a and 13, unit U4, ED-NEW-inst-u4-1). Velopack's <c>Setup.exe</c> stops every process in the
/// install root when it updates, so before it runs the setup asks a running nth, over its control pipe, whether it may quit:
/// <list type="number">
/// <item>Find nth inside the root (<see cref="IRunningNthProcesses"/>). None: <see cref="RunningNthState.Clear"/>, nothing is asked.</item>
/// <item>Ask <c>status</c> first (it changes nothing): the answering process must be inside the root, or it is a portable or development
/// copy elsewhere and none of the setup's business. Then <c>quit if-idle</c>: "ok" (nth saves and quits) or "busy" with its work.</item>
/// <item>Busy: the person picks "Update when they finish" (ask again every <see cref="RunningNthTimings.WorkPoll"/> until idle) or
/// "Stop them and update" (<c>quit stop</c>: nth stops what runs the way closing its window does).</item>
/// <item>Agreed: wait for the process to be gone. Not gone in <see cref="RunningNthTimings.ExitGrace"/> (15 s): ask "isn't responding.
/// Close it anyway?" and end it only on yes. While nth reports it is finishing a team merge before it quits, the 15 s start again at every
/// ask, because it waits for the merge up to four minutes (AppDelegate.swift:46-52) and ending it then would lose the merge.</item>
/// </list>
/// A nth with no answer at all (an older build with no pipe, or hung) gets the same question after <see cref="RunningNthTimings.StartupPatience"/>.
/// Nothing is ever ended without a yes. Cancelling calls off what nth was told (<c>withdraw</c>).
/// </summary>
public sealed class RunningNthHandshake : IRunningNth
{
    private readonly string root;
    private readonly INthControlChannel channel;
    private readonly IRunningNthProcesses processes;
    private readonly TimeProvider time;
    private readonly RunningNthTimings timings;
    private readonly object gate = new();
    private RunningNthStatus status = RunningNthStatus.Initial;
    private CancellationTokenSource? run;
    private TaskCompletionSource? runDone;
    private IReadOnlyList<RunningNthProcess> watched = [];
    private bool quitSent;
    private static readonly IReadOnlyList<NthWorkItem> NoWork = Array.Empty<NthWorkItem>();

    /// <param name="installRoot">The Velopack install root the setup is about to replace files in.</param>
    /// <param name="channel">The way to talk to nth.</param>
    /// <param name="processes">The way to find and end nth's programs.</param>
    /// <param name="time">The clock every wait runs on (system time by default).</param>
    /// <param name="timings">The waits (the plan's by default).</param>
    public RunningNthHandshake(string installRoot, INthControlChannel channel, IRunningNthProcesses processes, TimeProvider? time = null, RunningNthTimings? timings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        root = installRoot;
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        this.processes = processes ?? throw new ArgumentNullException(nameof(processes));
        this.time = time ?? TimeProvider.System;
        this.timings = timings ?? RunningNthTimings.Default;
    }

    public RunningNthStatus Status
    {
        get
        {
            lock (gate) return status;
        }
    }

    public event Action<RunningNthStatus>? StatusChanged;

    public async Task<bool> IsNthOpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await FindAsync().ConfigureAwait(false)).Count > 0;
    }

    public Task<RunningNthStatus> AskAsync(CancellationToken cancellationToken = default) =>
        Execute(cancellationToken, null, AskCoreAsync);

    public Task<RunningNthStatus> WaitForWorkToFinishAsync(CancellationToken cancellationToken = default) =>
        Execute(cancellationToken, [RunningNthState.Busy], WaitForWorkCoreAsync);

    public Task<RunningNthStatus> StopWorkAndQuitAsync(CancellationToken cancellationToken = default) =>
        Execute(cancellationToken, [RunningNthState.Busy], token => QuitAsync(NthControlRequest.QuitAndStop, token));

    public Task<RunningNthStatus> EndItAsync(CancellationToken cancellationToken = default) =>
        Execute(cancellationToken, [RunningNthState.NotResponding], EndCoreAsync);

    public Task<RunningNthStatus> KeepWaitingAsync(CancellationToken cancellationToken = default) =>
        Execute(cancellationToken, [RunningNthState.NotResponding], KeepWaitingCoreAsync);

    public async Task CancelAsync()
    {
        CancellationTokenSource? running;
        Task? pending;
        lock (gate)
        {
            running = run;
            pending = runDone?.Task;
        }
        if (running is not null)
        {
            try
            {
                running.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // It finished just now.
            }
            if (pending is not null) await pending.ConfigureAwait(false);
        }
        RunningNthState state;
        lock (gate) state = status.State;
        if (state is RunningNthState.Cancelled or RunningNthState.Clear or RunningNthState.Closed or RunningNthState.NotAsked) return;
        await CancelledAsync().ConfigureAwait(false);
    }

    // ---- the operations ----

    private async Task<RunningNthStatus> Execute(CancellationToken external, RunningNthState[]? allowed, Func<CancellationToken, Task<RunningNthStatus>> body)
    {
        CancellationTokenSource scope;
        lock (gate)
        {
            if (run is not null) throw new InvalidOperationException("The running-nth handshake is already busy; cancel it or wait for it.");
            if (allowed is not null && Array.IndexOf(allowed, status.State) < 0)
                throw new InvalidOperationException($"This step is not available while the handshake is {status.State}.");
            scope = CancellationTokenSource.CreateLinkedTokenSource(external);
            run = scope;
            runDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            return await body(scope.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (scope.IsCancellationRequested)
        {
            return await CancelledAsync().ConfigureAwait(false);
        }
        finally
        {
            TaskCompletionSource? done;
            lock (gate)
            {
                done = runDone;
                run = null;
                runDone = null;
            }
            scope.Dispose();
            done?.TrySetResult();
        }
    }

    private async Task<RunningNthStatus> AskCoreAsync(CancellationToken token)
    {
        quitSent = false;
        Set(RunningNthState.Asking);
        var found = await FindAsync().ConfigureAwait(false);
        if (found.Count == 0) return Set(RunningNthState.Clear);

        // Who answers? A running nth with no pipe yet may be starting up, so the setup asks again for a few seconds before it gives up.
        var since = time.GetTimestamp();
        NthControlResponse answer;
        while (true)
        {
            var hello = await channel.SendAsync(NthControlRequest.Status, token).ConfigureAwait(false);
            if (hello.Response is { } heard)
            {
                answer = heard;
                break;
            }
            if (hello.Failure == NthControlFailure.NotAllowed) return Refuse("not-allowed");
            found = await FindAsync().ConfigureAwait(false);
            if (found.Count == 0) return Set(RunningNthState.Closed); // it left by itself while it was being asked
            watched = found;
            if (time.GetElapsedTime(since) >= timings.StartupPatience) return Set(RunningNthState.NotResponding, reason: "no-answer");
            await Delay(timings.AskRetry, token).ConfigureAwait(false);
        }

        if (answer.Reply == NthControlReply.Refused) return Refuse(answer.Reason ?? "refused");
        if (answer.Reply != NthControlReply.Status) return Refuse("unexpected");
        watched = found; // Only the processes discovered in this attempt can clear its guard.
        if (!found.Any(process => process.ProcessId == answer.Pid) || !RunningNthPaths.IsInside(root, answer.Exe))
            return await WaitForExitAsync(finishingMerge: false, token).ConfigureAwait(false);
        // A different nth cannot give consent for these processes, even if its executable is also in the root.
        return await QuitAsync(NthControlRequest.QuitIfIdle, token).ConfigureAwait(false);
    }

    private async Task<RunningNthStatus> QuitAsync(NthControlRequest request, CancellationToken token)
    {
        quitSent = true;
        var reply = await channel.SendAsync(request, token).ConfigureAwait(false);
        return await ReadQuitReplyAsync(reply, token).ConfigureAwait(false);
    }

    private async Task<RunningNthStatus> ReadQuitReplyAsync(NthControlExchange reply, CancellationToken token)
    {
        if (reply.Response is not { } answer)
        {
            if (reply.Failure == NthControlFailure.NotAllowed) return Refuse("not-allowed");
            // The pipe went quiet after nth was told to quit: it may be leaving, or hung. The 15 s decide.
            return await WaitForExitAsync(finishingMerge: false, token).ConfigureAwait(false);
        }
        switch (answer.Reply)
        {
            case NthControlReply.Ok:
                return await WaitForExitAsync(answer.Deferred == NthWorkKind.Merge, token).ConfigureAwait(false);
            case NthControlReply.Busy:
                quitSent = false;
                return Set(RunningNthState.Busy, answer.Work, answer.WorkCount);
            case NthControlReply.Refused:
                quitSent = false;
                return Refuse(answer.Reason ?? "refused");
            default:
                quitSent = false;
                return Refuse("unexpected");
        }
    }

    private async Task<RunningNthStatus> WaitForWorkCoreAsync(CancellationToken token)
    {
        var shape = Shape(Status.Work);
        Set(RunningNthState.Waiting);
        var failures = 0;
        while (true)
        {
            await Delay(timings.WorkPoll, token).ConfigureAwait(false);
            quitSent = true; // an "ok" below means nth is quitting; a cancel after it calls that off
            var reply = await channel.SendAsync(NthControlRequest.QuitIfIdle, token).ConfigureAwait(false);
            if (reply.Response is { } answer)
            {
                failures = 0;
                if (answer.Reply == NthControlReply.Busy)
                {
                    var now = Shape(answer.Work);
                    if (now != shape) Set(RunningNthState.Waiting, answer.Work, answer.WorkCount);
                    shape = now;
                    continue;
                }
                watched = await FindAsync().ConfigureAwait(false);
                return await ReadQuitReplyAsync(reply, token).ConfigureAwait(false);
            }
            if (reply.Failure == NthControlFailure.NotAllowed) return Refuse("not-allowed");
            var left = await FindAsync().ConfigureAwait(false);
            if (left.Count == 0) return Set(RunningNthState.Closed); // the person closed nth themselves while the setup waited
            watched = left;
            if (++failures >= timings.WorkFailureLimit) return Set(RunningNthState.NotResponding, reason: "no-answer");
        }
    }

    private async Task<RunningNthStatus> WaitForExitAsync(bool finishingMerge, CancellationToken token)
    {
        Set(RunningNthState.Closing, merge: finishingMerge);
        var clock = time.GetTimestamp();
        var lastMergeAsk = clock;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!await AnyAliveAsync().ConfigureAwait(false)) return Set(RunningNthState.Closed);
            if (finishingMerge && time.GetElapsedTime(lastMergeAsk) >= timings.MergePoll)
            {
                lastMergeAsk = time.GetTimestamp();
                var ask = await channel.SendAsync(NthControlRequest.Status, token).ConfigureAwait(false);
                if (ask.Response is { } answer)
                {
                    if (answer.Work.Any(item => item.Kind == NthWorkKind.Merge))
                    {
                        clock = time.GetTimestamp(); // still finishing it: nth is not hung, so the 15 s start again
                    }
                    else
                    {
                        finishingMerge = false;
                        Set(RunningNthState.Closing, merge: false);
                    }
                }
            }
            if (time.GetElapsedTime(clock) >= timings.ExitGrace) return Set(RunningNthState.NotResponding, reason: "did-not-exit");
            await Delay(timings.ExitPoll, token).ConfigureAwait(false);
        }
    }

    private async Task<RunningNthStatus> EndCoreAsync(CancellationToken token)
    {
        Set(RunningNthState.Ending);
        var failure = (string?)null;
        foreach (var target in await FindAsync().ConfigureAwait(false))
        {
            var result = await Task.Run(() => processes.End(target), CancellationToken.None).ConfigureAwait(false);
            if (result == ProcessEndResult.AccessDenied) failure = "not-allowed";
            else if (result == ProcessEndResult.Failed) failure ??= "could-not-end";
        }
        var left = await FindAsync().ConfigureAwait(false);
        return left.Count == 0 ? Set(RunningNthState.Closed) : Refuse(failure ?? "could-not-end");
    }

    private async Task<RunningNthStatus> KeepWaitingCoreAsync(CancellationToken token)
    {
        // It never answered: ask again from the start. It agreed but did not leave: give it another 15 s.
        if (Status.Reason == "no-answer") return await AskCoreAsync(token).ConfigureAwait(false);
        watched = await FindAsync().ConfigureAwait(false);
        return await WaitForExitAsync(finishingMerge: false, token).ConfigureAwait(false);
    }

    private async Task<RunningNthStatus> CancelledAsync()
    {
        if (quitSent)
        {
            quitSent = false;
            try
            {
                using var window = new CancellationTokenSource(timings.WithdrawTimeout, time);
                _ = await channel.SendAsync(NthControlRequest.Withdraw, window.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Calling it off is best effort: a nth that does not answer is not going to quit on a request it never read.
            }
        }
        return Set(RunningNthState.Cancelled);
    }

    // ---- helpers ----

    private Task<IReadOnlyList<RunningNthProcess>> FindAsync() => Task.Run(() => processes.FindInside(root));

    private async Task<bool> AnyAliveAsync()
    {
        var list = watched;
        return await Task.Run(() => list.Any(processes.IsRunning)).ConfigureAwait(false);
    }

    private Task Delay(TimeSpan span, CancellationToken token) => Task.Delay(span, time, token);

    private RunningNthStatus Refuse(string reason) => Set(RunningNthState.Refused, reason: reason);

    // What the person sees of a work list: the names and kinds in order, and the minute each has run for.
    private static string Shape(IReadOnlyList<NthWorkItem> work) =>
        string.Join('|', work.Select(item => item.Kind + ":" + item.Name + ":" + (item.Seconds / 60)));

    private RunningNthStatus Set(RunningNthState state, IReadOnlyList<NthWorkItem>? work = null, int? count = null, bool merge = false, string? reason = null)
    {
        RunningNthStatus next;
        lock (gate)
        {
            var keep = state == RunningNthState.Waiting && work is null;
            var list = work ?? (keep ? status.Work : NoWork);
            next = new RunningNthStatus(state, list, count ?? (keep ? status.WorkCount : list.Count), merge, reason);
            if (next.State == status.State && next.Reason == status.Reason && next.FinishingMerge == status.FinishingMerge
                && next.WorkCount == status.WorkCount && ReferenceEquals(next.Work, status.Work)) return status;
            status = next;
        }
        try
        {
            StatusChanged?.Invoke(next);
        }
        catch (Exception)
        {
            // A window's handler failing must not break the handshake.
        }
        return next;
    }
}
