// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0097, OD-0099 Q-V-6 B and Q-V-11 C; first-release voice typing, VT-3):
// the vocabulary of a voice session (voice-SPEC V-15).
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Port.Voice.Session;

/// <summary>The states of V-15's table.</summary>
public enum VoiceSessionState { Rest, Hover, Asking, Listening, Processing, Landing, Blocked }

/// <summary>What ended listening (the table's "click / Enter / Space, or the 5-minute cap, or a stop event").</summary>
public enum VoiceStopReason { Click, Cap, DeviceEvent, ComposerLeft, AnotherOrb }

/// <summary>
/// A note the composer shows in its field (V-14.6) or an error. The wording of the errors is not designed yet (Q-V-7 A, D-VN); the
/// session only says which happened.
/// </summary>
public enum VoiceNoteKind { Cancelled, NoSpeech, Blocked, NoMicrophone, CaptureFailed, ModelLoadFailed, EngineFailed }

/// <summary>What a session reads when a click begins one: the two settings records, Windows' display language, the model to load.</summary>
public sealed record VoiceSessionSettings(VoiceOrbSettings Orb, VoiceTypingSettings Typing, string? WindowsUiLanguage, SpeechModel? Model)
{
    // VT-15, V-7.7: the hardware record is read once at the start, so a setting never changes a running session.
    public bool LiveWords { get; init; } = true;
}

/// <summary>Why a batch of operations was sent: a live pass, the final pass, Landing's ink, or the withdraw after a cancel (V-8.6).</summary>
public enum TranscriptUpdateKind { Live, Final, Landing, Withdraw }

/// <summary>A batch of operations for the dictation layer and the words they lead to (empty after a withdraw).</summary>
public sealed record TranscriptUpdate(TranscriptUpdateKind Kind, IReadOnlyList<ScheduledOp> Ops, IReadOnlyList<TranscriptWord> Words);

/// <summary>How voice text is sent (OD-0099 Q-V-11 C): as Return does (ComposerView.swift:370-381), whatever the thread is doing.</summary>
public enum SendIntentKind { AsReturn }

/// <summary>"Send it as the prompt" (V-8.8), raised 0.35 s after landing when the setting is on.</summary>
public sealed record SendIntent(string Text, SendIntentKind Kind);
