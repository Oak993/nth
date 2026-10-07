// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096, OD-0098; first-release voice typing, VT-15, voice-SPEC V-10.3, V-10.7): the voice first-run preparation service.
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The service the Getting ready screen, Welcome's voice question and the orb call (V-10.7): <see cref="ProbeAsync"/> (always allowed: it reads the PC and fetches nothing),
/// <see cref="Recommend"/> (V-10.2), <see cref="InstallAndTimeAsync"/> (download, verify and self-test, V-10.3) and <see cref="IsPrepared"/>. OD-0098 "D3 with consent": nothing is downloaded,
/// verified, loaded or stored until <see cref="GrantConsent"/> (Welcome's yes, or an orb click); a second call while a run is going joins it, so an early orb click shows the
/// download's progress. Progress is real events only (OD-0096): no timer, no estimate. Every collaborator is a seam; nothing here touches a network, a microphone or a model by itself.
/// </summary>
public sealed class VoicePreparation
{
    public const string ProbeId = "voice.probe";
    public const string DownloadId = "voice.download";
    public const string VerifyId = "voice.verify";
    public const string SelfTestId = "voice.selftest";

    /// <summary>The probe's four rows (V-10.1): the share of them done is the probe item's fraction.</summary>
    private const int ProbeRows = 4;

    /// <summary>The bundled clip is English (voice-pins.json selfTestClip.language en_us), so the self-test decodes it as English whatever the user's language.</summary>
    private const string ClipLanguage = "en";

    private readonly IHardwareProbe probe;
    private readonly IVoiceModelSource models;
    private readonly Func<ISpeechEngine> engine;
    private readonly IVoicePreparationSettings settings;
    private readonly Func<ReadOnlyMemory<float>> clip;
    private readonly string app;
    private readonly string runtimeVersion;
    private readonly TimeProvider clock;
    private readonly Func<string?>? currentKey;
    private readonly Func<string?>? currentPart;
    private readonly Lock gate = new();
    private readonly List<IProgress<VoicePrepItem>> subscribers = [];
    private HardwareProfile? profile;
    private RunState? running;
    private VoicePrepItem? latest;
    private volatile bool consent;

    /// <param name="app">The app's version and build as stored in the record's <c>app</c> and the key (V-10.5).</param>
    /// <param name="runtimeVersion">The Whisper.net runtime version of the key (V-10.5).</param>
    /// <param name="currentKey">Today's key when no probe has run yet (the startup check); null: only a probe can say.</param>
    public VoicePreparation(IHardwareProbe probe, IVoiceModelSource models, Func<ISpeechEngine> engine, IVoicePreparationSettings settings,
        Func<ReadOnlyMemory<float>> clip, string app, string runtimeVersion, TimeProvider? clock = null, Func<string?>? currentKey = null, Func<string?>? currentPart = null)
    {
        this.probe = probe;
        this.models = models;
        this.engine = engine;
        this.settings = settings;
        this.clip = clip;
        this.app = app;
        this.runtimeVersion = runtimeVersion;
        this.clock = clock ?? TimeProvider.System;
        this.currentKey = currentKey;
        this.currentPart = currentPart;
    }

    /// <summary>The hardware part of today's key (the key the slow-PC answer is stored under, R5) from the last probe; null before any.</summary>
    public string? HardwarePart
    {
        get
        {
            lock (gate) return profile is null ? currentPart?.Invoke() : HardwareKey.ComputeHardwarePart(profile);
        }
    }

    /// <summary>The slow-PC answer (Q-V-1 C, R5 to R8): stores it with its hardware part, applies it to the kept model (live: the next smaller rung, installed, live words on) and rewrites the stored record. Null: nothing stored.</summary>
    public async Task<VoiceHardwareRecord?> AnswerSlowQuestionAsync(LiveWordsChoice choice, CancellationToken cancellationToken)
    {
        var part = HardwarePart;
        var record = settings.Record;
        if (part is null || record is null || !consent) return null;
        var decision = SelfTestPlan.ApplyAnswer(record.Model, choice);
        var changed = !string.Equals(decision.ModelId, record.Model, StringComparison.Ordinal);
        if (changed)
        {
            try
            {
                await EnsureInstalledAsync(decision.ModelId, cancellationToken);
            }
            catch (PreparationFailure)
            {
                return null; // the answer is not stored: the question stays open
            }
        }
        var updated = record with { Model = decision.ModelId, LiveWords = decision.LiveWords ?? false };
        var typing = SelfTestPlan.WithSlowAnswer(settings.Typing, choice, part);
        if (changed && string.Equals(typing.Model, record.Model, StringComparison.Ordinal)) typing = typing with { Model = decision.ModelId };
        settings.Typing = typing;
        settings.Record = updated;
        return updated;
    }

    /// <summary>The size in bytes of the model the recommendation for the last probed PC would download (Welcome's <c>{mb}</c>); null before any probe.</summary>
    public long? CandidateBytes
    {
        get
        {
            HardwareProfile? known;
            lock (gate) known = profile;
            return known is null ? null : Marrow.Core.Port.Voice.ModelCatalog.Find(Recommend(known).ModelId)?.Bytes;
        }
    }

    /// <summary>The size in bytes of the smaller live model the slow question offers, the one a "live" answer installs for the stored result's model (Welcome's <c>{small}</c>); null with no stored result or no smaller model.</summary>
    public long? LiveAnswerBytes
    {
        get
        {
            var record = settings.Record;
            if (record is null) return null;
            var smaller = SelfTestPlan.ApplyAnswer(record.Model, LiveWordsChoice.Live);
            return smaller.LiveWords == true ? Marrow.Core.Port.Voice.ModelCatalog.Find(smaller.ModelId)?.Bytes : null;
        }
    }

    /// <summary>True once the user said yes (Welcome) or clicked the orb.</summary>
    public bool HasConsent => consent;

    /// <summary>The user's yes: from now <see cref="InstallAndTimeAsync"/> may download.</summary>
    public void GrantConsent() => consent = true;

    /// <summary>V-10.1 as a step (<see cref="ProbeId"/>): an item per finished row. Allowed without consent.</summary>
    public async Task<HardwareProfile> ProbeAsync(IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken)
    {
        var label = FirstRunSteps.Voice.Name;
        var done = 0;
        void Report(double fraction, VoicePrepState state) => progress?.Report(new VoicePrepItem(ProbeId, label, fraction, state));
        Report(0, VoicePrepState.Running);
        var found = await probe.RunAsync(row =>
        {
            done++;
            Report(Math.Min(done, ProbeRows) / (double)ProbeRows, done >= ProbeRows ? VoicePrepState.Done : VoicePrepState.Running);
        }, cancellationToken);
        if (done < ProbeRows) Report(1, VoicePrepState.Done);
        lock (gate) profile = found;
        return found;
    }

    /// <summary>V-10.2 for the probe's profile and the language the settings resolve to (V-7.14; Auto-detect and "ask" read as multilingual).</summary>
    public Recommendation Recommend(HardwareProfile found) =>
        Recommender.Recommend(found, VoiceLanguage.Resolve(settings.Typing.Lang, settings.Orb.LangMode, settings.UiLanguage).Language);

    /// <summary>
    /// Probe (unless a profile is already known), recommend, install and time: what the orb click and Welcome's yes call. A current stored result (<see cref="IsPrepared"/>) is given back
    /// with no work. Without consent the result is <see cref="VoicePreparationStatus.AwaitingConsent"/> after the probe.
    /// </summary>
    public async Task<VoicePreparationResult> PrepareAsync(IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken)
    {
        if (IsPrepared(out var stored) && UsableResult(stored))
        {
            return new VoicePreparationResult(VoicePreparationStatus.Prepared, stored, NeedsSlowAnswer(stored!), null);
        }
        HardwareProfile? known;
        lock (gate) known = profile;
        known ??= await ProbeAsync(progress, cancellationToken);
        // The startup key may be unavailable until this probe. A current record is reused after the read, never re-timed.
        if (IsPrepared(out stored) && UsableResult(stored))
            return new VoicePreparationResult(VoicePreparationStatus.Prepared, stored, NeedsSlowAnswer(stored!), null);
        return await InstallAndTimeAsync(Recommend(known).ModelId, known, progress, cancellationToken);
    }

    internal bool NeedsSlowAnswer(VoiceHardwareRecord record)
    {
        var part = HardwarePart;
        return record.Backend == VoiceBackend.Cpu && !record.LiveWords && SelfTestPlan.StepDown(record.Model) is not null
            && record.Rtf > SelfTestPlan.LiveWordsMaxRtf && (part is null || !SelfTestPlan.AnsweredFor(settings.Typing, part));
    }

    // IsPrepared is V-10.5's key check. A manual probe can write that same key without installing or timing any model (VT-14).
    // That record is useful hardware evidence, but never licenses skipping the consent-gated setup or starting capture.
    internal static bool UsableResult(VoiceHardwareRecord? record) => record is not null && Marrow.Core.Port.Voice.ModelCatalog.Find(record.Model) is not null;

    /// <summary>
    /// V-10.3 for model <paramref name="id"/> and the probed <paramref name="found"/>: install it if needed (steps <see cref="DownloadId"/>, <see cref="VerifyId"/>), time it (<see cref="SelfTestId"/>), step down
    /// once and time again when the processor is too slow, and store <c>voiceHardware</c> with origin auto. Failures store nothing and give <see cref="VoicePreparationStatus.Failed"/>; a cancel throws.
    /// A call while a run is going joins that run (its token is the one that counts).
    /// </summary>
    public Task<VoicePreparationResult> InstallAndTimeAsync(string id, HardwareProfile found, IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken)
    {
        RunState state;
        lock (gate)
        {
            if (!consent) return Task.FromResult(new VoicePreparationResult(VoicePreparationStatus.AwaitingConsent, null, false, null));
            if (running is { } joined)
            {
                if (progress is not null)
                {
                    if (latest is { } so) progress.Report(so);
                    subscribers.Add(progress);
                }
                return joined.Task.WaitAsync(cancellationToken);
            }
            state = new RunState();
            running = state;
            latest = null;
            subscribers.Clear();
            if (progress is not null) subscribers.Add(progress);
            state.Task = Run(id, found, state, cancellationToken); // may run on until its first real wait before this returns; the lock is re-entered by the same thread
        }
        return state.Task.WaitAsync(cancellationToken);
    }

    private sealed class RunState
    {
        public Task<VoicePreparationResult> Task { get; set; } = null!;
    }

    private void Emit(string id, string label, double? fraction, VoicePrepState state)
    {
        var item = new VoicePrepItem(id, label, fraction, state);
        IProgress<VoicePrepItem>[] to;
        lock (gate)
        {
            latest = item;
            to = [.. subscribers];
        }
        foreach (var s in to) s.Report(item);
    }

    private sealed class PreparationFailure(string message) : Exception(message);

    private async Task<VoicePreparationResult> Run(string id, HardwareProfile found, RunState state, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gpu = found.Backend != GpuBackend.None;
            var basis = gpu ? RecommendationBasis.Gpu : RecommendationBasis.Cpu;
            var part = HardwareKey.ComputeHardwarePart(found);
            var typing = settings.Typing;
            var answered = SelfTestPlan.AnsweredFor(typing, part);
            var audio = clip();
            var model = id;
            var steppedDown = false;
            double rtf;
            SelfTestDecision decision;
            while (true)
            {
                await EnsureInstalledAsync(model, cancellationToken);
                rtf = await TimeAsync(model, gpu, audio, cancellationToken);
                decision = SelfTestPlan.Decide(model, rtf, steppedDown, basis, answered);
                if (decision.Action != SelfTestAction.StepDown) break;
                model = decision.ModelId;
                steppedDown = true;
            }
            var live = decision.LiveWords;
            if (live is null)
            {
                var applied = SelfTestPlan.ApplyAnswer(model, SelfTestPlan.AnswerFor(typing, part));
                if (!string.Equals(applied.ModelId, model, StringComparison.Ordinal))
                {
                    model = applied.ModelId;
                    await EnsureInstalledAsync(model, cancellationToken);
                }
                live = applied.LiveWords;
            }
            var record = new VoiceHardwareRecord
            {
                Key = HardwareKey.Compute(found, app, runtimeVersion),
                Origin = HardwareOrigin.Auto,
                Cpu = found.CpuName,
                Cores = found.PhysicalCores,
                RamGB = found.MemoryGB,
                Gpu = found.GpuDescription,
                VramGB = Gigabytes(found.DedicatedVramBytes),
                FreeVramGB = Gigabytes(found.FreeVramBytes),
                Backend = found.Backend switch { GpuBackend.Vulkan => VoiceBackend.Vulkan, GpuBackend.Cuda => VoiceBackend.Cuda, _ => VoiceBackend.Cpu },
                Candidate = id,
                Model = model,
                Rtf = rtf,
                LiveWords = live ?? false,
                At = WholeSeconds(clock.GetUtcNow()),
                App = app,
            };
            settings.Record = record;
            var current = settings.Typing;
            if (current.Model is null) settings.Typing = current with { Model = model };
            lock (gate) profile = found;
            return new VoicePreparationResult(VoicePreparationStatus.Prepared, record, decision.AskUser, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PreparationFailure failure)
        {
            return new VoicePreparationResult(VoicePreparationStatus.Failed, null, false, failure.Message);
        }
        catch (Exception error)
        {
            return new VoicePreparationResult(VoicePreparationStatus.Failed, null, false, error.Message);
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(running, state)) running = null;
                subscribers.Clear();
            }
        }
    }

    private async Task EnsureInstalledAsync(string modelId, CancellationToken cancellationToken)
    {
        var file = Marrow.Core.Port.Voice.ModelCatalog.Find(modelId) ?? throw new PreparationFailure(modelId + " is not a voice model of the catalogue.");
        const string label = "Voice model";
        if (!models.IsInstalled(file))
        {
            Emit(DownloadId, label, 0.0, VoicePrepState.Running);
            var result = await models.InstallAsync(file, fraction => Emit(DownloadId, label, fraction, VoicePrepState.Running), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Status != ModelDownloadStatus.Installed)
                throw new PreparationFailure("The voice model download " + result.Status.ToString().ToLowerInvariant() + ".");
        }
        Emit(DownloadId, label, 1.0, VoicePrepState.Done);
        Emit(VerifyId, label, null, VoicePrepState.Running);
        var verification = await models.VerifyAsync(file, cancellationToken);
        if (verification is ModelVerification.Corrupt or ModelVerification.Missing)
            throw new PreparationFailure("The voice model did not verify (" + verification.ToString().ToLowerInvariant() + ").");
        Emit(VerifyId, label, 1.0, VoicePrepState.Done);
    }

    /// <summary>V-10.3: a warm-up load, then one timed transcription of the clip with the live parameters; RTF = seconds / 10.</summary>
    private async Task<double> TimeAsync(string modelId, bool gpu, ReadOnlyMemory<float> audio, CancellationToken cancellationToken)
    {
        const string label = "Voice speed test";
        var file = Marrow.Core.Port.Voice.ModelCatalog.Find(modelId)!;
        Emit(SelfTestId, label, null, VoicePrepState.Running);
        var speech = engine();
        await speech.LoadAsync(new SpeechModel(file.Id, models.PathFor(file)), new EngineLoadOptions(UseGpu: gpu), cancellationToken);
        var start = clock.GetTimestamp();
        await speech.TranscribeAsync(audio, new DecodeRequest(ClipLanguage, "", DecodeMode.Live, TimeSpan.Zero), cancellationToken);
        var seconds = clock.GetElapsedTime(start).TotalSeconds;
        Emit(SelfTestId, label, 1.0, VoicePrepState.Done);
        return SelfTestPlan.Rtf(seconds);
    }

    private static double Gigabytes(long bytes) => Math.Round(bytes / (double)(1L << 30), 1);

    private static DateTimeOffset WholeSeconds(DateTimeOffset at) =>
        new(at.UtcDateTime.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

    /// <summary>
    /// V-10.5: true when a stored result has today's key (from the last probe, else the constructor's key source); <paramref name="record"/> is the stored result, current or stale, or null.
    /// A stale or missing result runs the check again at the next first-run moment, never mid-session.
    /// </summary>
    public bool IsPrepared(out VoiceHardwareRecord? record)
    {
        record = settings.Record;
        HardwareProfile? known;
        lock (gate) known = profile;
        var key = known is not null ? HardwareKey.Compute(known, app, runtimeVersion) : currentKey?.Invoke();
        return record is not null && key is not null && !HardwareKey.IsStale(record, key);
    }

    /// <summary>
    /// V-7.7: the <c>LiveWords</c> switch, read from the stored record at the start of a session. With no record it is on, the live default of OD-0091 (a session cannot start before the
    /// first run has stored one; this is only the answer for a caller that asks first).
    /// </summary>
    public static bool LiveWordsOf(VoiceHardwareRecord? record) => record?.LiveWords ?? true;

    /// <summary>
    /// Registers the "Voice hardware" step (W1: id hw, order 60) in GR-1's registry as a work step: the Getting ready screen runs the probe in its place. The model is not downloaded there
    /// (D3 with consent, OD-0098). Dispose the result to take it out.
    /// </summary>
    public IDisposable RegisterStep() =>
        GpuWarmup.Register(GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, cancellationToken => ProbeAsync(null, cancellationToken)));
}
