// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0097, OD-0099; first-release voice typing, VT-3): the state machine of one
// composer's voice input (voice-SPEC V-15).
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Port.Voice.Session;

/// <summary>One composer's voice input. Main-actor only.</summary>
public sealed class VoiceSession : IAudioCaptureSink
{
    public static readonly TimeSpan Cap = TimeSpan.FromSeconds(300);
    public static readonly TimeSpan CountdownLength = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan LandingLength = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan SendDelay = TimeSpan.FromSeconds(0.35);
    public static readonly TimeSpan BlockedLength = TimeSpan.FromSeconds(5.2);
    public static readonly TimeSpan SilenceCheckLength = TimeSpan.FromSeconds(1.0);

    public VoiceSession(Func<VoiceSessionSettings> settings, Action<VoiceTypingSettings> storeTyping, VoiceSeamSet? seams = null,
        VoiceSessionHub? hub = null) => throw new NotImplementedException();

    public VoiceSessionState State => throw new NotImplementedException();

    public VoiceStopReason? LastStopReason => throw new NotImplementedException();

    /// <summary>True while a click on this orb waits for the running session to finish (V-7.11).</summary>
    public bool StartQueued => throw new NotImplementedException();

    /// <summary>The length of session audio still held; zero once the session ends (V-4.7).</summary>
    public TimeSpan BufferedAudio => throw new NotImplementedException();

    public VoiceLabelState Label => throw new NotImplementedException();

    public string LabelText => throw new NotImplementedException();

    public string? ResolvedLanguage => throw new NotImplementedException();

    public string? DetectedLanguage => throw new NotImplementedException();

    public bool LanguageNamed => throw new NotImplementedException();

    public bool CapCountdownActive => throw new NotImplementedException();

    public TimeSpan? CapCountdownRemaining => throw new NotImplementedException();

    public IReadOnlyList<TranscriptWord> Words => throw new NotImplementedException();

    public event Action<VoiceSessionState, VoiceSessionState>? StateChanged;

    public event Action<TranscriptUpdate>? TranscriptChanged;

    public event Action<VoiceNoteKind>? NoteRaised;

    public event Action<SendIntent>? SendRequested;

    public event Action? CapCountdownStarted;

    public void PointerEnter() => throw new NotImplementedException();

    public void PointerLeave() => throw new NotImplementedException();

    public void Activate() => throw new NotImplementedException();

    public void Cancel() => throw new NotImplementedException();

    public void AnswerLanguage(string answer) => throw new NotImplementedException();

    public void DismissQuestion() => throw new NotImplementedException();

    public void ComposerLeaving() => throw new NotImplementedException();

    public void LanguageDefaultChanged() => throw new NotImplementedException();

    void IAudioCaptureSink.OnAudio(ReadOnlySpan<float> interleavedSamples) => throw new NotImplementedException();

    void IAudioCaptureSink.OnDeviceEvent(CaptureDeviceEventKind kind) => throw new NotImplementedException();

    private void Unused() { StateChanged?.Invoke(default, default); TranscriptChanged?.Invoke(null!); NoteRaised?.Invoke(default); SendRequested?.Invoke(null!); CapCountdownStarted?.Invoke(); }
}
