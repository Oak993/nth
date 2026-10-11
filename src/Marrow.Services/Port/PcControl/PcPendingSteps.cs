// PORT-ONLY: Tactus, PC control (OD-0140, OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.2, critique C10). MCP
// clients end tool calls that run too long, so no call blocks for more than 25 s. A call still waiting at 25 s (for an
// approval, a confirmation card, the user typing in the target control, or a long batch) returns waiting_for_user with a
// token, the reason and the steps already done; the step itself keeps running and is never queued twice. pc_wait until
// "step" with the token waits up to 25 s more for that same step and returns its result. A token not resumed within 60 s,
// a cancelled resume and the end of the turn cancel the step, which withdraws its card or sheet. Holds one chat's steps.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>
/// What one call is waiting for right now (the waiting_for_user reason) and the steps it has finished (its progress), shared
/// by the steps of a batch. Thread-safe.
/// </summary>
public sealed class PcWaitState
{
    private readonly Lock gate = new();
    private readonly List<string> waiting = [];
    private readonly StringBuilder progress = new();

    /// <summary>The reason of the newest wait still open; null when the call is not waiting for the user.</summary>
    public string? Reason
    {
        get
        {
            lock (gate) return waiting.Count == 0 ? null : waiting[^1];
        }
    }

    /// <summary>The finished steps, one per line.</summary>
    public string Progress
    {
        get
        {
            lock (gate) return progress.ToString().TrimEnd('\n');
        }
    }

    /// <summary>Marks a wait for the user (approval, confirm_card, typing_here) until the scope is disposed.</summary>
    public IDisposable Waiting(string reason)
    {
        lock (gate) waiting.Add(reason);
        return new Scope(this, reason);
    }

    public void Done(string line)
    {
        lock (gate) progress.Append(line).Append('\n');
    }

    private void End(string reason)
    {
        lock (gate)
        {
            var at = waiting.LastIndexOf(reason);
            if (at >= 0) waiting.RemoveAt(at);
        }
    }

    private sealed class Scope(PcWaitState owner, string reason) : IDisposable
    {
        private int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0) owner.End(reason);
        }
    }
}

public sealed class PcPendingSteps(IPcClock clock)
{
    /// <summary>The longest a tool call blocks before it answers waiting_for_user.</summary>
    public const int BudgetMs = 25_000;

    /// <summary>How long a waiting step stays queued for pc_wait until "step" before it is cancelled.</summary>
    public const int ExpiryMs = 60_000;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Step> steps = new(StringComparer.Ordinal);

    /// <summary>The steps waiting now (for tests and the session view).</summary>
    public int Count
    {
        get
        {
            lock (gate) return steps.Count;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> under the 25 s budget. Its token is cancelled by <paramref name="cancellationToken"/>
    /// while this call waits, and afterwards only by the step's expiry, a cancelled resume or <see cref="CancelAll"/>.
    /// </summary>
    public async Task<PcResult> Run(Func<CancellationToken, Task<PcResult>> work, PcWaitState state, CancellationToken cancellationToken)
    {
        var step = new Step(state);
        Task<PcResult> running;
        using (cancellationToken.Register(step.Cancel))
        {
            running = step.Start(work);
            if (await Within(running, cancellationToken).ConfigureAwait(false)) return await running.ConfigureAwait(false);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            step.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
        var token = NewToken();
        lock (gate) steps[token] = step;
        Expire(token, step);
        return Waiting(token, step);
    }

    /// <summary>
    /// pc_wait until "step": waits up to 25 s more for the step under <paramref name="token"/> and returns its result, or
    /// waiting_for_user again with the same token. Null when no step waits under it.
    /// </summary>
    public async Task<PcResult?> Resume(string token, CancellationToken cancellationToken)
    {
        Step? step;
        lock (gate) steps.TryGetValue(token, out step);
        if (step is null) return null;
        var turn = step.Resumed();
        try
        {
            using (cancellationToken.Register(step.Cancel))
            {
                if (await Within(step.Work!, cancellationToken).ConfigureAwait(false))
                {
                    lock (gate) steps.Remove(token);
                    return await step.Work!.ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (gate) steps.Remove(token);
            throw;
        }
        if (cancellationToken.IsCancellationRequested)
        {
            lock (gate) steps.Remove(token);
            step.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (step.Turn == turn) Expire(token, step);
        return Waiting(token, step);
    }

    /// <summary>Cancels every waiting step (the end of the turn, a stop of this chat).</summary>
    public void CancelAll()
    {
        List<Step> all;
        lock (gate)
        {
            all = [.. steps.Values];
            steps.Clear();
        }
        foreach (var step in all) step.Cancel();
    }

    private static PcResult Waiting(string token, Step step) =>
        PcResult.WaitingFor(step.State.Reason ?? PcWaitReason.BatchBudget, token, step.State.Progress is { Length: > 0 } done ? "done so far:\n" + done : null);

    // True when the work ended within the budget; false at 25 s. Never a fixed sleep: the budget is a timeout, cancelled as
    // soon as the work ends.
    private async Task<bool> Within(Task work, CancellationToken cancellationToken)
    {
        if (work.IsCompleted) return true;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var budget = clock.Delay(BudgetMs, stop.Token);
        await Task.WhenAny(work, budget).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        return work.IsCompleted;
    }

    // A step not resumed within 60 s is cancelled and dropped; a resume in the meantime starts a new 60 s.
    private void Expire(string token, Step step)
    {
        var turn = step.Turn;
        _ = Later();

        async Task Later()
        {
            try { await clock.Delay(ExpiryMs, step.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            if (step.Turn != turn) return;
            bool removed;
            lock (gate) removed = steps.Remove(token);
            if (removed) step.Cancel();
        }
    }

    private static string NewToken() => "t" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    private sealed class Step(PcWaitState state)
    {
        private readonly CancellationTokenSource source = new();
        private int turn;

        public PcWaitState State { get; } = state;
        public Task<PcResult>? Work { get; private set; }
        public CancellationToken Token => source.Token;

        /// <summary>Counts resumes, so an older expiry timer knows it no longer applies.</summary>
        public int Turn => Volatile.Read(ref turn);

        public Task<PcResult> Start(Func<CancellationToken, Task<PcResult>> work)
        {
            Work = work(source.Token);
            return Work;
        }

        public int Resumed() => Interlocked.Increment(ref turn);

        public void Cancel()
        {
            try { _ = source.CancelAsync(); }
            catch (ObjectDisposedException) { }
        }
    }
}
