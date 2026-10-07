// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-8 A and Q-V-11 C; first-release voice typing, VT-10, voice-SPEC V-14): one composer's voice input, between its
// VoiceSession (VT-3) and the composer's views.
using System.Globalization;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Orb;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Services.Port.Voice.Cloud;

namespace Marrow.App.Port.Voice;

/// <summary>
/// What the composer's voice views read and call. It owns the composer's <see cref="VoiceSession"/> (made by <see cref="VoiceSessionFactory"/>, so a test binds scripted
/// seams), turns the session's events into what the composer shows (the orb's mode, the notes, the announcements) and does what they ask of the draft: the text lands in
/// it when the session lands (VT-10; VT-11 writes it live) and, with "Send it as the prompt", is sent through the composer's own Return 0.35 s later (V-8.8, Q-V-11 C).
/// Main-actor only.
/// </summary>
public sealed class ComposerVoiceController : MainActorModel
{
    private readonly ComposerVoiceHost? host;
    private readonly Func<VoiceOrbSettings> orbSettings;
    private readonly Func<DraftSelection> selection;
    private readonly Action<int, int, string> replace;
    private VoiceSessionState state = VoiceSessionState.Rest;
    private VoiceNoteContent? note;
    private string announcement = "";
    private bool pointerOver;
    private DraftSelection? original;
    private WorkItem? noteTimer;
    private int noteSerial;
    private readonly LevelFollowers followers = new();
    private OrbBands orbBands;
    private bool dictationOpen;
    private bool setupOpen;
    private bool preparing;
    private VoicePrepItem? preparationProgress;
    private int preparationSerial;
    private VoiceListeningStyle liveStyle;

    public ComposerVoiceController(ComposerVoiceHost host)
    {
        this.host = host;
        orbSettings = () => host.Settings().VoiceOrb;
        selection = host.Selection;
        replace = host.Replace;
        Session = VoiceSessionFactory.Current.Current.Create(SessionSettings, StoreTyping);
        Bind(host.Send);
    }

    /// <summary>CZ-7b: the same controller and views over an explicit scripted session and page-local text. No privacy or settings writes.</summary>
    internal ComposerVoiceController(VoiceSession session, Func<VoiceOrbSettings> orbSettings, Func<DraftSelection> selection, Action<int, int, string> replace, Action send)
    {
        this.orbSettings = orbSettings;
        this.selection = selection;
        this.replace = replace;
        Session = session;
        Bind(send);
    }

    private void Bind(Action send)
    {
        Session.StateChanged += OnState;
        Session.TranscriptChanged += OnTranscript;
        Session.NoteRaised += OnNote;
        Session.SendRequested += _ => send();
    }

    /// <summary>The composer's session (V-15's table).</summary>
    public VoiceSession Session { get; }

    internal VoiceFirstRun? FirstRun => host is null ? null : VoiceFirstRun.Find(host.Settings());
    internal bool SetupOpen { get => Read(setupOpen); set => Set(ref setupOpen, value); }
    internal bool Preparing { get => Read(preparing); private set => Set(ref preparing, value); }
    internal VoicePrepItem? PreparationProgress { get => Read(preparationProgress); private set => SetAlways(ref preparationProgress, value); }

    /// <summary>Listening has begun (VT-11): the draft and selection as they are, where the dictated words will go (V-8.1).</summary>
    public event Action<DraftSelection>? DictationBegan;

    /// <summary>The session's operations on the dictated words, as it reports them (V-7.5): live, final, landing's ink, the withdraw of a cancel.</summary>
    public event Action<TranscriptUpdate>? Transcript;

    /// <summary>The session ended with no landing and no withdraw (an error, or nothing heard): whatever the dictation had shown goes.</summary>
    public event Action? DictationAbandoned;

    /// <summary>The session's state, tracked.</summary>
    public VoiceSessionState State
    {
        get => Read(state);
        private set => Set(ref state, value);
    }

    /// <summary>
    /// How this session shows that it listens: the listening style (Pill, Cross or Float) as it was when listening began. A change of the setting takes effect with the next session (V-15), so the pill,
    /// the cross and the orb's lift read this and not the setting while the session is live.
    /// </summary>
    public VoiceListeningStyle LiveStyle
    {
        get => Read(liveStyle);
        private set => Set(ref liveStyle, value);
    }

    /// <summary>True while Listening or Processing: Esc cancels, the editor is read-only, the capture is open (V-14.4, V-8.5).</summary>
    public bool IsLive => State is VoiceSessionState.Listening or VoiceSessionState.Processing;

    /// <summary>The note showing now, null between notes.</summary>
    public VoiceNoteContent? Note
    {
        get => Read(note);
        private set => Set(ref note, value);
    }

    /// <summary>The last announcement, for the polite live region (V-14.5).</summary>
    public string Announcement
    {
        get => Read(announcement);
        private set => Set(ref announcement, value);
    }

    internal void Announce(string words) => Say(words);

    /// <summary>Whether the pointer is over the orb (the motion counts it at Rest and Landing only, V-11.4).</summary>
    public bool PointerOver
    {
        get => Read(pointerOver);
        private set => Set(ref pointerOver, value);
    }

    /// <summary><c>voiceOrb</c>, tracked: the orb, its size, the attachment's place, what follows voice input.</summary>
    public VoiceOrbSettings OrbSettings => orbSettings();

    /// <summary>
    /// The smoothed bands the orb takes (V-5.5): the session's published level, followed per band with attack 0.07 s and release 0.24 s, read once per frame by <see cref="StepLevel"/>.
    /// Not tracked: the orb's slot reads it on the frame that stepped it.
    /// </summary>
    public OrbBands OrbBands => orbBands;

    /// <summary>
    /// One frame of the level: <paramref name="dt"/> seconds since the last. The bands follow the session's level while Listening and its zero after, so they fall on the release when
    /// listening ends (the design's bands follow the audio all the time, voice-composer.js:718-722). Returns whether another frame is needed: Listening, or bands still above zero.
    /// </summary>
    public bool StepLevel(double dt)
    {
        followers.Step(State == VoiceSessionState.Listening ? Session.AudioLevel : LevelBands.Zero, dt);
        var bands = followers.Bands;
        orbBands = new OrbBands(bands.Low, bands.Mid, bands.High, bands.All);
        return State == VoiceSessionState.Listening || bands != LevelBands.Zero;
    }

    /// <summary>The bands forget the voice (the orb left the screen; no frame is coming to let them fall).</summary>
    public void ResetLevel()
    {
        followers.Reset();
        orbBands = default;
    }

    /// <summary>The orb's mode in the motion's terms (V-11.4's table).</summary>
    public OrbMotionMode Mode => State switch
    {
        VoiceSessionState.Listening => OrbMotionMode.Listening,
        VoiceSessionState.Processing => OrbMotionMode.Processing,
        VoiceSessionState.Landing => OrbMotionMode.Landing,
        VoiceSessionState.Blocked => OrbMotionMode.Blocked,
        _ => OrbMotionMode.Rest,
    };

    /// <summary>The accessible name of the orb for the state it is in (V-14.2).</summary>
    public string OrbName => State switch
    {
        VoiceSessionState.Listening => "Stop and finish the text",
        VoiceSessionState.Processing => "Finishing the text",
        _ => "Start voice input (" + (OrbSettings.Orb == VoiceOrbKind.Membrane ? "Voice Membrane" : "Siri Wave") + ")",
    };

    public void PointerEnter()
    {
        PointerOver = true;
        if (FirstRun is not { } flow || flow.CanListen) Session.PointerEnter();
    }

    public void PointerLeave()
    {
        PointerOver = false;
        Session.PointerLeave();
    }

    /// <summary>The orb clicked, or Enter or Space on it.</summary>
    public void Activate()
    {
        if (IsLive || State is VoiceSessionState.Asking or VoiceSessionState.Blocked || host is null || host.Settings().VoiceTyping.Engine != VoiceEngine.Local || FirstRun is not { } flow)
        {
            Session.Activate();
            return;
        }
        if (Preparing) return;
        if (flow.CanListen) { SetupOpen = false; Session.Activate(); return; }
        if (flow.Question.State == VoiceQuestionState.Slow) { SetupOpen = true; return; }
        SetupOpen = false;
        PreparationProgress = null;
        Preparing = true;
        var serial = ++preparationSerial;
        void ProgressChanged() => MainActor.Post(() => { if (serial == preparationSerial) PreparationProgress = flow.Progress; });
        flow.Changed += ProgressChanged;
        _ = MainActor.Spawn(async () =>
        {
            try { _ = await flow.OrbAsync(CancellationToken.None); }
            catch (OperationCanceledException) { }
            finally
            {
                flow.Changed -= ProgressChanged;
                if (serial == preparationSerial)
                {
                    Preparing = false;
                    SetupOpen = flow.Question.State == VoiceQuestionState.Slow;
                }
            }
        });
    }

    /// <summary>An answer of the language question (V-12.11): stored at once, then listening begins with it.</summary>
    public void AnswerLanguage(string code) => Session.AnswerLanguage(code);

    /// <summary>Esc or a click outside the question (V-12.11): it closes with nothing stored and nothing started.</summary>
    public void DismissQuestion() => Session.DismissQuestion();

    /// <summary>Esc or the cross (V-14.4): cancels a live session, says so, and does nothing otherwise.</summary>
    public bool Cancel()
    {
        if (Preparing)
        {
            ++preparationSerial;
            Preparing = false;
            SetupOpen = false;
            FirstRun?.Cancel();
            Say(VoiceAnnouncements.Cancelled);
            return true;
        }
        if (!IsLive) return false;
        Session.Cancel();
        if (Session.State == VoiceSessionState.Rest) Say(VoiceAnnouncements.Cancelled);
        return true;
    }

    /// <summary>The composer leaves the screen (V-14.7): the session stops as a second click would, the text lands and is never sent.</summary>
    public void ComposerLeaving()
    {
        if (Preparing) Cancel();
        Session.ComposerLeaving();
    }

    // The settings a click begins a session with (V-15). The model is the chosen catalogue id; its file is the model store's to find (VT-6, VT-7).
    private VoiceSessionSettings SessionSettings()
    {
        var settings = host!.Settings();
        var typing = settings.VoiceTyping;
        return new VoiceSessionSettings(settings.VoiceOrb, typing, CultureInfo.CurrentUICulture.Name,
            typing.Engine == VoiceEngine.Cloud ? new SpeechModel(OpenAiTranscriber.DefaultModel, null) : FirstRun is { } flow ? flow.Model(typing.Model) : typing.Model is { } id ? new SpeechModel(id, null) : null)
            { LiveWords = VoicePreparation.LiveWordsOf(settings.VoiceHardware) };
    }

    private void StoreTyping(VoiceTypingSettings typing) => host!.Settings().VoiceTyping = typing;

    private void OnState(VoiceSessionState from, VoiceSessionState to)
    {
        if (to == VoiceSessionState.Listening) LiveStyle = OrbSettings.ListeningStyle;
        State = to;
        switch (to)
        {
            case VoiceSessionState.Asking:
                Say(VoiceAnnouncements.LanguageQuestion); // V-12.11
                break;
            case VoiceSessionState.Listening:
                var began = selection();
                original = began;
                dictationOpen = true;
                DictationBegan?.Invoke(began);
                Say(VoiceAnnouncements.Listening);
                if (host is not null) ShowPrivacyNoteOnFirstUse();
                break;
            case VoiceSessionState.Processing when from == VoiceSessionState.Listening:
                Say(VoiceAnnouncements.Finishing);
                break;
            case VoiceSessionState.Landing:
                Land();
                break;
            case VoiceSessionState.Rest when dictationOpen:
                dictationOpen = false;
                DictationAbandoned?.Invoke();
                break;
        }
    }

    private void OnTranscript(TranscriptUpdate update)
    {
        if (update.Kind is TranscriptUpdateKind.Landing or TranscriptUpdateKind.Withdraw) dictationOpen = false; // the layer takes it from here
        Transcript?.Invoke(update);
    }

    private void OnNote(VoiceNoteKind kind)
    {
        if (kind == VoiceNoteKind.Blocked) Say(VoiceAnnouncements.Blocked);
        if (VoiceNotes.For(kind) is { } content) ShowNote(content);
    }

    // Q-V-8 A: the first time each engine is used, its privacy line, once per engine.
    private void ShowPrivacyNoteOnFirstUse()
    {
        var settings = host!.Settings();
        var engine = settings.VoiceTyping.Engine;
        var shown = settings.VoiceNotes;
        if (engine == VoiceEngine.Cloud ? shown.Cloud : shown.Local) return;
        settings.VoiceNotes = engine == VoiceEngine.Cloud ? shown with { Cloud = true } : shown with { Local = true };
        ShowNote(VoiceNotes.Privacy(engine));
    }

    private void ShowNote(VoiceNoteContent content)
    {
        noteTimer?.Cancel();
        var serial = ++noteSerial;
        Note = content;
        noteTimer = MainActor.AsyncAfter(content.Length, () =>
        {
            if (serial == noteSerial) Note = null;
        });
    }

    // Landing: the words go into the draft where the caret or selection was when the session began (V-8.1), in one edit, and the announcement says what happens next.
    private void Land()
    {
        var words = Session.Words;
        if (words.Count == 0) return;
        var at = original ?? selection();
        var now = selection();
        if (now.Text != at.Text) at = now; // the draft was written elsewhere meanwhile: the words go where its caret is now
        var result = new DictationInsertion(at).Apply(words);
        var start = Math.Clamp(at.Start, 0, at.Text.Length);
        var end = (int)Math.Clamp((long)at.Start + at.Length, start, at.Text.Length);
        replace(start, end - start, result.Text.Substring(start, result.Start - start));
        Say(VoiceAnnouncements.Done(string.Join(' ', words.Select(word => word.Text)), OrbSettings.AfterVoice == VoiceAfterVoice.Send && Session.LastStopReason != VoiceStopReason.ComposerLeft));
    }

    private void Say(string text) => Announcement = text;
}
