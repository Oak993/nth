using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-6: the seams the controls add to the mission runner, handed to <see cref="MissionRig"/>.</summary>
internal sealed record MissionRigControls
{
    public GlobalStop? StopKey { get; init; }

    public SchedulerHold? Hold { get; init; }

    public IPcStreamHold? PcStreams { get; init; }

    public Func<MissionRecord, DecisionItem?>? ReserveCard { get; init; }

    public Func<DecisionItem, ValueList<DecisionInput>?>? CurrentInputs { get; init; }

    public TimeProvider? Time { get; init; }
}

/// <summary>Tactus's streams as a test sees them: what a pause held and what a resume let go.</summary>
internal sealed class FakePcStreams : IPcStreamHold
{
    /// <summary>How many streams a Hold finds for a task (0: none was driving the PC).</summary>
    public int Streams { get; set; }

    public List<Guid> Held { get; } = [];

    public List<Guid> Released { get; } = [];

    public int Hold(Guid mission, IReadOnlyList<Guid> threads)
    {
        Held.Add(mission);
        return Streams;
    }

    public void Release(Guid mission) => Released.Add(mission);
}

/// <summary>
/// Dextra AS-6: a mission runner with Stop all, the controls, the scheduler's holds and the stop chord over fakes, on a clock the
/// test moves. Everything runs inline on the test's thread; the agents are scripted chats, the jobs are stand-ins.
/// </summary>
internal sealed class ControlsRig
{
    public ControlsRig(string root, bool stopKey = true, Func<MissionRecord, DecisionItem?>? reserveCard = null, MissionLimits? limits = null,
        Func<DecisionItem, ValueList<DecisionInput>?>? currentInputs = null, Func<ApprovalRequest, ApprovalVerdict>? policy = null,
        bool realStopClock = false)
    {
        Time = new ManualTimeProvider();
        Host = new FakeStopChordHost();
        Key = new GlobalStop(Host, () => null, Time);
        Hold = new SchedulerHold();
        Pc = new FakePcStreams();
        Rig = new MissionRig(root, policy ?? DefaultPolicy, limits, controls: new MissionRigControls
        {
            StopKey = stopKey ? Key : null,
            Hold = Hold,
            PcStreams = Pc,
            ReserveCard = reserveCard,
            CurrentInputs = currentInputs,
            Time = Time,
        });
        Stop = new StopAll(new StopAllOptions
        {
            Runner = Rig.Runner,
            Jobs = Rig.Jobs,
            Agents = Rig.Agents,
            Hold = Hold,
            Scopes = Scopes,
            Time = realStopClock ? TimeProvider.System : Time,
        });
        if (stopKey) Stop.Bind(Key);
        Controls = new MissionControls(Rig.Runner, Stop, Hold, Rig.Queue);
    }

    public ManualTimeProvider Time { get; }

    public FakeStopChordHost Host { get; }

    public GlobalStop Key { get; }

    public SchedulerHold Hold { get; }

    public FakePcStreams Pc { get; }

    public MissionRig Rig { get; }

    public StopAll Stop { get; }

    public MissionControls Controls { get; }

    public MissionRunner Runner => Rig.Runner;

    public DecisionQueue Queue => Rig.Queue;

    public FakeMissionAgents Agents => Rig.Agents;

    /// <summary>The scopes Stop all also stops beyond missions (a voice reply): they record that they were told.</summary>
    public List<RecordingScope> Scopes { get; } = [];

    /// <summary>The policy of most tests: git push is irreversible (asks, always confirmed), the rest asks.</summary>
    public static ApprovalVerdict DefaultPolicy(ApprovalRequest request) => request.Title switch
    {
        var t when t.StartsWith("git push", StringComparison.Ordinal) =>
            new(new Classification(ActionClass.Outward, "sends"), new PolicyDecision(Verdict.Ask, PolicyRule.AlwaysConfirmed, "Always confirmed")),
        var t when t.StartsWith("rm ", StringComparison.Ordinal) =>
            new(new Classification(ActionClass.HardDeny, "never"), new PolicyDecision(Verdict.Deny, PolicyRule.HardDeny, "Never runs")),
        _ => new(new Classification(ActionClass.Unknown, "not recognised"), new PolicyDecision(Verdict.Ask, PolicyRule.Unknown, "Not recognised")),
    };

    /// <summary>Starts a mission and waits until its lead runs.</summary>
    public MissionRecord Started(MissionRequest? request = null) => Rig.Started(request);

    public MissionRecord Now(MissionRecord record) => Rig.Now(record);

    /// <summary>Starts a team mission, adds a head under its lead (a fake chat that runs) and waits until it works.</summary>
    public (MissionRecord Record, Guid Lead, Guid Head) WithHead(string name = "Tests", bool running = true, params Guid[] dependsOn)
    {
        var record = Started(Rig.Request(team: true));
        var lead = record.LeadThread!.Value;
        var head = AddHead(record, name, running, dependsOn);
        return (Now(record), lead, head);
    }

    /// <summary>A head under the mission's lead: a Hydra head with its own scripted chat that has a job and holds a slot once it starts.</summary>
    public Guid AddHead(MissionRecord record, string name, bool running = true, params Guid[] dependsOn)
    {
        var lead = Now(record).LeadThread!.Value;
        var head = Guid.NewGuid();
        Runner.HeadAdded(lead, head, name, native: false, dependsOn);
        Agents.Restore(head, Agents.Of(lead).Launch).Running = running;
        return head;
    }

    /// <summary>Lets the head start (its gate wait ends and it is Working) when it may.</summary>
    public void StartHead(Guid head, bool wait = true)
    {
        var admitted = Runner.AdmitHead(head, ProviderKind.Claude);
        if (wait) Assert.True(MissionRig.Within(admitted, 10), "the head did not start");
    }

    public static ApprovalRequest Approval(string id, string command) => MissionRig.Approval(id, command);

    /// <summary>The lead's turn ends (the way AppModel reports it).</summary>
    public void EndTurn(Guid thread, string? answer = null, TurnStatus status = TurnStatus.Completed)
    {
        Agents.Finish(thread, answer);
        Runner.OnTurnFinished(thread, status, continues: false);
    }

    /// <summary>A job that has ended all its processes (the confirmation of a Stop).</summary>
    public void EndProcesses()
    {
        foreach (var job in Rig.JobFactory.Made) job.LiveProcesses = 0;
    }
}

/// <summary>A scope beyond missions that Stop all must stop (a voice reply, a teach recording): it records the call.</summary>
internal sealed class RecordingScope(string name) : IStopScope
{
    public string Name { get; } = name;

    public int Stops { get; private set; }

    public Exception? Throws { get; set; }

    public void Stop()
    {
        Stops++;
        if (Throws is { } error) throw error;
    }
}
