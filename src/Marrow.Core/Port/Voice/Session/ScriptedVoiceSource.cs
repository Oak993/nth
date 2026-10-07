// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the scripted session source (voice-SPEC V-14.8), used by the
// Customization preview (SPEC 1) and by the tests.
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Port.Voice.Session;

/// <summary>
/// A <see cref="VoiceSession"/> bound to <see cref="ScriptedCapture"/>, <see cref="ScriptedVad"/> and <see cref="ScriptedEngine"/> that replay a
/// <see cref="VoiceScript"/> on the clock. It opens no device, loads no model, touches no network and writes to no draft: the session's
/// updates go only to whoever subscribes to <see cref="Session"/>, so the preview composer shows them and a thread never does.
/// </summary>
public sealed class ScriptedVoiceSource
{
    /// <summary>CZ-7b: the design's timed recognition events and synthetic bands, on an isolated session hub.</summary>
    public static ScriptedVoiceSource ForDesign(Func<VoiceSessionSettings> settings, Action<VoiceTypingSettings>? storeTyping = null) => new(settings, storeTyping);

    private ScriptedVoiceSource(Func<VoiceSessionSettings> settings, Action<VoiceTypingSettings>? storeTyping)
    {
        Script = new VoiceScript([]);
        Capture = new ScriptedCapture();
        Vad = ScriptedVad.Silence();
        Engine = ScriptedEngine.Always(Hypothesis.Empty);
        Microphone = new ScriptedMicrophone();
        Session = new VoiceSession(settings, storeTyping ?? (_ => { }), VoiceSeamSet.Of(Capture, Vad, Engine, Microphone), new VoiceSessionHub(), VoiceDesignScript.Default);
    }

    /// <param name="script">What the speaker says.</param>
    /// <param name="settings">Read at each click, as a real session reads them.</param>
    /// <param name="storeTyping">Receives the language answer (V-12.11); the preview stores nothing by default.</param>
    /// <param name="hub">One session at a time (V-7.11); null: a hub of its own.</param>
    /// <param name="engineLatency">How long each scripted decode takes on the clock.</param>
    /// <param name="tail">How long the capture keeps delivering room noise after the last word.</param>
    public ScriptedVoiceSource(VoiceScript script, Func<VoiceSessionSettings> settings, Action<VoiceTypingSettings>? storeTyping = null,
        VoiceSessionHub? hub = null, TimeSpan? engineLatency = null, TimeSpan? tail = null)
    {
        Script = script;
        Capture = new ScriptedCapture();
        Capture.PlayOnStart(script.Signal(16000), TimeSpan.FromSeconds(script.SpeechEnd) + (tail ?? TimeSpan.FromSeconds(60)));
        Vad = script.CreateVad();
        Engine = script.CreateEngine(engineLatency ?? TimeSpan.Zero);
        Microphone = new ScriptedMicrophone();
        Session = new VoiceSession(settings, storeTyping ?? (_ => { }), VoiceSeamSet.Of(Capture, Vad, Engine, Microphone), hub ?? new VoiceSessionHub());
    }

    public VoiceScript Script { get; }

    public ScriptedCapture Capture { get; }

    public ScriptedVad Vad { get; }

    public ScriptedEngine Engine { get; }

    public ScriptedMicrophone Microphone { get; }

    public VoiceSession Session { get; }
}
