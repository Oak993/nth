// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 4, 12.6b, 16.2; unit AS-19; ED-NEW-dextra-as19-6). No Swift twin.
// The supervised trial of a draft: the skill walked step by step, stopping at every step that asks the owner and at every step he flagged. A
// step that asks is never done before he has answered, and "Don't" ends the trial with that step not done. With Tactus the trial does a plain
// step itself (an executor replays it on the screen); until then there is no executor, and the owner follows the draft: each step is handed
// to him and he says it is done. A step that is his alone (a password, a private window) is always handed to him, executor or not. The trial
// can be paused (it waits before the next step), resumed and stopped, and a stop reaches a step that is waiting for its answer.
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>What the owner answers to a step the trial puts to him.</summary>
public enum TrialAnswer { Continue, Skip, Stop }

/// <summary>Why a step is put to the owner.</summary>
public enum TrialAsk { Guarded, Flagged, HisAlone }

/// <summary>Where the trial stands, for the panel.</summary>
public sealed record TrialProgress(int Step, int Of, string Text, TrialStage Stage, string? Reason = null);

public enum TrialStage { Starting, Waiting, Doing, Done, Skipped, Failed, Paused }

public enum TrialOutcome { Finished, Stopped, Failed }

/// <summary>How a trial ended. <see cref="Log"/> has one line for each step it reached.</summary>
public sealed record TrialResult(TrialOutcome Outcome, int StoppedAt, string? Reason, IReadOnlyList<string> Log);

/// <summary>What a trial needs from the app: a way to put a step to the owner, and to tell him where it is.</summary>
public interface ITrialHost
{
    /// <summary>A step that asks (guarded, flagged or his alone). The trial waits for the answer; cancelling ends the wait.</summary>
    Task<TrialAnswer> Ask(TeachStep step, TrialAsk why, bool doneByOwner, CancellationToken cancel);

    /// <summary>A plain step handed to the owner (no executor): he does it and says it is done.</summary>
    Task<TrialAnswer> HandOver(TeachStep step, CancellationToken cancel);

    void Progress(TrialProgress progress);
}

/// <summary>Does a plain step on the PC (Tactus). Not bound until Tactus lands.</summary>
public interface ITeachStepExecutor
{
    /// <summary>Null when the step was done; else what went wrong.</summary>
    Task<string?> Perform(TeachStep step, CancellationToken cancel);
}

public sealed class TeachTrial(TeachDraft draft, ITrialHost host, ITeachStepExecutor? executor)
{
    private readonly object gate = new();
    private TaskCompletionSource? resume;

    public bool Paused
    {
        get
        {
            lock (gate) return resume is not null;
        }
    }

    /// <summary>The trial waits before its next step. A step already put to the owner stays put to him.</summary>
    public void Pause()
    {
        lock (gate) resume ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        TaskCompletionSource? waiting;
        lock (gate)
        {
            waiting = resume;
            resume = null;
        }
        waiting?.TrySetResult();
    }

    public async Task<TrialResult> RunAsync(CancellationToken cancel)
    {
        var log = new List<string>();
        var steps = draft.Steps;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var of = steps.Count;
            try
            {
                await WaitIfPaused(step, of, cancel).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                host.Progress(new TrialProgress(step.Number, of, step.Text, TrialStage.Starting));
                var answer = await Put(step, of, cancel).ConfigureAwait(false);
                if (answer == TrialAnswer.Stop) return Stopped(log, step, "You stopped the trial here.");
                if (answer == TrialAnswer.Skip)
                {
                    log.Add(Line(step, "skipped"));
                    host.Progress(new TrialProgress(step.Number, of, step.Text, TrialStage.Skipped));
                    continue;
                }
                if (Automatic(step))
                {
                    host.Progress(new TrialProgress(step.Number, of, step.Text, TrialStage.Doing));
                    var problem = await executor!.Perform(step, cancel).ConfigureAwait(false);
                    if (problem is not null)
                    {
                        log.Add(Line(step, "failed: " + problem));
                        host.Progress(new TrialProgress(step.Number, of, step.Text, TrialStage.Failed, problem));
                        return new TrialResult(TrialOutcome.Failed, step.Number, problem, log);
                    }
                }
                log.Add(Line(step, answer == TrialAnswer.Continue && step.AsksYou ? "asked, then done" : "done"));
                host.Progress(new TrialProgress(step.Number, of, step.Text, TrialStage.Done));
            }
            catch (OperationCanceledException)
            {
                return Stopped(log, step, "The trial was stopped.");
            }
        }
        return new TrialResult(TrialOutcome.Finished, 0, null, log);
    }

    // The step is done by the executor: there is one, and the step is not his alone.
    private bool Automatic(TeachStep step) => executor is not null && step.Guard?.OwnerDoesIt != true;

    // Puts the step to the owner as it needs: a step that asks always, a plain step only when he follows the draft himself.
    private Task<TrialAnswer> Put(TeachStep step, int of, CancellationToken cancel)
    {
        host.Progress(new TrialProgress(step.Number, of, step.Text, TrialStage.Waiting, step.Guard?.Reason));
        if (step.Guard?.OwnerDoesIt == true) return host.Ask(step, TrialAsk.HisAlone, doneByOwner: true, cancel);
        if (step.Guard is not null) return host.Ask(step, TrialAsk.Guarded, doneByOwner: !Automatic(step), cancel);
        if (step.Flagged) return host.Ask(step, TrialAsk.Flagged, doneByOwner: !Automatic(step), cancel);
        return Automatic(step) ? Task.FromResult(TrialAnswer.Continue) : host.HandOver(step, cancel);
    }

    private async Task WaitIfPaused(TeachStep step, int of, CancellationToken cancel)
    {
        Task? waiting;
        lock (gate) waiting = resume?.Task;
        if (waiting is null) return;
        host.Progress(new TrialProgress(step.Number, of, step.Text, TrialStage.Paused));
        await waiting.WaitAsync(cancel).ConfigureAwait(false);
    }

    private static TrialResult Stopped(List<string> log, TeachStep step, string reason)
    {
        log.Add(Line(step, "stopped"));
        return new TrialResult(TrialOutcome.Stopped, step.Number, reason, log);
    }

    private static string Line(TeachStep step, string what) =>
        $"{step.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)}. {step.Text} ({what})";
}
