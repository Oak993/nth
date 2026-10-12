// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1, 12.3, 15.1, 15.4; units AS-2 and AS-5; ED-NEW-dextra-as2-6, -8,
// ED-NEW-dextra-as5-5, -6, -11). No Swift twin.
// The Assistant's live feed as the app holds it (one bus, one set of taps), and the seams Dextra's controller hangs on:
// every launch of a mission chat is sealed and jobbed (MissionLaunchOf, whether or not the controller runs), and the
// app's approvals, questions, turn ends and Hydra heads reach the controller when one is bound. With none bound, nothing
// changes for any chat but a mission's, which stays sealed and capped.
using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;

namespace Marrow.App;

public sealed partial class AppModel
{
    private AssistantFeedTaps? assistantTaps;

    /// <summary>The read-only feed of everything the Assistant may watch (5.1). Subscribe to it; publish only through the taps.</summary>
    public AssistantEventBus AssistantEvents { get; } = new();

    /// <summary>The one-line taps the app's own code calls (ThreadRuntime, AppModel, MultiAccountRouting).</summary>
    internal AssistantFeedTaps AssistantTaps => assistantTaps ??= new AssistantFeedTaps(this, AssistantEvents);

    /// <summary>Dextra's controller, while Dextra runs (AS-8 starts it from the home; null before).</summary>
    public AssistantController? Dextra { get; private set; }

    /// <summary>The Job Object of every mission agent (12.6), by chat. A test may swap the factory before any launch.</summary>
    public AgentJobs MissionJobs { get; internal set; } = new(WindowsAgentJobFactory.Instance);

    /// <summary>
    /// Starts Dextra over its store (AS-8 calls this when the owner turns Dextra on): a controller whose chats are this
    /// app's, whose workspaces live under the store's worktrees folder and whose missions are journaled in the store; the
    /// missions the store held come back (a live one Paused and asking "Resume?", 15.3, when the store was opened with
    /// <see cref="MissionJournal.Interrupted"/> as its rule), and the controller is bound. The store's folder and
    /// <paramref name="protectedRoots"/> (the audit folder, nth's settings) are never written by an agent. Returns the
    /// controller; the caller owns it.
    /// </summary>
    public AssistantController StartDextra(AssistantStore store, Action<AuditEntry> audit, Func<IReadOnlyList<Marrow.Core.Port.Assistant.AssistantProfile>> profiles,
        ProviderKind provider = ProviderKind.Claude, string? model = null, IReadOnlyList<string>? protectedRoots = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(profiles);
        var controller = new AssistantController(new AssistantControllerOptions
        {
            Agents = new AppMissionAgents(this),
            Thread = Thread,
            Projects = () => [.. Projects.Select(p => new MissionProject(p.Id, p.Name, p.Path))],
            Profiles = profiles,
            Jobs = MissionJobs,
            Workspaces = new StoreMissionWorkspaces(mission => store.MissionWorktreeFolder(MissionJournal.StoreId(mission))),
            Audit = audit,
            Save = record => store.SaveMission(MissionJournal.StoreId(record.Id), MissionJournal.ToJson(record)),
            Provider = provider,
            Model = model,
            ProtectedRoots = [store.Root, .. protectedRoots ?? []],
        });
        controller.Runner.Load(store.Missions().Select(stored => MissionJournal.FromJson(stored.Body)).OfType<MissionRecord>());
        BindDextra(controller);
        return controller;
    }

    /// <summary>Binds Dextra's controller: the nth-dextra tools, approvals, questions and turn ends go to it.</summary>
    public void BindDextra(AssistantController controller)
    {
        AttachDextra(controller);
        AssistantMcpBridge.Host.SetProcessDefault(controller);
    }

    /// <summary>The app's hooks only (approvals, questions, turn ends, heads), without the process-wide tool host.</summary>
    internal void AttachDextra(AssistantController? controller) => Dextra = controller;

    /// <summary>Lets the controller go: the tools are no chat's any more. Mission chats stay sealed.</summary>
    public void UnbindDextra()
    {
        if (Dextra is null) return;
        Dextra = null;
        AssistantMcpBridge.Host.SetProcessDefault(NoAssistantToolHost.Instance);
    }

    /// <summary>A chat's launch: sealed and in its agent's job when it is a mission's (12.1a, 12.6), as it was otherwise.</summary>
    internal SessionConfiguration MissionLaunchOf(ChatThread thread, SessionConfiguration configuration) =>
        MissionLaunch.Apply(thread, configuration, t => Dextra?.Seal(t), id => MissionJobs.For(id), t => Dextra?.LaunchRefusal(t));

    /// <summary>ThreadRuntime's approval tap: a mission chat's approval goes to Dextra's queue.</summary>
    internal void DextraApproval(Guid thread, ApprovalRequest request) => Dextra?.OnApproval(thread, request);

    internal void DextraQuestion(Guid thread, QuestionRequest request) => Dextra?.OnQuestion(thread, request);

    internal void DextraRequestResolved(Guid thread, string requestId) => Dextra?.OnRequestResolved(thread, requestId);

    /// <summary>Hydra seam 3: a head under a mission's lead is the mission's agent too.</summary>
    internal void DextraHeadAdded(ChatThread lead, ChatThread head) => Dextra?.OnHeadAdded(lead, head);

    /// <summary>A head of a mission waits at the agent gate before its chat starts; at once for any other head.</summary>
    internal Task DextraAdmitHead(ChatThread head) =>
        head.MissionID is not null && Dextra is { } dextra ? dextra.Runner.AdmitHead(head.Id, head.Provider) : Task.CompletedTask;
}
