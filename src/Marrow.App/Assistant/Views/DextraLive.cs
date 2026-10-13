// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.1 the sidebar row and 2.2 the home read one model; 16.2 the board reads missions
// through IMissionJobs backed by the Assistant's own list; ED-NEW-dextra-as8-8). No Swift twin.
// One set of sources per running controller, shared by the sidebar row and the home (one fold of the runner's changes, not
// two), and the idle set shown while Dextra is off or before its first run: no tasks, no decisions, nothing to pause.
using Marrow.App.Safety;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant.Views;

public sealed class DextraLive : IDisposable
{
    private readonly IDisposable? owned;

    public DextraLive(IMissionJobs jobs, IDecisionSource decisions, IGlobalControls controls, AssistantController? controller = null, IDisposable? owned = null)
    {
        Jobs = jobs;
        Decisions = decisions;
        Controls = controls;
        Controller = controller;
        this.owned = owned;
    }

    public IMissionJobs Jobs { get; }

    public IDecisionSource Decisions { get; }

    public IGlobalControls Controls { get; }

    /// <summary>The controller these read, or null for the idle set.</summary>
    public AssistantController? Controller { get; }

    /// <summary>Nothing runs: shown while Dextra is off or its first run is not done.</summary>
    public static DextraLive Idle { get; } = new(IdleJobs.Instance, IdleDecisions.Instance, IdleControls.Instance);

    /// <summary>The running controller's sources: its missions, its queue and its controls, over the app's chats.</summary>
    public static DextraLive For(AppModel model, AssistantController controller, Func<string, string>? assistantName = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(controller);
        var jobs = RunnerMissionJobs.For(controller, new AppMissionAgents(model), assistantName);
        var controls = new ControllerGlobalControls(controller.Controls);
        return new DextraLive(jobs, new QueueDecisions(controller.Queue, controller.AnswerCard), controls, controller, new Both(jobs, controls));
    }

    public void Dispose() => owned?.Dispose();

    private sealed class Both(IDisposable a, IDisposable b) : IDisposable
    {
        public void Dispose()
        {
            a.Dispose();
            b.Dispose();
        }
    }

    private sealed class IdleJobs : IMissionJobs
    {
        public static IdleJobs Instance { get; } = new();

        private const string Off = "Dextra is not running.";

        public string Name => "idle";

        public IReadOnlyList<MissionJob> Snapshot(DateTimeOffset now) => [];

        public event Action? Changed { add { } remove { } }

        public string? Pause(Guid job) => Off;

        public string? Resume(Guid job) => Off;

        public string? Stop(Guid job) => Off;

        public string? Restart(Guid job) => Off;

        public Task<string?> Revert(Guid job) => Task.FromResult<string?>(Off);

        public string? PauseHead(Guid job, Guid head) => Off;

        public string? ResumeHead(Guid job, Guid head) => Off;

        public string? StopHead(Guid job, Guid head) => Off;

        public void Dismiss(Guid job)
        {
        }
    }

    private sealed class IdleDecisions : IDecisionSource
    {
        public static IdleDecisions Instance { get; } = new();

        public IReadOnlyList<DecisionItem> Waiting => [];

        public string? Answer(Guid id, DecisionAnswer answer) => "Dextra is not running.";

        public event Action? Changed { add { } remove { } }
    }

    private sealed class IdleControls : IGlobalControls
    {
        public static IdleControls Instance { get; } = new();

        public GlobalToggle Toggle { get; } = new(GlobalToggleKind.PauseAll, false, false);

        public StopReport? Stopped => null;

        public string PauseAll(string source) => "Nothing runs.";

        public string ResumeAll() => "Nothing to resume.";

        public StopReport StopAll(StopSource from) => new(from, "Stop all", 0, 0, 0, TimeSpan.Zero);

        public bool ForceEnd(Guid thread) => false;

        public event Action? Changed { add { } remove { } }
    }
}
