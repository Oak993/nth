// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6 "What Stop all does" and "Afterwards"; unit AS-6; ED-NEW-dextra-as6-3). No Swift twin.
// Stop all. Phase 1, on whatever thread the press came from (the chord's own thread, the tray, the UI): only flags. The
// scheduler is held, every mission's queued steps are dropped, each scope the Assistant owns beyond missions (a voice reply,
// a Tactus stream) is told to stop its own work, and the time this took is measured: the one part timed in milliseconds
// (50 ms). Phase 2, on the app's main thread: every live mission is stopped (the cancel flags, Interrupt, the Heads, the Job
// Object, pending approvals answered Deny, open decisions held). The job of every agent is also ended from the caller's
// thread, after the flags, so a busy UI cannot keep a process tree alive. Afterwards the report keeps the two times apart
// (the stop was SENT in ms; the agents CONFIRMED in seconds), names an agent not confirmed within 5 s as "Still stopping"
// with Force end, and says nothing was undone.
using Marrow.App.Safety;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>A scope the Assistant owns beyond missions that Stop all must also stop (a voice reply, a Tactus stream, a teach recording).</summary>
public interface IStopScope
{
    string Name { get; }

    /// <summary>Stops the scope's own work. Called on the press's thread: it only sets flags and never waits.</summary>
    void Stop();
}

/// <summary>Where Stop all stands: what was sent, who confirmed, who is still stopping.</summary>
public sealed record StopReport(StopSource Source, string SourceLabel, double SentMs, int Agents, int Confirmed, TimeSpan? ConfirmedIn)
{
    /// <summary>The agents not confirmed after 5 s: "Still stopping", each with a Force end.</summary>
    public ValueList<AgentStopLine> StillStopping { get; init; } = ValueList<AgentStopLine>.Empty;

    /// <summary>Every agent confirmed.</summary>
    public bool Done => Confirmed >= Agents;

    /// <summary>The strip's text (12.6 "Afterwards").</summary>
    public string Strip => ControlWords.StopStrip(SentMs, SourceLabel, Agents, Confirmed, ConfirmedIn);
}

public sealed class StopAllOptions
{
    public required MissionRunner Runner { get; init; }

    public required AgentJobs Jobs { get; init; }

    public required IMissionAgents Agents { get; init; }

    public SchedulerHold? Hold { get; init; }

    public IReadOnlyList<IStopScope> Scopes { get; init; } = [];

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>How long an agent may take to confirm before it shows "Still stopping" (12.6: 5 s).</summary>
    public TimeSpan StillStoppingAfter { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the report looks again while agents confirm.</summary>
    public TimeSpan Watch { get; init; } = TimeSpan.FromMilliseconds(100);
}

public sealed class StopAll : IDisposable
{
    private readonly StopAllOptions o;
    private readonly object gate = new();
    private IDisposable? chord;
    private ITimer? timer;
    private long pressedAt;
    private StopSource source;
    private string label = "";
    private double sentMs;
    private HashSet<Guid> agents = [];
    private TimeSpan? confirmedIn;
    private StopReport? last;
    private int presses;

    public StopAll(StopAllOptions options)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
        o.Runner.Changed += OnMissionChanged;
    }

    /// <summary>Raised on the main thread whenever the report changes (an agent confirmed, 5 s passed).</summary>
    public event Action<StopReport?>? Changed;

    /// <summary>The report of the last stop that is still in effect; null before one, and after Resume all.</summary>
    public StopReport? Report
    {
        get
        {
            lock (gate) return last;
        }
    }

    /// <summary>How many times it was pressed (the chord's tests count presses, not reports).</summary>
    public int Presses => Volatile.Read(ref presses);

    /// <summary>Subscribes this to the one stop chord (Ctrl+Alt+Esc stops every scope, Tactus's and the Assistant's).</summary>
    public void Bind(GlobalStop stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        chord?.Dispose();
        chord = stop.Subscribe(AssistantNames.Assistant, press => Press(press.Source, press.Source == StopSource.Chord ? stop.Chord : null));
    }

    /// <summary>
    /// Stop all. Returns at once with the first report (what was sent and how fast); the rest happens on the main thread and
    /// the report follows through <see cref="Changed"/>. Callable from any thread, even while the UI is busy.
    /// </summary>
    public StopReport Press(StopSource from, string? sourceLabel = null)
    {
        Interlocked.Increment(ref presses);
        var started = o.Time.GetTimestamp();
        // ---- phase 1: flags only (12.6: "the only part timed in milliseconds") ----
        o.Hold?.HoldForStop();
        o.Runner.FlagStop();
        foreach (var scope in o.Scopes)
        {
            try { scope.Stop(); }
            catch (Exception error) when (error is not OutOfMemoryException) { o.Runner.ReportFault($"{scope.Name} could not stop: {error.Message}"); }
        }
        var ms = o.Time.GetElapsedTime(started).TotalMilliseconds;
        var first = new StopReport(from, sourceLabel ?? Label(from), ms, 0, 0, null);
        lock (gate)
        {
            pressedAt = started;
            source = from;
            label = first.SourceLabel;
            sentMs = ms;
            agents = [];
            confirmedIn = null;
            last = first;
        }
        // ---- phase 2: the main thread, queued before anything the kill below can cause ----
        o.Agents.Post(Settle);
        // ---- the kill, from this thread: a busy UI must not keep a process tree alive ----
        foreach (var thread in o.Jobs.Threads) o.Jobs.Terminate(thread);
        return first;
    }

    /// <summary>Force end for an agent still stopping (12.6 "Afterwards"): its job is terminated and let go.</summary>
    public bool ForceEnd(Guid thread)
    {
        var ended = o.Runner.ForceEnd(thread);
        if (ended) Refresh();
        return ended;
    }

    /// <summary>Hides the strip (Resume all): the stop is over.</summary>
    public void Clear()
    {
        ITimer? old;
        lock (gate)
        {
            last = null;
            agents = [];
            confirmedIn = null;
            old = timer;
            timer = null;
        }
        old?.Dispose();
        Changed?.Invoke(null);
    }

    public void Dispose()
    {
        chord?.Dispose();
        o.Runner.Changed -= OnMissionChanged;
        ITimer? old;
        lock (gate)
        {
            old = timer;
            timer = null;
        }
        old?.Dispose();
    }

    // The main thread: stop every live mission, remember the agents that were stopping, and watch them confirm.
    private void Settle()
    {
        var live = o.Runner.Missions
            .Where(r => r.Mission.State is not (MissionState.Done or MissionState.Failed or MissionState.Stopped or MissionState.Stopping))
            .ToList();
        var ids = live.Select(r => r.Id).ToHashSet();
        // The agents with something to stop (a turn or a live process), counted before the stop reaches them.
        var stopping = o.Runner.StoppingAgents(r => ids.Contains(r.Id)).Where(a => !a.Settled).Select(a => a.Thread).ToHashSet();
        lock (gate)
        {
            agents = stopping;
            timer?.Dispose();
            timer = o.Time.CreateTimer(_ => o.Agents.Post(Refresh), null, o.Watch, o.Watch);
        }
        foreach (var record in live) o.Runner.Stop(record.Id, StopCause.StopAll);
        Refresh();
    }

    private void OnMissionChanged(MissionRecord record)
    {
        lock (gate)
        {
            if (last is null) return;
        }
        Refresh();
    }

    // Recomputes the report from what the agents are now.
    private void Refresh()
    {
        StopReport report;
        lock (gate)
        {
            if (last is null) return;
            var lines = o.Runner.StoppingAgents().Where(a => agents.Contains(a.Thread)).ToList();
            var confirmed = lines.Count(a => a.Settled);
            var total = agents.Count;
            // Agents that left the mission's list (a head that finished) count as confirmed.
            confirmed += total - lines.Count;
            var elapsed = o.Time.GetElapsedTime(pressedAt);
            if (confirmed >= total && confirmedIn is null) confirmedIn = elapsed;
            var still = elapsed >= o.StillStoppingAfter ? lines.Where(a => !a.Settled).ToList() : [];
            report = new StopReport(source, label, sentMs, total, Math.Min(confirmed, total), confirmedIn) { StillStopping = new ValueList<AgentStopLine>(still) };
            // Every agent confirmed: nothing left to watch.
            if (report.Done)
            {
                timer?.Dispose();
                timer = null;
            }
            if (report == last) return;
            last = report;
        }
        Changed?.Invoke(report);
    }

    private static string Label(StopSource from) => from switch
    {
        StopSource.Chord => "Ctrl+Alt+Esc",
        StopSource.Tray => "Tray: Stop Assistant",
        StopSource.Header => "Stop all button",
        StopSource.Voice => "Voice: Stop everything",
        StopSource.Row => "Stop all in the sidebar",
        _ => "Stop all",
    };
}
