// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.2, 12.6, 12.6a, 12.6b; unit AS-6; ED-NEW-dextra-as6-3 to -9). No Swift twin.
// The owner's controls of a mission that MissionControls serves (12.6b): Resume with everything the design asks of it (the stop
// key, kept answers, the reserve's card, Tactus streams, a cold start after the agents were let go), a head's Pause, Resume
// and Stop, the first-10-minutes rule for a paused task's agents, Force end, Revert changes, its Restore, and Restart.
// The runner stays confined to the app's main thread, like the rest of it.
using System.Globalization;
using Marrow.App.Safety;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Core.Models;

namespace Marrow.App.Assistant;

/// <summary>What a Resume did, for Resume all's line (12.6b).</summary>
public sealed record ResumeOutcome(string? Refused)
{
    public MissionState? State { get; init; }

    /// <summary>The word of the irreversible card that sent the task back to Needs you ("send", "pay"), when one did.</summary>
    public string? HeldWord { get; init; }

    /// <summary>Heads the owner stopped on their own that stay stopped, as "Head in “Task”".</summary>
    public ValueList<string> HeadsLeftStopped { get; init; } = ValueList<string>.Empty;
}

/// <summary>The note a task that has been Pausing for 5 s opens: its text and the names of the agents it waits for.</summary>
public sealed record PauseNote(string Text, ValueList<string> Waiting);

/// <summary>One agent a Stop is ending, and whether it is confirmed (no turn, no live process).</summary>
public sealed record AgentStopLine(Guid Mission, Guid Thread, string Name, bool Settled);

public sealed partial class MissionRunner
{
    public const string NoSuchMission = "There is no such mission.";

    /// <summary>The message a task's own Resume sends its lead when the turn ended while paused (12.6b "Which resume wins").</summary>
    public const string ContinueText = "Resumed by you. Continue from where you paused.";

    /// <summary>The message a head gets when it was stopped part way and is resumed.</summary>
    public const string HeadContinueText = "Resumed by you. Go on from where you stopped; do the last step again, since it may have been cut.";

    // ---- the stop key (12.6) ----

    private static string StopScope(Guid mission) => "mission:" + mission.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>A task holds the stop chord while it runs (or is on its way to), not while it is paused, stopped or over.</summary>
    private static bool HoldsStopKey(MissionState state) =>
        state is MissionState.Planning or MissionState.Working or MissionState.Reviewing or MissionState.NeedsYou
            or MissionState.Pausing or MissionState.Stopping;

    private bool HasLease(Guid id)
    {
        lock (gate) return LiveOf(id).Lease is not null;
    }

    private async Task<bool> EnsureStopKey(Guid id, CancellationToken token)
    {
        if (o.StopKey is not { } key || HasLease(id)) return true;
        var scope = StopScope(id);
        if (!key.TryAcquire(scope, out var lease))
        {
            // Fail closed: nothing starts. The mission waits and shows why; it goes on by itself once the key works again.
            var line = ControlWords.StopKeyUnavailable(key.Chord);
            OnMain(() => Update(id, r => r with { Waiting = line }));
            try { lease = await key.AcquireAsync(scope, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
            OnMain(() => Update(id, r => r.Waiting == line ? r with { Waiting = null } : r));
        }
        return KeepLease(id, lease!);
    }

    private bool KeepLease(Guid id, IDisposable lease)
    {
        var keep = false;
        lock (gate)
        {
            if (missions.TryGetValue(id, out var record) && HoldsStopKey(record.Mission.State) && LiveOf(id).Lease is null)
            {
                LiveOf(id).Lease = lease;
                keep = true;
            }
        }
        if (!keep) lease.Dispose();
        return keep || HasLease(id);
    }

    private void ReleaseLease(Guid id)
    {
        IDisposable? lease;
        lock (gate)
        {
            if (!live.TryGetValue(id, out var l)) return;
            lease = l.Lease;
            l.Lease = null;
        }
        lease?.Dispose();
    }

    /// <summary>What every change of a mission sets going or lets go: its stop lease, its 10-minute release timer, its waiting heads.</summary>
    private void Settle(MissionRecord before, MissionRecord after)
    {
        var id = after.Id;
        var was = before.Mission.State;
        var now = after.Mission.State;
        if (!HoldsStopKey(now)) ReleaseLease(id);
        if (now == MissionState.Pausing && was != MissionState.Pausing)
        {
            lock (gate) LiveOf(id).PausingSince = o.Time.GetUtcNow();
        }
        else if (now != MissionState.Pausing && was == MissionState.Pausing)
        {
            lock (gate) LiveOf(id).PausingSince = null;
        }
        if (now == MissionState.Paused && was != MissionState.Paused) ArmRelease(id, o.ReleaseAgentsAfter);
        else if (now != MissionState.Paused) CancelRelease(id);
        if (now != was || !before.Mission.Heads.SequenceEqual(after.Mission.Heads)) PokeHeads(id);
    }

    private void PokeHeads(Guid id)
    {
        TaskCompletionSource old;
        lock (gate)
        {
            var l = LiveOf(id);
            old = l.HeadsChanged;
            l.HeadsChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        old.TrySetResult();
    }

    // ---- kept answers (12.6b "Cards while paused") ----

    private bool KeepsAnswer(DecisionItem item) =>
        item.MissionId is { } id && !DecisionRules.HeldByPause(item)
        && Find(id)?.Mission is { State: MissionState.Paused, PausedBy: not PauseCause.Reserve };

    // ---- the agents of a paused task (12.6b "Paused tasks: slots, memory and cost") ----

    private void ArmRelease(Guid id, TimeSpan after)
    {
        ITimer? old;
        lock (gate)
        {
            var l = LiveOf(id);
            old = l.ReleaseTimer;
            l.ReleaseTimer = o.Time.CreateTimer(_ => OnMain(() => ReleaseAgents(id)), null, after, Timeout.InfiniteTimeSpan);
        }
        old?.Dispose();
    }

    private void CancelRelease(Guid id)
    {
        ITimer? old;
        lock (gate)
        {
            if (!live.TryGetValue(id, out var l)) return;
            old = l.ReleaseTimer;
            l.ReleaseTimer = null;
        }
        old?.Dispose();
    }

    /// <summary>
    /// After 10 minutes paused, each local agent's process tree is ended and its slot and RAM released: no turn runs, so
    /// nothing is cut. A turn that still waits on a held card is not cut either; it is looked at again in a minute.
    /// </summary>
    private void ReleaseAgents(Guid id)
    {
        if (Find(id) is not { } record || record.Mission.State != MissionState.Paused || record.AgentsReleased) return;
        var busy = false;
        foreach (var thread in Threads(record))
        {
            if (o.Agents.TurnRunning(thread))
            {
                busy = true;
                continue;
            }
            o.Agents.StopSession(thread);
            o.Jobs.End(thread);
            o.Gate.Leave(thread);
        }
        if (busy)
        {
            ArmRelease(id, TimeSpan.FromMinutes(1));
            return;
        }
        Update(id, r => r with { AgentsReleased = true });
        Log(record, null, "agents_released", ActionClass.Agents, "released after " + o.ReleaseAgentsAfter.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture) + " minutes paused");
    }

    // ---- Resume ----

    /// <summary>Resume (12.6b), a task's own. Null when it went ahead, else why not.</summary>
    public string? Resume(Guid id) => ResumeCore(id, all: false).Refused;

    /// <summary>
    /// Resume (12.6b): held cards come back bound to their inputs, kept answers act, Tactus streams the pause held are
    /// resumed, and a task that was stopped, interrupted by nth's end or let go after 10 minutes runs on behind the gate. A
    /// reserve's task gets its card back instead and never crosses a floor; the stop key must work. <paramref name="all"/>
    /// is Resume all: it leaves a head the owner stopped on its own stopped, and skips a reserve's task.
    /// </summary>
    internal ResumeOutcome ResumeCore(Guid id, bool all)
    {
        if (Find(id) is not { } record) return new ResumeOutcome(NoSuchMission);
        var l = LiveOf(id);
        bool reverting;
        lock (gate) reverting = l.Reverting;
        if (reverting) return new ResumeOutcome("Its changes are being reverted; Restart opens once that is done.");
        var m = record.Mission;
        var fromReserve = m.State is MissionState.Paused && m.PausedBy == PauseCause.Reserve;
        // A task the reserve holds: Resume puts it at Needs you with the reserve's card (AS-13 raised it when it held the task; a
        // fallback raises it when none waits) and never crosses the floor; only the card's answer lets it go on (ResumeAfterReserve).
        if (fromReserve && (all || (o.Queue.Of(id).Count == 0 && o.ReserveCard?.Invoke(record) is null)))
            return new ResumeOutcome("Your reserve holds this task; answer its card to resume it.");
        var heldIrreversible = o.Queue.Of(id).FirstOrDefault(c => c.State == DecisionState.Held && DecisionRules.HeldByPause(c));
        var wasStopped = m.State == MissionState.Stopped || record.Interrupted || l.Restored;
        var released = record.AgentsReleased;
        var move = MissionMachine.Resume(m, heldIrreversible is not null, resumeNeededHandStopped: !all);
        if (!move.Done) return new ResumeOutcome(move.Refused);
        var mission = move.Mission;
        // Nothing could run (every head that could go on was stopped on its own, and Resume all leaves those alone): it stays as it is.
        if (m.State == MissionState.Stopped && mission.State is MissionState.Stopped or MissionState.Stopping)
            return new ResumeOutcome("Every head that could go on was stopped on its own; resume one of them.");
        // It waited on cards it no longer has (lost with nth's end): it goes back to what it was doing and asks again.
        if (mission.State == MissionState.NeedsYou && !fromReserve && o.Queue.Of(id).Count == 0) mission = mission with { State = NeedsYouFrom(record) };
        // The stop key must work for anything that runs (12.6): a Resume that cannot take it changes nothing.
        if (m.State is MissionState.Paused or MissionState.Stopped && HoldsStopKey(mission.State) && o.StopKey is { } key && !HasLease(id))
        {
            if (!key.TryAcquire(StopScope(id), out var got)) return new ResumeOutcome(ControlWords.ResumeNeedsStopKey(key.Chord));
            lock (gate) l.Lease = got;
        }
        lock (gate)
        {
            if (l.Launch.IsCancellationRequested) l.Launch = new CancellationTokenSource();
            l.Restored = false;
            l.WholeStop = false;
            if (fromReserve) l.NeedsYouFrom = m.ResumeTo is { } back && MissionStates.IsRunning(back) ? back : MissionState.Working;
        }
        var keptHeads = mission.Heads.Where(h => h.State == HeadState.Stopped && h.StoppedByHand).Select(h => $"{h.Name} in {ControlWords.Quote(m.Title)}").ToList();
        // Its agents run again (or are about to): "agents released" no longer describes it. The cold start below still knows.
        Update(id, r => r with { Mission = mission, Interrupted = false, AgentsReleased = false });
        ApplyHeadResumes(id, m, mission);
        o.Queue.Release(c => c.MissionId == id, o.CurrentInputs);
        o.Queue.ApplyKept(c => c.MissionId == id);
        var continueMessage = false;
        lock (gate)
        {
            if (l.PcHeld)
            {
                l.PcHeld = false;
                continueMessage = true;
            }
        }
        if (continueMessage) o.PcStreams?.Release(id);
        Log(record, null, "resume", ActionClass.Agents, mission.State.ToString());
        var outcome = new ResumeOutcome(null)
        {
            State = mission.State,
            HeadsLeftStopped = new ValueList<string>(keptHeads),
            HeldWord = mission.State == MissionState.NeedsYou && heldIrreversible is not null && !fromReserve
                ? ControlWords.HeldCardWord(heldIrreversible.Class, heldIrreversible.Kind)
                : null,
        };
        var state = mission.State;
        if (state == MissionState.Planning && record.LeadThread is null)
        {
            _ = Launch(id);
            return outcome;
        }
        if (state == MissionState.NeedsYou) return outcome;
        // A review the reserve (or a pause) held before its reviewer began starts afresh.
        if (state == MissionState.Reviewing && record.ReviewerThread is null && record.LeadThread is not null)
        {
            _ = StartReview(id);
            return outcome;
        }
        if (wasStopped)
        {
            // The cut turn runs again: each agent that was working goes on behind the gate.
            ContinueLead(id, record, state, stoppedPart: true, cold: released);
            return outcome;
        }
        if (continueMessage)
        {
            // The turn ended because Tactus was held, not because the lead was done: it goes on, whatever it last said.
            lock (gate) l.LeadDone = false;
            ContinueLead(id, record, state, stoppedPart: false, cold: released);
            return outcome;
        }
        Advance(id);
        if (released && Find(id) is { } now && now.Mission.State == MissionState.Working && now.LeadThread is { } lead
            && !o.Agents.TurnRunning(lead) && !LiveOf(id).LeadDone && now.PendingRedirect is null
            && o.Agents.Heads(lead).All(h => h.Finished) && !Threads(now).Any(o.Agents.TurnRunning))
            ContinueLead(id, now, now.Mission.State, stoppedPart: false, cold: true);
        return outcome;
    }

    // Starts the lead (or the reviewer) again behind the gate and tells it to go on.
    private void ContinueLead(Guid id, MissionRecord record, MissionState state, bool stoppedPart, bool cold)
    {
        var again = state == MissionState.Reviewing ? record.ReviewerThread : record.LeadThread;
        if (again is not { } thread) return;
        var context = o.Agents.ContextTokens(thread);
        var coldLine = ControlWords.ColdCache(context);
        // The first turn reads the whole context again at the uncached price, and that cost counts in the task's spend.
        if (cold) Update(id, r => r with { Waiting = coldLine, ColdStartTokens = r.ColdStartTokens + Math.Max(0, context ?? 0) });
        var token = LiveOf(id).Launch.Token;
        _ = Admit(id, thread, record.Provider, token).ContinueWith(_ => OnMain(() =>
        {
            if (Find(id) is not { } now || !MissionStates.IsRunning(now.Mission.State)) return;
            var text = state == MissionState.Reviewing
                ? "Your review was cut off. Do it again from the start. " + ReviewVerdict.Instruction
                : stoppedPart
                    ? now.PendingRedirect ?? "The mission was stopped part way. Go on from where it stopped; do the last step again, since it may have been cut."
                    : now.PendingRedirect ?? ContinueText;
            if (cold) Update(id, r => r.Waiting == coldLine ? r with { Waiting = null } : r);
            // The usage reserve is asked before the turn runs again (11.5, 12.6b: Resume never crosses a floor): with no room, the
            // slot goes back and the words wait with the mission.
            if (Reserve is { } reserve && !reserve.BeforeTurn(now, thread, now.Provider, now.Model, ReserveTurns.Resume))
            {
                o.Gate.Leave(thread);
                if (state != MissionState.Reviewing) Update(id, r => r with { PendingRedirect = text });
                return;
            }
            if (o.Agents.Send(thread, text))
            {
                Reserve?.TurnStarted(id, thread, now.Model);
                if (now.PendingRedirect is not null && state != MissionState.Reviewing) Update(id, r => r with { PendingRedirect = null });
            }
        }), TaskScheduler.Default);
    }

    // ---- the "Still pausing" note (12.6b, r4 G9) ----

    /// <summary>After this long a pausing task opens a note saying what it still waits for (the pill's own 5 s).</summary>
    public static readonly TimeSpan StillPausingAfter = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The note of a task that has been Pausing for 5 s: the heads (or the lead) whose turn it waits for, and what the first of them
    /// does now. Null while the task is not pausing or has not waited 5 s yet.
    /// </summary>
    public PauseNote? StillPausing(Guid mission)
    {
        if (Find(mission) is not { } record || record.Mission.State != MissionState.Pausing) return null;
        DateTimeOffset? since;
        lock (gate) since = LiveOf(mission).PausingSince;
        if (since is not { } began || o.Time.GetUtcNow() - began < StillPausingAfter) return null;
        var waiting = new List<(string Name, Guid Thread)>();
        foreach (var head in record.Mission.Heads.Where(h => h.State == HeadState.Pausing))
            if (Guid.TryParse(head.Id, out var thread)) waiting.Add((head.Name, thread));
        if (waiting.Count == 0 && record.LeadThread is { } lead && o.Agents.TurnRunning(lead)) waiting.Add(("the lead", lead));
        if (waiting.Count == 0) return null;
        return new PauseNote(ControlWords.StillPausing([.. waiting.Select(w => w.Name)], o.Agents.Activity(waiting[0].Thread)), new ValueList<string>(waiting.Select(w => w.Name)));
    }

    /// <summary>The heads that wait on this head (a head's Stop asks when other heads wait on it, r4 B3).</summary>
    public IReadOnlyList<string> HeadsWaitingOn(Guid mission, Guid head)
    {
        if (Find(mission) is not { } record) return [];
        var id = HeadId(head);
        return [.. record.Mission.Heads.Where(h => h.State is not (HeadState.Done or HeadState.Stopped) && h.DependsOn.Contains(id)).Select(h => h.Name)];
    }

    // ---- heads (12.6b "Heads") ----

    /// <summary>Pause one head: no interrupt; at its turn boundary it is Paused. The others keep working.</summary>
    public string? PauseHead(Guid mission, Guid head)
    {
        if (Find(mission) is not { } record) return NoSuchMission;
        var id = HeadId(head);
        // A native head works inside its lead's turn, so the lead's turn is its boundary.
        var running = o.Agents.TurnRunning(head) || (record.Mission.Heads.FirstOrDefault(h => h.Id == id)?.State == HeadState.Working
            && record.LeadThread is { } lead && o.Agents.TurnRunning(lead));
        var move = MissionMachine.PauseHead(record.Mission, id, running);
        if (!move.Done) return move.Refused;
        Update(mission, r => r with { Mission = move.Mission });
        Log(record, head, "pause", ActionClass.Agents, move.Mission.Heads.First(h => h.Id == id).State.ToString());
        return null;
    }

    /// <summary>Resume one head: a cancelled pause, a paused head back to where it was, a stopped head started again.</summary>
    public string? ResumeHead(Guid mission, Guid head)
    {
        if (Find(mission) is not { } record) return NoSuchMission;
        var id = HeadId(head);
        bool whole;
        lock (gate) whole = LiveOf(mission).WholeStop;
        if (record.Mission.State is MissionState.Stopping or MissionState.Stopped && whole)
            return "The task was stopped as a whole; resume the task.";
        var move = MissionMachine.ResumeHead(record.Mission, id);
        if (!move.Done) return move.Refused;
        if (record.Mission.State is MissionState.Paused or MissionState.Stopped && HoldsStopKey(move.Mission.State) && o.StopKey is { } key && !HasLease(mission))
        {
            if (!key.TryAcquire(StopScope(mission), out var got)) return ControlWords.ResumeNeedsStopKey(key.Chord);
            lock (gate) LiveOf(mission).Lease = got;
        }
        Update(mission, r => r with { Mission = move.Mission });
        ApplyHeadResumes(mission, record.Mission, move.Mission);
        Log(record, head, "resume", ActionClass.Agents, move.Mission.Heads.First(h => h.Id == id).State.ToString());
        return null;
    }

    /// <summary>
    /// Stop one head: its turn is cut and made again on resume. Its pending provider approvals are answered Deny (asked again
    /// when the cut turn runs again), its other cards held; the heads that wait on it stay queued (the task may sum to Stopped).
    /// </summary>
    public string? StopHead(Guid mission, Guid head)
    {
        if (Find(mission) is not { } record) return NoSuchMission;
        var id = HeadId(head);
        var open = o.Agents.TurnRunning(head) || (o.Jobs.Existing(head)?.LiveProcesses ?? 0) > 0;
        var move = MissionMachine.StopHead(record.Mission, id, open);
        if (!move.Done) return move.Refused;
        Update(mission, r => r with { Mission = move.Mission });
        List<(Guid Thread, ApprovalRequest Request)> cut;
        lock (gate)
        {
            cut = [.. pendingRequests.Where(p => p.Key.Thread == head).Select(p => (p.Key.Thread, p.Value.Request))];
            foreach (var (thread, request) in cut) pendingRequests.Remove((thread, request.Id));
        }
        foreach (var (thread, request) in cut) o.Agents.Resolve(thread, request, approve: false);
        foreach (var card in o.Queue.Of(mission).Where(c => c.ThreadId == head && c.Kind is DecisionKind.Approval or DecisionKind.Question && c.RequestId is not null))
            o.Queue.Withdraw(card.Id, card.Kind == DecisionKind.Approval ? Withdrawn : WithdrawnQuestion);
        o.Queue.Hold(c => c.ThreadId == head, DecisionHold.Stop);
        o.Agents.Interrupt(head);
        o.Agents.StopHead(head);
        o.Jobs.Terminate(head);
        o.Gate.Leave(head);
        Log(record, head, "stop", ActionClass.Agents, "stop sent to head");
        CheckStopping();
        if (Find(mission)?.Mission.Heads.Any(h => h.State == HeadState.Stopping) == true) stopTimer ??= o.Time.CreateTimer(_ => OnMain(CheckStopping), null, StopCheck, StopCheck);
        return null;
    }

    /// <summary>
    /// What a Resume does for the heads that came back: a head whose finish a Pause held is finished now; a head that was
    /// working goes on behind the gate; a queued one is woken (AdmitHead waits on it).
    /// </summary>
    private void ApplyHeadResumes(Guid id, Mission before, Mission after)
    {
        foreach (var head in after.Heads)
        {
            if (before.Heads.FirstOrDefault(h => h.Id == head.Id) is not { } was || was.State == head.State) continue;
            if (head.State is not (HeadState.Working or HeadState.Queued) || was.State is HeadState.Queued or HeadState.Working or HeadState.Done) continue;
            if (!Guid.TryParse(head.Id, out var thread)) continue;
            TurnStatus status;
            bool held;
            lock (gate) held = LiveOf(id).HeldFinish.Remove(thread, out status);
            if (held)
            {
                // Its turn ended while it was paused: Hydra finishes it now and tells the lead.
                o.Agents.FinishHeld(thread, status);
                continue;
            }
            if (head.State != HeadState.Working || o.Agents.TurnRunning(thread)) continue;
            var provider = Find(id)?.Provider ?? ProviderKind.Claude;
            var token = LiveOf(id).Launch.Token;
            _ = Admit(id, thread, provider, token).ContinueWith(_ => OnMain(() =>
            {
                if (Find(id)?.Mission.Heads.FirstOrDefault(h => h.Id == head.Id) is not { State: HeadState.Working }) return;
                if (!o.Agents.TurnRunning(thread)) o.Agents.RestartHead(thread, HeadContinueText);
            }), TaskScheduler.Default);
        }
    }

    // ---- Stop's flags and confirmation (12.6) ----

    /// <summary>
    /// Stop all's first 50 ms (12.6): only flags, callable from any thread. Every mission's queued steps are dropped (its launch
    /// token is cancelled, so a lead waiting at the gate or for the stop key never starts), and nothing else is touched: the
    /// interrupts, the jobs and the cards follow on the main thread (<see cref="Stop"/>).
    /// </summary>
    public int FlagStop()
    {
        List<CancellationTokenSource> sources;
        lock (gate) sources = [.. live.Values.Select(l => l.Launch)];
        foreach (var source in sources) source.Cancel();
        return sources.Count;
    }

    /// <summary>
    /// The agents a stop is ending, each with whether it is confirmed: the mission's chats that have a turn or a live
    /// process (or are being stopped now). Used for the strip after Stop all and for "Still stopping".
    /// </summary>
    public IReadOnlyList<AgentStopLine> StoppingAgents(Func<MissionRecord, bool>? scope = null)
    {
        var lines = new List<AgentStopLine>();
        foreach (var record in Missions.Where(r => scope?.Invoke(r) ?? true))
        {
            var l = LiveOf(record.Id);
            foreach (var thread in Threads(record))
            {
                var forced = false;
                lock (gate) forced = l.ForceEnded.Contains(thread);
                var settled = forced || (!o.Agents.TurnRunning(thread) && (o.Jobs.Existing(thread)?.LiveProcesses ?? 0) == 0);
                lines.Add(new AgentStopLine(record.Id, thread, AgentName(record.Id, thread), settled));
            }
        }
        return lines;
    }

    /// <summary>
    /// Force end (12.6 "Afterwards"): an agent not confirmed within 5 s is ended outright: its job is terminated and let go, its
    /// session ended, its slot freed, and it counts as stopped whatever the provider says.
    /// </summary>
    public bool ForceEnd(Guid thread)
    {
        if (MissionOf(thread) is not { } record) return false;
        lock (gate) LiveOf(record.Id).ForceEnded.Add(thread);
        o.Jobs.End(thread);
        o.Agents.StopSession(thread);
        o.Gate.Leave(thread);
        Log(record, thread, "force_end", ActionClass.Agents, "ended outright");
        CheckStopping();
        return true;
    }

    // ---- Revert, Restore, Restart (12.6b) ----

    /// <summary>
    /// Revert changes: only on a stopped task that changed files. The work comes out of its workspace and is kept, restorable
    /// from the Activity log for 7 days; its open cards are withdrawn; the owner's own files are never touched. Null on success.
    /// </summary>
    public async Task<string?> Revert(Guid id)
    {
        if (Find(id) is not { } record) return NoSuchMission;
        var check = MissionMachine.Revert(record.Mission);
        if (!check.Done) return check.Refused;
        if (record.Workspace is not { } workspace) return "There is no workspace to revert.";
        var l = LiveOf(id);
        lock (gate)
        {
            if (l.Reverting) return "Its changes are already being reverted.";
            l.Reverting = true;
        }
        // Intent is journaled before the work comes out, the outcome after: a crash in between is found at the next start.
        var gone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OnMain(() =>
        {
            Update(id, r => r with { RevertIntent = true });
            gone.TrySetResult();
        });
        await gone.Task.ConfigureAwait(false);
        RevertedWork reverted;
        try { reverted = await o.Workspaces.Revert(workspace, id, o.Time.GetUtcNow(), record.Mission.ChangedFiles).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            lock (gate) l.Reverting = false;
            // A revert that failed part way may have left its ref: look, and if it did, complete the record rather than lose it.
            if (await FindInterrupted(id, workspace).ConfigureAwait(false) is { } done)
            {
                await CompleteRevert(id, done).ConfigureAwait(false);
                return null;
            }
            OnMain(() => Update(id, r => r with { RevertIntent = false }));
            return "Not reverted: " + error.Message;
        }
        lock (gate) l.Reverting = false;
        await CompleteRevert(id, reverted).ConfigureAwait(false);
        return null;
    }

    // The outcome of a revert, recorded: the task is Reverted, its work kept, its open cards withdrawn.
    private Task CompleteRevert(Guid id, RevertedWork reverted)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OnMain(() =>
        {
            foreach (var card in o.Queue.Of(id)) o.Queue.Withdraw(card.Id, "Withdrawn by Revert changes.");
            Update(id, r =>
            {
                // What an interrupted revert found does not know how many files it moved: the record does.
                var kept = reverted with { ChangedFiles = reverted.ChangedFiles > 0 ? reverted.ChangedFiles : r.Mission.ChangedFiles };
                var move = MissionMachine.Revert(r.Mission with { ChangedFiles = Math.Max(1, kept.ChangedFiles) });
                // The work is kept either way; the task becomes Reverted only if it is still the stopped task it was.
                return move.Done
                    ? r with { Mission = move.Mission with { WorktreePath = null }, Workspace = null, RevertedWork = kept, MergePending = false, RevertIntent = false }
                    : r with { RevertedWork = kept, RevertIntent = false };
            });
            if (Find(id) is { } now)
                Log(now, null, "revert", ActionClass.Work,
                    string.Create(CultureInfo.InvariantCulture, $"reverted {ControlWords.Plural(now.RevertedWork?.ChangedFiles ?? 0, "changed file")}; kept until {reverted.Expires:yyyy-MM-dd}"), reverted.Ref);
            done.TrySetResult();
        });
        return done.Task;
    }

    private async Task<RevertedWork?> FindInterrupted(Guid id, MissionWorkspace workspace)
    {
        try { return await o.Workspaces.FindReverted(workspace, id).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Faulted?.Invoke("What an interrupted revert did could not be read: " + error.Message);
            return null;
        }
    }

    /// <summary>
    /// The crash guard of a Revert (12.6b): a mission that came back from the journal with a Revert begun and not recorded. What the
    /// revert did is looked for (its hidden ref, or its set-aside copy) and the record completed; when it did nothing, the intent is
    /// cleared and the mission is as it was. Never run again blind.
    /// </summary>
    private async Task RecoverRevert(MissionRecord record)
    {
        if (record.Workspace is not { } workspace) return;
        if (await FindInterrupted(record.Id, workspace).ConfigureAwait(false) is { } done) await CompleteRevert(record.Id, done).ConfigureAwait(false);
        else OnMain(() => Update(record.Id, r => r with { RevertIntent = false }));
    }

    /// <summary>The Activity log's restore (12.6b): puts reverted work back, within 7 days, while the task has not been restarted.</summary>
    public async Task<string?> RestoreReverted(Guid id)
    {
        if (Find(id) is not { } record) return NoSuchMission;
        if (record.Mission.State != MissionState.Stopped || !record.Mission.Reverted || record.RevertedWork is not { } reverted)
            return "Only a stopped task whose changes were reverted can have them restored.";
        if (!MissionWorkspaces.Restorable(reverted, o.Time.GetUtcNow())) return "The reverted work was kept for 7 days and is gone.";
        MissionWorkspace restored;
        try { restored = await o.Workspaces.Restore(reverted, id).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException) { return "Not restored: " + error.Message; }
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnMain(() =>
        {
            Update(id, r => r with
            {
                Workspace = restored,
                RevertedWork = null,
                Mission = r.Mission with { Reverted = false, ChangedFiles = reverted.ChangedFiles, WorktreePath = restored.WorkingFolder, Branch = restored.Branch },
            });
            Log(Find(id) ?? record, null, "restore", ActionClass.Work, "restored the reverted changes", reverted.Ref);
            done.TrySetResult(null);
        });
        return await done.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Restart: from Failed, or from Stopped once its changes are reverted. It begins again from the brief on a fresh branch;
    /// a failed branch stays 7 days for review. Under a Stop all or Pause all hold it starts paused.
    /// </summary>
    public string? Restart(Guid id)
    {
        if (Find(id) is not { } record) return NoSuchMission;
        var l = LiveOf(id);
        bool reverting;
        lock (gate) reverting = l.Reverting;
        if (reverting) return "Its changes are being reverted; Restart opens once that is done.";
        var move = MissionMachine.Restart(record.Mission);
        if (!move.Done) return move.Refused;
        var old = Threads(record);
        ITimer? timer;
        lock (gate)
        {
            l.Launch.Cancel();
            timer = l.ReleaseTimer;
            foreach (var thread in old) threadMission.Remove(thread);
            live[id] = new Live();
        }
        timer?.Dispose();
        ReleaseLease(id);
        foreach (var thread in old)
        {
            o.Jobs.End(thread);
            o.Gate.Leave(thread);
        }
        var now = o.Time.GetUtcNow();
        Update(id, r => r with
        {
            Mission = move.Mission with { ThreadIds = ValueList<Guid>.Empty, WorktreePath = null, Branch = null },
            // The failed (or stopped-after-failure) branch stays for review; a reverted task has no workspace left.
            Previous = r.Workspace is { } workspace ? r.Previous.Appending(new PreviousWorkspace(workspace, now)) : r.Previous,
            Workspace = null,
            Attempt = r.Attempt + 1,
            LeadThread = null,
            ReviewerThread = null,
            Report = null,
            Reason = null,
            Waiting = null,
            PendingRedirect = null,
            MergePending = false,
            Ended = null,
            AgentsReleased = false,
            Interrupted = false,
        });
        Log(record, null, "restart", ActionClass.Agents, "restarted from the brief on a fresh branch");
        if (Find(id) is { } restarted) Reserve?.MissionBegan(restarted, null);
        if (o.Hold?.StartsPaused(record.Mission.Origin) == true)
        {
            Update(id, r => r with { Mission = MissionMachine.Pause(r.Mission, turnRunning: false, PauseCause.PauseAll).Mission });
            return null;
        }
        _ = Launch(id);
        return null;
    }

    /// <summary>
    /// The 7-day sweeps of what Revert and Restart keep: the workspace of an attempt a Restart left, and the work a Revert took out
    /// (its hidden ref or set-aside copy). Called with CleanUpWorkspaces.
    /// </summary>
    private async Task SweepKept()
    {
        var now = o.Time.GetUtcNow();
        foreach (var record in Missions)
        {
            foreach (var previous in record.Previous.Where(p => MissionWorkspaces.Due(p.At, kept: false, now)))
            {
                try { await o.Workspaces.Discard(previous.Workspace).ConfigureAwait(false); }
                catch (Exception error) when (error is not OperationCanceledException) { continue; }
                OnMain(() => Update(record.Id, r => r with { Previous = new ValueList<PreviousWorkspace>(r.Previous.Where(p => p != previous)) }));
            }
            if (record.RevertedWork is { } reverted && MissionWorkspaces.RevertedDue(reverted, now))
            {
                try { await o.Workspaces.DropReverted(reverted, record.Id).ConfigureAwait(false); }
                catch (Exception error) when (error is not OperationCanceledException) { continue; }
                OnMain(() => Update(record.Id, r => r with { RevertedWork = null }));
            }
        }
    }
}
