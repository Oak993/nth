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
using Marrow.UI.Port;

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
    private readonly bool production;
    private readonly BackgroundStartSeams? startup;
    private readonly Func<IReadOnlyList<AssistantProfile>>? asturs;
    private readonly DextraVoiceStarter? voiceStarter;
    private VoiceStopScope? voiceScope;
    private AssistantController? started;
    private IDisposable? owned;
    private IReadOnlyList<AssistantProfile> restoredProfiles = [];
    private bool disposed;
    private bool starting;
    private readonly Marrow.UI.Port.IFrameSource? voiceFrames;
    private readonly Action<TimeSpan> voiceFrame;
    private Marrow.UI.Port.IFrameSource? waitingVoice;
    private readonly bool explicitVoice;
    private readonly Func<AppModel, DextraHost, VoiceStopScope, CancellationToken, Task<DextraVoice?>>? asyncVoice;
    private CancellationTokenSource? startingVoice;
    private bool voiceFrameSeen;
    private Task voiceTeardown = Task.CompletedTask;

    /// <param name="voice">AS-18: the voice of a running controller. Null with the app's own starter gives the real one (the chord, the microphone, the PC's voices); null with a test's starter gives none.</param>
    public DextraHost(AppModel model, DextraSettings settings, DextraStarter? starter = null, Func<IReadOnlyList<AssistantProfile>>? asturs = null, DextraVoiceStarter? voice = null, BackgroundStartSeams? startup = null, Marrow.UI.Port.IFrameSource? voiceFrames = null, Func<AppModel, DextraHost, VoiceStopScope, CancellationToken, Task<DextraVoice?>>? voiceAsync = null)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.starter = starter ?? ((app, prefs, id) => Production(app, prefs, id, startup, StartupMode.Normal, null, false));
        production = starter is null;
        this.voiceFrames = voiceFrames;
        voiceFrame = OnVoiceFrame;
        explicitVoice = voice is not null || voiceAsync is not null;
        asyncVoice = voiceAsync ?? (production && !explicitVoice && startup is null
            ? static (app, host, scope, token) => DextraVoice.CreateDeferred(app, host, scope, token) : null);
        this.startup = startup;
        this.asturs = asturs;
        voiceStarter = voice ?? (starter is null && startup is null ? static (app, host, scope) => DextraVoice.Create(app, host, scope) : null);
        Settings.Changed += OnSettings;
    }

    public DextraSettings Settings { get; }

    /// <summary>Dextra's identity, made once and kept.</summary>
    public Guid Id => Settings.EnsureId();

    /// <summary>The identity string the controller and the profiles use.</summary>
    public string AssistantId => Id.ToString("N");

    /// <summary>The running controller, or null (off, first run not done, or the start failed).</summary>
    public AssistantController? Controller => started;

    /// <summary>The sources the sidebar row and the home read: the running controller's, else the idle set.</summary>
    public DextraLive Live { get; private set; } = DextraLive.Idle;

    /// <summary>Dextra's voice (AS-18): the talk chord and the conversation of the running controller, or null (off, not started, or no voice in this build).</summary>
    public DextraVoice? Voice { get; private set; }
    internal Task VoiceStartup { get; private set; } = Task.CompletedTask;

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
        var root = restoredProfiles.FirstOrDefault(p => p.Id == AssistantId && p.IsRoot);
        var current = new List<AssistantProfile> { root ?? new(AssistantId, Name) };
        current.AddRange(asturs?.Invoke() ?? []);
        current.AddRange(restoredProfiles.Where(p => current.All(c => c.Id != p.Id)));
        return current;
    }

    /// <summary>Starts the controller when Dextra may run and none runs; keeps its grants and its brain's switches in step.</summary>
    public AssistantController? EnsureStarted(BackgroundStartSeams? seams = null, StartupMode? mode = null, Action? quit = null, bool restoringDuties = false)
    {
        if (disposed || !MayRun || starting) return started;
        if (started is not null) return started;
        AssistantController? controller;
        starting = true; // the identity's first store raises Changed; that must not start a second controller
        try { controller = model.Dextra ?? (production
            ? Production(model, Settings, Id, startup ?? seams, mode ?? StartupMode.Normal, quit, restoringDuties)
            : starter(model, Settings, Id)); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // what the start opened before it failed is let go now, not when the host goes
            if (model.Dextra is { } failed)
            {
                model.UnbindDextra();
                failed.Dispose();
            }
            restoredProfiles = [];
            owned?.Dispose();
            owned = null;
            StartFault = error.Message;
            Changed?.Invoke();
            return null;
        }
        finally { starting = false; }
        if (controller is null)
        {
            restoredProfiles = [];
            owned?.Dispose();
            owned = null;
            return null;
        }
        StartFault = null;
        started = controller;
        controller.RestoreGrants(Settings.Value.Grants);
        controller.GrantsChanged += KeepGrants;
        Live = DextraLive.For(model, controller, NameOf);
        model.AssistantEvents.Brain = Settings.Brain(BrainProvider());
        if ((startup ?? seams) is null || explicitVoice)
        {
            // ED-NEW-dextra-smooth-7: normal and hidden production launches share the bound frame source.
            // The native chord thread's readiness/register waits must not precede nth's first frame.
            var frames = voiceFrames ?? (production && FrameClock.IsBound ? FrameClock.Source.Current : null);
            if ((voiceStarter is not null || asyncVoice is not null) && frames is not null)
            {
                voiceFrameSeen = false; waitingVoice = frames; frames.RequestFrame(voiceFrame);
            }
            else StartVoice();
        }
        Changed?.Invoke();
        return controller;
    }

    /// <summary>
    /// Dextra turned off: every task is stopped (Stop all never asks), the tools are no chat's any more and the
    /// controller is let go.
    /// </summary>
    public void Stop()
    {
        waitingVoice?.CancelFrame(voiceFrame);
        waitingVoice = null;
        startingVoice?.Cancel();
        startingVoice = null;
        if ((started ?? model.Dextra) is not { } controller) return;
        started = null;
        controller.GrantsChanged -= KeepGrants;
        if (Voice is { } oldVoice)
        {
            oldVoice.Dispose();
            TrackVoiceCleanup(oldVoice.NativeCleanup);
        }
        Voice = null;
        var live = Live;
        Live = DextraLive.Idle;
        live.Dispose();
        try { controller.Controls.StopAll(StopSource.Program); }
        catch (Exception error) when (error is not OperationCanceledException) { StartFault = error.Message; }
        if (model.Dextra == controller) model.UnbindDextra();
        controller.Dispose();
        restoredProfiles = [];
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

    private void StartVoice()
    {
        if (disposed || started is null || !MayRun) return;
        if (asyncVoice is not null)
        {
            var cancellation = new CancellationTokenSource();
            startingVoice = cancellation;
            var prior = JoinVoiceWork(voiceTeardown, VoiceStartup);
            VoiceStartup = StartVoiceAsync(started, voiceScope ?? new VoiceStopScope(), cancellation, prior);
            return;
        }
        try { Voice = voiceStarter?.Invoke(model, this, voiceScope ?? new VoiceStopScope()); }
        catch (Exception error) when (error is not OperationCanceledException) { StartFault = "Voice could not start: " + error.Message; }
    }

    private void OnVoiceFrame(TimeSpan time)
    {
        if (waitingVoice is null) return;
        // Let the first frame finish before starting native preparation on a later frame.
        if (!voiceFrameSeen) { voiceFrameSeen = true; waitingVoice.RequestFrame(voiceFrame); return; }
        waitingVoice = null; StartVoice();
        if (asyncVoice is null) Changed?.Invoke();
    }

    private static Task JoinVoiceWork(Task left, Task right) =>
        left.IsCompletedSuccessfully ? right : right.IsCompletedSuccessfully ? left : Task.WhenAll(left, right);

    private async Task StartVoiceAsync(AssistantController controller, VoiceStopScope scope, CancellationTokenSource cancellation, Task prior)
    {
        DextraVoice? made = null;
        try
        {
            // An abandoned generation must actually release its OS chord before another prepares/registers.
            // The barrier itself is never canceled; repeated stop/start cannot forget an older reservation.
            await prior.ConfigureAwait(false);
            // Canceled preparation can record its cleanup after this generation captured prior.
            var teardown = await MainActor.Run(() => voiceTeardown).ConfigureAwait(false);
            await teardown.ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            var preparation = await MainActor.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                return asyncVoice!(model, this, scope, cancellation.Token);
            }).ConfigureAwait(false);
            made = await preparation.ConfigureAwait(false);
            DextraVoice? published = null;
            await MainActor.Run(() =>
            {
                if (disposed || cancellation.IsCancellationRequested || !ReferenceEquals(startingVoice, cancellation) || started != controller) return;
                Voice = made; made = null;
                Voice?.Activate();
                published = Voice;
                Changed?.Invoke();
            }).ConfigureAwait(false);
            if (published is not null) await published.ChordRefresh.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { /* stopped generation */ }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await MainActor.Run(() =>
            {
                if (disposed || cancellation.IsCancellationRequested || !ReferenceEquals(startingVoice, cancellation)) return;
                StartFault = "Voice could not start: " + error.Message; Changed?.Invoke();
            }).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (made is not null)
                {
                    await MainActor.Run(() => { made.Dispose(); TrackVoiceCleanup(made.NativeCleanup); }).ConfigureAwait(false);
                    await made.NativeCleanup.ConfigureAwait(false);
                }
            }
            finally
            {
                await MainActor.Run(() => { if (ReferenceEquals(startingVoice, cancellation)) startingVoice = null; }).ConfigureAwait(false);
                cancellation.Dispose();
            }
        }
    }

    internal void VoiceFault(DextraVoice voice, Exception error)
    {
        if (disposed || !ReferenceEquals(Voice, voice)) return;
        StartFault = "Voice could not start: " + error.Message; Changed?.Invoke();
    }

    internal void TrackVoiceCleanup(Task cleanup)
    {
        if (cleanup.IsCompletedSuccessfully) return;
        MainActor.AssumeIsolated();
        voiceTeardown = JoinVoiceWork(voiceTeardown, cleanup);
    }

    private void KeepGrants()
    {
        if (started is { } controller) Settings.KeepGrants(controller.Grants, SwiftClock.Provider.Current.GetUtcNow());
    }

    // The production start: the store and the audit log on their default roots, the stop chord over the owner's
    // setting and the tray the app made. Kept here so the views never touch Credential Manager.
    private AssistantController? Production(AppModel app, DextraSettings settings, Guid id, BackgroundStartSeams? seams, StartupMode mode, Action? quit, bool restoringDuties)
    {
        var roots = seams?.Roots() ?? AssistantStorage.Roots.Current;
        var store = AssistantStore.Open(roots, new AssistantStoreOptions { InterruptedMission = MissionJournal.Interrupted });
        AuditLog audit;
        try { audit = AuditLog.Open(roots, seams?.Keys() ?? AuditKeyStore.Production()); }
        catch { store.Dispose(); throw; }
        owned = new Owned(store, audit);
        using (var team = AssistantTeam.Open(store))
        {
            var rootFile = Path.Combine(store.Root, "assistants", AssistantId, "profile.json");
            if (File.Exists(rootFile) && team.Get(AssistantId) is not { IsRoot: true })
                throw new InvalidDataException("Dextra's saved profile could not be read safely: " + string.Join(" ", team.Problems));
            restoredProfiles = team.Profiles;
        }
        // Older duties can name an assistant that never had a profile; a present unusable profile is never replaced.
        var missing = BackgroundStart.AssistantsWithDuties(store)
            .Where(assistant => !File.Exists(Path.Combine(store.Root, "assistants", assistant, "profile.json"))).ToList();
        restoredProfiles = [.. restoredProfiles, .. BackgroundStart.Profiles(store, missing)];
        var stopKey = seams?.StopKey ?? app.EnsureStopKey(() => settings.Chord);
        // AS-20 (12.7, 16.2): purchases.json beside the store's other files, the card in Credential Manager, Windows Hello. This build
        // has no fill path (Visus and Tactus are not here), so the switch is hidden and every payment is handed to the owner.
        var purchases = new PurchaseSetup { Book = PurchaseBook.Over(store), Hello = new WindowsHelloGate(), Vault = CardVault.Production };
        voiceScope = new VoiceStopScope(); // Stop all and the stop chord cut a spoken reply (12.6); bound to the conversation once the voice exists
        var controller = app.StartDextra(store, entry => audit.Append(entry), Profiles, BrainProvider() ?? ProviderKind.Claude, BrainModel(),
            protectedRoots: [roots.AuditDirectory], stopKey: stopKey, tray: seams?.Tray ?? app.Tray, stopScopes: [voiceScope], purchases: purchases, accountId: settings.Value.BrainAccount);
        controller.RestoreGrants(settings.Value.Grants);
        var ids = BackgroundStart.AssistantsWithDuties(store);
        var duties = app.StartDuties(controller, store, mode, quit: quit,
            assistantIds: () => [.. Profiles().Where(p => !p.Retired).Select(p => p.Id).Distinct(StringComparer.Ordinal)],
            tray: seams?.Tray ?? app.Tray, seams: seams?.Host, digest: app.DigestSetupOf(controller, roots.AuditDirectory),
            withDigest: !restoringDuties || BackgroundStart.HasDigestDuty(store, ids));
        foreach (var note in duties.Start())
            app.AssistantEvents.Publish(FeedDraft.About(FeedEventKind.WatcherChanged, FeedSeverity.Error, TimeProvider.System.GetUtcNow(), note));
        return controller;
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        disposed = true;
        Settings.Changed -= OnSettings;
    }

    private sealed class Owned(AssistantStore store, AuditLog audit) : IDisposable
    {
        public void Dispose()
        {
            audit.Dispose();
            store.Dispose();
        }
    }
}
