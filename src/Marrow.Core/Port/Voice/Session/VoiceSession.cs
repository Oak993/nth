// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0097, OD-0099; first-release voice typing, VT-3): the state machine of one
// composer's voice input (voice-SPEC V-15).
using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Port.Voice.Session;

/// <summary>
/// One composer's voice input: the table of V-15 on the seams of V-2.4. Main-actor only: capture delivers to <see cref="OnAudio"/> on the main
/// executor (the production pipeline of VT-5 posts its batches), and every wait is a timer on <c>SwiftClock</c> or a token, so a test moves
/// it all with the manual clock. The session reports; it draws nothing and writes to no draft: the composer (VT-10 to VT-12) listens to the
/// events and the dictation layer applies the operations.
/// </summary>
public sealed class VoiceSession : IAudioCaptureSink
{
    public static readonly TimeSpan Cap = TimeSpan.FromSeconds(300);
    public static readonly TimeSpan CountdownLength = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan LandingLength = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan SendDelay = TimeSpan.FromSeconds(0.35);
    public static readonly TimeSpan BlockedLength = TimeSpan.FromSeconds(5.2);
    public static readonly TimeSpan SilenceCheckLength = TimeSpan.FromSeconds(1.0);

    private const float SpeechEnter = 0.5f;
    private const float SpeechLeave = 0.35f;
    private static readonly TimeSpan SpeechLeaveAfter = TimeSpan.FromMilliseconds(100);

    private readonly Func<VoiceSessionSettings> settings;
    private readonly Action<VoiceTypingSettings> storeTyping;
    private readonly VoiceSeamSet seams;
    private readonly VoiceSessionHub hub;
    private readonly SpeechLabel label = new();
    private readonly AudioLevelMeter meter = new();

    private VoiceSessionState state = VoiceSessionState.Rest;
    private VoiceSessionSettings? active;
    private LanguageResolution? resolution;
    private StreamingTranscriber? transcriber;
    private ISpeechEngine? sessionEngine;
    private IReadOnlyList<TranscriptWord> shown = [];
    private string? detectedLanguage;
    private VoiceStopReason? lastStop;
    private bool suppressSend;
    private int generation;

    private bool captureRunning;
    private CaptureFormat format;
    private long zeroFrames;
    private bool zeroChecked;
    private readonly List<float> pending = [];
    private bool inSpeech;
    private TimeSpan quiet;
    private ContinuousInstant listenStart;
    private ContinuousInstant? lastAddAt;
    private ContinuousInstant? processingOpsUntil;
    private bool countdownActive;
    private Task<bool> loadWait = Task.FromResult(true);

    private WorkItem? capTimer;
    private WorkItem? countdownTimer;
    private WorkItem? processingTimer;
    private WorkItem? landingTimer;
    private WorkItem? sendTimer;
    private WorkItem? blockedTimer;
    private TaskHandle? passTask;
    private TaskHandle? finalTask;

    public VoiceSession(Func<VoiceSessionSettings> settings, Action<VoiceTypingSettings> storeTyping, VoiceSeamSet? seams = null,
        VoiceSessionHub? hub = null)
    {
        this.settings = settings;
        this.storeTyping = storeTyping;
        this.seams = seams ?? VoiceSeamSet.Ambient;
        this.hub = hub ?? VoiceSessionHub.Shared.Current;
    }

    public VoiceSessionState State => state;

    /// <summary>Why the last listening ended (the table's click, cap or stop event); null before any.</summary>
    public VoiceStopReason? LastStopReason => lastStop;

    /// <summary>True while a click on this orb waits for the running session to finish (V-7.11).</summary>
    public bool StartQueued => ReferenceEquals(hub.Queued, this);

    /// <summary>The length of session audio still held; zero once the session ends (V-4.7).</summary>
    public TimeSpan BufferedAudio => transcriber?.HeldAudio ?? TimeSpan.Zero;

    /// <summary>The pill's state now (V-6.3).</summary>
    public VoiceLabelState Label => label.State(SwiftClock.Now);

    /// <summary>The pill's words now, from the set the session started with (V-12.9).</summary>
    public string LabelText => label.Words((active ?? settings()).Orb.Labels, SwiftClock.Now);

    /// <summary>The language this session decodes in: a Whisper code or "auto" (V-7.13); null before a session starts.</summary>
    public string? ResolvedLanguage => resolution?.Language;

    /// <summary>The language the engine detected, once it reported one (V-12.10).</summary>
    public string? DetectedLanguage => detectedLanguage;

    /// <summary>Whether the pill names the detected language (V-12.10, language default B).</summary>
    public bool LanguageNamed => resolution?.Named ?? false;

    /// <summary>The words as the composer holds them (committed solid, then the tentative tail).</summary>
    public IReadOnlyList<TranscriptWord> Words => shown;

    /// <summary>The VAD's speech state now (V-6.2): on at p &gt;= 0.5, off after 100 ms below 0.35.</summary>
    public bool InSpeech => state == VoiceSessionState.Listening && inSpeech;

    /// <summary>
    /// The raw level and bands of the audio being listened to (V-5.3), published from the processing thread at 60 Hz of the signal (VT-11, for the orb and the glow, which smooth them
    /// with <see cref="LevelFollowers"/> on the UI thread, V-5.5). The values come from the processing thread, but this property reads the session's state, which is UI-thread only: read it on the UI thread (the meter's own <c>Latest</c> is the any-thread part); zero outside Listening.
    /// </summary>
    public LevelBands AudioLevel => state == VoiceSessionState.Listening ? meter.Latest : LevelBands.Zero;

    /// <summary>Q-V-6 B: true from 270 s of listening until the cap at 300 s.</summary>
    public bool CapCountdownActive => state == VoiceSessionState.Listening && countdownActive;

    /// <summary>Q-V-6 B: what is left of the 30 s countdown; null outside it.</summary>
    public TimeSpan? CapCountdownRemaining
    {
        get
        {
            if (!CapCountdownActive) return null;
            var left = Cap - (SwiftClock.Now - listenStart);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>The error that ended the session in a note (VT-17: what the note's words are chosen from): a capture, load or engine exception; null when none was raised.</summary>
    public Exception? FailureCause { get; private set; }

    /// <summary>The last device event the capture reported while listening (V-4.4); null when none.</summary>
    public CaptureDeviceEventKind? LastDeviceEvent { get; private set; }

    public event Action<VoiceSessionState, VoiceSessionState>? StateChanged;

    public event Action<TranscriptUpdate>? TranscriptChanged;

    public event Action<VoiceNoteKind>? NoteRaised;

    public event Action<SendIntent>? SendRequested;

    public event Action? CapCountdownStarted;

    // V-15: Rest -> Hover. The model load starts here or at the click, whichever comes first (V-7.9).
    public void PointerEnter()
    {
        if (state != VoiceSessionState.Rest || StartQueued) return;
        SetState(VoiceSessionState.Hover);
        if (settings().Model is { } model) _ = hub.EnsureLoaded(seams.Engine, model);
    }

    public void PointerLeave()
    {
        if (state == VoiceSessionState.Hover) SetState(VoiceSessionState.Rest);
    }

    /// <summary>The orb clicked, or Enter or Space on it.</summary>
    public void Activate()
    {
        switch (state)
        {
            case VoiceSessionState.Rest or VoiceSessionState.Hover or VoiceSessionState.Landing:
                Begin();
                break;
            case VoiceSessionState.Asking:
                CloseQuestion(); // V-15: the orb clicked again closes the question
                break;
            case VoiceSessionState.Listening:
                Stop(VoiceStopReason.Click);
                break;
            // Processing: ignored, the orb is not pressable. Blocked: leaves only by its 5.2 s (ED-292 (8)).
        }
    }

    /// <summary>Esc or the cross (V-14.4). Outside Listening and Processing it does nothing here.</summary>
    public void Cancel()
    {
        switch (state)
        {
            case VoiceSessionState.Listening or VoiceSessionState.Processing:
                CancelSession();
                break;
            case VoiceSessionState.Asking:
                CloseQuestion();
                break;
            default:
                hub.Dequeue(this);
                break;
        }
    }

    /// <summary>An answer of the language question (V-12.11): stored at once, then listening starts with it.</summary>
    public void AnswerLanguage(string answer)
    {
        if (state != VoiceSessionState.Asking) return;
        var current = settings();
        var typing = VoiceLanguage.WithQuestionAnswer(current.Typing, answer);
        hub.CloseQuestion(this);
        storeTyping(typing);
        var updated = current with { Typing = typing };
        StartListening(updated, VoiceLanguage.Resolve(typing.Lang, updated.Orb.LangMode, updated.WindowsUiLanguage));
    }

    /// <summary>Esc, a click outside the card or the orb clicked again (V-12.11).</summary>
    public void DismissQuestion()
    {
        if (state == VoiceSessionState.Asking) CloseQuestion();
    }

    /// <summary>The language default changed in Customization (V-15 last row): an open question closes.</summary>
    public void LanguageDefaultChanged() => DismissQuestion();

    /// <summary>The composer is leaving the screen (V-14.7): stop as a second click would; the text lands and is never sent.</summary>
    public void ComposerLeaving()
    {
        hub.Dequeue(this);
        switch (state)
        {
            case VoiceSessionState.Listening:
                suppressSend = true;
                Stop(VoiceStopReason.ComposerLeft);
                break;
            case VoiceSessionState.Processing or VoiceSessionState.Landing:
                suppressSend = true;
                sendTimer?.Cancel();
                break;
            case VoiceSessionState.Asking:
                CloseQuestion();
                break;
            case VoiceSessionState.Hover:
                SetState(VoiceSessionState.Rest);
                break;
            case VoiceSessionState.Blocked:
                blockedTimer?.Cancel();
                SetState(VoiceSessionState.Rest);
                break;
        }
    }

    /// <summary>The capture's processing-thread tap (V-2.2): the block goes to the level meter, off the UI thread, allocating nothing.</summary>
    public void OnAudioBlock(CaptureFormat captureFormat, ReadOnlySpan<float> interleavedSamples) => meter.Feed(captureFormat, interleavedSamples);

    /// <summary>Capture delivers interleaved float samples in the started format (main executor).</summary>
    public void OnAudio(ReadOnlySpan<float> interleavedSamples)
    {
        if (state != VoiceSessionState.Listening || transcriber is null) return;
        try
        {
            Ingest(interleavedSamples);
        }
        catch (Exception)
        {
            if (state == VoiceSessionState.Listening) Fail(VoiceNoteKind.CaptureFailed); // an unconvertible format fails closed (V-2.5)
        }
    }

    /// <summary>The device changed under the capture (V-4.4).</summary>
    public void OnDeviceEvent(CaptureDeviceEventKind kind)
    {
        if (state != VoiceSessionState.Listening) return;
        LastDeviceEvent = kind;
        if (kind == CaptureDeviceEventKind.InUse) Fail(VoiceNoteKind.CaptureFailed);
        else Stop(VoiceStopReason.DeviceEvent);
    }

    // ---- starting -------------------------------------------------------------------------------------------------------------------

    private void Begin()
    {
        if (StartQueued)
        {
            hub.Dequeue(this); // the same orb clicked again withdraws its wait
            return;
        }
        if (hub.Active is { } running && !ReferenceEquals(running, this))
        {
            hub.Queue(this); // V-7.11: finish the running session first, then start this one
            running.FinishForAnotherOrb();
            return;
        }
        StartNow();
    }

    internal void StartFromQueue() => StartNow();

    private void StartNow()
    {
        if (hub.Asking is { } asking && !ReferenceEquals(asking, this)) asking.DismissQuestion(); // one question at a time
        sendTimer?.Cancel();
        landingTimer?.Cancel();
        var current = settings();
        var resolved = VoiceLanguage.Resolve(current.Typing.Lang, current.Orb.LangMode, current.WindowsUiLanguage);
        if (resolved.Ask)
        {
            // V-15 Asking: no capture, no pop, nothing loaded; the question opens.
            Teardown();
            hub.OpenQuestion(this);
            SetState(VoiceSessionState.Asking);
            return;
        }
        StartListening(current, resolved);
    }

    private void StartListening(VoiceSessionSettings current, LanguageResolution resolved)
    {
        LastDeviceEvent = null;
        Teardown();
        sendTimer?.Cancel();
        landingTimer?.Cancel();
        if (current.Model is not { } model)
        {
            Fail(VoiceNoteKind.ModelLoadFailed);
            return;
        }
        var engine = seams.Engine;
        var cloud = current.Typing.Engine == VoiceEngine.Cloud;
        if (cloud && engine is IProcessingSpeechEngine processing)
        {
            try { processing.ValidateReady(); }
            catch (Exception error) { Fail(VoiceNoteKind.ModelLoadFailed, error); return; }
        }
        switch (seams.Microphone.Status())
        {
            case MicrophoneAccessStatus.Denied:
                EnterBlocked();
                return;
            case MicrophoneAccessStatus.NoDevice:
                Fail(VoiceNoteKind.NoMicrophone);
                return;
        }
        active = current;
        resolution = resolved;
        sessionEngine = engine;
        transcriber = new StreamingTranscriber(engine, resolved.Language, current.LiveWords, processingOnly: cloud);
        shown = [];
        detectedLanguage = null;
        suppressSend = false;
        lastStop = null;
        FailureCause = null;
        lastAddAt = null;
        processingOpsUntil = null;
        label.Reset();
        pending.Clear();
        inSpeech = false;
        quiet = TimeSpan.Zero;
        zeroFrames = 0;
        zeroChecked = false;
        countdownActive = false;
        seams.Vad.Reset();
        meter.Reset(); // the last session's level is not this one's; no capture is feeding it now
        try
        {
            format = seams.Capture.Start(this);
            captureRunning = true;
        }
        catch (Exception error)
        {
            Fail(VoiceNoteKind.CaptureFailed, error);
            return;
        }
        loadWait = hub.EnsureLoaded(engine, model);
        hub.Enter(this);
        listenStart = SwiftClock.Now;
        capTimer = MainActor.AsyncAfter(Cap, () => Stop(VoiceStopReason.Cap));
        countdownTimer = MainActor.AsyncAfter(Cap - CountdownLength, () =>
        {
            countdownActive = true;
            CapCountdownStarted?.Invoke();
        });
        SetState(VoiceSessionState.Listening);
        var gen = generation;
        var wait = loadWait;
        _ = MainActor.Spawn(async () =>
        {
            if (!await wait && gen == generation && state is VoiceSessionState.Listening or VoiceSessionState.Processing) Fail(VoiceNoteKind.ModelLoadFailed, hub.LoadFailure);
        });
    }

    // ---- audio ----------------------------------------------------------------------------------------------------------------------

    private void Ingest(ReadOnlySpan<float> samples)
    {
        var channels = Math.Max(1, format.Channels);
        if (!zeroChecked)
        {
            // V-4.5: the first second of exact zeros is what Windows hands a blocked desktop app.
            foreach (var sample in samples)
            {
                if (sample == 0f) continue;
                zeroChecked = true;
                break;
            }
            zeroFrames += samples.Length / channels;
            if (!zeroChecked && zeroFrames >= (long)(format.SampleRate * SilenceCheckLength.TotalSeconds))
            {
                EnterBlockedFromListening();
                return;
            }
        }
        if (channels == 1) AddMono(samples);
        else
        {
            var mono = new float[samples.Length / channels];
            for (var f = 0; f < mono.Length; f++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++) sum += samples[f * channels + c];
                mono[f] = sum / channels; // V-5.1: the average of the channels
            }
            AddMono(mono);
        }
        var vad = seams.Vad;
        var frameSamples = vad.FrameSamples;
        var frameTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * frameSamples / StreamingTranscriber.SampleRate);
        var taken = 0;
        var now = SwiftClock.Now;
        while (pending.Count - taken >= frameSamples)
        {
            var frame = pending.GetRange(taken, frameSamples).ToArray();
            taken += frameSamples;
            var probability = vad.Probability(frame);
            if (!inSpeech)
            {
                if (probability >= SpeechEnter)
                {
                    inSpeech = true;
                    quiet = TimeSpan.Zero;
                }
            }
            else if (probability < SpeechLeave)
            {
                quiet += frameTime;
                if (quiet >= SpeechLeaveAfter) inSpeech = false; // V-6.2: leaves after 100 ms below 0.35
            }
            else quiet = TimeSpan.Zero;
            transcriber!.Append(frame, inSpeech);
            label.SetSpeech(inSpeech, now);
        }
        pending.RemoveRange(0, taken);
        PumpPasses();
    }

    private void AddMono(ReadOnlySpan<float> mono)
    {
        if (format.SampleRate == StreamingTranscriber.SampleRate) pending.AddRange(mono);
        else seams.Resampler.Process(format.SampleRate, mono, pending);
    }

    private void FlushPending()
    {
        if (pending.Count == 0 || transcriber is null) return;
        transcriber.Append(pending.ToArray(), inSpeech); // V-7.6: the last audio is flushed with the state it had
        pending.Clear();
    }

    // ---- live passes ----------------------------------------------------------------------------------------------------------------

    private void PumpPasses()
    {
        if (state != VoiceSessionState.Listening || active?.LiveWords == false || passTask is not null || transcriber is not { } current) return;
        if (current.DuePass is not { } pass) return;
        var gen = generation;
        var wait = loadWait;
        passTask = MainActor.Spawn(async () =>
        {
            try
            {
                if (!await wait || gen != generation || SwiftTask.IsCancelled) return; // a failed load is the watcher's to report
                var delta = await current.RunPass(pass, SwiftTask.CancellationToken);
                if (gen != generation || state != VoiceSessionState.Listening) return;
                ApplyLive(delta);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (gen == generation && state == VoiceSessionState.Listening) Fail(VoiceNoteKind.EngineFailed, error);
            }
            finally
            {
                if (gen == generation)
                {
                    passTask = null;
                    PumpPasses();
                }
            }
        });
    }

    private void ApplyLive(TranscriptDelta delta)
    {
        shown = delta.Words;
        if (delta.Language is { } language) detectedLanguage = language;
        if (delta.Ops.Count == 0) return;
        var now = SwiftClock.Now;
        var scheduled = TranscriptOps.PaceLive(delta.Ops, lastAddAt is { } at ? now - at : null);
        var adds = scheduled.Where(s => s.Op.Kind == WordOpKind.Add).ToList();
        if (adds.Count > 0)
        {
            lastAddAt = now + adds[^1].At;
            label.WordArrived(now);
        }
        TranscriptChanged?.Invoke(new TranscriptUpdate(TranscriptUpdateKind.Live, scheduled, delta.Words));
    }

    // ---- stopping -------------------------------------------------------------------------------------------------------------------

    internal void FinishForAnotherOrb()
    {
        if (state == VoiceSessionState.Listening) Stop(VoiceStopReason.AnotherOrb);
    }

    private void Stop(VoiceStopReason reason)
    {
        if (state != VoiceSessionState.Listening || transcriber is not { } current) return;
        lastStop = reason;
        capTimer?.Cancel();
        countdownTimer?.Cancel();
        countdownActive = false;
        StopCapture();
        FlushPending();
        if (!current.SawSpeech && shown.Count == 0)
        {
            // V-15: stop with nothing written and no speech seen -> Rest and "No speech was heard"; V-6.4: the engine was never called.
            Teardown();
            SetState(VoiceSessionState.Rest);
            hub.Release(this);
            NoteRaised?.Invoke(VoiceNoteKind.NoSpeech);
            return;
        }
        EnterProcessing(current);
    }

    private void EnterProcessing(StreamingTranscriber current)
    {
        SetState(VoiceSessionState.Processing);
        label.Finish();
        var clickAt = SwiftClock.Now;
        var live = passTask;
        live?.Cancel();
        var gen = generation;
        var wait = loadWait;
        var streaming = active?.Typing.Engine == VoiceEngine.Cloud ? sessionEngine as IProcessingSpeechEngine : null;
        void StreamOps(IReadOnlyList<WordOp> ops)
        {
            // The HTTP reader may be on the pool; every draft operation belongs to the main executor. Generation rejects queued operations after cancel or a new session.
            void Apply()
            {
                if (gen == generation && state == VoiceSessionState.Processing) ApplyProcessingOps(ops);
            }
            if (MainActor.IsIsolated) Apply(); else MainActor.Post(Apply);
        }
        finalTask = MainActor.Spawn(async () =>
        {
            try
            {
                if (live is not null) await live.Completion;
                if (gen != generation || SwiftTask.IsCancelled) return;
                if (!await wait || gen != generation) return;
                if (streaming is not null) streaming.OpsProduced += StreamOps;
                var delta = await current.RunFinal(SwiftTask.CancellationToken);
                if (gen != generation || state != VoiceSessionState.Processing) return;
                FinishProcessing(delta, clickAt);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (gen == generation && state == VoiceSessionState.Processing) Fail(VoiceNoteKind.EngineFailed, error);
            }
            finally
            {
                if (streaming is not null) streaming.OpsProduced -= StreamOps;
            }
        });
    }

    private void ApplyProcessingOps(IReadOnlyList<WordOp> ops)
    {
        var words = shown.ToList();
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case WordOpKind.Add: words.Insert(op.Index, new TranscriptWord(op.Text!, op.Tentative)); break;
                case WordOpKind.Revise: words[op.Index] = words[op.Index] with { Text = op.Text! }; break;
                case WordOpKind.Remove: words.RemoveAt(op.Index); break;
                case WordOpKind.Confirm: words[op.Index] = words[op.Index] with { Tentative = false }; break;
            }
        }
        // A later SSE event may correct a word whose Add is still paced into the future. Keep every dependent operation after earlier operations, including across batches;
        // otherwise DictationWords would ignore the revision before the word exists. Adds still keep V-7.5's 60 ms spacing.
        var now = SwiftClock.Now;
        var at = processingOpsUntil is { } end && end > now ? end - now : TimeSpan.Zero;
        var nextAdd = lastAddAt is { } last && last + TranscriptOps.MinAddSpacing > now ? last + TranscriptOps.MinAddSpacing - now : TimeSpan.Zero;
        var scheduled = new List<ScheduledOp>(ops.Count);
        foreach (var op in ops)
        {
            if (op.Kind == WordOpKind.Add)
            {
                if (nextAdd > at) at = nextAdd;
                lastAddAt = now + at;
                nextAdd = at + TranscriptOps.MinAddSpacing;
                label.WordArrived(now);
            }
            scheduled.Add(new ScheduledOp(at, op));
        }
        processingOpsUntil = now + at;
        shown = words;
        if (ops.Count > 0) TranscriptChanged?.Invoke(new TranscriptUpdate(TranscriptUpdateKind.Live, scheduled, words));
    }

    private void FinishProcessing(TranscriptDelta delta, ContinuousInstant clickAt)
    {
        if (delta.Words.Count == 0)
        {
            // Nothing recognised: nothing to land, as for a session without speech.
            Teardown();
            SetState(VoiceSessionState.Rest);
            hub.Release(this);
            NoteRaised?.Invoke(VoiceNoteKind.NoSpeech);
            return;
        }
        var plan = TranscriptOps.PlanFinal(shown, delta.Words);
        // Finish after the stream's queued word operations have arrived. Final corrections and Landing must not overtake a paced cloud batch.
        if (processingOpsUntil is { } streamEnd && streamEnd > SwiftClock.Now)
        {
            var backlog = streamEnd - SwiftClock.Now;
            plan = new FinalPlan(plan.Ops.Select(op => op with { At = op.At + backlog }).ToList(), plan.ScheduleEnd + backlog);
        }
        shown = delta.Words;
        if (delta.Language is { } language) detectedLanguage = language;
        TranscriptChanged?.Invoke(new TranscriptUpdate(TranscriptUpdateKind.Final, plan.Ops, delta.Words));
        var resultAfterClick = SwiftClock.Now - clickAt;
        var remaining = plan.ProcessingDuration(resultAfterClick) - resultAfterClick;
        processingTimer = MainActor.AsyncAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, EnterLanding);
    }

    private void EnterLanding()
    {
        if (state != VoiceSessionState.Processing) return;
        var solid = shown.Select(w => w with { Tentative = false }).ToList();
        var ink = TranscriptOps.PlanLanding(shown);
        shown = solid;
        SetState(VoiceSessionState.Landing);
        TranscriptChanged?.Invoke(new TranscriptUpdate(TranscriptUpdateKind.Landing, ink, solid));
        var text = string.Join(' ', solid.Select(w => w.Text));
        if (!suppressSend && text.Length > 0 && settings().Orb.AfterVoice == VoiceAfterVoice.Send)
            sendTimer = MainActor.AsyncAfter(SendDelay, () => SendRequested?.Invoke(new SendIntent(text, SendIntentKind.AsReturn)));
        landingTimer = MainActor.AsyncAfter(LandingLength, () =>
        {
            if (state != VoiceSessionState.Landing) return;
            Teardown();
            SetState(VoiceSessionState.Rest);
        });
        hub.Release(this); // a queued orb starts now (V-7.11)
    }

    private void CancelSession()
    {
        var count = shown.Count;
        Teardown();
        StopCapture();
        if (count > 0) TranscriptChanged?.Invoke(new TranscriptUpdate(TranscriptUpdateKind.Withdraw, TranscriptOps.PlanWithdraw(count), []));
        shown = [];
        SetState(VoiceSessionState.Rest);
        hub.Release(this);
        if (count == 0) NoteRaised?.Invoke(VoiceNoteKind.Cancelled);
    }

    // ---- blocked, errors, teardown --------------------------------------------------------------------------------------------------

    private void EnterBlockedFromListening()
    {
        capTimer?.Cancel();
        countdownTimer?.Cancel();
        Teardown();
        StopCapture();
        EnterBlocked();
        hub.Release(this);
    }

    private void EnterBlocked()
    {
        SetState(VoiceSessionState.Blocked);
        NoteRaised?.Invoke(VoiceNoteKind.Blocked);
        blockedTimer = MainActor.AsyncAfter(BlockedLength, () =>
        {
            if (state == VoiceSessionState.Blocked) SetState(VoiceSessionState.Rest);
        });
    }

    private void Fail(VoiceNoteKind note, Exception? cause = null)
    {
        FailureCause = cause;
        Teardown();
        StopCapture();
        shown = [];
        if (state != VoiceSessionState.Rest) SetState(VoiceSessionState.Rest);
        hub.Release(this);
        NoteRaised?.Invoke(note);
    }

    private void CloseQuestion()
    {
        hub.CloseQuestion(this);
        SetState(VoiceSessionState.Rest);
    }

    private void StopCapture()
    {
        if (!captureRunning) return;
        captureRunning = false;
        try
        {
            seams.Capture.Stop();
        }
        catch (Exception)
        {
            // The device is gone already; nothing more to close.
        }
    }

    // Ends everything a session started: tasks and timers cancelled, stale callbacks ignored, the audio zeroed (V-4.7).
    private void Teardown()
    {
        generation += 1;
        capTimer?.Cancel();
        countdownTimer?.Cancel();
        processingTimer?.Cancel();
        passTask?.Cancel();
        finalTask?.Cancel();
        passTask = null;
        finalTask = null;
        countdownActive = false;
        transcriber?.Clear();
        transcriber = null;
        sessionEngine = null;
        pending.Clear();
    }

    private void SetState(VoiceSessionState next)
    {
        if (next == state) return;
        var previous = state;
        state = next;
        StateChanged?.Invoke(previous, next);
    }
}
