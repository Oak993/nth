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
    private bool assistantFeedStopped;

    /// <summary>The read-only feed of everything the Assistant may watch (5.1). Subscribe to it; publish only through the taps.</summary>
    public AssistantEventBus AssistantEvents { get; } = new();

    /// <summary>The one-line taps the app's own code calls (ThreadRuntime, AppModel, MultiAccountRouting).</summary>
    internal AssistantFeedTaps AssistantTaps
    {
        get
        {
            if (assistantTaps is null)
            {
                assistantTaps = new AssistantFeedTaps(this, AssistantEvents);
                if (assistantFeedStopped) assistantTaps.Dispose();
            }
            return assistantTaps;
        }
    }

    private void DisposeAssistantFeed()
    {
        if (assistantFeedStopped) return;
        assistantFeedStopped = true;
        assistantTaps?.Dispose();
        AssistantEvents.Dispose();
    }

    /// <summary>Dextra's controller, while Dextra runs (AS-8 starts it from the home; null before).</summary>
    public AssistantController? Dextra { get; private set; }

    /// <summary>The usage reserve (AS-13) of the running controller, if it has one: the taps feed it, the board and the plan card read it.</summary>
    internal ReserveGuard? Reserve => Dextra?.Reserve;

    /// <summary>The cost controls (AS-15) of the running controller, if it has them: the taps feed them, the Reserves view lists their caps.</summary>
    internal CostGuard? Costs => Dextra?.Costs;

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
        // The usage reserve (AS-13): the owner's rules and what nth learned of his usage come from the store and go back to it.
        var reserveSettings = new ReserveSettings();
        var calibrator = new UsageCalibrator();
        var turnCosts = new TurnCostHistory();
        // The cost controls (AS-15): what each assistant spent today (the caps' figures) and which duties the breaker paused, kept beside them.
        var costLedger = new CostLedger();
        var dutyHealth = new DutyHealthBook();
        var reservePersistence = new ReservePersistence(store, reserveSettings, calibrator, turnCosts, costs: costLedger, duties: dutyHealth);
        var controller = new AssistantController(new AssistantControllerOptions
        {
            Reserve = new ReserveSetup
            {
                Settings = reserveSettings,
                Calibrator = calibrator,
                Turns = turnCosts,
                AccountOf = thread => ExistingRuntime(thread)?.UsageAccount is { } account ? AssistantTaps.AccountRef(account) : null,
                AccountForNew = record => AssistantTaps.AccountRef(Marrow.Core.Port.UsageAccountKey.Default(record.Provider)),
                AccountOfSnapshot = account => AssistantTaps.AccountRef(account),
                Owned = reservePersistence,
                Costs = new CostSetup
                {
                    Ledger = costLedger,
                    WindowsOf = account => Reserve?.WindowKinds(account) ?? [],
                    Prices = (account, model) => Marrow.Core.Port.SwiftEnum<ProviderKind>.FromRawValue(account.ProviderId) is { } kind
                        ? Marrow.Core.Port.Ignis.IgnisSeams.Controller.Current()?.Points.Find(kind, model)?.Prices
                        : null,
                    AssistantOf = thread => Thread(thread)?.AssistantID?.ToString("N"),
                    AssistantName = id => profiles().FirstOrDefault(profile => profile.Id == id)?.Name,
                },
            },
            DutyHealth = dutyHealth,
            Triage = new TriageSetup { Options = TriageModels, Brain = () => BrainModelOf(provider, model) },
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

    /// <summary>
    /// Every model a provider this PC can use offers, with its price in the points file (AS-15, 13.2). A model with no price is listed
    /// without one and is never picked for being cheap; a local model server's models cost nothing and stay on this PC.
    /// </summary>
    internal IReadOnlyList<Marrow.Core.Port.Assistant.BrainModel> TriageModels()
    {
        var list = new List<Marrow.Core.Port.Assistant.BrainModel>();
        foreach (var kind in Marrow.Core.Port.SwiftEnum<ProviderKind>.AllCases)
        {
            if (!Providers.CountsAsInstalled(kind)) continue;
            foreach (var option in Providers.Models(kind)) list.Add(BrainModelOf(kind, option.Id));
        }
        return list;
    }

    private Marrow.Core.Port.Assistant.BrainModel BrainModelOf(ProviderKind kind, string? model)
    {
        var account = AssistantTaps.AccountRef(Marrow.Core.Port.UsageAccountKey.Default(kind));
        var prices = Marrow.Core.Port.Ignis.IgnisSeams.Controller.Current()?.Points.Find(kind, model);
        return new Marrow.Core.Port.Assistant.BrainModel(kind.Id, model ?? "", account.Key, account.Name)
        {
            InputPerMillion = prices?.Prices.Input,
            OutputPerMillion = prices?.Prices.Output,
            ContextWindow = prices?.ContextWindow,
            RunsLocally = kind == ProviderKind.Local,
        };
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

    /// <summary>
    /// ThreadRuntime.Resolve's tap, before the option reaches the provider: a mission chat's approval answered in the chat
    /// is logged, and goes as a decline when it cannot be or its mission is stopping (12.5, 12.6; ED-NEW-dextra-as5-13).
    /// </summary>
    internal ApprovalRequest.Option DextraChatAnswer(Guid thread, ApprovalRequest request, ApprovalRequest.Option option) =>
        Dextra?.OnChatAnswer(thread, request, option) ?? option;

    /// <summary>A mission chat's helper joins its mission (ED-NEW-dextra-as5-14); false when no running mission takes it.</summary>
    internal bool DextraHelperAdded(ChatThread parent, ChatThread helper) => Dextra?.OnHelperAdded(parent, helper) ?? false;

    /// <summary>A mission's helper waits at the agent gate before nth starts its turn.</summary>
    internal Task DextraAdmitHelper(ChatThread helper) =>
        helper.MissionID is not null && Dextra is { } dextra ? dextra.Runner.AdmitHelper(helper.Id, helper.Provider) : Task.CompletedTask;

    /// <summary>Hydra seam 3: a head under a mission's lead is the mission's agent too.</summary>
    internal void DextraHeadAdded(ChatThread lead, ChatThread head) => Dextra?.OnHeadAdded(lead, head);

    /// <summary>A head of a mission waits at the agent gate before its chat starts; at once for any other head.</summary>
    internal Task DextraAdmitHead(ChatThread head) =>
        head.MissionID is not null && Dextra is { } dextra ? dextra.Runner.AdmitHead(head.Id, head.Provider) : Task.CompletedTask;
}
