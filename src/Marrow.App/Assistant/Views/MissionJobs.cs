// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.3 "Background tasks and the mission board share one data model", 12.6b "One model,
// one set of words", 16.2 "AS-8: the board reads missions through an IMissionJobs interface backed by the Assistant's own list";
// ED-NEW-dextra-as8-3). No Swift twin.
// What every task list reads: the Board's cards, the home's Result list and the sidebar row. A job is an immutable snapshot of one
// mission as the owner sees it (its state, label, slots, mark, detail line, the head at work and what Stop would cut), built once
// per change, so the views only draw. The interface is shaped like web surfaces' IBackgroundJobSource (Name, Snapshot, Stop,
// Pause, Resume, Dismiss), so the board moves onto BackgroundJobs with one binding when the pane workspace lands.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>One account's share of a task's spend ("Codex A 2.1%").</summary>
public sealed record MissionSpend(string Account, double Percent);

/// <summary>One Hydra head of a task, as its line shows it (12.6b "Heads").</summary>
public sealed record HeadJob(Guid Thread, string Name, HeadState State)
{
    public ControlSlots Slots { get; init; } = MissionStates.Slots(State);

    /// <summary>The short note after the name: "finishing its turn", "waits on Subtitles", "held by your reserve; answer its card".</summary>
    public string Note { get; init; } = "";

    /// <summary>What it does now, while its turn runs ("generating the voiceover (3:42 of script)").</summary>
    public string? Activity { get; init; }

    /// <summary>What a Stop now would cut (a turn in flight that says what it makes); empty when nothing would be lost.</summary>
    public string Risk { get; init; } = "";

    /// <summary>The heads that wait on this one: its Stop asks when there are any.</summary>
    public ValueList<string> Waiters { get; init; } = ValueList<string>.Empty;

    public bool HeldByReserve { get; init; }

    public ArmillaState Mark => ArmillaPoses.For(State);

    public ArmillaGlyph Glyph => ArmillaPoses.GlyphFor(State);
}

/// <summary>One task (a mission) as every list shows it.</summary>
public sealed record MissionJob(Guid Id, string Title, MissionState State)
{
    public string Owner { get; init; } = AssistantNames.Assistant;

    public ControlSlots Slots { get; init; } = MissionStates.Slots(State, false, false);

    public BoardGroup Group { get; init; } = MissionStates.Group(State);

    public bool Dimmed { get; init; } = MissionStates.Dimmed(State);

    /// <summary>The one line under the title (TaskWords.Detail).</summary>
    public string Detail { get; init; } = "";

    /// <summary>What the head at work does now, else the lead; the hover on the mark and the card's activity line.</summary>
    public string? Activity { get; init; }

    /// <summary>What a Stop now would cut; empty when Stop loses nothing and acts at once.</summary>
    public string Risk { get; init; } = "";

    public ValueList<HeadJob> Heads { get; init; } = ValueList<HeadJob>.Empty;

    public int ChangedFiles { get; init; }

    public bool Reverted { get; init; }

    public string? Branch { get; init; }

    /// <summary>The 5 s "Still pausing" note (12.6b, r4 G9); null while it is not due.</summary>
    public string? StillPausing { get; init; }

    /// <summary>The names the still-pausing note waits for.</summary>
    public ValueList<string> PausingWaits { get; init; } = ValueList<string>.Empty;

    /// <summary>An answer to one of its reversible cards is kept and runs on resume (12.6b, r4 G3).</summary>
    public bool AnswerKept { get; init; }

    public DateTimeOffset Created { get; init; }

    public DateTimeOffset? Ended { get; init; }

    /// <summary>How many agents work for it now (the lead and its heads that are not done).</summary>
    public int Agents { get; init; }

    public string? ReviewedBy { get; init; }

    public ValueList<MissionSpend> Spend { get; init; } = ValueList<MissionSpend>.Empty;

    /// <summary>The share done, 0 to 1, for the progress track.</summary>
    public double Progress { get; init; }

    public Guid? LeadThread { get; init; }

    public ArmillaState Mark => ArmillaPoses.For(State);

    public ArmillaGlyph Glyph => ArmillaPoses.GlyphFor(State);

    /// <summary>A task that runs no turn now pauses at once; one that runs a turn finishes it first.</summary>
    public bool Runs => MissionStates.IsRunning(State);
}

/// <summary>
/// The tasks a board shows and the controls it calls (16.2's fallback, bound later onto BackgroundJobs). Every member runs on the UI
/// thread; <see cref="Changed"/> is raised there.
/// </summary>
public interface IMissionJobs
{
    /// <summary>The source's name, for logs and tests.</summary>
    string Name { get; }

    /// <summary>Every task now, oldest first. Cheap: the views call it on each change and on their 4 Hz value tick.</summary>
    IReadOnlyList<MissionJob> Snapshot(DateTimeOffset now);

    /// <summary>Raised on the UI thread after any task changed.</summary>
    event Action? Changed;

    /// <summary>Null when taken, else the line that says why not.</summary>
    string? Pause(Guid job);

    string? Resume(Guid job);

    string? Stop(Guid job);

    string? Restart(Guid job);

    Task<string?> Revert(Guid job);

    string? PauseHead(Guid job, Guid head);

    string? ResumeHead(Guid job, Guid head);

    string? StopHead(Guid job, Guid head);

    /// <summary>The owner cleared a finished task from the list.</summary>
    void Dismiss(Guid job);
}

/// <summary>
/// The Assistant's own list as jobs: the runner's missions, its decision queue (kept answers), the agents' activity and the reserve's
/// spend. Changes from the runner, the queue and the reserve are folded into one Changed on the UI thread.
/// </summary>
public sealed class RunnerMissionJobs : IMissionJobs, IDisposable
{
    private readonly MissionControls controls;
    private readonly DecisionQueue queue;
    private readonly IMissionAgents agents;
    private readonly ReserveGuard? reserve;
    private readonly Func<string, string> assistantName;
    private readonly Action<Action> post;
    private readonly HashSet<Guid> dismissed = [];
    private int queued;

    /// <param name="assistantName">The owner's name for an assistant id (Dextra, or an Astur's name).</param>
    /// <param name="post">Runs work on the UI thread (default: the dispatcher's Post).</param>
    public RunnerMissionJobs(MissionControls controls, DecisionQueue queue, IMissionAgents agents, ReserveGuard? reserve = null,
        Func<string, string>? assistantName = null, Action<Action>? post = null)
    {
        this.controls = controls;
        this.queue = queue;
        this.agents = agents;
        this.reserve = reserve;
        this.assistantName = assistantName ?? (_ => AssistantNames.Assistant);
        this.post = post ?? (work => Avalonia.Threading.Dispatcher.UIThread.Post(work));
        controls.Runner.Changed += OnRunner;
        queue.Changed += OnAny;
        if (reserve is not null) reserve.Changed += OnAny;
    }

    public string Name => "Dextra's missions";

    public event Action? Changed;

    public static RunnerMissionJobs For(AssistantController controller, IMissionAgents agents, Func<string, string>? assistantName = null) =>
        new(controller.Controls, controller.Queue, agents, controller.Reserve, assistantName);

    private void OnRunner(MissionRecord _) => OnAny();

    private void OnAny()
    {
        // one Changed for a burst of changes, on the UI thread
        if (Interlocked.Exchange(ref queued, 1) == 1) return;
        post(() =>
        {
            Volatile.Write(ref queued, 0);
            Changed?.Invoke();
        });
    }

    public IReadOnlyList<MissionJob> Snapshot(DateTimeOffset now)
    {
        var jobs = new List<MissionJob>();
        foreach (var record in controls.Runner.Missions)
        {
            if (dismissed.Contains(record.Id)) continue;
            jobs.Add(Of(record));
        }
        return jobs;
    }

    private MissionJob Of(MissionRecord record)
    {
        var m = record.Mission;
        var heads = new List<HeadJob>();
        var byId = m.Heads.ToDictionary(h => h.Id, StringComparer.Ordinal);
        foreach (var h in m.Heads)
        {
            var thread = Guid.TryParse(h.Id, out var t) ? t : Guid.Empty;
            var running = thread != Guid.Empty && h.State is HeadState.Working or HeadState.Pausing && agents.TurnRunning(thread);
            var activity = running ? Clean(agents.Activity(thread)) : null;
            var waiters = m.Heads.Where(o => o.State is not (HeadState.Done or HeadState.Stopped) && o.DependsOn.Contains(h.Id)).Select(o => o.Name);
            var heldBy = h.State == HeadState.Queued
                ? h.DependsOn.Select(d => byId.GetValueOrDefault(d)).FirstOrDefault(d => d is { State: HeadState.Pausing or HeadState.Paused or HeadState.Stopping or HeadState.Stopped })
                : null;
            heads.Add(new HeadJob(thread, h.Name, h.State)
            {
                Slots = m.Reverted ? new ControlSlots(SlotA.Resume, false, SlotB.None, false) : MissionStates.Slots(h.State, h.HeldByReserve),
                Activity = activity,
                Risk = TaskWords.Risk(activity),
                Waiters = new ValueList<string>(waiters),
                HeldByReserve = h.HeldByReserve,
                Note = TaskWords.HeadNote(h.State, h.HeldByReserve, heldBy?.Name, activity, pausingLong: m.State == MissionState.Pausing && controls.StillPausing(m.Id) is not null),
            });
        }
        // the line, the hover and the risk follow the head at work (r4 B3); with no heads, the lead's own turn
        var atWork = heads.Where(h => h.State is HeadState.Working or HeadState.Pausing && h.Activity is not null).ToList();
        string? leadActivity = null;
        if (heads.Count == 0 && record.LeadThread is { } lead && (MissionStates.IsRunning(m.State) || m.State == MissionState.Pausing) && agents.TurnRunning(lead))
            leadActivity = Clean(agents.Activity(lead));
        var activityNow = atWork.Count > 0 ? atWork[0].Activity : leadActivity;
        var risk = heads.Count > 0 ? string.Join(" ", atWork.Select(h => h.Risk).Where(r => r.Length > 0)) : TaskWords.Risk(leadActivity);
        var kept = queue.Of(m.Id).Any(d => d.Kept is not null && d.State is DecisionState.Open or DecisionState.Held);
        var note = controls.StillPausing(m.Id);
        var job = new MissionJob(m.Id, m.Title, m.State)
        {
            Owner = assistantName(m.AssistantId),
            Slots = m.Slots,
            Group = m.Group,
            Dimmed = MissionStates.Dimmed(m.State, m.PausedBy == PauseCause.Reserve),
            Activity = activityNow,
            Risk = risk,
            Heads = new ValueList<HeadJob>(heads),
            ChangedFiles = m.ChangedFiles,
            Reverted = m.Reverted,
            Branch = m.Branch ?? record.Workspace?.Branch,
            StillPausing = note?.Text,
            PausingWaits = note?.Waiting ?? ValueList<string>.Empty,
            AnswerKept = kept,
            Created = record.Created,
            Ended = record.Ended,
            Agents = (record.LeadThread is null || m.State is MissionState.Done or MissionState.Stopped ? 0 : 1) + m.Heads.Count(h => h.State is not (HeadState.Done or HeadState.Stopped)),
            ReviewedBy = record.ReviewedBy,
            Spend = reserve is null ? ValueList<MissionSpend>.Empty : new ValueList<MissionSpend>(reserve.SpendOf(m.Id).Select(s => new MissionSpend(s.Account, s.Percent))),
            Progress = TaskWords.Progress(m),
            LeadThread = record.LeadThread,
        };
        return job with
        {
            Detail = TaskWords.Detail(job, m, record.Waiting, record.Reason, record.AgentsReleased, record.Interrupted),
        };
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public string? Pause(Guid job) => controls.Pause(job);

    public string? Resume(Guid job) => controls.Resume(job);

    public string? Stop(Guid job) => controls.Stop(job);

    public string? Restart(Guid job) => controls.Restart(job);

    public Task<string?> Revert(Guid job) => controls.Revert(job);

    public string? PauseHead(Guid job, Guid head) => controls.PauseHead(job, head);

    public string? ResumeHead(Guid job, Guid head) => controls.ResumeHead(job, head);

    public string? StopHead(Guid job, Guid head) => controls.StopHead(job, head);

    public void Dismiss(Guid job)
    {
        if (controls.Runner.Find(job) is { Mission.State: MissionState.Done } && dismissed.Add(job)) Changed?.Invoke();
    }

    public void Dispose()
    {
        controls.Runner.Changed -= OnRunner;
        queue.Changed -= OnAny;
        if (reserve is not null) reserve.Changed -= OnAny;
    }
}
