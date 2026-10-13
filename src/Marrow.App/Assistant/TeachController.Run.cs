// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.3, 10 step 4, 12.6b; unit AS-19; ED-NEW-dextra-as19-5, -6). No Swift twin.
// Trying a draft before trusting it: the dry run (the steps read out in "would" words, nothing done or read or written) and the supervised
// trial (the skill walked step by step, stopping at every step that asks and every step the owner flagged). A step put to the owner is a card
// on the decision queue and a line in the draft's view, and the first answer from either one counts. Until Tactus lands there is no executor, so
// the owner follows the draft himself and says each step is done.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>A step the trial has put to the owner and waits on.</summary>
public sealed record TrialPending(TeachStep Step, TrialAsk? Why, bool DoneByOwner, string Question, IReadOnlyList<string> Options, Guid? Card);

/// <summary>The trial the views show: where it is, what it waits on, how it ended.</summary>
public sealed record TrialView(string RecordingId, TrialProgress? Progress, TrialPending? Pending, bool Paused, TrialResult? Result);

public sealed partial class TeachController
{
    private sealed class TrialRun
    {
        public required string RecordingId { get; init; }

        public required TeachTrial Trial { get; init; }

        public required CancellationTokenSource Stop { get; init; }

        public TrialProgress? Progress { get; set; }

        public TrialPending? Pending { get; set; }

        public TaskCompletionSource<TrialAnswer>? Waiting { get; set; }

        public TrialResult? Result { get; set; }
    }

    private TrialRun? trial;

    /// <summary>The words the options of a trial's question carry.</summary>
    public const string DidIt = "I did it";

    public const string GoAhead = "Go ahead";

    public const string SkipIt = "Skip this step";

    public const string StopIt = "Stop the trial";

    // ---- the dry run ----

    /// <summary>
    /// The draft's dry run: its steps in "would" words, each marked as the real run would do it on its own, ask, or refuse. Nothing is done,
    /// read or written: it is a walk over the saved draft, and it writes nothing (not the recording, not the draft, not the activity log).
    /// </summary>
    public IReadOnlyList<DryStep>? DryRun(string id) => recordings.Draft(id) is { } draft ? TeachDryRun.Walk(draft) : null;

    /// <summary>The dry run as the page prints it: the banner, the steps, how many would ask.</summary>
    public string? DryRunReport(string id) => recordings.Draft(id) is { } draft ? TeachDryRun.Report(draft) : null;

    // ---- the supervised trial ----

    /// <summary>The trial that is running or has just ended, for the views; null when there is none.</summary>
    public TrialView? Trial
    {
        get
        {
            lock (gate) return trial is { } run ? new TrialView(run.RecordingId, run.Progress, run.Pending, run.Trial.Paused, run.Result) : null;
        }
    }

    /// <summary>
    /// Starts the supervised trial of a saved draft. The trial pauses at every step that asks and every step the owner flagged. Returns the task
    /// of the trial; it completes with how the trial ended. A refusal says why (a recording is running, a trial already is, no draft).
    /// </summary>
    public (Task<TrialResult>? Task, string? Refusal) StartTrial(string id)
    {
        var draft = recordings.Draft(id);
        if (draft is null) return (null, "There is no draft to try.");
        if (draft.Steps.Count == 0) return (null, "The draft has no steps.");
        TrialRun run;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Active is not null) return (null, "A recording is running. Stop it first.");
            if (trial is { Result: null }) return (null, "A trial is already running.");
            run = new TrialRun { RecordingId = id, Stop = new CancellationTokenSource(), Trial = new TeachTrial(draft, new TrialHost(this), o.Executor) };
            trial = run;
        }
        Log("trial", $"recording {id}: started, {draft.Steps.Count} steps, {draft.Boundary.Count} that ask");
        Changed?.Invoke();
        return (Task.Run(() => Finished(run), CancellationToken.None), null);
    }

    private async Task<TrialResult> Finished(TrialRun run)
    {
        TrialResult result;
        try
        {
            result = await run.Trial.RunAsync(run.Stop.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            result = new TrialResult(TrialOutcome.Failed, 0, "The trial failed: " + error.GetType().Name + ".", []);
        }
        lock (gate)
        {
            run.Result = result;
            run.Pending = null;
            run.Waiting = null;
        }
        WithdrawTrialCards(run, "The trial ended.");
        Log("trial", $"recording {run.RecordingId}: {result.Outcome}" + (result.StoppedAt > 0 ? $" at step {result.StoppedAt}" : ""));
        Changed?.Invoke();
        return result;
    }

    public void PauseTrial() => CurrentRun()?.Trial.Pause();

    public void ResumeTrial() => CurrentRun()?.Trial.Resume();

    /// <summary>Stops the trial now, wherever it is, including a step waiting for his answer.</summary>
    public void StopTrial()
    {
        var run = CurrentRun();
        if (run is null) return;
        try { run.Stop.Cancel(); }
        catch (ObjectDisposedException) { /* the trial already ended */ }
    }

    /// <summary>Clears an ended trial from the views.</summary>
    public void ClearTrial()
    {
        lock (gate)
        {
            if (trial is { Result: not null }) trial = null;
        }
        Changed?.Invoke();
    }

    /// <summary>The owner's answer to the step the trial waits on, from the draft's view. The step's card, if any, is withdrawn.</summary>
    public bool AnswerTrial(TrialAnswer answer)
    {
        TrialRun? run;
        TaskCompletionSource<TrialAnswer>? waiting;
        lock (gate)
        {
            run = trial;
            waiting = run?.Waiting;
        }
        if (run is null || waiting is null) return false;
        var taken = waiting.TrySetResult(answer);
        if (taken) WithdrawTrialCards(run, "You answered it in the draft.");
        return taken;
    }

    private TrialRun? CurrentRun()
    {
        lock (gate) return trial is { Result: null } run ? run : null;
    }

    private void WithdrawTrialCards(TrialRun run, string note)
    {
        Guid? card;
        lock (gate) card = run.Pending?.Card;
        if (card is { } waiting) o.Queue.Withdraw(waiting, note);
    }

    // The trial's way to the owner: a card on the queue for a step that asks, a line in the view for every step.
    private sealed class TrialHost(TeachController owner) : ITrialHost
    {
        public Task<TrialAnswer> Ask(TeachStep step, TrialAsk why, bool doneByOwner, CancellationToken cancel) => owner.Put(step, why, doneByOwner, cancel);

        public Task<TrialAnswer> HandOver(TeachStep step, CancellationToken cancel) => owner.Put(step, null, doneByOwner: true, cancel);

        public void Progress(TrialProgress progress)
        {
            lock (owner.gate)
            {
                if (owner.trial is { Result: null } run) run.Progress = progress;
            }
            owner.Changed?.Invoke();
        }
    }

    private Task<TrialAnswer> Put(TeachStep step, TrialAsk? why, bool doneByOwner, CancellationToken cancel)
    {
        TrialRun? run;
        lock (gate) run = trial;
        if (run is null) return Task.FromResult(TrialAnswer.Stop);
        var waiting = new TaskCompletionSource<TrialAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var number = step.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var question = why switch
        {
            TrialAsk.HisAlone => $"Trial, step {number}: {step.Text}",
            TrialAsk.Guarded => $"Trial, step {number} asks you: {step.Text}",
            TrialAsk.Flagged => $"Trial, step {number} is one you flagged: {step.Text}",
            _ => $"Trial, step {number}: {step.Text}",
        };
        string[] options = doneByOwner ? [DidIt, SkipIt, StopIt] : [GoAhead, SkipIt, StopIt];
        Guid? card = null;
        if (why is not null)
        {
            // A step the trial does for him and that cannot be undone is the irreversible card, with Do it and Don't. Anything he does himself is a question.
            var irreversible = !doneByOwner && step.Guard is { OwnerDoesIt: false } guard && ActionClasses.IsIrreversible(guard.Class);
            var item = new DecisionItem(Guid.NewGuid(), irreversible ? DecisionKind.ToolCall : DecisionKind.Question, irreversible ? DecisionWeight.Irreversible : DecisionWeight.Question, question)
            {
                Why = (step.Guard?.Reason ?? "You flagged this step so the trial would stop here.")
                    + (doneByOwner ? " You do this step yourself; say when it is done." : " It happens on the screen only after you say so."),
                Rule = step.Guard is { } g ? ActionClasses.RuleText(g.Class) : "Asks first: teaching",
                AssistantId = o.AssistantId,
                Class = step.Guard?.Class,
                Options = irreversible ? ValueList<string>.Empty : new ValueList<string>(options),
                Inputs = new ValueList<DecisionInput>([new DecisionInput("step", step.Text)]),
            };
            card = o.Queue.Raise(item, (answered, answer) =>
            {
                var mapped = answer.Kind switch
                {
                    DecisionAnswerKind.DoIt => TrialAnswer.Continue,
                    DecisionAnswerKind.Dont or DecisionAnswerKind.Deny => TrialAnswer.Stop,
                    DecisionAnswerKind.Pick when answer.Text == SkipIt => TrialAnswer.Skip,
                    DecisionAnswerKind.Pick when answer.Text == StopIt => TrialAnswer.Stop,
                    DecisionAnswerKind.Pick => TrialAnswer.Continue,
                    _ => TrialAnswer.Stop,
                };
                waiting.TrySetResult(mapped);
            }).Id;
        }
        lock (gate)
        {
            run.Waiting = waiting;
            run.Pending = new TrialPending(step, why, doneByOwner, question, options, card);
        }
        Changed?.Invoke();
        var registration = cancel.Register(() =>
        {
            waiting.TrySetCanceled(cancel);
            if (card is { } waitingCard) o.Queue.Withdraw(waitingCard, "The trial was stopped.");
        });
        return Settled(run, waiting, registration);
    }

    private async Task<TrialAnswer> Settled(TrialRun run, TaskCompletionSource<TrialAnswer> waiting, CancellationTokenRegistration registration)
    {
        try
        {
            return await waiting.Task.ConfigureAwait(false);
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            lock (gate)
            {
                if (ReferenceEquals(run.Waiting, waiting))
                {
                    run.Waiting = null;
                    run.Pending = null;
                }
            }
            Changed?.Invoke();
        }
    }
}
