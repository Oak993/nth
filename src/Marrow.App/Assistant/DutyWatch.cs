// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Failed runs never retry endlessly", 7.1; unit AS-15; ED-NEW-dextra-as15-5). No Swift twin.
// Watches how every duty's runs end. A duty that fails three times in a row is paused (the scheduler, AS-10, asks IsPaused before it
// fires one) and a card goes to the owner: try it again, or keep it paused. The count and the pause are kept on disk through the book
// (DutyHealthBook), so a restart neither forgets three failures nor lets a paused duty run. Runs on the app's main thread; the book
// is thread-safe, the watch's own state sits behind one lock and nothing outside it is called while it is held.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>How one run of a duty ended. <paramref name="Reason"/> is data (a page or a file can put words in it), shown on the card and never in the log.</summary>
public sealed record DutyRun(string DutyId, string Name, string AssistantId, bool Succeeded, string? Reason = null);

public sealed class DutyWatchOptions
{
    public required DecisionQueue Queue { get; init; }

    public required DutyHealthBook Book { get; init; }

    /// <summary>Writes one activity log line; a failure is reported through <see cref="Faulted"/>.</summary>
    public Action<AuditEntry>? Audit { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;
}

public sealed class DutyWatch
{
    private readonly DutyWatchOptions o;
    private readonly object gate = new();
    private readonly Dictionary<string, Guid> cards = new(StringComparer.Ordinal);

    public DutyWatch(DutyWatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        o = options;
    }

    /// <summary>The duty was paused by the breaker (the board and the toast listen).</summary>
    public event Action<DutyHealth, string>? Paused;

    /// <summary>The activity log could not be written; the watch went on.</summary>
    public event Action<string>? Faulted;

    /// <summary>The breaker paused this duty: the scheduler does not fire it until the owner lets it go on.</summary>
    public bool IsPaused(string dutyId) => o.Book.Of(dutyId).Paused;

    public DutyHealth HealthOf(string dutyId) => o.Book.Of(dutyId);

    /// <summary>A run of a duty ended. The third failure in a row pauses it and raises the card.</summary>
    public void RunEnded(DutyRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var result = DutyBreaker.Record(o.Book.Of(run.DutyId), run.Succeeded ? DutyOutcome.Succeeded : DutyOutcome.Failed, run.Reason, o.Time.GetUtcNow());
        o.Book.Set(result.Health);
        if (!result.Tripped) return;
        Raise(run, result.Health);
        Log(run.AssistantId, run.DutyId, "duty_paused", $"{result.Health.ConsecutiveFailures} failures in a row");
        Paused?.Invoke(result.Health, run.Name);
    }

    /// <summary>The duties the breaker paused, oldest first (the scheduler lists them when nth starts).</summary>
    public IReadOnlyList<DutyHealth> PausedDuties => o.Book.Paused;

    /// <summary>
    /// nth started and a duty the breaker paused is still paused: its card did not survive the restart (the queue is not kept), so it is
    /// raised again, with the reasons the book remembered. False when the duty is not paused or its card is already waiting.
    /// </summary>
    public bool Reraise(string dutyId, string name, string assistantId)
    {
        var health = o.Book.Of(dutyId);
        if (!health.Paused) return false;
        lock (gate)
        {
            if (cards.ContainsKey(dutyId)) return false;
        }
        Raise(new DutyRun(dutyId, name, assistantId, false), health);
        return true;
    }

    /// <summary>The owner turned the duty on again (its own switch, or the card's "Try it again"): the count starts again and an open card is withdrawn.</summary>
    public void Resume(string dutyId, string assistantId = "")
    {
        o.Book.Set(DutyBreaker.Resume(o.Book.Of(dutyId)));
        Guid? card;
        lock (gate) card = cards.Remove(dutyId, out var id) ? id : null;
        if (card is { } open) o.Queue.Withdraw(open, "The duty was turned on again.");
        Log(assistantId, dutyId, "duty_resumed", "the owner let it go on");
    }

    private void Raise(DutyRun run, DutyHealth health)
    {
        var inputs = new List<DecisionInput> { new("Duty", run.Name), new("Failures", $"{health.ConsecutiveFailures} in a row") };
        for (var i = 0; i < health.Reasons.Count; i++) inputs.Add(new($"Run {i + 1} failed because", health.Reasons[i]));
        var item = new DecisionItem(Guid.NewGuid(), DecisionKind.Duty, DecisionWeight.Question, DutyWords.Title(run.Name))
        {
            Inputs = new ValueList<DecisionInput>(inputs),
            Why = DutyWords.Why,
            Rule = "Failed runs never retry endlessly",
            AssistantId = run.AssistantId,
            Options = new ValueList<string>([DutyWords.TryAgain, DutyWords.KeepPaused]),
            Note = DutyWords.Note,
        };
        var raised = o.Queue.Raise(item, (card, answer) => Answered(run, card, answer));
        lock (gate) cards[run.DutyId] = raised.Id;
    }

    private void Answered(DutyRun run, DecisionItem card, DecisionAnswer answer)
    {
        lock (gate)
        {
            if (!cards.TryGetValue(run.DutyId, out var open) || open != card.Id) return;
            cards.Remove(run.DutyId);
        }
        if (string.Equals(answer.Text, DutyWords.TryAgain, StringComparison.Ordinal))
        {
            o.Book.Set(DutyBreaker.Resume(o.Book.Of(run.DutyId)));
            Log(run.AssistantId, run.DutyId, "duty_resumed", DutyWords.TryAgain);
        }
        else Log(run.AssistantId, run.DutyId, "duty_kept_paused", DutyWords.KeepPaused);
    }

    // The log names the duty by its id and the event by a stable word; what a run said about its failure is never written to it.
    private void Log(string assistant, string dutyId, string tool, string result)
    {
        if (o.Audit is not { } audit) return;
        try
        {
            audit(new AuditEntry
            {
                Assistant = assistant,
                Tool = tool,
                ActionClass = ActionClass.Agents.ToString(),
                Target = dutyId,
                Result = result,
            });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Faulted?.Invoke("The activity log could not be written: " + error.Message);
        }
    }
}
