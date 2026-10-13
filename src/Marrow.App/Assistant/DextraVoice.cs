// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4, 12.6 "It registers the chord whenever any of these runs: ... a voice reply", 16.2 "AS-18: Voice works in the nth
// window (the voice sheet); the global chord brings nth to the front; decision cards show in the window and the toast"; unit AS-18; ED-NEW-dextra-as18-9, -10).
// No Swift twin.
// Dextra's voice as the app wires it, one set for each running controller: the talk chord, the voice conversation over the local speech engine and the
// microphone in communications mode, the voice that speaks (the PC's own, nothing from a network), and the stop scope that lets Stop all cut a reply. The
// production parts are the default; a test gives its own. The Tactus pill is not in this tree: the conversation's state is plain properties and a Changed
// event, which the pill reads when it lands (the lead connects it at the merge), and until then the chord brings nth to the front and opens the sheet.
using System.Globalization;
using Marrow.App.Assistant.Views;
using Marrow.App.Port.Voice;
using Marrow.App.Safety;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Voice.Capture;

namespace Marrow.App.Assistant;

/// <summary>An <see cref="IStopScope"/> that is handed to the controller before the conversation exists, and bound to it afterwards (Stop all cuts a reply).</summary>
public sealed class VoiceStopScope : IStopScope
{
    private volatile VoiceConversation? target;

    public string Name => "Voice";

    public void Bind(VoiceConversation? conversation) => target = conversation;

    public void Stop() => target?.Stop();
}

/// <summary>The parts a voice is made of. Production fills every one; a test gives its own.</summary>
public sealed class DextraVoiceParts
{
    /// <summary>The thread the talk chord lives on.</summary>
    public Func<IStopChordHost> ChordHost { get; init; } = static () => new StopChordThread();

    /// <summary>The microphone, opened in communications mode so Windows' echo cancellation runs (2.4).</summary>
    public Func<IAudioCapture> Capture { get; init; } = static () => new WasapiCapture(CaptureSeams.Host.Current, () => VoiceComposition.StoredTyping().Mic, communications: true);

    public Func<IVoiceActivityDetector> Vad { get; init; } = static () => VoiceSeams.Vad.Current;

    public Func<IStreamingResampler> Resampler { get; init; } = static () => new PolyphaseResampler();

    /// <summary>The local speech engine; never the cloud one.</summary>
    public Func<ISpeechEngine> Engine { get; init; } = static () => VoiceSeams.Engine.Current;

    public Func<IMicrophoneAccess> Microphone { get; init; } = static () => VoiceSeams.Microphone.Current;

    /// <summary>The voices of this PC.</summary>
    public Func<ISpeechSynth> Synth { get; init; } = static () => new WinRtSpeechSynth();

    /// <summary>The speakers, in the communications category.</summary>
    public Func<ISpeechPlayer> Player { get; init; } = static () => new WasapiSpeechPlayer();

    /// <summary>The speech model voice typing uses; null when none is set up. Cloud transcription is never a model here.</summary>
    public Func<AppModel, SpeechModel?> Model { get; init; } = static app =>
    {
        var typing = VoiceComposition.StoredTyping();
        return VoiceFirstRun.Find(app.Settings) is { } flow ? flow.Model(typing.Model) : typing.Model is { } id ? new SpeechModel(id, null) : null;
    };

    public Func<VoiceTypingSettings> Typing { get; init; } = static () => VoiceComposition.StoredTyping();

    /// <summary>Ends a dictation that listens, so the microphone is the conversation's.</summary>
    public Action PreemptDictation { get; init; } = static () => VoiceSessionHub.Shared.Current.Active?.Cancel();

    /// <summary>Brings nth's window to the front and shows the Assistant home (what the global chord does first).</summary>
    public Action<AppModel> BringForward { get; init; } = static app =>
    {
        Marrow.App.Window.WindowManager.Shared.ShowMain();
        app.ShowAssistantHome();
    };

    /// <summary>The chords the talk chord must differ from, besides the stop chord: Tactus's pause chord.</summary>
    public IReadOnlyList<PcHotkey> OtherChords { get; init; } = [PcHotkey.PauseDefault];

    /// <summary>Runs an action on the UI thread.</summary>
    public Action<Action> Post { get; init; } = static action => Avalonia.Threading.Dispatcher.UIThread.Post(action);
}

public sealed class DextraVoice : IDisposable
{
    private readonly AppModel model;
    private readonly DextraHost host;
    private readonly VoiceStopScope scope;
    private readonly DextraVoiceParts parts;
    private bool disposed;

    private DextraVoice(AppModel model, DextraHost host, VoiceStopScope scope, DextraVoiceParts parts, SpeechOut speech)
    {
        this.model = model;
        this.host = host;
        this.scope = scope;
        this.parts = parts;
        Speech = speech;
        var live = host.Live;
        var controller = host.Controller;
        Conversation = new VoiceConversation(new VoiceConversationOptions
        {
            Capture = parts.Capture(),
            Vad = parts.Vad,
            Resampler = parts.Resampler,
            Engine = parts.Engine,
            Model = () => parts.Model(model),
            Microphone = parts.Microphone(),
            Speech = speech,
            Brain = new HomeChatBrain(model, host),
            Jobs = live.Jobs,
            Decisions = live.Decisions,
            Controls = live.Controls,
            Language = () => parts.Typing().Lang is { } lang && VoiceLanguage.Find(lang) is not null ? lang : "auto",
            Vocabulary = () => string.Join(", ", host.Profiles().Select(p => p.Name)) + ".",
            ShowCard = id => CardRequested?.Invoke(id),
            StopLease = () => model.StopKey is { } key ? (key.TryAcquire("voice reply", out var held) ? held : null) : NoLease.Instance,
            PreemptDictation = parts.PreemptDictation,
            Post = action => parts.Post(action),
        });
        scope.Bind(Conversation);
        Chord = new VoiceChord(parts.ChordHost(), () => host.Settings.Value.TalkChord, () => [ParseOrDefault(host.Settings.Chord), .. parts.OtherChords]);
        Chord.Pressed += OnChord;
        host.Settings.Changed += OnSettings;
        _ = controller; // the controller outlives this: the host disposes the voice first
        Chord.Register();
    }

    /// <summary>The conversation the sheet shows and the chord, the header's Talk and the talk button start.</summary>
    public VoiceConversation Conversation { get; }

    public VoiceChord Chord { get; }

    public SpeechOut Speech { get; }

    /// <summary>The conversation opened a card on the screen (it may not answer it); the home shows the Decisions tab.</summary>
    public event Action<Guid>? CardRequested;

    /// <summary>
    /// Builds the voice of the host's running controller over <paramref name="parts"/> (production when null). The chord is registered now; a chord another
    /// app holds shows in Settings, and the rest of the voice still works from the header and the talk button.
    /// </summary>
    public static DextraVoice Create(AppModel model, DextraHost host, VoiceStopScope scope, DextraVoiceParts? parts = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(scope);
        var use = parts ?? new DextraVoiceParts();
        var speech = new SpeechOut(use.Synth(), use.Player, new VoiceEchoReference(), new SpeechOutOptions { Language = CultureInfo.CurrentUICulture.Name },
            voice: () => host.Settings.Value.VoiceName);
        return new DextraVoice(model, host, scope, use, speech);
    }

    /// <summary>The chord was pressed (any thread): nth comes to the front, the home shows, and the conversation toggles (2.4, 16.2).</summary>
    public void PressChord() => OnChord();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        host.Settings.Changed -= OnSettings;
        Chord.Pressed -= OnChord;
        scope.Bind(null);
        Chord.Dispose();
        Conversation.Dispose();
        Speech.Dispose();
    }

    private void OnChord() => parts.Post(() =>
    {
        if (disposed) return;
        parts.BringForward(model);
        Conversation.Toggle();
    });

    // The owner changed a chord or the voice in Settings: the talk chord registers again; the voice is read at the next reply.
    private void OnSettings() => parts.Post(() =>
    {
        if (!disposed) Chord.Register();
    });

    private static PcHotkey ParseOrDefault(string text) => PcHotkey.TryParse(text, out var chord) ? chord : PcHotkey.StopDefault;

    private sealed class NoLease : IDisposable
    {
        public static NoLease Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
