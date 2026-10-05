// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-1 C and Q-V-13 B; first-release voice typing, VT-14): what the Voice typing page's Speech model section does (voice-SPEC V-10.6, V-16.3.2, V-16.3.3).
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Support;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The Speech model section's behaviour, apart from how it looks: the hardware check (probe, recommendation, the stored record), "Use recommended" and "Keep", the model rows' Download, Use,
/// cancel and Remove, and the slow-PC answer (OD-0099 Q-V-1 C). One per settings object, so a download that ends after the page was closed still selects its model. It reads its seams when it
/// acts (<see cref="SpeechModelSeams"/>, <see cref="HardwareProbeSeams"/>), which fail closed. Every change tells <see cref="Changed"/> on the UI thread.
/// </summary>
internal sealed class SpeechModelController
{
    private static readonly Logger Log = new("voice", "page");
    private static readonly ConditionalWeakTable<AppSettings, SpeechModelController> All = new();

    private readonly AppSettings settings;
    private readonly Dictionary<string, ISpeechModelDownload> active = new(StringComparer.Ordinal);
    private IReadOnlyList<ProbeRow> rows = [];
    private Task checkTask = Task.CompletedTask;
    private Action<bool>? pendingDone;
    private int changeQueued;

    private SpeechModelController(AppSettings settings) => this.settings = settings;

    /// <summary>The controller of these settings (made on first use).</summary>
    public static SpeechModelController For(AppSettings settings) => All.GetValue(settings, s => new SpeechModelController(s));

    /// <summary>Raised on the UI thread whenever anything the section shows has changed.</summary>
    public event Action? Changed;

    public HardwareCardPhase Phase { get; private set; } = HardwareCardPhase.Idle;

    /// <summary>The probe rows reported so far in the check that is running or ran last (V-10.1).</summary>
    public IReadOnlyList<ProbeRow> Rows => rows;

    /// <summary>What the last check, or the last answer's reading, found.</summary>
    public HardwareProfile? Profile { get; private set; }

    /// <summary>The recommendation of the last finished check (V-10.2).</summary>
    public Recommendation? Recommendation { get; private set; }

    public ModelFile? RecommendedFile => ModelCatalog.Find(Recommendation?.ModelId);

    /// <summary>True once the user chose in the recommendation box (Use recommended or Keep) and until the next check.</summary>
    public bool Decided { get; private set; }

    /// <summary>The model that is downloading to be selected when it arrives (the line reads `Downloading, then in use`).</summary>
    public string? PendingId { get; private set; }

    /// <summary>The reason line under the recommendation (V-10.4); empty until a time was measured for that model.</summary>
    public string Reason { get; private set; } = "";

    public VoiceHardwareRecord? Record => settings.VoiceHardware;

    public long InstalledBytes => Backend.InstalledBytes;

    private static ISpeechModelBackend Backend => SpeechModelSeams.Backend.Current;

    public ModelInstallState State(ModelFile file) => Backend.State(file);

    // ---- the hardware check (V-10.6) ----------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// `Check my hardware` / `Check again`: the probe's rows resolve in order, then the recommendation (V-10.2) is made and the record stored (origin manual); nothing is downloaded. A check
    /// already running is returned as it is. A probe that cannot run (V-2.5, or a read that throws) ends the check where it stood, with nothing stored.
    /// </summary>
    public Task Check()
    {
        if (Phase == HardwareCardPhase.Running) return checkTask;
        Phase = HardwareCardPhase.Running;
        rows = [];
        Profile = null;
        Recommendation = null;
        Decided = false;
        Reason = "";
        var probe = HardwareProbeSeams.Probe.Current;
        Raise();
        return checkTask = RunCheck(probe);
    }

    private async Task RunCheck(IHardwareProbe probe)
    {
        HardwareProfile? found = null;
        try
        {
            found = await probe.RunAsync(row => OnUi(() => { rows = [.. rows, row]; Raise(); }), CancellationToken.None);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log.Error("the hardware check could not run: " + error.GetType().Name);
        }
        OnUi(() => Finish(found));
    }

    private void Finish(HardwareProfile? found)
    {
        if (found is null)
        {
            Phase = HardwareCardPhase.Idle;
            Raise();
            return;
        }
        Profile = found;
        var typing = settings.VoiceTyping;
        Recommendation = Recommender.Recommend(found, typing.Lang);
        settings.VoiceHardware = ManualHardwareCheck.Record(found, Recommendation, settings.VoiceHardware, typing.Model, SwiftDate.Now, HardwareCheckIdentity.App, HardwareCheckIdentity.Runtime);
        Reason = ManualHardwareCheck.Reason(found, Recommendation, settings.VoiceHardware);
        Phase = HardwareCardPhase.Done;
        Raise();
    }

    /// <summary>`Keep {current model}`: the recommendation is left as it is.</summary>
    public void Keep()
    {
        Decided = true;
        Raise();
    }

    /// <summary>
    /// `Use recommended` (V-10.6): the candidate is downloaded when it is not there, timed by the self-test seam (VT-15) and the result selected. Until a download ends the model in use
    /// stays; a download that fails or is cancelled gives the choice back.
    /// </summary>
    public void UseRecommended()
    {
        if (RecommendedFile is not { } file || Profile is not { } profile) return;
        Decided = true;
        if (IsInstalled(file))
        {
            PendingId = file.Id;
            Install(file, profile);
        }
        else
        {
            PendingId = file.Id;
            pendingDone = installed =>
            {
                if (installed) Install(file, profile);
                else Abandon();
            };
            if (!StartDownload(file)) Abandon();
        }
        Raise();
    }

    private void Install(ModelFile file, HardwareProfile profile)
    {
        var selfTest = SpeechModelSeams.SelfTest.Current;
        Task<SelfTestOutcome?> run;
        try
        {
            run = selfTest.RunAsync(file.Id, profile, CancellationToken.None);
        }
        catch (Exception error)
        {
            Log.Error("the self-test could not start: " + error.GetType().Name);
            Select(file.Id, outcome: null);
            return;
        }
        if (run.IsCompleted) Selected(file, run);
        else run.ContinueWith(task => OnUi(() => Selected(file, task)), TaskScheduler.Default);
    }

    private void Selected(ModelFile candidate, Task<SelfTestOutcome?> run)
    {
        SelfTestOutcome? outcome = null;
        if (run.IsCompletedSuccessfully) outcome = run.Result;
        else Log.Error("the self-test failed");
        Select(outcome?.ModelId ?? candidate.Id, outcome);
        if (Recommendation is { } recommendation && Profile is { } profile) Reason = ManualHardwareCheck.Reason(profile, recommendation, settings.VoiceHardware);
        PendingId = null;
        pendingDone = null;
        Raise();
    }

    private void Abandon()
    {
        PendingId = null;
        pendingDone = null;
        Decided = false;
        Raise();
    }

    // ---- the model rows (V-16.3.3) -----------------------------------------------------------------------------------------------------------------------

    /// <summary>`Download`: fetches the variant and leaves the model in use alone.</summary>
    public void Download(ModelFile file)
    {
        if (!IsInstalled(file)) StartDownload(file);
        Raise();
    }

    /// <summary>`Use`: selects an installed variant.</summary>
    public void Use(ModelFile file)
    {
        if (!IsInstalled(file)) return;
        Select(file.Id, outcome: null);
        Raise();
    }

    /// <summary>The cross: stops the transfer (the partial is kept, V-9.5 item 4); a choice waiting on it is dropped.</summary>
    public void Cancel(ModelFile file)
    {
        if (active.TryGetValue(file.Id, out var download)) download.Cancel();
    }

    /// <summary>Q-V-13 B: removes an installed variant; the store refuses the one in use.</summary>
    public ModelRemoveResult Remove(ModelFile file)
    {
        var result = Backend.Remove(file);
        Raise();
        return result;
    }

    // ---- the slow-PC answer (OD-0099 Q-V-1 C) ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The preference row's answer: stored with the hardware part of the key (R5; the hardware is read first when no check has seen it), then applied (R6): live takes the rung below the accurate
    /// model with live words on, accurate keeps the model with text after the stop. A smaller model that is not installed is downloaded and selected when it arrives. An answer that cannot be keyed
    /// to this hardware is not stored.
    /// </summary>
    public async Task Answer(LiveWordsChoice choice)
    {
        if (SlowPcPreference.AccurateModelId(settings.VoiceHardware) is not { } accurate) return;
        var profile = Profile;
        if (profile is null)
        {
            try
            {
                profile = await HardwareProbeSeams.Probe.Current.RunAsync(null, CancellationToken.None);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log.Error("the hardware could not be read for the answer: " + error.GetType().Name);
                return;
            }
            Profile = profile;
        }
        settings.VoiceTyping = SelfTestPlan.WithSlowAnswer(settings.VoiceTyping, choice, HardwareKey.ComputeHardwarePart(profile));
        var decision = SlowPcPreference.Apply(accurate, choice);
        if (ModelCatalog.Find(decision.ModelId) is not { } file) return;
        void Apply() => Select(file.Id, new SelfTestOutcome(file.Id, settings.VoiceHardware?.Model == file.Id ? settings.VoiceHardware.Rtf : 0, decision.LiveWords == true));
        if (IsInstalled(file))
        {
            Apply();
        }
        else
        {
            PendingId = file.Id;
            pendingDone = installed =>
            {
                if (installed) Apply();
                PendingId = null;
                pendingDone = null;
            };
            StartDownload(file);
        }
        Raise();
    }

    // ---- shared ---------------------------------------------------------------------------------------------------------------------------------------------

    private bool IsInstalled(ModelFile file) => Backend.State(file).Kind is ModelInstallKind.Installed or ModelInstallKind.InUse;

    /// <summary>
    /// Makes the model the one in use (voiceTyping.model). The stored check follows it: the model, and its timing when there is one (the self-test's outcome); a model chosen by hand has
    /// no measured time (ENGINEERING DECISION: live words stay as they were; timing it again is the self-test's, VT-15).
    /// </summary>
    private void Select(string id, SelfTestOutcome? outcome)
    {
        settings.VoiceTyping = settings.VoiceTyping with { Model = id };
        if (settings.VoiceHardware is not { } record) return;
        if (outcome is not null) settings.VoiceHardware = record with { Model = id, Rtf = outcome.Rtf, LiveWords = outcome.LiveWords };
        else if (!string.Equals(record.Model, id, StringComparison.Ordinal)) settings.VoiceHardware = record with { Model = id, Rtf = 0 };
    }

    private bool StartDownload(ModelFile file)
    {
        ISpeechModelDownload download;
        try
        {
            download = Backend.Download(file);
        }
        catch (Exception error)
        {
            Log.Error("a model download could not start: " + error.GetType().Name);
            return false;
        }
        active[file.Id] = download;
        download.FractionChanged += _ => RaiseSoon();
        download.Completion.ContinueWith(task => OnUi(() => Ended(file, task.IsCompletedSuccessfully ? task.Result : ModelDownloadStatus.Failed)), TaskScheduler.Default);
        return true;
    }

    private void Ended(ModelFile file, ModelDownloadStatus status)
    {
        active.Remove(file.Id);
        if (string.Equals(PendingId, file.Id, StringComparison.Ordinal) && pendingDone is { } done)
        {
            done(status == ModelDownloadStatus.Installed);
            if (status != ModelDownloadStatus.Installed && PendingId is not null) Abandon();
        }
        Raise();
    }

    private void RaiseSoon()
    {
        if (Interlocked.Exchange(ref changeQueued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref changeQueued, 0);
            Raise();
        });
    }

    private void Raise() => OnUi(() => Changed?.Invoke());

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
