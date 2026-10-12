// PORT-ONLY: Dextra (DESIGN.md rev 5, 3.1, 12.6b; unit AS-1; ED-NEW-dextracore-5). No Swift twin.
// A mission and its Hydra heads, the moves of 12.6b (Pause, Resume, Stop, Revert, Restart for a task and for a head)
// and the rule that sums a task's heads into one state. Pure: MissionControls (AS-6) and MissionRunner (AS-5) call
// these and do the real work (turn boundaries, Interrupt, Job Objects, the worktree).
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>Where a mission came from (3.1).</summary>
public enum MissionOrigin { Owner, Duty, Delegation }

/// <summary>What paused a task: the owner (its own Pause or Pause all) or a reserve (11).</summary>
public enum PauseCause { Owner, PauseAll, Reserve }

/// <summary>What stopped a task: its own Stop or Stop all. Resume all resumes only what Stop all stopped.</summary>
public enum StopCause { Owner, StopAll }

/// <summary>A Hydra head of a mission (12.6b).</summary>
public sealed record MissionHead(string Id, string Name, HeadState State)
{
    /// <summary>The state it paused or stopped from, to return to on Resume.</summary>
    public HeadState? ResumeTo { get; init; }

    /// <summary>The owner stopped this head on its own: a task's Resume and Resume all leave it stopped.</summary>
    public bool StoppedByHand { get; init; }

    /// <summary>The heads it waits on (from the Hydra plan).</summary>
    public ValueList<string> DependsOn { get; init; } = ValueList<string>.Empty;

    /// <summary>A reserve holds it: answering the reserve's card resumes it, not Resume.</summary>
    public bool HeldByReserve { get; init; }
}

/// <summary>A mission (3.1), as far as its state and controls go.</summary>
public sealed record Mission(Guid Id, string Title, string AssistantId, MissionOrigin Origin, MissionState State)
{
    /// <summary>The state it paused or stopped from.</summary>
    public MissionState? ResumeTo { get; init; }

    public PauseCause? PausedBy { get; init; }

    public StopCause? StoppedBy { get; init; }

    /// <summary>Revert changes ran: the task waits for Restart.</summary>
    public bool Reverted { get; init; }

    /// <summary>How many files its worktree changed.</summary>
    public int ChangedFiles { get; init; }

    public ValueList<MissionHead> Heads { get; init; } = ValueList<MissionHead>.Empty;

    public Guid? ParentMissionId { get; init; }

    public ValueList<Guid> ChildMissionIds { get; init; } = ValueList<Guid>.Empty;

    /// <summary>The lead chats and heads working on it.</summary>
    public ValueList<Guid> ThreadIds { get; init; } = ValueList<Guid>.Empty;

    /// <summary>The mission's taint (12.4): cleared when it ends.</summary>
    public TaintContext TaintContext { get; init; } = TaintContext.Clean;

    /// <summary>Reviewer rounds so far (two, then Failed: AS-5).</summary>
    public int ReviewRounds { get; init; }

    public string? WorktreePath { get; init; }

    public string? Branch { get; init; }

    /// <summary>Stopped after it failed: Stop kept its branch, and only Restart begins it again (12.6b, r4 G6).</summary>
    public bool StoppedAfterFailure => State == MissionState.Stopped && ResumeTo == MissionState.Failed;

    public ControlSlots Slots => MissionStates.Slots(State, ChangedFiles > 0, Reverted, StoppedAfterFailure);

    public BoardGroup Group => MissionStates.Group(State, ResumeTo, PausedBy == PauseCause.Reserve);
}

/// <summary>The outcome of a move: the mission after it, or the reason it was refused (and the mission unchanged).</summary>
public sealed record MissionMove(Mission Mission, string? Refused = null)
{
    public bool Done => Refused is null;
}

/// <summary>The moves of 12.6b for a task and for each head.</summary>
public static class MissionMachine
{
    private static MissionMove No(Mission m, string reason) => new(m, reason);

    /// <summary>
    /// Pause: at the next turn boundary. A task with a turn in flight shows Pausing until every working head reaches its
    /// boundary; queued heads and a task that runs no turn (Needs you, Blocked) pause at once.
    /// </summary>
    public static MissionMove Pause(Mission m, bool turnRunning, PauseCause cause = PauseCause.Owner)
    {
        switch (m.State)
        {
            case MissionState.Failed: return No(m, "A failed task has Restart, not Pause: no turn runs.");
            case MissionState.Done: return No(m, "The task is done.");
            case MissionState.Pausing or MissionState.Paused: return No(m, "The task is already pausing or paused.");
            case MissionState.Stopping or MissionState.Stopped: return No(m, "The task is stopping or stopped.");
        }
        var from = m.State;
        if (m.Heads.Count > 0)
        {
            var heads = m.Heads.Select(h => h.State switch
            {
                HeadState.Working => h with { State = turnRunning ? HeadState.Pausing : HeadState.Paused, ResumeTo = HeadState.Working },
                HeadState.Queued or HeadState.Blocked => h with { State = HeadState.Paused, ResumeTo = h.State },
                _ => h,
            }).ToList();
            var paused = m with { Heads = new ValueList<MissionHead>(heads), ResumeTo = from, PausedBy = cause };
            return new MissionMove(paused with { State = heads.Any(h => h.State == HeadState.Pausing) ? MissionState.Pausing : MissionState.Paused });
        }
        var next = MissionStates.IsRunning(from) && turnRunning ? MissionState.Pausing : MissionState.Paused;
        return new MissionMove(m with { State = next, ResumeTo = from, PausedBy = cause });
    }

    /// <summary>A turn ended: a pausing head (or a task with no heads) is now paused.</summary>
    public static MissionMove TurnBoundary(Mission m, string? headId = null)
    {
        if (m.Heads.Count == 0)
            return m.State == MissionState.Pausing ? new MissionMove(m with { State = MissionState.Paused }) : new MissionMove(m);
        var heads = m.Heads.Select(h => (headId is null || h.Id == headId) && h.State == HeadState.Pausing ? h with { State = HeadState.Paused } : h);
        return new MissionMove(Sum(m with { Heads = new ValueList<MissionHead>(heads) }));
    }

    /// <summary>
    /// Resume. On Pausing it cancels the pause. On Paused each head returns to the state it paused from, and the task
    /// to what it was doing, except that a reserve's pause and a held irreversible card bring the task back to Needs you
    /// (their card resumes it). On Stopped it continues from the saved state, unless the changes were reverted
    /// (then it is Restart). A head the owner stopped on its own stays stopped unless the task cannot go on without it
    /// (and only when <paramref name="resumeNeededHandStopped"/> holds: Resume all leaves such a head stopped).
    /// </summary>
    public static MissionMove Resume(Mission m, bool heldIrreversibleCard = false, bool resumeNeededHandStopped = true)
    {
        switch (m.State)
        {
            case MissionState.Stopping: return No(m, "The task is stopping; Resume opens once it has stopped.");
            case MissionState.Stopped when m.Reverted: return No(m, "Its changes were reverted: Restart begins again from the brief.");
            case MissionState.Stopped when m.StoppedAfterFailure: return No(m, "It failed: Restart begins again from the brief.");
            case MissionState.Pausing or MissionState.Paused or MissionState.Stopped: break;
            default: return No(m, "There is nothing to resume.");
        }
        var back = m.ResumeTo ?? MissionState.Working;
        var heads = m.Heads.Select(h => h.State switch
        {
            HeadState.Pausing => h with { State = HeadState.Working, ResumeTo = null },
            HeadState.Paused when !h.HeldByReserve => h with { State = h.ResumeTo ?? HeadState.Queued, ResumeTo = null },
            HeadState.Stopped when !h.StoppedByHand => h with { State = h.ResumeTo ?? HeadState.Queued, ResumeTo = null },
            _ => h,
        }).ToList();
        // A head stopped by hand that a waiting head needs is resumed too, when nothing else could run. Resume all never
        // does (it leaves a head stopped on its own stopped and names it; resumeNeededHandStopped is false there).
        if (resumeNeededHandStopped && heads.Count > 0 && TaskRollup.Sum(heads).Sum != TaskSum.Running)
        {
            var needed = heads.Where(h => h.State != HeadState.Done).SelectMany(h => h.DependsOn).ToHashSet(StringComparer.Ordinal);
            heads = heads.Select(h => h.State == HeadState.Stopped && h.StoppedByHand && needed.Contains(h.Id)
                ? h with { State = h.ResumeTo ?? HeadState.Queued, ResumeTo = null, StoppedByHand = false }
                : h).ToList();
        }
        var resumed = m with
        {
            Heads = new ValueList<MissionHead>(heads),
            ResumeTo = null,
            PausedBy = null,
            StoppedBy = null,
        };
        if (m.PausedBy == PauseCause.Reserve || heldIrreversibleCard)
            return new MissionMove(resumed with { State = MissionState.NeedsYou });
        resumed = resumed with { State = back };
        return new MissionMove(heads.Count > 0 ? Sum(resumed) : resumed);
    }

    /// <summary>
    /// Stop: in any live state, including Pausing and Paused. Stopping until every agent confirms, or Stopped at once
    /// when no agent is open. The state it stopped from is the one it paused from, when it was paused.
    /// </summary>
    public static MissionMove Stop(Mission m, bool agentsOpen, StopCause cause = StopCause.Owner)
    {
        if (m.State is MissionState.Stopping or MissionState.Stopped) return No(m, "The task is already stopping or stopped.");
        if (m.State == MissionState.Done) return No(m, "The task is done.");
        var from = m.State is MissionState.Pausing or MissionState.Paused ? m.ResumeTo ?? MissionState.Working : m.State;
        var heads = m.Heads.Select(h => h.State switch
        {
            HeadState.Done or HeadState.Stopped or HeadState.Stopping => h,
            _ => h with
            {
                State = agentsOpen ? HeadState.Stopping : HeadState.Stopped,
                ResumeTo = h.State is HeadState.Pausing or HeadState.Paused ? h.ResumeTo ?? HeadState.Queued : h.State,
                StoppedByHand = false,
            },
        });
        return new MissionMove(m with
        {
            State = agentsOpen ? MissionState.Stopping : MissionState.Stopped,
            ResumeTo = from,
            StoppedBy = cause,
            // A reserve's hold survives a Stop: Resume never crosses a floor (12.6b, r4 G7), stopped or not.
            PausedBy = m.PausedBy == PauseCause.Reserve ? PauseCause.Reserve : null,
            Heads = new ValueList<MissionHead>(heads),
        });
    }

    /// <summary>An agent confirmed it stopped: a stopping head (or a task with no heads) is now stopped.</summary>
    public static MissionMove ConfirmStopped(Mission m, string? headId = null)
    {
        if (m.Heads.Count == 0)
            return m.State == MissionState.Stopping ? new MissionMove(m with { State = MissionState.Stopped }) : new MissionMove(m);
        var heads = m.Heads.Select(h => (headId is null || h.Id == headId) && h.State == HeadState.Stopping ? h with { State = HeadState.Stopped } : h).ToList();
        var next = m with { Heads = new ValueList<MissionHead>(heads) };
        if (m.State == MissionState.Stopping)
            return new MissionMove(heads.Any(h => h.State == HeadState.Stopping) ? next : next with { State = MissionState.Stopped });
        return new MissionMove(Sum(next));
    }

    /// <summary>Revert changes: only on a stopped task that changed files (12.6b, r4 B1).</summary>
    public static MissionMove Revert(Mission m)
    {
        if (m.State != MissionState.Stopped || m.ChangedFiles == 0 || m.Reverted)
            return No(m, "Revert changes is only on a stopped task that changed files.");
        return new MissionMove(m with { Reverted = true, ChangedFiles = 0 });
    }

    /// <summary>Restart: from Failed, or from Stopped once its changes are reverted. Begins again from the brief.</summary>
    public static MissionMove Restart(Mission m)
    {
        var allowed = m.State == MissionState.Failed || (m.State == MissionState.Stopped && (m.Reverted || m.StoppedAfterFailure));
        if (!allowed)
            return No(m, m.State == MissionState.Stopped
                ? "Its changes are kept: Resume continues from the saved state."
                : "Restart is for a failed task, or a stopped one after Revert changes.");
        return new MissionMove(m with
        {
            State = MissionState.Planning,
            ResumeTo = null,
            PausedBy = null,
            StoppedBy = null,
            Reverted = false,
            ChangedFiles = 0,
            ReviewRounds = 0,
            Heads = ValueList<MissionHead>.Empty,
        });
    }

    /// <summary>Whether Resume all picks this task up (12.6b, r4 B6).</summary>
    public static bool ResumedByResumeAll(Mission m) => m.State switch
    {
        MissionState.Pausing => true,
        MissionState.Paused => m.PausedBy != PauseCause.Reserve,
        MissionState.Stopped => m.StoppedBy == StopCause.StopAll && !m.Reverted && !m.StoppedAfterFailure && m.PausedBy != PauseCause.Reserve,
        _ => false,
    };

    // ---- heads ----

    /// <summary>A queued head never starts before every head it waits on is done.</summary>
    public static bool CanStart(MissionHead head, IEnumerable<MissionHead> heads)
    {
        if (head.State != HeadState.Queued) return false;
        var byId = heads.ToDictionary(h => h.Id, StringComparer.Ordinal);
        return head.DependsOn.All(d => byId.TryGetValue(d, out var dep) && dep.State == HeadState.Done);
    }

    public static MissionMove StartHead(Mission m, string headId) =>
        Head(m, headId, h => CanStart(h, m.Heads) ? (h with { State = HeadState.Working }, null) : (h, $"{h.Name} waits on the heads before it."));

    public static MissionMove PauseHead(Mission m, string headId, bool turnRunning) =>
        Head(m, headId, h => h.State switch
        {
            HeadState.Working => (h with { State = turnRunning ? HeadState.Pausing : HeadState.Paused, ResumeTo = HeadState.Working }, null),
            HeadState.Queued or HeadState.Blocked => (h with { State = HeadState.Paused, ResumeTo = h.State }, null),
            _ => (h, $"{h.Name} cannot pause while {MissionStates.Label(h.State)}."),
        });

    public static MissionMove ResumeHead(Mission m, string headId)
    {
        // Resuming one head may not undo a task that is stopping, failed, or had its changes reverted, nor a reserve
        // pause: those go through the task's own Resume or Restart (and its card), so Stop all and the reserve hold
        // (12.6b). A task that summed to Stopped from a head stopped by hand is still resumed head by head.
        switch (m.State)
        {
            case MissionState.Stopping:
                return No(m, "The task is stopping; Resume the task once it has stopped.");
            case MissionState.Failed:
                return No(m, "The task failed; Restart it to begin again.");
            case MissionState.Stopped when m.Reverted:
                return No(m, "Its changes were reverted; Restart begins again from the brief.");
        }
        if (m.PausedBy == PauseCause.Reserve) return No(m, "Your reserve holds this task; answer its card.");
        return Head(m, headId, h => h.State switch
        {
            _ when h.HeldByReserve => (h, $"{h.Name} is held by your reserve; answer its card."),
            HeadState.Pausing => (h with { State = HeadState.Working, ResumeTo = null }, null),
            HeadState.Paused or HeadState.Stopped => (h with { State = h.ResumeTo ?? HeadState.Queued, ResumeTo = null, StoppedByHand = false }, null),
            _ => (h, $"{h.Name} has nothing to resume."),
        });
    }

    public static MissionMove StopHead(Mission m, string headId, bool agentOpen) =>
        Head(m, headId, h => h.State switch
        {
            HeadState.Done or HeadState.Stopping or HeadState.Stopped => (h, $"{h.Name} is already done or stopped."),
            _ => (h with
            {
                State = agentOpen ? HeadState.Stopping : HeadState.Stopped,
                ResumeTo = h.State is HeadState.Pausing or HeadState.Paused ? h.ResumeTo ?? HeadState.Queued : h.State,
                StoppedByHand = true,
            }, null),
        });

    public static MissionMove FinishHead(Mission m, string headId) =>
        Head(m, headId, h => h.State == HeadState.Working ? (h with { State = HeadState.Done }, null) : (h, $"{h.Name} is not working."));

    private static MissionMove Head(Mission m, string headId, Func<MissionHead, (MissionHead Head, string? Refused)> move)
    {
        var index = m.Heads.ToList().FindIndex(h => h.Id == headId);
        if (index < 0) return No(m, $"No head {headId} in this task.");
        if (m.State is MissionState.Done) return No(m, "The task is done.");
        var (head, refused) = move(m.Heads[index]);
        if (refused is not null) return No(m, refused);
        var heads = m.Heads.ToList();
        heads[index] = head;
        return new MissionMove(Sum(m with { Heads = new ValueList<MissionHead>(heads) }));
    }

    /// <summary>The task's state after its heads changed (12.6b, "The task's state sums up its heads").</summary>
    public static Mission Sum(Mission m)
    {
        if (m.Heads.Count == 0 || m.State is MissionState.Done or MissionState.Failed) return m;
        var sum = TaskRollup.Sum(m.Heads).Sum;
        var active = MissionStates.IsRunning(m.State) || m.State == MissionState.NeedsYou;
        var cameFrom = active ? m.State : m.ResumeTo;
        if (sum == TaskSum.Running)
        {
            if (active) return m;
            var back = m.ResumeTo is { } r && (MissionStates.IsRunning(r) || r == MissionState.NeedsYou) ? r : MissionState.Working;
            return m with { State = back, ResumeTo = null, PausedBy = null, StoppedBy = null };
        }
        return sum switch
        {
            TaskSum.Stopping => m with { State = MissionState.Stopping, ResumeTo = cameFrom },
            TaskSum.Pausing => m with { State = MissionState.Pausing, ResumeTo = cameFrom, PausedBy = m.PausedBy ?? PauseCause.Owner },
            TaskSum.Paused => m with { State = MissionState.Paused, ResumeTo = cameFrom, PausedBy = m.PausedBy ?? PauseCause.Owner },
            TaskSum.Stopped => m with { State = MissionState.Stopped, ResumeTo = cameFrom, StoppedBy = m.StoppedBy ?? StopCause.Owner },
            TaskSum.Blocked => m with { State = MissionState.Blocked },
            _ => m,
        };
    }
}

/// <summary>What a task's heads sum to.</summary>
public enum TaskSum { Running, Stopping, Pausing, Paused, Stopped, Blocked, Finished, ReserveHeld }

/// <summary>The sum of a task's heads and the line that counts them ("1 of 2 stopped" and "Thumbnail waits on Subtitles").</summary>
public sealed record TaskRollupResult(TaskSum Sum, string Line);

/// <summary>The rule that sums a task's heads into one state (12.6b, r4 B4 and G11).</summary>
public static class TaskRollup
{
    // U+00B7 MIDDLE DOT, the separator of a task's detail line.
    private const string Dot = " \U000000B7 ";

    /// <summary>
    /// Running while any head works, or any queued head can start because everything it waits on is done. Otherwise
    /// the first that applies: any head Stopping, Pausing, Paused, then Stopped; else Blocked. Finished heads and heads a
    /// reserve holds are not counted.
    /// </summary>
    public static TaskRollupResult Sum(IEnumerable<MissionHead> allHeads)
    {
        var heads = allHeads.ToList();
        var counted = heads.Where(h => h.State != HeadState.Done && !h.HeldByReserve).ToList();
        TaskSum sum;
        if (counted.Count == 0) sum = heads.Any(h => h.HeldByReserve && h.State != HeadState.Done) ? TaskSum.ReserveHeld : TaskSum.Finished;
        else if (counted.Any(h => h.State == HeadState.Working || MissionMachine.CanStart(h, heads))) sum = TaskSum.Running;
        else if (counted.Any(h => h.State == HeadState.Stopping)) sum = TaskSum.Stopping;
        else if (counted.Any(h => h.State == HeadState.Pausing)) sum = TaskSum.Pausing;
        else if (counted.Any(h => h.State == HeadState.Paused)) sum = TaskSum.Paused;
        else if (counted.Any(h => h.State == HeadState.Stopped)) sum = TaskSum.Stopped;
        else sum = TaskSum.Blocked;
        return new TaskRollupResult(sum, Line(heads, counted));
    }

    private static string Line(List<MissionHead> heads, List<MissionHead> counted)
    {
        var parts = new List<string>();
        foreach (var (state, word) in new[]
                 {
                     (HeadState.Stopping, "stopping"), (HeadState.Pausing, "pausing"), (HeadState.Paused, "paused"), (HeadState.Stopped, "stopped"),
                 })
        {
            var n = counted.Count(h => h.State == state);
            if (n > 0) parts.Add($"{n} of {counted.Count} {word}");
        }
        var byId = heads.ToDictionary(h => h.Id, StringComparer.Ordinal);
        foreach (var h in counted.Where(h => h.State is HeadState.Queued or HeadState.Blocked))
        {
            var waitsOn = h.DependsOn.Select(d => byId.GetValueOrDefault(d)).FirstOrDefault(d => d is not null && d.State != HeadState.Done);
            if (waitsOn is not null) parts.Add($"{h.Name} waits on {waitsOn.Name}");
        }
        return string.Join(Dot, parts);
    }
}
