// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.2 the home thread, 2.6 nothing outward before the first-run card is done,
// 5.4 the brain's switches, 12.2 grants kept, 12.6 the stop chord; ED-NEW-dextra-as8-8). No Swift twin.
// What lives with Dextra in the app: its settings, its identity, its home thread, and the controller's start and stop.
// The controller starts only when Dextra is on and the first-run card is done; the start itself (the store, the audit
// log behind Credential Manager, the stop chord and the tray) is one seam, so tests start a fake.
using Marrow.App.Assistant.Views;
using Marrow.App.Safety;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>How the app starts Dextra's controller (production: the store, the audit log, the chord and the tray).</summary>
public delegate AssistantController? DextraStarter(AppModel model, DextraSettings settings, Guid id);

/// <summary>How the app gives a running controller its voice (AS-18: the talk chord, the conversation, the speech). Null: no voice.</summary>
public delegate DextraVoice? DextraVoiceStarter(AppModel model, DextraHost host, VoiceStopScope scope);

public sealed class DextraHost : IDisposable
{
    public const string Name = "Dextra";

    private readonly AppModel model;
    private readonly DextraStarter starter;
    private readonly Func<IReadOnlyList<AssistantProfile>>? asturs;
    private readonly DextraVoiceStarter? voiceStarter;
    private VoiceStopScope? voiceScope;
    private AssistantController? started;
    private IDisposable? owned;
    private bool disposed;
    private bool starting;
    private Task<AssistantController?>? initialization;
    private int startEpoch;

    /// <param name="voice">AS-18: the voice of a running controller. Null with the app's own starter gives the real one (the chord, the microphone, the PC's voices); null with a test's starter gives none.</param>
    public DextraHost(AppModel model, DextraSettings settings, DextraStarter? starter = null, Func<IReadOnlyList<AssistantProfile>>? asturs = null, DextraVoiceStarter? voice = null)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.starter = starter ?? Production;
        this.asturs = asturs;
        voiceStarter = voice ?? (starter is null ? static (app, host, scope) => DextraVoice.Create(app, host, scope) : null);
        Settings.Changed += OnSettings;
    }

    public DextraSettings Settings { get; }

    /// <summary>Dextra's identity, made once and kept.</summary>
    public Guid Id => Settings.EnsureId();

    /// <summary>The identity string the controller and the profiles use.</summary>
    public string AssistantId => Id.ToString("N");

    internal void AdoptIdentity(Guid id)
    {
        var wasStarting = starting;
        starting = true;
        try { Settings.Update(value => value with { Id = id }); }
        finally { starting = wasStarting; }
    }

    /// <summary>The running controller, or null (off, first run not done, or the start failed).</summary>
    public AssistantController? Controller => started;

    /// <summary>The sources the sidebar row and the home read: the running controller's, else the idle set.</summary>
    public DextraLive Live { get; private set; } = DextraLive.Idle;

    /// <summary>Dextra's voice (AS-18): the talk chord and the conversation of the running controller, or null (off, not started, or no voice in this build).</summary>
    public DextraVoice? Voice { get; private set; }

    /// <summary>The morning digest and the Suggestions lane (AS-11) while Dextra's duties run; null before they start.</summary>
    public DigestService? Digests => model.Duties?.Digests;

    /// <summary>An assistant's name by its id (Dextra's or an Astur's).</summary>
    public string NameOf(string assistantId) => Profiles().FirstOrDefault(p => p.Id == assistantId)?.Name ?? Name;

    /// <summary>Why the last start failed, or null.</summary>
    public string? StartFault { get; private set; }

    /// <summary>The controller started or stopped, or a setting the home shows changed (raised on the caller's thread).</summary>
    public event Action? Changed;

    /// <summary>Whether Dextra may run now: on, and the first-run card done (2.6).</summary>
    public bool MayRun => Settings.Value is { On: true, FirstRunDone: true };

    /// <summary>Dextra's profile and its Asturs' (9.1).</summary>
    public IReadOnlyList<AssistantProfile> Profiles()
    {
        var team = model.Dextra?.Team?.Profiles ?? asturs?.Invoke() ?? [];
        return team.Any(p => p.IsRoot) ? team : [new AssistantProfile(AssistantId, Name), .. team.Where(p => p.Id != AssistantId)];
    }

    /// <summary>Starts the controller when Dextra may run and none runs; keeps its grants and its brain's switches in step.</summary>
    public AssistantController? EnsureStarted()
    {
        if (disposed || starting) return started;
        if (model.Dextra is null && !MayRun) return started;
        if (started is not null) return started;
        AssistantController? controller;
        starting = true; // the identity's first store raises Changed; that must not start a second controller
        try { controller = model.Dextra ?? starter(model, Settings, Id); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // what the start opened before it failed is let go now, not when the host goes
            owned?.Dispose();
            owned = null;
            StartFault = error.Message;
            Changed?.Invoke();
            return null;
        }
        finally { starting = false; }
        if (controller is null)
        {
            owned?.Dispose();
            owned = null;
            return null;
        }
        return Publish(controller);
    }

    private AssistantController Publish(AssistantController controller)
    {
        StartFault = null;
        started = controller;
        controller.RestoreGrants(Settings.Value.Grants);
        controller.GrantsChanged += KeepGrants;
        Live = DextraLive.For(model, controller, NameOf);
        model.AssistantEvents.Brain = Settings.Brain(BrainProvider());
        try { Voice = voiceStarter?.Invoke(model, this, voiceScope ?? new VoiceStopScope()); }
        catch (Exception error) when (error is not OperationCanceledException) { StartFault = "Voice could not start: " + error.Message; }
        Changed?.Invoke();
        return controller;
    }

    internal void OwnStartup(IDisposable storage) => owned = storage;

    /// <summary>
    /// Dextra turned off: every task is stopped (Stop all never asks), the tools are no chat's any more and the
    /// controller is let go.
    /// </summary>
    public void Stop()
    {
        startEpoch++;
        initialization = null;
        if ((started ?? model.Dextra) is not { } controller) { owned?.Dispose(); owned = null; return; }
        started = null;
        controller.GrantsChanged -= KeepGrants;
        Voice?.Dispose();
        Voice = null;
        var live = Live;
        Live = DextraLive.Idle;
        live.Dispose();
        try { controller.Controls.StopAll(StopSource.Program); }
        catch (Exception error) when (error is not OperationCanceledException) { StartFault = error.Message; }
        if (model.Dextra == controller) model.UnbindDextra();
        controller.Dispose();
        owned?.Dispose();
        owned = null;
        model.AssistantEvents.Brain = FeedBrain.Unknown;
        Changed?.Invoke();
    }

    /// <summary>The brain's provider: the owner's pick when it is available, else Claude, else the first available one.</summary>
    public ProviderKind? BrainProvider()
    {
        var available = model.Providers.AvailableProviders;
        if (Settings.Value.BrainProvider is { } picked) return picked;
        if (available.Contains(ProviderKind.Claude)) return ProviderKind.Claude;
        return available.Count > 0 ? available[0] : null;
    }

    /// <summary>The brain's model: the owner's pick, else the provider's default (null lets the provider choose).</summary>
    public string? BrainModel() => Settings.Value.BrainModel;

    /// <summary>
    /// Dextra's home thread (2.2): the chat flagged with its AssistantID and no MissionID, made the first time in the
    /// selected chat's project (or the first project) and working in Dextra's own folder. Null with no project yet.
    /// </summary>
    public ChatThread? HomeThread(Func<string>? folder = null)
    {
        var id = Id;
        if (model.Threads.FirstOrDefault(t => t.AssistantID == id && t.MissionID is null && t.ParentThreadID is null) is { } home) return home;
        var project = (model.SelectedThreadID is { } selected ? model.Thread(selected) : null) is { } chat
            ? model.Project(chat.ProjectID) : null;
        project ??= model.Projects.FirstOrDefault();
        if (project is null) return null;
        var provider = BrainProvider() ?? ProviderKind.Claude;
        var thread = new ChatThread(project.Id, provider, BrainModel(), null, SealedProviderConfig.CappedMode)
        {
            Title = Name,
            HasCustomTitle = true,
            AssistantID = id,
            ProviderAccountID = Settings.Value.BrainAccount,
            WorktreePath = folder?.Invoke(),
        };
        model.InsertThread(thread);
        return thread;
    }

    private void OnSettings()
    {
        if (disposed) return;
        if (!Settings.Value.On) { Stop(); return; }
        if (started is null) { if (MayRun) EnsureStarted(); }
        else model.AssistantEvents.Brain = Settings.Brain(BrainProvider());
        Changed?.Invoke();
    }

    private void KeepGrants()
    {
        if (started is { } controller) Settings.KeepGrants(controller.Grants, SwiftClock.Provider.Current.GetUtcNow());
    }

    // The production start: the store and the audit log on their default roots, the stop chord over the owner's
    // setting and the tray the app made. Kept here so the views never touch Credential Manager.
    private AssistantController? Production(AppModel app, DextraSettings settings, Guid id)
    {
        _ = app; _ = settings; _ = id;
        _ = EnsureStartedAsync();
        return null;
    }

    /// <summary>One initialization shared by foreground and hidden starts. Store, key and team opening run off the UI thread.</summary>
    public Task<AssistantController?> EnsureStartedAsync(BackgroundStartSeams? seams = null, StartupMode? mode = null, Action? quit = null, bool restoringDuties = false)
    {
        if (disposed || !MayRun) return Task.FromResult<AssistantController?>(null);
        if (started is not null) return Task.FromResult<AssistantController?>(started);
        if (model.Dextra is { } running) return Task.FromResult<AssistantController?>(Publish(running));
        if (initialization is not null) return initialization;
        var wasStarting = starting;
        starting = true;
        try { initialization = InitializeAsync(Id, seams ?? new BackgroundStartSeams(), mode ?? StartupMode.Normal, quit, restoringDuties, startEpoch); }
        finally { starting = wasStarting; }
        return initialization;
    }

    private async Task<AssistantController?> InitializeAsync(Guid id, BackgroundStartSeams seams, StartupMode mode, Action? quit, bool restoringDuties, int epoch)
    {
        DextraStartStorage? storage = null;
        try
        {
            storage = await Task.Run(() => DextraStartStorage.Open(seams, id.ToString("N"))).ConfigureAwait(false);
            var opened = storage;
            return await MainActor.Run<AssistantController?>(() =>
            {
                if (disposed || !MayRun || epoch != startEpoch) { opened.Dispose(); return null; }
                var team = model.AdoptAsturs(opened.Team);
                owned = opened;
                voiceScope = new VoiceStopScope();
                var controller = model.StartDextra(opened.Store, entry => opened.Audit.Append(entry), () => team.Profiles, BrainProvider() ?? ProviderKind.Claude, BrainModel(),
                    protectedRoots: [opened.Roots.AuditDirectory], stopKey: seams.StopKey ?? model.EnsureStopKey(() => Settings.Chord), tray: seams.Tray ?? model.Tray,
                    stopScopes: [voiceScope], purchases: new PurchaseSetup { Book = PurchaseBook.Over(opened.Store), Hello = new WindowsHelloGate(), Vault = CardVault.Production },
                    team: team, homes: new AppAssistantHomes(model, opened.Store));
                var ids = team.Active.Select(p => p.Id).ToArray();
                var duties = model.StartDuties(controller, opened.Store, mode, quit: quit, assistantIds: () => team.Active.Select(p => p.Id).ToArray(), tray: seams.Tray ?? model.Tray,
                    seams: seams.Host, digest: model.DigestSetupOf(controller, opened.Roots.AuditDirectory), withDigest: !restoringDuties || BackgroundStart.HasDigestDuty(opened.Store, ids));
                var notes = duties.Start();
                var published = Publish(controller);
                if (notes.Count > 0) StartFault = string.Join(" ", notes);
                return published;
            }).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            try
            {
                await MainActor.Run(() =>
                {
                    if (epoch != startEpoch || disposed) return;
                    // A controller may already be bound when duty attachment fails. Stop owns its subscriptions too.
                    Stop();
                    initialization = null;
                    StartFault = "Dextra did not start: " + error.Message;
                    Changed?.Invoke();
                }).ConfigureAwait(false);
            }
            finally { storage?.Dispose(); }
            return null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        disposed = true;
        Settings.Changed -= OnSettings;
    }

}
