// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1, 11.6, 12.3, 12.4, 15.1, 15.4; units AS-2, AS-5, AS-7 and AS-14; ED-NEW-dextra-as2-6,
// -8, ED-NEW-dextra-as5-5, -6, -11, ED-NEW-dextra-as7-7 to -9, -12 to -15, ED-NEW-dextra-as14-5, -8). No Swift twin.
// The Assistant's live feed as the app holds it (one bus, one set of taps), and the seams Dextra's controller hangs on:
// every launch of a mission chat is sealed and jobbed (MissionLaunchOf, whether or not the controller runs), and the
// app's approvals, questions, turn ends and Hydra heads reach the controller when one is bound, and so do every Dextra
// chat's tool events (its taint) and the owner's own messages (which clear the home chat's taint). With none bound,
// nothing changes for any chat but a mission's, which stays sealed and capped.
using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tray;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.MultiAccount;
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
        DisposeAssistantHost(); // AS-8: Dextra stops before its feed and its stop key go
        assistantTaps?.Dispose();
        AssistantEvents.Dispose();
        DisposeSafety();
    }

    /// <summary>
    /// The one stop chord of nth (12.6), shared by Tactus and the Assistant; null until <see cref="EnsureStopKey"/> (the Assistant's
    /// start, or Tactus's). The chord is registered only while some work holds a lease on it.
    /// </summary>
    public GlobalStop? StopKey { get; private set; }

    /// <summary>The one tray icon of nth (7.4); null until <see cref="EnsureTray"/>.</summary>
    public AppTray? Tray { get; private set; }

    /// <summary>
    /// The stop chord service, made on first use over its own message-loop thread. <paramref name="chordSetting"/> reads the
    /// owner's chord text ("Ctrl+Alt+Esc" when null or unreadable); Settings persists it (AS-8 for the Assistant, Tactus's
    /// page for its own).
    /// </summary>
    public GlobalStop EnsureStopKey(Func<string?>? chordSetting = null) =>
        StopKey ??= new GlobalStop(new StopChordThread(), chordSetting ?? (() => null));

    /// <summary>
    /// The tray icon, made on first use. It shows while the window is hidden (<paramref name="windowHidden"/>), always when
    /// <paramref name="alwaysShow"/> says so, or while a feature that added items says it is live; <paramref name="open"/>
    /// brings the window back.
    /// </summary>
    public AppTray EnsureTray(Func<bool> windowHidden, Func<bool>? alwaysShow = null, Action? open = null) =>
        Tray ??= new AppTray(new AvaloniaTrayHost(), windowHidden, alwaysShow, open);

    private void DisposeSafety()
    {
        dextraTray?.Dispose();
        dextraTray = null;
        Tray?.Dispose();
        Tray = null;
        StopKey?.Dispose();
        StopKey = null;
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
    /// controller; the caller owns it. Auto-answering turns on only once the injection self-check passes in this
    /// process (AS-7); <paramref name="autoAnswer"/> swaps that switch (a test's). <paramref name="personalDataRecipients"/>
    /// and <paramref name="allowlist"/> are the owner's lists from Settings (empty until Settings has them).
    /// </summary>
    public AssistantController StartDextra(AssistantStore store, Action<AuditEntry> audit, Func<IReadOnlyList<Marrow.Core.Port.Assistant.AssistantProfile>> profiles,
        ProviderKind provider = ProviderKind.Claude, string? model = null, IReadOnlyList<string>? protectedRoots = null,
        AutoAnswerSwitch? autoAnswer = null, Func<IReadOnlyList<string>>? personalDataRecipients = null, Func<IReadOnlyList<string>>? allowlist = null,
        GlobalStop? stopKey = null, AppTray? tray = null, IPcStreamHold? pcStreams = null,
        IReadOnlyList<IStopScope>? stopScopes = null, INoteProposer? noteProposer = null)
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
        // Dextra's memory (AS-12): the notes live in the store; the capture runs only when a cheap model is bound (noteProposer).
        string? DextraId() => profiles().FirstOrDefault(p => p.ParentId is null)?.Id;
        var memoryStore = new MemoryStore(store, new MemoryStoreOptions { MainAssistantId = DextraId });
        var memoryImport = new MemoryImport(memoryStore, DextraId);
        IReadOnlyList<MemoryImportSource> ImportSources() => MemoryImportSources.FromAccounts(Settings.Accounts.Accounts,
            new Marrow.Services.Port.Accounts.AccountEnvironment(() => Marrow.Services.Store.Storage.Root),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), [.. Projects.Select(p => p.Path)]);
        var memoryWatch = new MemoryImportWatch(memoryImport, ImportSources);
        // The watch follows the folders only once the owner has imported (8.4); the Memory view starts it after each import he runs.
        if (memoryStore.HasImported) memoryWatch.Start();
        var controller = new AssistantController(new AssistantControllerOptions
        {
            Notes = new MemorySetup
            {
                Store = memoryStore,
                Capture = noteProposer is null ? null : new MemoryCapture(memoryStore, noteProposer),
                Import = memoryImport,
                ImportSources = ImportSources,
                Watch = memoryWatch,
                Owned = memoryWatch,
            },
            Router = new RouterSetup { Accounts = RouterAccounts, Models = RouterModelsOf },
            Reserve = new ReserveSetup
            {
                Settings = reserveSettings,
                Calibrator = calibrator,
                Turns = turnCosts,
                AccountOf = thread => ExistingRuntime(thread)?.UsageAccount is { } account ? AssistantTaps.AccountRef(account) : null,
                AccountForNew = AccountForNewChat,
                AccountOfSnapshot = account => AssistantTaps.AccountRef(account),
                Owned = reservePersistence,
                Costs = new CostSetup
                {
                    Ledger = costLedger,
                    WindowsOf = account => Reserve?.WindowKinds(account) ?? [],
                    Prices = (account, model) => Marrow.Core.Port.SwiftEnum<ProviderKind>.FromRawValue(account.ProviderId) is { } kind
                        ? IgnisRuntimeSeams.Points.Current()?.Find(kind, model)?.Prices
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
            Workspaces = new StoreMissionWorkspaces((mission, attempt) =>
                store.MissionWorktreeFolder(MissionJournal.StoreId(mission) + (attempt > 1 ? "-" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""))),
            Audit = audit,
            Save = record => store.SaveMission(MissionJournal.StoreId(record.Id), MissionJournal.ToJson(record)),
            Provider = provider,
            Model = model,
            ProtectedRoots = [store.Root, .. protectedRoots ?? []],
            AutoAnswer = autoAnswer ?? AutoAnswerSwitch.AfterCorpus(),
            PersonalDataRecipients = personalDataRecipients ?? (() => []),
            Allowlist = allowlist ?? (() => []),
            InstructionFound = (thread, source, finding) => AssistantTaps.TapInstructionFound(thread, source, finding),
            // AS-7 (ED-NEW-dextra-as7-14): the home chats' taint survives a restart; StartDextra runs after the library loads.
            LoadHomeTaint = store.HomeTaint,
            SaveHomeTaint = store.SaveHomeTaint,
            HomeChats = () => [.. Threads.Where(t => t.AssistantID is not null && t.MissionID is null).Select(t => t.Id)],
            StopKey = stopKey,
            PcStreams = pcStreams,
            StopScopes = stopScopes ?? [],
        });
        // The day's folding of the notes (8.2) runs at start when it has not run today; the digest (AS-11) calls it each morning from then on.
        _ = Task.Run(() =>
        {
            try { memoryStore.FoldIfDue(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException) { controller.AuditMemory(null, "fold", "failed: " + error.GetType().Name); }
        });
        controller.Runner.Load(store.Missions().Select(stored => MissionJournal.FromJson(stored.Body)).OfType<MissionRecord>());
        // What Revert and Restart kept for 7 days, and the workspaces of missions over for 7 days, are swept at start (the
        // scheduler, AS-10, repeats it daily); a sweep that fails is reported and never stops the start.
        _ = SweepKept(controller);
        // The Assistant's items in nth's one tray icon (7.4, 12.6): Stop works with the window closed.
        if (tray is not null) dextraTray = AssistantTray.Bind(tray, controller);
        BindDextra(controller);
        return controller;
    }

    /// <summary>
    /// The account a chat the mission has yet to start would run on (the reserve's question): the one the router chose for it, else
    /// the one the provider's own account order gives a new chat.
    /// </summary>
    private ReserveAccountRef AccountForNewChat(MissionRecord record)
    {
        var routed = record.AccountId is { } chosen
            ? Settings.Accounts.Accounts.FirstOrDefault(candidate => candidate.Id == chosen && candidate.Service == record.Provider)
            : null;
        var account = routed ?? Settings.Accounts.Choose(record.Provider);
        return account is null
            ? AssistantTaps.AccountRef(UsageAccountKey.Default(record.Provider))
            : AssistantTaps.AccountRef(record.Provider, account.Id);
    }

    /// <summary>
    /// The accounts the router may use (AS-14): those enabled and connected, each with whether it can be used now (CanUseAccount) and
    /// its place in the owner's pill order. The pill used for new threads comes first, then his other pills, then the rest by the
    /// order he gave the accounts of each service.
    /// </summary>
    private IReadOnlyList<RouterAccount> RouterAccounts()
    {
        var connected = new HandshakeAccountStore(Settings.Accounts, IsAccountConnected);
        var order = PillOrder.Sequence(Settings.Pills.Pills, Settings.Pills.UseForNewThreads, connected);
        return [.. Settings.Accounts.Accounts.Where(row => row.Enabled && IsAccountConnected(row)).Select(row =>
        {
            var id = row.Id.ToString("D");
            var place = order.ToList().FindIndex(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
            return new RouterAccount(row.Id, AssistantTaps.AccountRef(row.Service, row.Id), row.Service, CanUseAccount(row.Id), place >= 0 ? place : order.Count + row.Priority);
        })];
    }

    /// <summary>A provider's models with their tiers, read from the price the Ignis points know for each (no price: standard).</summary>
    private IReadOnlyList<RouteModel> RouterModelsOf(ProviderKind provider) =>
        RouterModels.Of(provider, Providers.Models(provider), IgnisRuntimeSeams.Points.Current());

    private AssistantTray? dextraTray;

    private static async Task SweepKept(AssistantController controller)
    {
        try { await controller.Runner.CleanUpWorkspaces().ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            controller.Runner.ReportFault("The 7-day sweep of kept work failed: " + error.Message);
        }
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
        var prices = IgnisRuntimeSeams.Points.Current()?.Find(kind, model);
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
        dextraTray?.Dispose();
        dextraTray = null;
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

    /// <summary>
    /// A head whose finish a Pause held (12.6b) is released: Hydra finishes it now with the status its turn ended with, and
    /// tells the lead. The turn's own tail runs again, and finds the head no longer held.
    /// </summary>
    internal void DextraFinishHeldHead(ChatThread head, TurnStatus status) => HydraHeadTurnFinished(head, status);

    /// <summary>
    /// Resume on a head Stop cut (12.6b): Hydra counts it as running again, and its chat gets the continue message. False when
    /// the chat cannot take it now.
    /// </summary>
    internal bool DextraRestartHead(Guid head, string text)
    {
        UpdateHydraHead(head, value => value with
        {
            HydraHeadInfoStatus = HydraHeadInfo.Status.Running,
            FinishedAt = null,
            ExecutionFinishedAt = null,
        });
        return Runtime(head).SendMissionMessage(text);
    }

    /// <summary>ThreadRuntime.Handle's tap (AS-7): what a Dextra chat's tool calls bring in taints it.</summary>
    internal void DextraEvent(Guid thread, ProviderEvent @event) => Dextra?.OnProviderEvent(thread, @event);

    /// <summary>ThreadRuntime's send taps (AS-7): the owner's own message starts a fresh, untainted turn in Dextra's home chat.</summary>
    internal void DextraOwnerMessage(Guid thread) => Dextra?.OwnerMessage(thread);

    /// <summary>A parent sent its open helper a new prompt (AS-7, ED-NEW-dextra-as7-15): the helper carries the parent's taint.</summary>
    internal void DextraHelperPrompted(ChatThread parent, ChatThread helper)
    {
        if (helper.MissionID is not null) Dextra?.OnHelperPrompted(parent, helper);
    }

    /// <summary>Hydra seam 3: a head under a mission's lead is the mission's agent too; a head of the home chat carries its taint.</summary>
    internal void DextraHeadAdded(ChatThread lead, ChatThread head) => Dextra?.OnHeadAdded(lead, head);

    /// <summary>
    /// A head of a Dextra chat is done: a mission's frees its gate slot; the home chat's hands its lead its taint with its
    /// report (AS-7, ED-NEW-dextra-as7-13). True when a mission holds the head's finish (12.6b): the caller stops there.
    /// </summary>
    internal bool DextraHeadFinished(ChatThread head, TurnStatus status) =>
        (head.MissionID is not null || head.AssistantID is not null) && Dextra?.OnHeadFinished(head.Id, status) == true;

    /// <summary>A head of a mission waits at the agent gate before its chat starts; at once for any other head.</summary>
    internal Task<bool> DextraAdmitHead(ChatThread head) =>
        head.MissionID is not null && Dextra is { } dextra ? dextra.Runner.AdmitHead(head.Id, head.Provider) : Task.FromResult(true);

    /// <summary>
    /// A head Stop kept from starting waits here until the owner resumes it (12.6b); false when its mission is over and it never
    /// starts. Its chat is started by the caller once this says true and the head has been admitted again.
    /// </summary>
    internal Task<bool> DextraAwaitHeadResume(ChatThread head) =>
        head.MissionID is not null && Dextra is { } dextra ? dextra.Runner.AwaitHeadResume(head.Id) : Task.FromResult(false);
}
