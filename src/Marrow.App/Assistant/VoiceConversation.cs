// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "The voice orb": hold, the global toggle, the voice sheet, barge-in, what voice may answer, spoken status,
// Asturs by name; 12.6 "Voice: Stop everything"; 15.2 VoiceConversation.cs; 16 unit AS-18; ED-NEW-dextra-as18-8, -9, -10). No Swift twin.
// One spoken conversation with Dextra, on this PC only. The microphone is opened in communications mode (Windows' echo cancellation runs), what the
// owner says is cut into requests by the voice detector (1.2 s of silence ends one), a local speech engine turns each into words, and the words are
// either one of the few things the voice layer answers itself (stop, pause or resume everything, a status question, a short answer to a card Dextra
// has just read out) or are sent to Dextra, or to an Astur by name, like typed words. The answer is said by a voice installed on this PC and shown
// as text with the spoken word lit. While Dextra talks the microphone is still listening: the owner speaking over it stops the speech within 200 ms
// (the barge-in gate tells his voice from Dextra's own coming back through the speakers) and the conversation listens to him.
// Nothing is spoken unprompted: speech needs a turn that began with something the owner said, and nothing here subscribes to the feed, a toast or
// a decision. Voice never answers a Send, Pay, Delete, Install or Commitment card (VoiceCardRules), whatever is said.
//
// Threads: the capture's processing thread runs OnAudioBlock (resample, voice detector, endpointing, barge-in; it never blocks); the speech engine
// and Dextra's answer run on the background runner; every change the sheet shows is posted to the UI thread. State shared between them is under
// <see cref="gate"/>; a turn number makes a result that arrives after a barge-in, a stop or a close harmless.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Assistant;
using Marrow.App.Assistant.Views;

namespace Marrow.App.Assistant;

/// <summary>Where the conversation is. Closed: the sheet is not open and the microphone is shut. Idle: the sheet is open and the microphone is shut.</summary>
public enum VoicePhase { Closed, Idle, Listening, Thinking, Speaking }

/// <summary>How listening was started: Toggle (the global chord, the header's Talk: ends on 1.2 s of silence or a second press) or Hold (the talk button held: ends when it is let go).</summary>
public enum VoiceStart { Toggle, Hold }

public sealed class VoiceConversationOptions
{
    /// <summary>The microphone, opened in communications mode in production.</summary>
    public required IAudioCapture Capture { get; init; }

    public required Func<IVoiceActivityDetector> Vad { get; init; }

    public required Func<IStreamingResampler> Resampler { get; init; }

    /// <summary>The local speech engine. Never the cloud one (owner, 2026-10-11: voice runs on the PC only).</summary>
    public required Func<ISpeechEngine> Engine { get; init; }

    /// <summary>The speech model the owner chose for voice typing; null means none is set up and the conversation says so.</summary>
    public required Func<SpeechModel?> Model { get; init; }

    public required IMicrophoneAccess Microphone { get; init; }

    public required SpeechOut Speech { get; init; }

    public required IVoiceBrain Brain { get; init; }

    public required IMissionJobs Jobs { get; init; }

    public required IDecisionSource Decisions { get; init; }

    public required IGlobalControls Controls { get; init; }

    /// <summary>The language of the request: a Whisper code or "auto".</summary>
    public Func<string> Language { get; init; } = () => "auto";

    /// <summary>Names the engine should expect (Dextra, the Asturs), sent as the prompt of each decode.</summary>
    public Func<string> Vocabulary { get; init; } = () => "Dextra.";

    /// <summary>Today's digest as plain text, for "Read my digest"; null or empty says there is none yet.</summary>
    public Func<string?>? Digest { get; init; }

    /// <summary>Opens a card on the screen (the Board's Decisions tab): what voice does to a card it may not answer.</summary>
    public Action<Guid>? ShowCard { get; init; }

    /// <summary>Takes the stop key's lease for a spoken reply (12.6); null in the result means the key is unavailable. Null: no lease is taken.</summary>
    public Func<IDisposable?>? StopLease { get; init; }

    /// <summary>Ends a dictation that is listening, so the microphone is the conversation's.</summary>
    public Action? PreemptDictation { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Runs an action on the UI thread. Tests run it at once.</summary>
    public Action<Action> Post { get; init; } = action => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    /// <summary>Runs the speech engine and Dextra's answer off the audio thread. Tests run them at once.</summary>
    public Func<Func<Task>, Task> Background { get; init; } = work => Task.Run(work);

    public VoiceBargeInOptions BargeIn { get; init; } = new();

    public VoiceEndpointerOptions Endpointer { get; init; } = new();

    /// <summary>After a reply in toggle mode the conversation listens this long for a follow-up before it shuts the microphone.</summary>
    public TimeSpan FollowUp { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>A live transcript pass runs every this many 32 ms frames while the owner speaks (about 0.7 s).</summary>
    public int LiveEveryFrames { get; init; } = 22;

    /// <summary>After a reply ends, nothing the microphone hears counts as speech for this long: the room still rings with Dextra's own voice.</summary>
    public TimeSpan HoldOff { get; init; } = TimeSpan.FromMilliseconds(500);
}

public sealed partial class VoiceConversation : IAudioCaptureSink, IStopScope, IDisposable
{
    /// <summary>Audio kept from before speech was heard (and before a barge-in), so the first syllable is not lost: 8 frames, 256 ms.</summary>
    public const int PrerollFrames = 8;

    private readonly VoiceConversationOptions o;
    private readonly object gate = new();
    private VoicePhase phase = VoicePhase.Closed;
    private VoiceStart mode;
    private string heard = "";
    private string reply = "";
    private string? note;
    private string? speaker;
    private DecisionItem? focus;
    private int turn;
    private CancellationTokenSource? turnCancel;
    private bool selfStop;
    private bool changedPosted;
    private bool disposed;

    public VoiceConversation(VoiceConversationOptions options)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
        o.Speech.Changed += Notify;
    }

    /// <summary>Raised on the UI thread when anything the sheet shows changed (the phase, the words, the note).</summary>
    public event Action? Changed;

    public VoicePhase Phase
    {
        get
        {
            lock (gate) return phase;
        }
    }

    /// <summary>The sheet is open.</summary>
    public bool IsOpen => Phase != VoicePhase.Closed;

    public VoiceStart Mode
    {
        get
        {
            lock (gate) return mode;
        }
    }

    /// <summary>What the owner said: the live words while he speaks, then the final ones.</summary>
    public string Heard
    {
        get
        {
            lock (gate) return heard;
        }
    }

    /// <summary>What Dextra says (and shows), or "".</summary>
    public string Reply
    {
        get
        {
            lock (gate) return reply;
        }
    }

    /// <summary>The word being spoken now, as an index into <see cref="SpokenText.Words"/> of <see cref="Reply"/>; -1 when nothing is spoken.</summary>
    public int SpokenWord => Phase == VoicePhase.Speaking ? o.Speech.WordIndex : -1;

    /// <summary>One line under the words: what to do, or why voice cannot go on ("No speech model is set up yet ...").</summary>
    public string? Note
    {
        get
        {
            lock (gate) return note;
        }
    }

    /// <summary>The Astur who is answering, or null for Dextra.</summary>
    public string? Speaker
    {
        get
        {
            lock (gate) return speaker;
        }
    }

    /// <summary>The card Dextra has just read out (what a "yes" or "the second one" would answer), or null.</summary>
    public DecisionItem? Focus
    {
        get
        {
            lock (gate) return focus;
        }
    }

    /// <summary>The voice level 0 to 1 the sheet's Armilla follows: the microphone while listening, Dextra's own speech while it speaks, else 0.</summary>
    public double Level => Phase switch
    {
        VoicePhase.Listening => Math.Clamp(Volatile.Read(ref micLevel), 0, 1),
        VoicePhase.Speaking => Math.Clamp(o.Speech.Level, 0, 1),
        _ => 0,
    };

    /// <summary>The word for the state label: Listening, Thinking, Speaking or Ready.</summary>
    public string StateLabel => Phase switch
    {
        VoicePhase.Listening => "Listening",
        VoicePhase.Thinking => "Thinking",
        VoicePhase.Speaking => "Speaking",
        _ => "Ready",
    };

    /// <summary>IStopScope: Stop all and the stop chord cut the speech and end the turn, flags only (12.6).</summary>
    string IStopScope.Name => "Voice";

    void IStopScope.Stop() => Stop();

    // ---- the owner's moves ----

    /// <summary>Opens the sheet and listens: <paramref name="how"/> says whether it ends on silence (Toggle) or on letting go (Hold).</summary>
    public void Start(VoiceStart how)
    {
        lock (gate)
        {
            if (disposed) return;
            if (phase is VoicePhase.Listening or VoicePhase.Thinking or VoicePhase.Speaking) return;
            mode = how;
            holding = how == VoiceStart.Hold;
            phase = VoicePhase.Idle; // the sheet is open from here; a failure to listen leaves it open with its reason
            note = null;
            heard = "";
            reply = "";
            speaker = null;
            focus = null;
        }
        Listen(followUp: false);
    }

    /// <summary>The chord or the header's Talk: starts listening, ends the request now, or cuts Dextra and listens (2.4).</summary>
    public void Toggle()
    {
        switch (Phase)
        {
            case VoicePhase.Closed:
            case VoicePhase.Idle:
                Start(VoiceStart.Toggle);
                break;
            case VoicePhase.Listening:
                if (!EndRequestNow()) Rest(null);
                break;
            case VoicePhase.Speaking:
            case VoicePhase.Thinking:
                Interrupt();
                break;
        }
    }

    /// <summary>The talk button let go (a hold): the request ends now.</summary>
    public void Release()
    {
        if (Phase != VoicePhase.Listening || !holding) return;
        if (!EndRequestNow()) Rest("Hold the button and speak.");
    }

    /// <summary>A press shorter than this is a tap, not a hold.</summary>
    public static readonly TimeSpan TapTime = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// The talk button pressed (2.4: hold it to talk, or tap it). When nothing is going on it starts a hold and returns true (the release says what to
    /// do); otherwise it acts as the chord does (end the request, or cut Dextra and listen) and returns false.
    /// </summary>
    public bool PressTalk()
    {
        if (Phase is VoicePhase.Closed or VoicePhase.Idle)
        {
            Start(VoiceStart.Hold);
            return Phase == VoicePhase.Listening && holding;
        }
        Toggle();
        return false;
    }

    /// <summary>
    /// The talk button let go after <paramref name="held"/>. A tap leaves the conversation listening and ends the request on silence, like the chord;
    /// a hold ends the request now.
    /// </summary>
    public void ReleaseTalk(TimeSpan held)
    {
        if (Phase != VoicePhase.Listening || !holding) return;
        if (held >= TapTime)
        {
            Release();
            return;
        }
        lock (gate) mode = VoiceStart.Toggle;
        holding = false;
        lock (audioGate)
        {
            if (!endpointer.InSpeech) endpointer = new VoiceEndpointer(o.Endpointer);
        }
    }

    /// <summary>The sheet's Interrupt: cut what Dextra says (or the answer being made) and listen.</summary>
    public void Interrupt()
    {
        var phaseNow = Phase;
        if (phaseNow is not (VoicePhase.Speaking or VoicePhase.Thinking)) return;
        o.Speech.Stop();
        CancelTurn();
        Listen(followUp: false, keepWords: true);
    }

    /// <summary>The sheet's Done or Escape: everything stops, the microphone shuts, the sheet closes.</summary>
    public void Close()
    {
        o.Speech.Stop();
        CancelTurn();
        ShutCapture();
        lock (gate)
        {
            phase = VoicePhase.Closed;
            heard = "";
            reply = "";
            note = null;
            speaker = null;
            focus = null;
        }
        Notify();
    }

    /// <summary>
    /// Stop all or the stop chord (12.6): flags only. The speech is cut now and the turn is cancelled; the microphone shuts on the UI thread. A stop the
    /// owner asked for by voice is its own: it cuts the speech and leaves the turn to say what it did.
    /// </summary>
    public void Stop()
    {
        o.Speech.Stop();
        if (selfStop) return;
        CancelTurn();
        o.Post(() =>
        {
            if (Phase is VoicePhase.Closed or VoicePhase.Idle) return;
            ShutCapture();
            lock (gate)
            {
                phase = VoicePhase.Idle;
                note = "Stopped.";
            }
            Notify();
        });
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        o.Speech.Changed -= Notify;
        o.Speech.Stop();
        CancelTurn();
        ShutCapture();
    }

    // ---- state ----

    private void SetPhase(VoicePhase next, string? line = null)
    {
        lock (gate)
        {
            phase = next;
            if (line is not null) note = line;
        }
        Notify();
    }

    private void Rest(string? line)
    {
        ShutCapture();
        lock (gate)
        {
            if (phase == VoicePhase.Closed) return;
            phase = VoicePhase.Idle;
            note = line;
        }
        Notify();
    }

    private void Notify()
    {
        lock (gate)
        {
            if (changedPosted) return;
            changedPosted = true;
        }
        o.Post(() =>
        {
            lock (gate) changedPosted = false;
            Changed?.Invoke();
        });
    }

    private bool Stale(int id)
    {
        lock (gate) return disposed || id != turn;
    }

    // A new owner utterance, a barge-in, a stop or a close: the answers of the old turn are no longer wanted.
    private void CancelTurn()
    {
        CancellationTokenSource? old;
        lock (gate)
        {
            turn++;
            old = turnCancel;
            turnCancel = null;
        }
        old?.Cancel();
        old?.Dispose();
    }

    private (int Id, CancellationToken Token) NewTurn()
    {
        CancellationTokenSource made = new();
        CancellationTokenSource? old;
        int id;
        lock (gate)
        {
            id = ++turn;
            old = turnCancel;
            turnCancel = made;
        }
        old?.Cancel();
        old?.Dispose();
        return (id, made.Token);
    }

    private double micLevel;
    private string? lastFault;

    /// <summary>The last thing that went wrong without stopping the conversation (a live pass, a microphone that would not close); null before one.</summary>
    public string? LastFault
    {
        get
        {
            lock (gate) return lastFault;
        }
    }

    private void Fault(string what, Exception error)
    {
        lock (gate) lastFault = what + ": " + Brief(error);
    }
}
